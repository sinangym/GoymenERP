using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// KLN-D42 (STK-S31): the host adapter from the Purchasing-owned <see cref="IPurchaseOpenLineReadPortV1"/> to Inventory's
/// <see cref="IInventoryShortageSupplySourceV1"/> (Inventory never references Purchasing). It answers the context company's
/// approved purchase lines still waiting for goods receipt with the owner's own remaining text, never recomputed here.
/// <para>
/// The port carries no purchase delivery date, so <see cref="InventoryShortageSupplyAnswerV1.DatesAnswered"/> is false and every
/// line's date is null: the calculator then says "date unknown", never "on time". A dated port is requested from the
/// Purchasing owner; it replaces this adapter without an Inventory contract change. Fail-closed: a line for another company, a
/// repeated line, an empty cursor, more pages than <see cref="MaximumPages"/> or any exception is Unavailable.
/// </para>
/// </summary>
public sealed class InventoryShortageSupplyHostSource(IPurchaseOpenLineReadPortV1 purchasing, ICompanyContext companyContext)
    : IInventoryShortageSupplySourceV1
{
    /// <summary>The owner's largest line page.</summary>
    public const int PageSize = 100;

    /// <summary>Technical read bound: more open lines than this is answered Unavailable instead of a cut list.</summary>
    public const int MaximumPages = 100;

    private static readonly InventoryShortageSupplyAnswerV1 Unavailable =
        new(InventoryShortageSourceOutcomeV1.Unavailable, null, false);

    public async Task<InventoryShortageSupplyAnswerV1> ListOpenSupplyAsync(CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            return Unavailable;
        }

        var companyId = companyContext.CompanyId;
        try
        {
            var lines = new List<InventoryShortageSupplyLineV1>();
            var seen = new HashSet<Guid>();
            string? cursor = null;
            var pages = 0;
            do
            {
                if (++pages > MaximumPages)
                {
                    return Unavailable;
                }

                var page = await purchasing.ListOpenApprovedLinesAsync(PageSize, cursor, cancellationToken);
                if (page?.Items is null || page.Items.Count > PageSize
                    || page.NextCursor is not null && page.NextCursor.Length == 0)
                {
                    return Unavailable;
                }

                foreach (var line in page.Items)
                {
                    if (line is null || line.CompanyId != companyId || line.OrderId == Guid.Empty || line.LineId == Guid.Empty
                        || line.ProductId == Guid.Empty || line.UnitId == Guid.Empty
                        || string.IsNullOrEmpty(line.RemainingText) || !seen.Add(line.LineId))
                    {
                        return Unavailable;
                    }

                    lines.Add(new InventoryShortageSupplyLineV1(line.CompanyId, line.OrderId, line.LineId, line.ProductId,
                        line.UnitId, line.RemainingText, null));
                }

                cursor = page.NextCursor;
            }
            while (cursor is not null);

            return new InventoryShortageSupplyAnswerV1(InventoryShortageSourceOutcomeV1.Answered, lines, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Fail-closed: an owner that cannot answer reliably is "unavailable" (503), never "no purchases".
            return Unavailable;
        }
    }
}

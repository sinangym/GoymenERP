using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// KLN-D42 (STK-S31): the context company's open Sales lines, read only through Sales-declared ports (Inventory never reads
/// Sales tables). Approved lines come page by page from <see cref="ISalesOrderLineReferencePortV1.ListApprovedLinesAsync"/>;
/// a line's open quantity is the same reading as <see cref="InventoryFutureDemandSource"/> — the shippable quantity (approved,
/// or the re-approval shipping cap when smaller) minus what the delivery owner recorded as shipped for the line, never below
/// zero; a cancelled remainder is 0 — so the two Inventory consumers of "açık satış" cannot drift. Only lines with an open
/// quantity above zero are answered; the due date is the line's requested delivery date.
/// <para>
/// Fail-closed: a missing port, a page or answer for another company, an unknown remainder state, a malformed quantity, a
/// shipment in another unit, more pages than <see cref="MaximumPages"/> or any exception is
/// <see cref="InventoryShortageSourceOutcomeV1.Unavailable"/>, never a shorter list. No authority (the service measures its
/// own); nothing is written.
/// </para>
/// </summary>
public sealed class InventoryShortageDemandSource(
    ICompanyContext companyContext,
    ISalesOrderLineReferencePortV1? salesLines = null,
    ISalesOrderDeliveryReadPortV1? deliveries = null) : IInventoryShortageDemandSourceV1
{
    /// <summary>The owner's largest line page (<see cref="ISalesOrderLineReferencePortV1.ListApprovedLinesAsync"/>: 1–100).</summary>
    public const int PageSize = 100;

    /// <summary>Technical read bound: more approved lines than this is answered Unavailable instead of a cut list.</summary>
    public const int MaximumPages = 100;

    private static readonly InventoryShortageDemandAnswerV1 Unavailable =
        new(InventoryShortageSourceOutcomeV1.Unavailable, null);

    public async Task<InventoryShortageDemandAnswerV1> ListOpenDemandAsync(CancellationToken cancellationToken = default)
    {
        if (salesLines is null || deliveries is null || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            return Unavailable;
        }

        var companyId = companyContext.CompanyId;
        try
        {
            var approved = new List<SalesApprovedLineReferenceV1>();
            var seenLines = new HashSet<Guid>();
            string? cursor = null;
            var pages = 0;
            do
            {
                if (++pages > MaximumPages)
                {
                    return Unavailable;
                }

                var page = await salesLines.ListApprovedLinesAsync(PageSize, cursor, cancellationToken);
                if (page?.Items is null || page.Items.Count > PageSize)
                {
                    return Unavailable;
                }

                foreach (var line in page.Items)
                {
                    if (line is null || line.CompanyId != companyId || line.OrderId == Guid.Empty || line.LineId == Guid.Empty
                        || line.ProductId == Guid.Empty || line.OrderUnitId == Guid.Empty
                        || string.IsNullOrWhiteSpace(line.DocumentNo) || !seenLines.Add(line.LineId))
                    {
                        return Unavailable;
                    }

                    approved.Add(line);
                }

                if (page.NextCursor is not null && page.NextCursor.Length == 0)
                {
                    return Unavailable;
                }

                cursor = page.NextCursor;
            }
            while (cursor is not null);

            var approvedByLine = approved.ToDictionary(line => line.LineId);
            var shippedByLine = new Dictionary<Guid, decimal>();
            var openOrders = approved
                .Where(line => !IsCancelled(line))
                .Select(line => line.OrderId)
                .Distinct()
                .ToArray();
            foreach (var batch in openOrders.Chunk(ISalesOrderDeliveryReadPortV1.MaximumBatchSize))
            {
                var answers = await deliveries.ReadOrdersDeliveriesAsync(batch, cancellationToken);
                if (answers is null || answers.Count != batch.Length)
                {
                    return Unavailable;
                }

                var expectedOrders = batch.ToHashSet();
                foreach (var answer in answers)
                {
                    if (answer is null || answer.CompanyId != companyId || !expectedOrders.Remove(answer.OrderId)
                        || answer.Lines is null)
                    {
                        return Unavailable;
                    }

                    foreach (var entry in answer.Lines)
                    {
                        if (entry is null || entry.ShippedQuantity < 0m)
                        {
                            return Unavailable;
                        }

                        if (!approvedByLine.TryGetValue(entry.OrderLineId, out var line))
                        {
                            // A shipment for a line that is no longer listed as approved is not this read's demand.
                            continue;
                        }

                        if (line.OrderId != answer.OrderId || entry.UnitId != line.OrderUnitId)
                        {
                            return Unavailable;
                        }

                        shippedByLine[line.LineId] = shippedByLine.GetValueOrDefault(line.LineId) + entry.ShippedQuantity;
                    }
                }
            }

            var open = new List<InventoryShortageDemandLineV1>();
            foreach (var line in approved)
            {
                if (IsCancelled(line))
                {
                    continue;
                }

                if (!string.Equals(line.RemainderState, SalesRemainderChangeNamesV1.Open, StringComparison.Ordinal)
                    && !string.Equals(line.RemainderState, SalesRemainderChangeNamesV1.Revised, StringComparison.Ordinal))
                {
                    return Unavailable;
                }

                if (!TryQuantity(line.QuantityText, out var ordered))
                {
                    return Unavailable;
                }

                if (line.ShippingCapQuantityText is not null)
                {
                    if (!TryQuantity(line.ShippingCapQuantityText, out var cap))
                    {
                        return Unavailable;
                    }

                    ordered = Math.Min(ordered, cap);
                }

                var remaining = Math.Max(0m, ordered - shippedByLine.GetValueOrDefault(line.LineId));
                if (remaining == 0m)
                {
                    continue;
                }

                open.Add(new InventoryShortageDemandLineV1(companyId, line.OrderId, line.DocumentNo, line.LineId, line.LineNo,
                    line.ProductId, line.ProductCode, line.ProductName, line.OrderUnitId, line.RequestedDeliveryDate,
                    remaining.ToString(CultureInfo.InvariantCulture)));
            }

            return new InventoryShortageDemandAnswerV1(InventoryShortageSourceOutcomeV1.Answered, open);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Fail-closed: an owner that cannot answer reliably is "unavailable" (503), never an assumed empty demand.
            return Unavailable;
        }
    }

    private static bool IsCancelled(SalesApprovedLineReferenceV1 line) =>
        string.Equals(line.RemainderState, SalesRemainderChangeNamesV1.Cancelled, StringComparison.Ordinal);

    private static bool TryQuantity(string? text, out decimal value)
    {
        value = 0m;
        return !string.IsNullOrEmpty(text)
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && value >= 0m;
    }
}

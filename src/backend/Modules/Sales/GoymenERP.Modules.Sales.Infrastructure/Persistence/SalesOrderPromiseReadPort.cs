using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// WP14 (YOL-D29): the Sales-owned implementation of <see cref="ISalesOrderPromiseReadPortV1"/> over the company-filtered draft
/// store. The promised day is the line's requested delivery date, else the order's. The quantity is the line's standing quantity
/// as the shipping line port names it (SAT-D103/D115); for a CLOSED order or a line closed short — which the shipping port
/// refuses because nothing more ships — it is the current revision's quantity when that revision is approved and accepted,
/// otherwise the line is unmeasured (counted, never guessed).
/// </summary>
public sealed class SalesOrderPromiseReadPort(SalesStore store, ICompanyContext companyContext) : ISalesOrderPromiseReadPortV1
{
    public async Task<SalesOrderPromisePageV1> ListPromisedLinesAsync(DateOnly from, DateOnly to, int pageSize, string? cursor,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
            throw new InvalidOperationException("Sales promise read requires the resolved company.");
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to));
        if (pageSize is < 1 or > ISalesOrderPromiseReadPortV1.MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var page = await store.ListPromisedDraftsAsync(from, to, pageSize, cursor, cancellationToken);
        var lines = new List<SalesOrderPromisedLineV1>();
        var unmeasured = 0;
        foreach (var draft in page.Items)
        {
            if (draft.CompanyId != companyContext.CompanyId || draft.State == SalesDraftState.Cancelled) continue;
            foreach (var line in draft.Lines.OrderBy(item => item.LineNo))
            {
                var promised = line.RequestedDeliveryDate ?? draft.Header.RequestedDeliveryDate;
                if (promised is not { } day || day < from || day > to) continue;
                var quantity = Quantity(draft, line);
                if (quantity is null)
                {
                    unmeasured++;
                    continue;
                }

                lines.Add(new SalesOrderPromisedLineV1(draft.CompanyId, draft.Id, draft.DocumentNo, line.Id, line.LineNo, draft.Header.PartyId,
                    draft.Header.CompanyAccountId, line.OrderUnit.UnitId, line.OrderUnit.UnitCode, day, quantity));
            }
        }

        return new SalesOrderPromisePageV1(companyContext.CompanyId, lines, unmeasured, page.NextCursor);
    }

    private static string? Quantity(SalesOrderDraft draft, SalesOrderDraftLine line)
    {
        var closed = draft.State == SalesDraftState.Closed || draft.IsLineClosed(line.Id);
        string? text;
        if (!closed)
        {
            text = SalesOrderLineReferencePort.Stand(draft, line) is { RefusedState: null, QuantityText: { } standing } ? standing : null;
        }
        else
        {
            text = draft.ApprovalState == InternalApprovalState.Approved && draft.IsCustomerAccepted ? line.QuantityText : null;
        }

        return text is not null && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value > 0
            ? text
            : null;
    }
}

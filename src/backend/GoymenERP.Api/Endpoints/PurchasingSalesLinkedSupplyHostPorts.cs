using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// A4 / MOD-03 (SAT-D219): Purchasing's adapter of Sales's <see cref="ISalesOrderLinkedSupplyReadPortV1"/> (SupplyKind
/// <see cref="SalesLinkedSupplyKindsV1.Purchasing"/>), registered by the host because Purchasing does not reference Sales. It
/// translates the Purchasing-owned <see cref="IPurchaseSalesLinkedSupplyReadPortV1"/> answer and grants nothing: the Sales caller
/// measures its own authority. Present carries every OPEN purchase order line serving the order; None means the owner read its rows
/// and found none; any failure (a closed runtime, an exception, a malformed row) is Unreadable with a reason code — never None
/// (fail-closed). It creates nothing.
/// </summary>
public sealed class PurchasingSalesOrderLinkedSupplyReadPort(IPurchaseSalesLinkedSupplyReadPortV1 purchasing)
    : ISalesOrderLinkedSupplyReadPortV1
{
    /// <summary>The Unreadable reason when Purchasing could not read its own rows.</summary>
    public const string UnreadableReason = "purchasing_linked_supply_unreadable";

    public string SupplyKind => SalesLinkedSupplyKindsV1.Purchasing;

    public async Task<SalesOrderLinkedSupplyAnswerV1> ReadOrderLinkedSupplyAsync(
        SalesOrderLinkedSupplyQueryV1 query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            var lines = await purchasing.ListOpenForSalesOrderAsync(query.CompanyId, query.OrderId, query.OrderLineIds, cancellationToken);
            if (lines is null || lines.Any(line => line.CompanyId != query.CompanyId || string.IsNullOrWhiteSpace(line.OpenQuantityText)
                    || !decimal.TryParse(line.OpenQuantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var open) || open <= 0m
                    || (query.OrderLineIds is { Count: > 0 } narrowing && !narrowing.Contains(line.SalesOrderLineId))))
            {
                return Unreadable(query);
            }

            if (lines.Count == 0)
            {
                return Answer(SalesCancellationImpactStatusesV1.None, query, [], null);
            }

            // (document, line) is unique in the answer; a duplicate would be a double count, so it is not trusted.
            if (lines.Select(line => (line.PurchaseOrderId, line.LineId)).Distinct().Count() != lines.Count)
            {
                return Unreadable(query);
            }

            return Answer(SalesCancellationImpactStatusesV1.Present, query,
                [.. lines.Select(line => new SalesOrderLinkedSupplyItemV1(
                    line.PurchaseOrderId, line.LineId, SalesLinkedSupplyDocumentTypesV1.PurchaseOrder, null, line.SalesOrderLineId,
                    line.Status, line.OpenQuantityText, line.UnitId, null, null))], null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Unreadable(query);
        }
    }

    private static SalesOrderLinkedSupplyAnswerV1 Unreadable(SalesOrderLinkedSupplyQueryV1 query) =>
        Answer(SalesCancellationImpactStatusesV1.Unreadable, query, [], UnreadableReason);

    private static SalesOrderLinkedSupplyAnswerV1 Answer(string status, SalesOrderLinkedSupplyQueryV1 query,
        IReadOnlyList<SalesOrderLinkedSupplyItemV1> items, string? reason) =>
        new(status, query.CompanyId, query.OrderId, SalesLinkedSupplyKindsV1.Purchasing, items, reason);
}

/// <summary>
/// A4 / MOD-03 (SAT-D219, EK38): the declaration that Purchasing RECORDS links to Sales order lines (a request line's Sales source,
/// carried onto a purchase order line). It ships with <see cref="PurchasingSalesOrderLinkedSupplyReadPort"/> in the same change, so
/// Sales reads a recorded kind without an adapter as Unreadable and never as "no source". It carries no authority and reads nothing.
/// </summary>
public sealed class PurchasingSalesOrderLinkedSupplyRecorder : ISalesOrderLinkedSupplyRecorderV1
{
    public string SupplyKind => SalesLinkedSupplyKindsV1.Purchasing;
}

/// <summary>
/// SAT-D220 (reader only): the host's translation of Sales's approved-line port to Purchasing's
/// <see cref="IPurchaseRequestSalesSourcePortV1"/>. Approved only when Sales answers Approved with that very order and line in the
/// context company; an unknown outcome, a mismatched line or any exception is Unavailable (fail-closed), never Approved. Reading
/// creates nothing; this adapter is not yet called by request creation (see the Purchasing README).
/// </summary>
public sealed class PurchasingRequestSalesSourcePort(ISalesOrderLineReferencePortV1 salesLines) : IPurchaseRequestSalesSourcePortV1
{
    public async Task<PurchaseRequestSalesSourceOutcomeV1> CheckSalesOrderLineAsync(
        Guid salesOrderId, Guid salesOrderLineId, CancellationToken cancellationToken = default)
    {
        try
        {
            var answer = await salesLines.GetApprovedLineAsync(salesOrderId, salesOrderLineId, cancellationToken);
            return answer.Outcome switch
            {
                SalesOrderLineReferenceOutcomeV1.Approved when answer.Line is { } line && line.OrderId == salesOrderId && line.LineId == salesOrderLineId
                    => PurchaseRequestSalesSourceOutcomeV1.Approved,
                SalesOrderLineReferenceOutcomeV1.NotApproved => PurchaseRequestSalesSourceOutcomeV1.NotApproved,
                SalesOrderLineReferenceOutcomeV1.NotFound => PurchaseRequestSalesSourceOutcomeV1.NotFound,
                _ => PurchaseRequestSalesSourceOutcomeV1.Unavailable,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return PurchaseRequestSalesSourceOutcomeV1.Unavailable;
        }
    }
}

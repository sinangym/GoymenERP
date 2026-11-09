namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// A4 / MOD-03 (SAT-D219): the Purchasing-owned read behind the Sales cancellation preview's "bağlı satın alma". Purchasing does not
/// reference Sales: this port speaks only Purchasing's own words, and the host adapter (<c>PurchasingSalesLinkedSupplyReadPort</c>)
/// translates it to Sales's <c>ISalesOrderLinkedSupplyReadPortV1</c>. It answers the OPEN purchase order lines that serve a Sales
/// order through the request line source (<c>SourceKind = Sales</c>, <c>SourceDocumentId = orderId</c>, index
/// <c>IX_PurchaseRequestLines_Source</c>): an APPROVED order (a Draft reserves nothing, a Cancelled one is not future supply) whose
/// line still has a remainder above zero. Since A4b a Sales-sourced line is recorded only after Sales's approved-line port verified it, but this read still
/// does not call Sales: an unknown order is simply an empty answer. It carries no authority and creates nothing. The company must be the caller's server-resolved
/// company; a different one is refused (never read unscoped).
/// </summary>
public interface IPurchaseSalesLinkedSupplyReadPortV1
{
    Task<IReadOnlyList<PurchaseSalesLinkedSupplyLineV1>> ListOpenForSalesOrderAsync(
        Guid companyId, Guid salesOrderId, IReadOnlyList<Guid>? salesOrderLineIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// One open purchase order line serving a Sales order line. <see cref="OpenQuantityText"/> is the exact decimal text of what is still
/// expected on the commitment (the general line's remaining plus its technical slices' remaining — the slice is already inside the
/// general line's ordered quantity, so the commitment is never counted twice), in <see cref="UnitId"/> (a Settings unit identity).
/// <see cref="LineId"/> is always the GENERAL line; technical lines are never listed on their own.
/// </summary>
public sealed record PurchaseSalesLinkedSupplyLineV1(
    Guid CompanyId,
    Guid PurchaseOrderId,
    Guid LineId,
    Guid SalesOrderLineId,
    string Status,
    string OpenQuantityText,
    Guid UnitId);

/// <summary>
/// SAT-D220 (reader only): the Purchasing-owned consumer port a Sales-sourced NEW request line is checked against. The host adapts
/// it to Sales's <c>ISalesOrderLineReferencePortV1.GetApprovedLineAsync</c>. Approved: the order line exists in the company and is an
/// approved need; NotApproved / NotFound: refused with a stable code; Unavailable (no adapter, or the owner failed): fail-closed,
/// never read as Approved. Nothing here is wired into request creation yet (see the Purchasing README: it collides with #951's
/// A1-2b closure of Sales-sourced new lines).
/// </summary>
public interface IPurchaseRequestSalesSourcePortV1
{
    Task<PurchaseRequestSalesSourceOutcomeV1> CheckSalesOrderLineAsync(Guid salesOrderId, Guid salesOrderLineId, CancellationToken cancellationToken = default);
}

public enum PurchaseRequestSalesSourceOutcomeV1
{
    Approved = 1,
    NotApproved = 2,
    NotFound = 3,
    Unavailable = 4,
}

/// <summary>The stable problem codes a refused Sales-sourced request line carries (the field is <c>lines[i].source</c>).</summary>
public static class PurchaseRequestSalesSourceCodesV1
{
    public const string NotApproved = "purchasing_request_source_sales_not_approved";
    public const string NotFound = "purchasing_request_source_sales_not_found";

    /// <summary>The owner could not answer (503, fail-closed).</summary>
    public const string Unavailable = "purchasing_request_source_sales_unavailable";

    /// <summary>The stable code of an outcome, or null for Approved.</summary>
    public static string? CodeOf(PurchaseRequestSalesSourceOutcomeV1 outcome) => outcome switch
    {
        PurchaseRequestSalesSourceOutcomeV1.Approved => null,
        PurchaseRequestSalesSourceOutcomeV1.NotApproved => NotApproved,
        PurchaseRequestSalesSourceOutcomeV1.NotFound => NotFound,
        _ => Unavailable,
    };
}

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// SAT-D406 / SAT-D367 (SINAN-CEVAP 33): the Sales-owned port of the "sevk planı miktarı" the risk remainder is measured on — per
/// Sales order line, the quantity on issued (approved), not yet shipped loading instructions. Draft and cancelled instructions do
/// not count. The Logistics owner answers it (its <c>logistics.sales-order-planned-quantity.v1</c> read); the host binds this port
/// to it (Logistics references Sales, so Sales declares its own port). Not registered means "plan unknown" and the remainder is
/// "Unavailable", never an assumed zero.
/// <para>
/// A line missing from the answer of an ANSWERED order means zero; a present line always carries a positive quantity. At most
/// <see cref="SalesOrderPlannedQuantityContractV1.MaximumOrderIds"/> distinct order ids per call; an answer covers whole orders
/// only and, when <see cref="SalesOrderPlannedQuantityReadV1.HasMore"/> is true, the caller asks again for the orders not in
/// <see cref="SalesOrderPlannedQuantityReadV1.AnsweredOrderIds"/>. Company-filtered and side-effect free; the caller measures its
/// own permission around the call.
/// </para>
/// </summary>
public interface ISalesOrderPlannedQuantityReadPortV1
{
    Task<SalesOrderPlannedQuantityReadV1> ReadOpenInstructionQuantitiesAsync(Guid companyId, IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken = default);
}

/// <summary>Limits of the planned-quantity read.</summary>
public static class SalesOrderPlannedQuantityContractV1
{
    /// <summary>At most this many distinct order ids per call (the owner's page).</summary>
    public const int MaximumOrderIds = 200;
}

/// <summary>The fail-closed outcome of one read; anything but <see cref="Found"/> is "plan unknown".</summary>
public enum SalesOrderPlannedQuantityOutcomeV1
{
    Found = 1,
    Denied = 2,
    Unavailable = 3,
}

/// <summary>One answer; <see cref="Lines"/> and <see cref="AnsweredOrderIds"/> are meaningful only when <see cref="Outcome"/> is Found.</summary>
public sealed record SalesOrderPlannedQuantityReadV1(
    SalesOrderPlannedQuantityOutcomeV1 Outcome,
    Guid CompanyId,
    IReadOnlyList<SalesOrderPlannedQuantityLineV1> Lines,
    IReadOnlyList<Guid> AnsweredOrderIds,
    bool HasMore);

/// <summary>One order line's open instruction quantity (exact, positive) in the Settings unit <see cref="UnitId"/>.</summary>
public sealed record SalesOrderPlannedQuantityLineV1(Guid OrderId, Guid OrderLineId, Guid UnitId, decimal OpenInstructionQuantity);

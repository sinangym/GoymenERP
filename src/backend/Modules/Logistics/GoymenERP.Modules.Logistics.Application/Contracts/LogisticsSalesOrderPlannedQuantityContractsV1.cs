namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// SAT-D367 (SINAN-CEVAP 33): the Logistics-owned read of the "sevk planı miktarı" SAT-D313 names — per Sales order line and
/// unit, the quantity that stands on <b>issued</b> (approved) loading instructions and is not shipped yet. Draft instructions
/// and cancelled instructions are not counted. The open quantity of one instruction line is its instructed quantity minus its
/// shipped quantity (<c>CK_LoadingInstructionLines_Shares</c> keeps shipped ≤ loaded ≤ instructed, so it is never negative);
/// the answer sums it over the issued instructions of the order line. Loaded-but-not-shipped quantity is still open.
/// <para>
/// An order line no issued instruction serves, or whose issued instructions are fully shipped, has <b>no</b> line in the
/// answer: the consumer reads a missing line as zero. Lines are never padded with explicit zeros, so a present line always
/// carries a positive quantity.
/// </para>
/// <para>
/// The port is company-filtered, untracked and side-effect free: no attempt, audit or version effect, no authority (LOG-4
/// D6; the Sales caller measures its own permission around the call). The company comes from the server-resolved context:
/// without one, or when <c>companyId</c> differs from it, the answer is <see cref="LogisticsSalesOrderPlannedQuantityOutcomeV1.Denied"/>
/// with no lines (fail closed, never another company's data). A stored row that cannot be rebuilt as a valid quantity answers
/// <see cref="LogisticsSalesOrderPlannedQuantityOutcomeV1.Unavailable"/>. More than
/// <see cref="LogisticsSalesOrderPlannedQuantityContractV1.MaximumOrderIds"/> distinct order ids is a caller error.
/// </para>
/// <para>
/// An answer holds every line of the orders it covers, never half an order: when the requested orders carry more lines than
/// <see cref="LogisticsSalesOrderPlannedQuantityContractV1.MaximumLines"/>, the answer stops at a whole-order boundary,
/// <see cref="LogisticsSalesOrderPlannedQuantityReadV1.HasMore"/> is true and
/// <see cref="LogisticsSalesOrderPlannedQuantityReadV1.AnsweredOrderIds"/> names the orders it covers; the consumer asks
/// again for the others. The first (lowest-id) order is always answered whole, even past the cap, so a page is never empty
/// while orders remain. A missing line means zero only for an answered order.
/// </para>
/// </summary>
public interface ILogisticsSalesOrderPlannedQuantityReadPortV1
{
    Task<LogisticsSalesOrderPlannedQuantityReadV1> ReadOpenInstructionQuantitiesAsync(Guid companyId,
        IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);
}

/// <summary>Stable names and limits of the SAT-D367 planned-quantity read.</summary>
public static class LogisticsSalesOrderPlannedQuantityContractV1
{
    /// <summary>The versioned contract name.</summary>
    public const string ContractName = "logistics.sales-order-planned-quantity.v1";

    /// <summary>At most this many distinct order ids per call (the Sales consumer's page).</summary>
    public const int MaximumOrderIds = 200;

    /// <summary>At most this many lines per answer; past it the answer stops at a whole-order boundary with HasMore.</summary>
    public const int MaximumLines = 2000;
}

/// <summary>The fail-closed outcome of one read; a consumer treats anything but <see cref="Found"/> as "plan unknown".</summary>
public enum LogisticsSalesOrderPlannedQuantityOutcomeV1
{
    /// <summary>The read ran under the context company; <see cref="LogisticsSalesOrderPlannedQuantityReadV1.Lines"/> is the answer (possibly empty).</summary>
    Found = 1,

    /// <summary>No server-resolved company, or the asked company is not the context company; nothing is answered.</summary>
    Denied = 2,

    /// <summary>A stored row cannot be rebuilt as a valid quantity; nothing is answered.</summary>
    Unavailable = 3,
}

/// <summary>Machine-readable reasons of a non-found answer (stable codes; no user text).</summary>
public static class LogisticsSalesOrderPlannedQuantityReasonCodesV1
{
    public const string CompanyNotResolved = "logistics_planned_quantity_company_not_resolved";
    public const string CompanyMismatch = "logistics_planned_quantity_company_mismatch";
    public const string RecordInconsistent = "logistics_planned_quantity_record_inconsistent";
}

/// <summary>
/// One answer of <see cref="ILogisticsSalesOrderPlannedQuantityReadPortV1"/>. <see cref="Lines"/> and
/// <see cref="AnsweredOrderIds"/> are empty and <see cref="ReasonCode"/> is set unless <see cref="Outcome"/> is
/// <see cref="LogisticsSalesOrderPlannedQuantityOutcomeV1.Found"/>. Lines are ordered by order id, order line id, unit id.
/// </summary>
public sealed record LogisticsSalesOrderPlannedQuantityReadV1(
    LogisticsSalesOrderPlannedQuantityOutcomeV1 Outcome,
    string? ReasonCode,
    Guid CompanyId,
    IReadOnlyList<LogisticsSalesOrderPlannedQuantityLineV1> Lines,
    IReadOnlyList<Guid> AnsweredOrderIds,
    bool HasMore)
{
    public static LogisticsSalesOrderPlannedQuantityReadV1 Denied(Guid companyId, string reasonCode) =>
        new(LogisticsSalesOrderPlannedQuantityOutcomeV1.Denied, reasonCode, companyId, [], [], false);

    public static LogisticsSalesOrderPlannedQuantityReadV1 Unavailable(Guid companyId, string reasonCode) =>
        new(LogisticsSalesOrderPlannedQuantityOutcomeV1.Unavailable, reasonCode, companyId, [], [], false);

    public static LogisticsSalesOrderPlannedQuantityReadV1 Found(Guid companyId,
        IReadOnlyList<LogisticsSalesOrderPlannedQuantityLineV1> lines, IReadOnlyList<Guid> answeredOrderIds, bool hasMore) =>
        new(LogisticsSalesOrderPlannedQuantityOutcomeV1.Found, null, companyId,
            lines ?? throw new ArgumentNullException(nameof(lines)),
            answeredOrderIds ?? throw new ArgumentNullException(nameof(answeredOrderIds)), hasMore);
}

/// <summary>
/// One order line's open instruction quantity. <see cref="OpenInstructionQuantity"/> is exact and positive, in
/// <see cref="QuantityUnit"/> (the shared TEK-02 unit: kilograms carry at most three decimals, pieces are whole); the consumer
/// wraps it in its own quantity value object (<c>Quantity&lt;TRefusals&gt;.Of(value, unit)</c>), which accepts it as is.
/// <see cref="UnitId"/> is the Settings unit the instruction lines are in.
/// </summary>
public sealed record LogisticsSalesOrderPlannedQuantityLineV1(
    Guid OrderId,
    Guid OrderLineId,
    Guid UnitId,
    GoymenERP.BuildingBlocks.Quantities.QuantityUnit QuantityUnit,
    decimal OpenInstructionQuantity);

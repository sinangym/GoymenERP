using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// Who asked, under which permission and in which conversation — the ADR-009 audit envelope of
/// one write. Every value comes from the caller's server-side context (a later command surface);
/// this module never makes one up. The company is not here: it is always the server-resolved
/// company context, and a record of any other company is refused.
/// </summary>
public sealed record ManufacturingCommandMetadata(
    string ActorId,
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string SourceChannel,
    string? ClientIpAddress = null,
    string? UserAgent = null);

/// <summary>What a write did: a new record, or the same request again with no second effect (R3).</summary>
public enum ManufacturingWriteOutcome
{
    Recorded = 1,
    Replayed = 2,
}

/// <summary>The stable refusal codes of the Manufacturing store, beside the domain's own rule codes.</summary>
public static class ManufacturingStoreCodes
{
    /// <summary>The record's company is not the server-resolved company context (or there is none).</summary>
    public const string CompanyContextMismatch = "manufacturing_company_context_mismatch";

    /// <summary>This company has no such plan (another company's plan is not found either).</summary>
    public const string PlanNotFound = "manufacturing_plan_not_found";

    /// <summary>This company has no such order (another company's order is not found either).</summary>
    public const string OrderNotFound = "manufacturing_order_not_found";

    /// <summary>The same plan id was offered again with a different content.</summary>
    public const string PlanIdConflict = "manufacturing_plan_id_conflict";

    /// <summary>The same order id was offered again for another plan.</summary>
    public const string OrderIdConflict = "manufacturing_order_id_conflict";

    /// <summary>
    /// The same fact id (of the same kind) was offered again with a different content: another
    /// order, roll, mass, class or time. The first fact stands; nothing of this write is stored.
    /// </summary>
    public const string FactIdConflict = "manufacturing_fact_id_conflict";

    /// <summary>
    /// Another write changed the same plan or order first. Nothing of this write was stored; the
    /// caller may read again and retry with the same fact id.
    /// </summary>
    public const string ConcurrentChange = "manufacturing_concurrent_change";

    /// <summary>A text or mass does not fit its stored column; it is refused, never cut.</summary>
    public const string ValueDoesNotFit = "manufacturing_value_does_not_fit";
}

/// <summary>A manufacturing record could not be written; <see cref="Code"/> says why. Nothing was stored.</summary>
public sealed class ManufacturingStoreException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>The persistent plan store of the server-resolved company.</summary>
public interface IManufacturingPlanStore
{
    /// <summary>
    /// Stores a new DRAFT plan. The same plan id with the same content is a replay with no second
    /// effect; with another content it is refused (<see cref="ManufacturingStoreCodes.PlanIdConflict"/>).
    /// </summary>
    Task<ManufacturingWriteOutcome> SavePlanAsync(ProductionPlan plan, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>Approves the stored plan at the version the caller read (the domain's rules and fence).</summary>
    Task<ProductionPlan> ApprovePlanAsync(Guid planId, long expectedVersion, Guid approvedByUserId, DateTimeOffset approvedAt,
        ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>The company's stored plan, or null (another company's plan is null too).</summary>
    Task<ProductionPlan?> FindPlanAsync(Guid planId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The persistent order store of the server-resolved company. Every realization fact is stored
/// with the order's new version and its audit event in one save, or not at all.
/// </summary>
public interface IManufacturingOrderStore
{
    /// <summary>
    /// Creates an order from a stored APPROVED plan, with its own snapshot of the plan's inputs and
    /// target. The same order id for the same plan again is a replay; for another plan it is refused.
    /// </summary>
    Task<ManufacturingWriteOutcome> CreateOrderAsync(Guid orderId, Guid planId, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<ManufacturingWriteOutcome> IssueInputAsync(Guid orderId, long expectedVersion, Guid issueId, Guid parentRollId,
        Kilograms mass, DateTimeOffset issuedAt, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<ManufacturingWriteOutcome> ReturnUnusedInputAsync(Guid orderId, long expectedVersion, Guid returnId,
        Guid parentRollId, Kilograms mass, DateTimeOffset returnedAt, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<ManufacturingWriteOutcome> DeclareOutputAsync(Guid orderId, long expectedVersion, Guid outputId,
        Guid parentRollId, OutputClass outputClass, Kilograms mass, DateTimeOffset declaredAt,
        ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<ManufacturingWriteOutcome> RecordWorkInProgressAsync(Guid orderId, long expectedVersion, Guid measurementId,
        Guid parentRollId, Kilograms mass, DateTimeOffset measuredAt, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the stored order at the version the caller read. The decision is the domain's
    /// (MFG-4: every roll reconciles on its own; remaining WIP does not block; any unexplained
    /// difference refuses), taken on the order rebuilt from its stored facts.
    /// </summary>
    Task CloseOrderAsync(Guid orderId, long expectedVersion, Guid closedByUserId, DateTimeOffset closedAt,
        ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>The company's stored order rebuilt from its facts, or null (another company's order is null too).</summary>
    Task<ProductionOrder?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}

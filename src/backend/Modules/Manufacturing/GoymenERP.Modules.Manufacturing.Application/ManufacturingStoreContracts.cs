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
    string? UserAgent = null,
    Func<object, ManufacturingCommandRecord>? Completion = null);

/// <summary>
/// One completed command attempt (ADR-011): unique per (company, command name, key hash). The key
/// is kept only as the uppercase SHA-256 of the trimmed <c>Idempotency-Key</c>; the request hash is
/// the canonical payload's, the actor is stored beside it, and the result is the answer to replay.
/// </summary>
public sealed record ManufacturingCommandRecord(
    string CommandName,
    string IdempotencyKeyHash,
    string ActorId,
    string RequestHash,
    string ResultJson,
    string OriginalCorrelationId);

/// <summary>
/// The Manufacturing command log of the server-resolved company (ADR-011 §2–§8). A successful write
/// adds its record in the same save as the business change and its audit event, through
/// <see cref="ManufacturingCommandMetadata.Completion"/>; a terminal refusal (not found, version
/// conflict) is recorded on its own. Records are append-only.
/// </summary>
public interface IManufacturingCommandLog
{
    Task<ManufacturingCommandRecord?> FindCommandAsync(string commandName, string idempotencyKeyHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a record without a business change. Another writer that stored the same
    /// (command, key) first makes this <see cref="ManufacturingStoreCodes.ConcurrentChange"/>.
    /// </summary>
    Task RecordCommandAsync(ManufacturingCommandRecord record, CancellationToken cancellationToken = default);
}

/// <summary>What a write did: a new record, or the same request again with no second effect (R3).</summary>
public enum ManufacturingWriteOutcome
{
    Recorded = 1,
    Replayed = 2,
}

/// <summary>The stable refusal codes of the Manufacturing store, beside the domain's own rule codes.</summary>
public static class ManufacturingStoreCodes
{
    /// <summary>The final persistence-bound authority fence no longer grants this exact company and actor.</summary>
    public const string AuthorityDenied = "manufacturing_authority_denied";

    /// <summary>The record's company is not the server-resolved company context (or there is none).</summary>
    public const string CompanyContextMismatch = "manufacturing_company_context_mismatch";

    /// <summary>This company has no such plan (another company's plan is not found either).</summary>
    public const string PlanNotFound = "manufacturing_plan_not_found";

    /// <summary>This company has no such order (another company's order is not found either).</summary>
    public const string OrderNotFound = "manufacturing_order_not_found";

    /// <summary>
    /// The plan names a work center that is not one of this company's (Organization's composite key
    /// refused it). Nothing was stored.
    /// </summary>
    public const string WorkCenterNotFound = "manufacturing_work_center_not_found";

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

    /// <summary>MFG-PLAN-EDIT: the longest revision reason (trimmed) the stored column keeps; a longer one is refused.</summary>
    public const int PlanRevisionReasonMaximumLength = 256;

    /// <summary>A text or mass does not fit its stored column; it is refused, never cut.</summary>
    public const string ValueDoesNotFit = "manufacturing_value_does_not_fit";

    /// <summary>
    /// The list cursor cannot be read, or belongs to another company or another ordering. Nothing is
    /// listed: a cursor is never guessed and never silently restarted at the first page.
    /// </summary>
    public const string ListCursorInvalid = "manufacturing_list_cursor_invalid";
}

/// <summary>
/// The final authority fence of a policy-backed close. The application captures the request's exact company and actor;
/// the store invokes the fresh check after rebuilding the stored order and immediately before any close, decision,
/// audit or command-completion state is staged.
/// </summary>
public sealed class ManufacturingPersistenceAuthorityFenceV1
{
    private readonly Func<CancellationToken, Task<bool>> _revalidate;

    public ManufacturingPersistenceAuthorityFenceV1(Guid companyId, Guid actorId,
        Func<CancellationToken, Task<bool>> revalidate)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket kimliği boş olamaz.", nameof(companyId));
        }

        if (actorId == Guid.Empty)
        {
            throw new ArgumentException("Kullanıcı kimliği boş olamaz.", nameof(actorId));
        }

        CompanyId = companyId;
        ActorId = actorId;
        _revalidate = revalidate ?? throw new ArgumentNullException(nameof(revalidate));
    }

    public Guid CompanyId { get; }

    public Guid ActorId { get; }

    public Task<bool> RevalidateAsync(Guid companyId, Guid actorId, CancellationToken cancellationToken = default) =>
        companyId == CompanyId && actorId == ActorId
            ? _revalidate(cancellationToken)
            : Task.FromResult(false);
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
/// One stored plan as the plan list reads it (MFG-PLAN-LIST): the plan's own row and the count and planned
/// mass of its planned inputs. No engine answer, no slitting spec, no roll label and no order it may have
/// opened — the plan read and the order list answer those, and the label is Inventory's read.
/// </summary>
public sealed record ManufacturingPlanSummary(
    Guid PlanId, Guid CompanyId, Guid WorkCenterId, ManufacturingProcess Process, PlanStatus Status, long Version,
    int PlannedRollCount, Kilograms PlannedInputMass, TargetBasis? TargetBasis, Kilograms? TargetMass,
    DateTimeOffset CreatedAtUtc, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAtUtc);

/// <summary>
/// One page of the company's plans, newest first (createdAtUtc desc, id desc), as the order list pages:
/// <see cref="NextCursor"/> null is the end, and the cursor is opaque and bound to the company and to the
/// plan list's own ordering (an order-list cursor is not a plan-list cursor, and the reverse).
/// </summary>
public sealed record ManufacturingPlanPage(IReadOnlyList<ManufacturingPlanSummary> Plans, string? NextCursor);

/// <summary>
/// The plan list of the server-resolved company. A port of its own, beside <see cref="IManufacturingPlanStore"/>,
/// so the plan store's existing implementations are not widened; the one store implements both.
/// </summary>
public interface IManufacturingPlanListStore
{
    /// <summary>
    /// One page of this company's plans, newest first, optionally only those in <paramref name="status"/>.
    /// <paramref name="cursor"/> is the previous page's <see cref="ManufacturingPlanPage.NextCursor"/>; another
    /// company's, another ordering's (an order-list cursor included) or an unreadable cursor is
    /// <see cref="ManufacturingStoreCodes.ListCursorInvalid"/>. A page size outside the store's bound is an
    /// <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    Task<ManufacturingPlanPage> ListPlansAsync(int pageSize, string? cursor, PlanStatus? status,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// MFG-PLAN-EDIT: one entry of a plan's history. Revision 0 is the plan as created (no reason, no reviser: the
/// plan row keeps its creation content unchanged); every later revision is one recorded Draft edit with the
/// complete content it set, its reason, who recorded it and when, and the plan version it produced.
/// </summary>
public sealed record ManufacturingPlanRevision(
    int Revision, long PlanVersion, Guid WorkCenterId, IReadOnlyList<PlannedInput> Inputs, ProductionTarget? Target,
    string? Reason, string? RevisedBy, DateTimeOffset RecordedAtUtc, CutToLengthSpec? CutToLengthSpec = null);

/// <summary>
/// MFG-PLAN-EDIT: the Draft edit of a stored plan and its history. A port of its own, beside
/// <see cref="IManufacturingPlanStore"/>, so the plan store's existing implementations are not widened (the
/// plan-list pattern); the one store implements it.
/// </summary>
public interface IManufacturingPlanRevisionStore
{
    /// <summary>
    /// Revises the stored plan at the version the caller read: the domain's rules (<c>ProductionPlan.Revise</c>)
    /// decide on the plan rebuilt from storage, and a plan that already opened an order is refused as not a draft.
    /// The revision (append-only), the plan's new version, its audit event and the command record commit in one
    /// save or not at all.
    /// </summary>
    Task<ProductionPlan> RevisePlanAsync(Guid planId, long expectedVersion, Guid workCenterId,
        ManufacturingProcess workCenterProcess, IReadOnlyList<PlannedInput> inputs, ProductionTarget? target,
        string reason, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<ProductionPlan> RevisePlanWithCutLinesAsync(Guid planId, long expectedVersion, Guid workCenterId,
        ManufacturingProcess workCenterProcess, IReadOnlyList<PlannedInput> inputs, ProductionTarget? target,
        CutToLengthSpec? cutToLengthSpec, string reason, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default) => cutToLengthSpec is null
            ? RevisePlanAsync(planId, expectedVersion, workCenterId, workCenterProcess, inputs, target, reason, metadata,
                cancellationToken)
            : throw new NotSupportedException("Bu plan deposu boy kesme satırlı revizyonu desteklemiyor.");

    /// <summary>
    /// The plan's history, oldest first (revision 0 = as created), or null when the company has no such plan
    /// (another company's plan is null too). It reads and stores nothing else.
    /// </summary>
    Task<IReadOnlyList<ManufacturingPlanRevision>?> ListPlanRevisionsAsync(Guid planId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One stored order as a list reads it: the order's own row and the count and planned mass of its
/// input snapshot. No realization fact is rebuilt and nothing is calculated — a balance, a
/// difference and a roll label are not here. The label belongs to Inventory's label read (ÜRÜN),
/// and the roll ids are not in the summary at all.
/// </summary>
public sealed record ManufacturingOrderSummary(
    Guid OrderId, Guid CompanyId, Guid PlanId, Guid WorkCenterId, ManufacturingProcess Process, OrderStatus Status,
    long Version, int PlannedRollCount, Kilograms PlannedInputMass, TargetBasis? TargetBasis, Kilograms? TargetMass,
    DateTimeOffset CreatedAtUtc, Guid? ClosedByUserId, DateTimeOffset? ClosedAtUtc);

/// <summary>
/// One page of the company's orders, newest first (createdAtUtc desc, id desc), as Sales' draft list
/// pages (#257): <see cref="NextCursor"/> null is the end, and the cursor is opaque and bound to the
/// company and ordering that produced it.
/// </summary>
public sealed record ManufacturingOrderPage(IReadOnlyList<ManufacturingOrderSummary> Orders, string? NextCursor);

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
    /// difference refuses), taken on the order rebuilt from its stored facts. The close, its audit
    /// event and its command record (ADR-011 §6) commit in one save or not at all.
    /// </summary>
    Task CloseOrderAsync(Guid orderId, long expectedVersion, Guid closedByUserId, DateTimeOffset closedAt,
        ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Policy-backed close. The store rebuilds the order, derives the fire numerator and net-issued denominator from
    /// its own facts, invokes the captured company/actor authority fence immediately before staging any change,
    /// verifies the optional exception evidence, and commits the close, immutable decision row, audit and ADR-011
    /// command record together. Existing test doubles need not implement this until they exercise the policy-backed
    /// runtime; production does.
    /// </summary>
    Task CloseOrderWithPolicyAsync(Guid orderId, long expectedVersion, Guid closedByUserId, DateTimeOffset closedAt,
        ClosureTolerancePolicySnapshotV1 policy, ClosureExceptionApprovalV1? exceptionApproval,
        ManufacturingPersistenceAuthorityFenceV1 persistenceAuthority, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Policy-backed Manufacturing close store is not available.");

    /// <summary>The company's stored order rebuilt from its facts, or null (another company's order is null too).</summary>
    Task<ProductionOrder?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of this company's orders, newest first, optionally only those in
    /// <paramref name="status"/>. <paramref name="cursor"/> is the previous page's
    /// <see cref="ManufacturingOrderPage.NextCursor"/>; another company's, another ordering's or an
    /// unreadable cursor is <see cref="ManufacturingStoreCodes.ListCursorInvalid"/>. A page size
    /// outside the store's bound is an <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    Task<ManufacturingOrderPage> ListOrdersAsync(int pageSize, string? cursor, OrderStatus? status,
        CancellationToken cancellationToken = default);
}

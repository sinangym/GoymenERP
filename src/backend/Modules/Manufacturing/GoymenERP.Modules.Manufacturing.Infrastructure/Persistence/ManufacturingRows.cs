namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>Every Manufacturing row belongs to one company and is filtered and guarded by it.</summary>
internal interface IManufacturingTenantRow
{
    Guid CompanyId { get; }
}

/// <summary>
/// A production plan. Its content (company, work center, process, target and inputs) never changes;
/// only the approval and the version do. <see cref="PayloadHash"/> is the canonical content hash
/// that tells a replayed plan from a different plan offered under the same id.
/// </summary>
internal sealed class PlanRow : IManufacturingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid WorkCenterId { get; set; }
    public int Process { get; set; }
    public int Status { get; set; }
    public int? TargetBasis { get; set; }
    public decimal? TargetKg { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>One planned parent roll of a plan, in the plan's own order. Never changes.</summary>
internal sealed class PlanInputRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid PlanId { get; set; }
    public Guid ParentRollId { get; set; }
    public int Sequence { get; set; }
    public decimal PlannedKg { get; set; }
}

/// <summary>
/// A production order with its own snapshot of the plan's target (the inputs are
/// <see cref="OrderInputRow"/>). Only the close and the version change.
/// </summary>
internal sealed class OrderRow : IManufacturingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PlanId { get; set; }
    public Guid WorkCenterId { get; set; }
    public int Process { get; set; }
    public int Status { get; set; }
    public int? TargetBasis { get; set; }
    public decimal? TargetKg { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>One planned parent roll of an order's snapshot. Never changes.</summary>
internal sealed class OrderInputRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ParentRollId { get; set; }
    public int Sequence { get; set; }
    public decimal PlannedKg { get; set; }
}

/// <summary>The kind of a stored realization fact.</summary>
internal enum FactKind
{
    InputIssue = 1,
    InputReturn = 2,
    OutputDeclaration = 3,
    WipMeasurement = 4,
}

/// <summary>
/// One realization fact (R6): its own id, its order and parent roll, its mass and when it happened;
/// for an output also its class and cost status. Append-only. A fact id is unique per company and
/// kind; <see cref="PayloadHash"/> is the canonical content hash that tells a replay from a
/// different fact offered under the same id.
/// </summary>
internal sealed class FactRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public int Kind { get; set; }
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid ParentRollId { get; set; }
    public decimal MassKg { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public int? OutputClass { get; set; }
    public int? CostStatus { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>Organization's company table, referenced for the foreign key only; not migrated here.</summary>
/// <summary>
/// One completed command attempt (ADR-011): (company, command, key hash) is its key; the key itself
/// is never stored, only its uppercase SHA-256. Append-only.
/// </summary>
internal sealed class CommandRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public string OriginalCorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal sealed class OrganizationCompanyReference
{
    public Guid Id { get; set; }
}

/// <summary>
/// Organization's work-center table, referenced by (company, work center) against Organization's
/// alternate key, so a plan or order can never point at another company's work center. Not
/// migrated here.
/// </summary>
internal sealed class OrganizationWorkCenterReference
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
}

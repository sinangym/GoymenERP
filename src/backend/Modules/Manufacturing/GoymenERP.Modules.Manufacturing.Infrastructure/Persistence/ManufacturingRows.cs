namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>Every Manufacturing row belongs to one company and is filtered and guarded by it.</summary>
internal interface IManufacturingTenantRow
{
    Guid CompanyId { get; }
}

/// <summary>
/// A production plan. Its content (company, work center, process, target and inputs) never changes;
/// only the approval and the version do. <see cref="PayloadHash"/> is the canonical content hash
/// that tells a replayed plan from a different plan offered under the same id. MFG-PLAN-EDIT: this row and its
/// inputs stay the plan AS CREATED (revision 0); a Draft revision is appended in <see cref="PlanRevisionRow"/> and
/// moves only the version and the update time here.
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

    /// <summary>MFG-6: the slitting spec's winding target, or null (no spec, or a spec that winds to no target).</summary>
    public decimal? WindingTargetKg { get; set; }

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
    public long? ExpectedLabelVersion { get; set; }
}

/// <summary>
/// MFG-6: one slit line of a plan's slitting spec, in the spec's own order. A plan has a spec exactly
/// when it has slit lines. Part of the plan's content: never changes.
/// </summary>
internal sealed class PlanSlitLineRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid PlanId { get; set; }
    public int Sequence { get; set; }
    public decimal BandWidthMm { get; set; }
    public int BandCount { get; set; }
}

/// <summary>
/// MFG-PLAN-EDIT: one recorded Draft revision of a plan (append-only). Revision 0 is the plan's own rows as created
/// and is never written here; a revision is the next number, with the complete work center and target it set, the
/// user's reason, who recorded it, when, and the plan version it produced. Never modified, never deleted.
/// </summary>
internal sealed class PlanRevisionRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid PlanId { get; set; }
    public int RevisionNo { get; set; }
    public Guid WorkCenterId { get; set; }
    public int? TargetBasis { get; set; }
    public decimal? TargetKg { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long PlanVersion { get; set; }
    public string RevisedBy { get; set; } = string.Empty;
    public DateTimeOffset RevisedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// MFG-PLAN-EDIT: one planned parent roll of a recorded revision, in the revision's own order, with the Inventory
/// label version accepted for it. The complete input set of the revision; append-only.
/// </summary>
internal sealed class PlanRevisionInputRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid PlanId { get; set; }
    public int RevisionNo { get; set; }
    public Guid ParentRollId { get; set; }
    public int Sequence { get; set; }
    public decimal PlannedKg { get; set; }
    public long ExpectedLabelVersion { get; set; }
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
    public long? ExpectedLabelVersion { get; set; }
}

/// <summary>
/// The append-only decision that justified one policy-backed order close. A missing row is valid only for a legacy
/// order closed before this runtime existed. Company/order come from the composite key; the optional approval fields
/// are all-or-none and repeat the exact order/policy versions they approved.
/// </summary>
internal sealed class OrderClosureDecisionRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public long OrderVersion { get; set; }
    public Guid PolicyId { get; set; }
    public long PolicyVersion { get; set; }
    public int Process { get; set; }
    public string MaximumFirePercent { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveThrough { get; set; }
    public DateOnly SelectedForDate { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public string? ApprovalReason { get; set; }
    public long? ApprovalOrderVersion { get; set; }
    public Guid? ApprovalPolicyId { get; set; }
    public long? ApprovalPolicyVersion { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
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

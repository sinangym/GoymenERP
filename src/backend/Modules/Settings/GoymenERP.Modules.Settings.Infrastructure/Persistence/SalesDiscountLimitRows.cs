namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// SAT-D63: a discount limit group of one company, separate from roles. Never deleted; <see cref="IsActive"/> false is
/// the passive state. <see cref="Code"/> is the stable key the SAT-D66 multi-company binding matches across companies.
/// <see cref="Version"/> is the optimistic command version (rename, deactivation and every limit record move it);
/// <see cref="LimitVersion"/> is the latest limit record's version (0 = none recorded).
/// </summary>
internal sealed class SalesDiscountLimitGroupRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public long Version { get; set; }
    public long LimitVersion { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// SAT-D36 / SAT-D62: one immutable limit record of a group — a fixed percent, or a linked group + extra points. A change
/// is a new record (never an update); <see cref="ProfileId"/> is the profile identity the Settings policy read returns.
/// </summary>
internal sealed class SalesDiscountLimitVersionRow
{
    public Guid CompanyId { get; set; }
    public Guid LimitGroupId { get; set; }
    public long Version { get; set; }
    public Guid ProfileId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public decimal? Percent { get; set; }
    public Guid? LinkedGroupId { get; set; }
    public decimal? ExtraPoints { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// SAT-D63 / SAT-D66: a person's binding in one company. The bound groups are the immutable
/// <see cref="SalesDiscountLimitMembershipGroupRow"/> rows of <see cref="Version"/>; a change writes a new version.
/// </summary>
internal sealed class SalesDiscountLimitMembershipRow
{
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>One group of one binding version (immutable; earlier versions stay as history).</summary>
internal sealed class SalesDiscountLimitMembershipGroupRow
{
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public long MembershipVersion { get; set; }
    public Guid LimitGroupId { get; set; }
}

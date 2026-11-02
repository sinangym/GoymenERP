namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// API-01: one Manufacturing parameter profile version (the header) of one company × process × optional work center. Its
/// lifecycle state moves Draft → Approved → Published (D-MFG-PROFILE-AUTH-01=A); a published row never changes (trigger).
/// </summary>
internal sealed class ManufacturingParameterProfileRow
{
    public Guid CompanyId { get; set; }
    public Guid ProfileId { get; set; }
    public string Process { get; set; } = string.Empty;
    public Guid? WorkCenterId { get; set; }
    public long ProfileVersion { get; set; }
    public long StateVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveThrough { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public DateTimeOffset PreparedAtUtc { get; set; }
    public string PreparationReason { get; set; } = string.Empty;
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? ApprovalReason { get; set; }
    public string? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string? PublicationReason { get; set; }
}

/// <summary>One typed parameter value of a profile version (insert-only; a new key needs no migration).</summary>
internal sealed class ManufacturingParameterProfileValueRow
{
    public Guid CompanyId { get; set; }
    public Guid ProfileId { get; set; }
    public string ParameterKey { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal? NumericValue { get; set; }
    public string? ChoiceValue { get; set; }
}

/// <summary>
/// API-02: one Manufacturing tolerance profile version in its lifecycle. Publishing writes the immutable
/// <see cref="ManufacturingTolerancePolicyRow"/> (same profile id and version) the tolerance provider reads.
/// </summary>
internal sealed class ManufacturingToleranceProfileVersionRow
{
    public Guid CompanyId { get; set; }
    public Guid ProfileId { get; set; }
    public string Process { get; set; } = string.Empty;
    public long ProfileVersion { get; set; }
    public long StateVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal MaximumFirePercent { get; set; }
    public decimal MassBalanceDifferenceTolerancePercent { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveThrough { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public DateTimeOffset PreparedAtUtc { get; set; }
    public string PreparationReason { get; set; } = string.Empty;
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? ApprovalReason { get; set; }
    public string? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string? PublicationReason { get; set; }
}

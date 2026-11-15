namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// One immutable segregation of duties version of a company (F-024, NS-01; insert-only). The latest version is the setting
/// in force; no row means the decided default (the preparer may not approve).
/// </summary>
internal sealed class SegregationOfDutiesPolicyVersionRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public bool PreparerMayApprove { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

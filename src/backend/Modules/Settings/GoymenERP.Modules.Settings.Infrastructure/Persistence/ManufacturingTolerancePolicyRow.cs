namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal sealed class ManufacturingTolerancePolicyRow
{
    public Guid CompanyId { get; set; }
    public Guid ProfileId { get; set; }
    public string Process { get; set; } = string.Empty;
    public long Version { get; set; }
    public decimal MaximumFirePercent { get; set; }
    public bool IsActive { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveThrough { get; set; }
    public string ApprovalStatus { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTimeOffset ApprovedAtUtc { get; set; }
    public string ApprovalReason { get; set; } = string.Empty;
}

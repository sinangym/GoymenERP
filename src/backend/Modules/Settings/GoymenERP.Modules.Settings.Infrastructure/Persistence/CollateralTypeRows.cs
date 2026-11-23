namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal sealed class CollateralTypeRow
{
    public Guid CompanyId { get; set; }
    public Guid EntryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

internal sealed class CollateralTypeVersionRow
{
    public Guid CompanyId { get; set; }
    public Guid EntryId { get; set; }
    public long Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }
    public string RatePercent { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

internal sealed record CollateralTypeSnapshot(CollateralTypeRow Entry, CollateralTypeVersionRow? Latest);

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>F029-S3: immutable full snapshot of the money profile for one company, currency and consuming operation.</summary>
internal sealed class MoneyProfileVersionRow
{
    public Guid CompanyId { get; set; }
    public Guid CurrencyId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public long Version { get; set; }
    public Guid ProfileId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public int Scale { get; set; }
    public int? UnitPriceScale { get; set; }
    public bool IsActive { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string? DecisionReference { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>The permanent source switch of one company/currency/operation; an empty stored timeline never restores a catalog fallback.</summary>
internal sealed class MoneyProfileBaselineTransferRow
{
    public Guid CompanyId { get; set; }
    public Guid CurrencyId { get; set; }
    public string Operation { get; set; } = string.Empty;
}

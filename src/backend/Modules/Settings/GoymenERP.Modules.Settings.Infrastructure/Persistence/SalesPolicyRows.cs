namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// F-029 S4b: the one-time marker that a company's policy family was transferred from the compiled catalog (insert-only).
/// While it is absent the catalog answers for the family; once it exists the version table is the family's only source.
/// </summary>
internal sealed class SalesPolicyBaselineTransferRow
{
    public Guid CompanyId { get; set; }
    public string PolicyFamily { get; set; } = string.Empty;
    public int EntryCount { get; set; }
    public DateTimeOffset TransferredAtUtc { get; set; }
    public string TransferredBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>One immutable delivery tolerance version of a company (SAT-D5; insert-only).</summary>
internal sealed class SalesDeliveryToleranceVersionRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid ProfileId { get; set; }
    public decimal WarnPercent { get; set; }
    public decimal ApprovalPercent { get; set; }
    public decimal BlockPercent { get; set; }
    public bool IsActive { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string? DecisionReference { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>One immutable order-approval amount threshold version of a company and currency (SAT-D6; insert-only).</summary>
internal sealed class SalesApprovalAmountThresholdVersionRow
{
    public Guid CompanyId { get; set; }
    public Guid CurrencyId { get; set; }
    public long Version { get; set; }
    public Guid ProfileId { get; set; }
    public bool IsEnabled { get; set; }
    public decimal? ThresholdAmount { get; set; }
    public byte? ThresholdAmountScale { get; set; }
    public bool IsActive { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string? DecisionReference { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

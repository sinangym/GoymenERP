namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// SAT-D139: the default-tax series of one item type in one company (the edit fence of its Settings records). Created with
/// its first record; until then only the reviewed catalog answers (version "0").
/// </summary>
internal sealed class TaxItemTypeDefaultSeriesRow
{
    public Guid CompanyId { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public long Version { get; set; }
    public long LastRecordVersion { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// One immutable default-tax record of an item type: VAT applies with a percent or not ("KDV yok", optional exemption
/// code); withholding (only with VAT) with a percent and an optional code, or not. A change is a new record.
/// </summary>
internal sealed class TaxItemTypeDefaultRecordRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public long RecordVersion { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public bool VatApplies { get; set; }
    public decimal? VatPercent { get; set; }
    public string? ExemptionCode { get; set; }
    public bool WithholdingApplies { get; set; }
    public decimal? WithholdingPercent { get; set; }
    public string? WithholdingCode { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>The withdrawal of a default record before it took effect (insert-only; the record row stays as written).</summary>
internal sealed class TaxItemTypeDefaultWithdrawalRow
{
    public Guid CompanyId { get; set; }
    public Guid RecordId { get; set; }
    public Guid? ReplacedByRecordId { get; set; }
    public DateTimeOffset WithdrawnAtUtc { get; set; }
    public string WithdrawnBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A code list of one company (the edit fence of its versions). Created with its first version.</summary>
internal sealed class TaxCodeListRow
{
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public long Version { get; set; }
    public long LastListVersion { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>One immutable, whole code list version; its entries are <see cref="TaxCodeListEntryRow"/>.</summary>
internal sealed class TaxCodeListVersionRow
{
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public long ListVersion { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

internal sealed class TaxCodeListEntryRow
{
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public long ListVersion { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>The withdrawal of a code list version before it took effect (insert-only).</summary>
internal sealed class TaxCodeListWithdrawalRow
{
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public long ListVersion { get; set; }
    public long? ReplacedByListVersion { get; set; }
    public DateTimeOffset WithdrawnAtUtc { get; set; }
    public string WithdrawnBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// The withholding amount threshold of one company (VUK 232; the edit fence of its records). Created with its first record.
/// </summary>
internal sealed class TaxWithholdingThresholdRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public long LastRecordVersion { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>One immutable threshold record: an amount above 0 in an entered ISO 4217 currency, from its business date.</summary>
internal sealed class TaxWithholdingThresholdRecordRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public long RecordVersion { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>The withdrawal of a threshold record before it took effect (insert-only).</summary>
internal sealed class TaxWithholdingThresholdWithdrawalRow
{
    public Guid CompanyId { get; set; }
    public Guid RecordId { get; set; }
    public Guid? ReplacedByRecordId { get; set; }
    public DateTimeOffset WithdrawnAtUtc { get; set; }
    public string WithdrawnBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

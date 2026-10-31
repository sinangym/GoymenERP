namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// MOD-03 (SAT-D74/D82): a tax category of one company — a VAT class, a withholding class or a buyer category. Never deleted.
/// <see cref="Version"/> is the optimistic command version (a rename and every rate record of the class move it);
/// <see cref="RecordVersion"/> is the latest rate record's version of the class (0 = none).
/// </summary>
internal sealed class TaxCategoryRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Version { get; set; }
    public long RecordVersion { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// One immutable rate record: a VAT class's rate, or a withholding class's withholding for one buyer category (applying with
/// a percent and a UBL-TR code, or explicitly not applied). A change is a new record, never an update.
/// </summary>
internal sealed class TaxRateRecordRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid ClassId { get; set; }
    public Guid? BuyerCategoryId { get; set; }
    public long RecordVersion { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public bool Applies { get; set; }
    public decimal? RatePercent { get; set; }
    public string? WithholdingCode { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>The withdrawal of a rate record before it took effect (insert-only; the record row stays as written).</summary>
internal sealed class TaxRateRecordWithdrawalRow
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

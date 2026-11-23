namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// One immutable version of one company policy key (B-13, ADR-SET-002; insert-only). The latest version of a key is the value
/// in force; no row means "not recorded", which each key answers on its own (never as a permissive value). Only the columns
/// of the key's shape are filled; the check constraint refuses any other combination.
/// </summary>
internal sealed class CompanyPolicyVersionRow
{
    public Guid CompanyId { get; set; }
    public string PolicyKey { get; set; } = string.Empty;
    public long Version { get; set; }

    /// <summary><c>finance.opening-period</c>: open (true) or closed.</summary>
    public bool? BooleanValue { get; set; }

    /// <summary><c>manufacturing.due-date-buffer</c>: working days (≥ 0).</summary>
    public int? WholeNumberValue { get; set; }

    /// <summary>Separate sales.contract-discount-limit percentage; null for every other key.</summary>
    public decimal? PercentValue { get; set; }

    public bool? VatApplies { get; set; }
    public decimal? VatPercent { get; set; }
    public string? ExemptionCode { get; set; }
    public bool? WithholdingApplies { get; set; }
    public decimal? WithholdingPercent { get; set; }
    public string? WithholdingCode { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// FNS-D97: one immutable credit account mapping version of a company and purpose (insert-only). No write path exists yet,
/// so the table starts empty and every purpose reads as "eşleme eksik".
/// </summary>
internal sealed class FinanceCreditAccountMappingVersionRow
{
    public Guid CompanyId { get; set; }
    public int Purpose { get; set; }
    public long Version { get; set; }
    public string LedgerAccountCode { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

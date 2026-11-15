namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>One immutable loss reason list version of a company (SAT-D25; insert-only). Its entries are its children.</summary>
internal sealed class SalesQuoteLossReasonListVersionRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid ProfileId { get; set; }
    public bool IsActive { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string? DecisionReference { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>One entry of a stored loss reason list version, at its position in the list (insert-only).</summary>
internal sealed class SalesQuoteLossReasonEntryRow
{
    public Guid CompanyId { get; set; }
    public long ListVersion { get; set; }
    public int Position { get; set; }
    public Guid ReasonId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool RequiresExplanation { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>One immutable default quote validity version of a company (SAT-D39; insert-only).</summary>
internal sealed class SalesQuoteDefaultValidityVersionRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid ProfileId { get; set; }
    public int ValidityDays { get; set; }
    public bool IsActive { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string? DecisionReference { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>One immutable e-Fatura scenario code list version of a company (SAT-D102; insert-only). Its codes are its children.</summary>
internal sealed class EInvoiceScenarioCodeListVersionRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid ProfileId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string? DecisionReference { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>One code of a stored e-Fatura scenario code list version, at its position in the list (insert-only).</summary>
internal sealed class EInvoiceScenarioCodeRow
{
    public Guid CompanyId { get; set; }
    public long ListVersion { get; set; }
    public int Position { get; set; }
    public string Code { get; set; } = string.Empty;
}

/// <summary>One immutable default e-Fatura scenario version of a company (SAT-D102; insert-only).</summary>
internal sealed class EInvoiceScenarioDefaultVersionRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid ProfileId { get; set; }
    public string ScenarioCode { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string? DecisionReference { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

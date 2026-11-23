namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>WP12 / FNS-D13: one statement import of one Finance bank account. A control record: zero money effect.</summary>
internal sealed class FinanceBankStatementImportRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid BankAccountId { get; set; }
    public int Format { get; set; }
    public string? BankKey { get; set; }
    public int State { get; set; }
    public Guid? DocumentId { get; set; }
    public int? DocumentVersion { get; set; }
    public string? DocumentSha256 { get; set; }
    public Guid? CsvMappingProfileId { get; set; }
    public long? CsvMappingProfileVersion { get; set; }
    public int LineCount { get; set; }
    public int DuplicateLineCount { get; set; }
    public DateOnly? FirstTransactionOn { get; set; }
    public DateOnly? LastTransactionOn { get; set; }
    public string ReservedBy { get; set; } = string.Empty;
    public DateTimeOffset ReservedAtUtc { get; set; }
    public string? ParsedBy { get; set; }
    public DateTimeOffset? ParsedAtUtc { get; set; }
}

/// <summary>
/// One statement line, stored once per bank account: (<c>BankAccountId</c>, <c>Fingerprint</c>, <c>Occurrence</c>) is
/// unique, so a line read again from an overlapping or repeated file is counted as a duplicate and not stored twice.
/// Amounts are exact non-negative decimal text; the direction carries the sign. A line never creates a movement (FNS-D13).
/// </summary>
internal sealed class FinanceBankStatementLineRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ImportId { get; set; }
    public Guid BankAccountId { get; set; }
    public int LineNumber { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public int Occurrence { get; set; }
    public DateOnly TransactionOn { get; set; }
    public DateOnly? ValueOn { get; set; }
    public int Direction { get; set; }
    public string AmountText { get; set; } = string.Empty;
    public string? BalanceText { get; set; }
    public string? CurrencyText { get; set; }
    public string? Reference { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? CounterpartyIban { get; set; }
}

internal sealed class FinanceBankStatementCommandRow : IFinanceTenantRow
{
    public Guid CompanyId { get; set; }
    public string KeyHash { get; set; } = string.Empty;
    public string CommandName { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public Guid ImportId { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>
/// FNS-D113: a person's confirmation that a statement line is one recorded movement. Immutable except the single step that
/// links its reversal record (<c>ReversalId</c>, version 1 → 2). One active match per line and per record (filtered unique
/// indexes). No deletion.
/// </summary>
internal sealed class FinanceBankStatementMatchRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid ImportId { get; set; }
    public Guid LineId { get; set; }
    public Guid BankAccountId { get; set; }
    public int CandidateKind { get; set; }
    public Guid RecordId { get; set; }
    public int CounterpartyCheck { get; set; }
    public string ConfirmedBy { get; set; } = string.Empty;
    public DateTimeOffset ConfirmedAtUtc { get; set; }
    public Guid? ReversalId { get; set; }
}

/// <summary>The reversal record ("ters kayıt") of a confirmed match. Append-only.</summary>
internal sealed class FinanceBankStatementMatchReversalRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid MatchId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ReversedBy { get; set; } = string.Empty;
    public DateTimeOffset ReversedAtUtc { get; set; }
}

internal static class FinanceBankStatementImportStates
{
    public const int Reserved = 1;
    public const int Parsed = 2;
}

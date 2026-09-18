namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>A row of the server-resolved company (ADR-002): its company is set once and never changes.</summary>
internal interface IAccountingTenantRow
{
    Guid CompanyId { get; }
}

/// <summary>
/// One row per company whose version every period opening bumps, so two openings that read the
/// same calendar cannot both commit: the race guard of "no overlapping periods".
/// </summary>
internal sealed class PeriodCalendarRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// A stored <see cref="Domain.Periods.AccountingPeriod"/>. Every posting into the period and its
/// close bump <see cref="Version"/>, so a close racing a posting cannot both commit.
/// </summary>
internal sealed class PeriodRow : IAccountingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public int Status { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// A stored <see cref="Domain.Journals.Journal"/>. Append-only: a mistake is answered by a new
/// reversal or correction journal, never by editing this row. The filtered unique indexes hold R4
/// under a race: one original per fact, one reversal per journal, one journal per correction id.
/// </summary>
internal sealed class JournalRow : IAccountingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public int Kind { get; set; }
    public Guid SourceId { get; set; }
    public string PostingPurpose { get; set; } = string.Empty;
    public string PostingRuleVersion { get; set; } = string.Empty;
    public Guid CurrencyId { get; set; }
    public DateOnly TransactionDate { get; set; }
    public DateOnly AccountingDate { get; set; }
    public DateOnly? ValueDate { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public Guid? LinkedJournalId { get; set; }
    public Guid? CorrectionId { get; set; }
    public Guid PeriodId { get; set; }
    public DateTimeOffset StoredAtUtc { get; set; }
    public string StoredBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>One line of a stored journal: an account of the journal's company and one positive side. Append-only.</summary>
internal sealed class JournalLineRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid JournalId { get; set; }
    public int LineNumber { get; set; }
    public Guid AccountId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

/// <summary>
/// One accepted delivery (R4 inbox): (company, consumer, message id) is its key, so a message is
/// processed at most once per company. It keeps what the first delivery answered, so a replay
/// returns it. Append-only.
/// </summary>
internal sealed class DeliveryRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public string Consumer { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;

    /// <summary>The canonical content hash of the message (ACC-4). Null only for a delivery stored before ACC-4.</summary>
    public string? RequestHash { get; set; }

    public int OutcomeKind { get; set; }
    public Guid? JournalId { get; set; }
    public string? QueueEntryJson { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// The exception-queue entry of one fact (source, purpose): open while the latest delivery had a
/// missing account mapping, resolved once a delivery closes that gap. Never deleted.
/// </summary>
internal sealed class PostingExceptionRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid SourceId { get; set; }
    public string PostingPurpose { get; set; } = string.Empty;
    public int Status { get; set; }
    public string PostingRuleVersion { get; set; } = string.Empty;
    public string MissingMappingKeysJson { get; set; } = "[]";
    public string FirstMessageId { get; set; } = string.Empty;
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// The pending-valuation entry of one fact: open while the latest delivery had an unknown amount,
/// resolved once one closes that gap. Never deleted; an unknown amount is never stored as zero.
/// </summary>
internal sealed class PendingValuationRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid SourceId { get; set; }
    public string PostingPurpose { get; set; } = string.Empty;
    public int Status { get; set; }
    public string PostingRuleVersion { get; set; } = string.Empty;
    public int LinesAwaitingAmount { get; set; }
    public string FirstMessageId { get; set; } = string.Empty;
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>Organization's company, referenced by identity only (the table is Organization's, not migrated here).</summary>
internal sealed class OrganizationCompanyReference
{
    public Guid Id { get; set; }
}

using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>
/// Who asked, under which permission and in which conversation: the ADR-009 audit envelope of one
/// write. Every value comes from the caller's server-side context (a later command surface); this
/// module never makes one up. The company is not here: it is always the server-resolved company
/// context, and a record of any other company is refused.
/// </summary>
public sealed record AccountingCommandMetadata(
    string ActorId,
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string SourceChannel,
    string? ClientIpAddress = null,
    string? UserAgent = null,
    Func<object, AccountingCommandRecord>? Completion = null);

/// <summary>
/// One completed HTTP command attempt (ADR-011): unique per (company, command name, key hash). The key
/// is kept only as the uppercase SHA-256 of the trimmed <c>Idempotency-Key</c>; the request hash is the
/// canonical payload's, the actor is stored beside it, and the result is the answer to replay (the
/// Manufacturing command log's shape).
/// </summary>
public sealed record AccountingCommandRecord(
    string CommandName,
    string IdempotencyKeyHash,
    string ActorId,
    string RequestHash,
    string ResultJson,
    string OriginalCorrelationId);

/// <summary>
/// The Accounting command log of the server-resolved company (ADR-011 §2–§8). A successful write adds its
/// record in the same save as the business change and its audit event, through
/// <see cref="AccountingCommandMetadata.Completion"/>; a terminal refusal is recorded on its own.
/// Records are append-only.
/// </summary>
public interface IAccountingCommandLog
{
    Task<AccountingCommandRecord?> FindCommandAsync(string commandName, string idempotencyKeyHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a record without a business change. Another writer that stored the same (command, key)
    /// first makes this <see cref="AccountingStoreCodes.ConcurrentChange"/>.
    /// </summary>
    Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken cancellationToken = default);
}

/// <summary>A stored period with its concurrency version (bumped by its approval, its close and every posting into it).</summary>
public sealed record AccountingPeriodRecord(AccountingPeriod Period, long Version);

/// <summary>A stored journal with the period it was posted into and when it was stored.</summary>
public sealed record AccountingJournalRecord(Journal Journal, Guid PeriodId, DateTimeOffset StoredAtUtc);

/// <summary>One page of a keyset list; <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record AccountingPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>
/// The caller named a version that is not the stored one (ADR-011 §7: a terminal refusal of the attempt).
/// Nothing was stored.
/// </summary>
public sealed class AccountingVersionConflictException(long expectedVersion, long currentVersion)
    : Exception("The period changed since it was read.")
{
    public long ExpectedVersion { get; } = expectedVersion;

    public long CurrentVersion { get; } = currentVersion;
}

/// <summary>What a period write did: a new record, or the same request again with no second effect.</summary>
public enum AccountingWriteOutcome
{
    Recorded = 1,
    Replayed = 2,
}

/// <summary>The stable refusal codes of the Accounting store, beside the domain's own rule codes.</summary>
public static class AccountingStoreCodes
{
    /// <summary>The record's company is not the server-resolved company context (or there is none).</summary>
    public const string CompanyContextMismatch = "accounting_company_context_mismatch";

    /// <summary>No period of this company has that id.</summary>
    public const string PeriodNotFound = "accounting_period_not_found";

    /// <summary>The same period id was offered again with other dates.</summary>
    public const string PeriodConflict = "accounting_period_conflict";

    /// <summary>The new period overlaps a stored period of this company.</summary>
    public const string PeriodOverlap = "accounting_period_overlap";

    /// <summary>A journal of this company already has the proposed journal id.</summary>
    public const string JournalIdTaken = "accounting_journal_id_taken";

    /// <summary>
    /// Another write changed the same period, fact or queue entry first, or took the same unique
    /// identity. Nothing of this write was stored; the caller may retry the same request, which
    /// then answers from what the winner stored.
    /// </summary>
    public const string ConcurrentChange = "accounting_concurrent_change";

    /// <summary>A text or amount does not fit its stored column; it is refused, never cut or rounded.</summary>
    public const string ValueDoesNotFit = "accounting_value_does_not_fit";

    /// <summary>
    /// A stored delivery has the same (consumer, message id) but other content. A message is
    /// immutable: its first answer is not given to other content, and nothing is stored.
    /// </summary>
    /// <summary>The same period id was proposed again by another preparer (R3: the same id is the same proposal).</summary>
    public const string PeriodPreparerConflict = "accounting_period_preparer_conflict";

    /// <summary>A list cursor that this company's list did not issue, or a page size outside 1…100.</summary>
    public const string ListRequestInvalid = "accounting_list_request_invalid";

    public const string DeliveryContentConflict = "accounting_delivery_content_conflict";
}

/// <summary>A stored accounting fact could not be written; <see cref="Code"/> says why. Nothing was stored.</summary>
public sealed class AccountingStoreException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// The persistent periods of the server-resolved company (Ö5: posting only into an open period; Ö4: an
/// opening is proposed by one person and approved by ANOTHER). There is no direct "open".
/// </summary>
public interface IAccountingPeriodStore
{
    /// <summary>
    /// Stores a new PROPOSED period. The same id with the same dates and preparer is a replay; with other
    /// dates, another preparer, or overlapping another period of the company (proposed, open or closed),
    /// it is refused.
    /// </summary>
    Task<(AccountingPeriodRecord Record, AccountingWriteOutcome Outcome)> ProposePeriodAsync(Guid periodId, DateOnly startsOn,
        DateOnly endsOn, Guid preparedByUserId, AccountingCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a proposed period at the version the approver read (Ö4: never by its preparer); it is then
    /// open for posting.
    /// </summary>
    Task<AccountingPeriodRecord> ApprovePeriodAsync(Guid periodId, long expectedVersion, Guid approvedByUserId,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes an open period at the version the closer read and records who closed it and when. A second
    /// close is refused by the domain rule; a close racing a posting into the same period cannot both commit.
    /// </summary>
    Task<AccountingPeriodRecord> ClosePeriodAsync(Guid periodId, long expectedVersion, Guid closedByUserId,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>The company's stored period, or null (another company's period is null too).</summary>
    Task<AccountingPeriodRecord?> FindPeriodAsync(Guid periodId, CancellationToken cancellationToken = default);

    /// <summary>The company's periods, newest first (by start date, then id), one page at a time.</summary>
    Task<AccountingPage<AccountingPeriodRecord>> ListPeriodsAsync(int pageSize, string? cursor,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The persistent source-posting ledger of the server-resolved company. Each call is one save:
/// the journal, the queue entries, the delivery record and the audit event commit together or not
/// at all. The outcomes are exactly the domain ledger's.
/// </summary>
public interface IAccountingPostingStore
{
    Task<PostingOutcome> ReceiveAsync(PostingRequest request, Guid periodId, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<PostingOutcome> ReverseAsync(string consumer, string messageId, Guid reversedJournalId, Guid reversalJournalId,
        PostingDates dates, Guid periodId, AccountingCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<PostingOutcome> CorrectAsync(string consumer, string messageId, Guid correctionId, Guid correctedJournalId,
        Guid correctionJournalId, string postingRuleVersion, PostingDates dates, IEnumerable<JournalLine> lines,
        Guid periodId, AccountingCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>The company's stored journal, or null (another company's journal is null too).</summary>
    Task<Journal?> FindJournalAsync(Guid journalId, CancellationToken cancellationToken = default);

    /// <summary>The company's stored journal with its period and storage time, or null (another company's is null too).</summary>
    Task<AccountingJournalRecord?> ReadJournalAsync(Guid journalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The company's journals, newest accounting date first (then storage time, then id), optionally of
    /// one period, one page at a time.
    /// </summary>
    Task<AccountingPage<AccountingJournalRecord>> ListJournalsAsync(Guid? periodId, int pageSize, string? cursor,
        CancellationToken cancellationToken = default);

    /// <summary>The company's open exception entries.</summary>
    Task<IReadOnlyList<PostingException>> ReadOpenExceptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>The company's open pending-valuation entries.</summary>
    Task<IReadOnlyList<PendingValuation>> ReadPendingValuationsAsync(CancellationToken cancellationToken = default);
}

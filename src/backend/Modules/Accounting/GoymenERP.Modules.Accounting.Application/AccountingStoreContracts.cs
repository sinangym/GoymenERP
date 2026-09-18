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
    string? UserAgent = null);

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
}

/// <summary>A stored accounting fact could not be written; <see cref="Code"/> says why. Nothing was stored.</summary>
public sealed class AccountingStoreException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>The persistent periods of the server-resolved company (Ö5: posting only into an open period).</summary>
public interface IAccountingPeriodStore
{
    /// <summary>
    /// Stores a new open period. The same id with the same dates is a replay; with other dates, or
    /// overlapping another period of the company, it is refused.
    /// </summary>
    Task<AccountingWriteOutcome> OpenPeriodAsync(Guid periodId, DateOnly startsOn, DateOnly endsOn,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a stored period and records who closed it and when. A second close is refused by the
    /// domain rule; a close racing a posting into the same period cannot both commit.
    /// </summary>
    Task ClosePeriodAsync(Guid periodId, Guid closedByUserId, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>The company's stored period, or null (another company's period is null too).</summary>
    Task<AccountingPeriod?> FindPeriodAsync(Guid periodId, CancellationToken cancellationToken = default);
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

    /// <summary>The company's open exception entries.</summary>
    Task<IReadOnlyList<PostingException>> ReadOpenExceptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>The company's open pending-valuation entries.</summary>
    Task<IReadOnlyList<PendingValuation>> ReadPendingValuationsAsync(CancellationToken cancellationToken = default);
}

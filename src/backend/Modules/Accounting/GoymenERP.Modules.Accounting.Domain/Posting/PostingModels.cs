using GoymenERP.Modules.Accounting.Domain.Journals;

namespace GoymenERP.Modules.Accounting.Domain.Posting;

public enum PostingSide
{
    Debit = 1,
    Credit = 2,
}

/// <summary>
/// A line as a posting rule proposes it, before it is a journal line. <see cref="Account"/> is
/// null when the rule found no mapping for <see cref="MappingKey"/>; <see cref="Amount"/> is null
/// when the value is not known yet (R6: an unknown amount is not zero).
/// </summary>
public sealed record ProposedLine(string MappingKey, AccountRef? Account, PostingSide Side, decimal? Amount);

/// <summary>
/// One delivery of a source posting. <see cref="MessageId"/> identifies the delivery (inbox);
/// <see cref="Source"/> identifies the business fact. They are checked separately (R4).
/// </summary>
public sealed record PostingRequest(
    string Consumer,
    string MessageId,
    Guid JournalId,
    SourceReference Source,
    string PostingRuleVersion,
    Guid CurrencyId,
    PostingDates Dates,
    IReadOnlyList<ProposedLine> Lines);

/// <summary>
/// An exception-queue item: the source could not be posted because a line had no account
/// mapping. Nothing is posted to a guessed or fallback account.
/// </summary>
public sealed record PostingException(
    SourceReference Source,
    string PostingRuleVersion,
    IReadOnlyList<string> MissingMappingKeys,
    string FirstMessageId);

/// <summary>
/// A visible gap: the source is known but its amount is not valued yet, so no journal exists and
/// no zero is recorded in its place.
/// </summary>
public sealed record PendingValuation(
    SourceReference Source,
    string PostingRuleVersion,
    int LinesAwaitingAmount,
    string FirstMessageId);

public enum PostingOutcomeKind
{
    Posted = 1,
    DuplicateMessage = 2,
    AlreadyPosted = 3,
    ExceptionQueued = 4,
    WaitingValuation = 5,
    AlreadyReversed = 6,
}

public sealed record PostingOutcome(
    PostingOutcomeKind Kind,
    Journal? Journal = null,
    PostingException? Exception = null,
    PendingValuation? Pending = null);

/// <summary>
/// A stored correction: the correction identity (the producer's decision id) and the journal it
/// produced. Used only to rebuild a ledger from stored facts.
/// </summary>
public sealed record StoredCorrection(Guid CorrectionId, Guid JournalId);

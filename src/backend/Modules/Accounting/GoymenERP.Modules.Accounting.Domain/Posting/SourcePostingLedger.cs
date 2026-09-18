using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;

namespace GoymenERP.Modules.Accounting.Domain.Posting;

/// <summary>
/// One company's source postings (plan §7.6, R4, R6, §13). It keeps two identities apart:
/// <list type="bullet">
/// <item>the delivery — (consumer, messageId): a redelivered message has no new effect;</item>
/// <item>the business fact — (company, sourceId, postingPurpose): at most one ORIGINAL journal,
/// whatever message or posting rule version brings it.</item>
/// </list>
/// A reversal or correction is a new journal linked to the one it answers; nothing is edited or
/// deleted. A missing account mapping goes to the exception queue and an unknown amount to the
/// pending-valuation list; neither becomes a journal with a guessed account or a zero amount.
/// <para>
/// A failed posting (unbalanced, closed period, company mismatch) throws and records nothing, not
/// even the delivery: effect and inbox marker commit together or not at all.
/// </para>
/// <para>
/// In-memory aggregate. Persistence, concurrency and the real inbox table belong to the central
/// storage work this slice waits on.
/// </para>
/// </summary>
public sealed class SourcePostingLedger
{
    private readonly Dictionary<(string Consumer, string MessageId), PostingOutcomeKind> _inbox = [];
    private readonly Dictionary<(Guid SourceId, string Purpose), Journal> _originals = [];
    private readonly Dictionary<(Guid SourceId, string Purpose), PostingException> _exceptions = [];
    private readonly Dictionary<(Guid SourceId, string Purpose), PendingValuation> _pending = [];
    private readonly Dictionary<Guid, Journal> _journals = [];
    private readonly Dictionary<Guid, Journal> _reversalsByOriginal = [];

    public SourcePostingLedger(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference, "A ledger belongs to one company.");
        }

        CompanyId = companyId;
    }

    public Guid CompanyId { get; }

    public IReadOnlyCollection<Journal> Journals => _journals.Values;

    public IReadOnlyCollection<PostingException> OpenExceptions => _exceptions.Values;

    public IReadOnlyCollection<PendingValuation> PendingValuations => _pending.Values;

    public Journal? FindOriginal(Guid sourceId, string postingPurpose) =>
        _originals.GetValueOrDefault((sourceId, postingPurpose));

    public Journal? FindReversalOf(Guid journalId) => _reversalsByOriginal.GetValueOrDefault(journalId);

    /// <summary>Accepts one delivery of a source posting.</summary>
    public PostingOutcome Receive(PostingRequest request, AccountingPeriod period)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(period);
        RequireOwnCompany(request.Source.CompanyId);
        var delivery = Delivery(request.Consumer, request.MessageId);
        if (_inbox.ContainsKey(delivery))
        {
            return new PostingOutcome(PostingOutcomeKind.DuplicateMessage);
        }

        var key = (request.Source.SourceId, request.Source.PostingPurpose);
        if (_originals.TryGetValue(key, out var existing))
        {
            // Another message, or a newer rule version, for a fact already posted: no second original.
            _inbox[delivery] = PostingOutcomeKind.AlreadyPosted;
            return new PostingOutcome(PostingOutcomeKind.AlreadyPosted, Journal: existing);
        }

        var missing = request.Lines
            .Where(line => line.Account is null)
            .Select(line => line.MappingKey)
            .ToArray();
        if (missing.Length > 0)
        {
            var queued = _exceptions.TryGetValue(key, out var open)
                ? open with { PostingRuleVersion = request.PostingRuleVersion, MissingMappingKeys = missing }
                : new PostingException(request.Source, request.PostingRuleVersion, missing, request.MessageId);
            _exceptions[key] = queued;
            _inbox[delivery] = PostingOutcomeKind.ExceptionQueued;
            return new PostingOutcome(PostingOutcomeKind.ExceptionQueued, Exception: queued);
        }

        var unvalued = request.Lines.Count(line => line.Amount is null);
        if (unvalued > 0)
        {
            var pending = _pending.TryGetValue(key, out var waiting)
                ? waiting with { PostingRuleVersion = request.PostingRuleVersion, LinesAwaitingAmount = unvalued }
                : new PendingValuation(request.Source, request.PostingRuleVersion, unvalued, request.MessageId);
            _pending[key] = pending;
            _inbox[delivery] = PostingOutcomeKind.WaitingValuation;
            return new PostingOutcome(PostingOutcomeKind.WaitingValuation, Pending: pending);
        }

        var lines = request.Lines.Select(line => line.Side == PostingSide.Debit
            ? new JournalLine(line.Account!.Value, line.Amount!.Value, 0m)
            : new JournalLine(line.Account!.Value, 0m, line.Amount!.Value));
        var journal = Journal.Post(
            request.JournalId,
            JournalKind.Original,
            request.Source,
            request.PostingRuleVersion,
            request.CurrencyId,
            request.Dates,
            lines,
            period,
            linkedJournalId: null);

        RequireNewJournalId(journal.Id);
        _originals[key] = journal;
        _journals[journal.Id] = journal;
        _exceptions.Remove(key);
        _pending.Remove(key);
        _inbox[delivery] = PostingOutcomeKind.Posted;
        return new PostingOutcome(PostingOutcomeKind.Posted, Journal: journal);
    }

    /// <summary>
    /// Reverses a posted journal with a NEW journal that mirrors its lines and links to it. A
    /// journal is reversed at most once, so a replayed or repeated request cannot reverse twice.
    /// A reversal is not itself reversed: re-establishing the effect is a correction.
    /// </summary>
    public PostingOutcome Reverse(
        string consumer,
        string messageId,
        Guid reversedJournalId,
        Guid reversalJournalId,
        PostingDates dates,
        AccountingPeriod period)
    {
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(period);
        var delivery = Delivery(consumer, messageId);
        if (_inbox.ContainsKey(delivery))
        {
            return new PostingOutcome(PostingOutcomeKind.DuplicateMessage);
        }

        var target = RequireJournal(reversedJournalId);
        if (target.Kind == JournalKind.Reversal)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.ReversalOfReversal,
                "A reversal is not reversed; post a correction instead.");
        }

        if (_reversalsByOriginal.TryGetValue(target.Id, out var earlier))
        {
            _inbox[delivery] = PostingOutcomeKind.AlreadyReversed;
            return new PostingOutcome(PostingOutcomeKind.AlreadyReversed, Journal: earlier);
        }

        var reversal = Journal.Post(
            reversalJournalId,
            JournalKind.Reversal,
            target.Source,
            target.PostingRuleVersion,
            target.CurrencyId,
            dates,
            target.MirroredLines(),
            period,
            linkedJournalId: target.Id);

        RequireNewJournalId(reversal.Id);
        _journals[reversal.Id] = reversal;
        _reversalsByOriginal[target.Id] = reversal;
        _inbox[delivery] = PostingOutcomeKind.Posted;
        return new PostingOutcome(PostingOutcomeKind.Posted, Journal: reversal);
    }

    /// <summary>
    /// Posts a correction as a NEW balanced journal linked to the journal it corrects. The
    /// corrected journal stays as it is, even when its period is closed; the correction itself
    /// must land in an open period. It is not an original, so it does not compete for the
    /// source's single original.
    /// </summary>
    public PostingOutcome Correct(
        string consumer,
        string messageId,
        Guid correctedJournalId,
        Guid correctionJournalId,
        string postingRuleVersion,
        PostingDates dates,
        IEnumerable<JournalLine> lines,
        AccountingPeriod period)
    {
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(period);
        var delivery = Delivery(consumer, messageId);
        if (_inbox.ContainsKey(delivery))
        {
            return new PostingOutcome(PostingOutcomeKind.DuplicateMessage);
        }

        var target = RequireJournal(correctedJournalId);
        var correction = Journal.Post(
            correctionJournalId,
            JournalKind.Correction,
            target.Source,
            postingRuleVersion,
            target.CurrencyId,
            dates,
            lines,
            period,
            linkedJournalId: target.Id);

        RequireNewJournalId(correction.Id);
        _journals[correction.Id] = correction;
        _inbox[delivery] = PostingOutcomeKind.Posted;
        return new PostingOutcome(PostingOutcomeKind.Posted, Journal: correction);
    }

    private static (string, string) Delivery(string consumer, string messageId)
    {
        if (string.IsNullOrWhiteSpace(consumer) || string.IsNullOrWhiteSpace(messageId))
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference, "A delivery needs a consumer and a message id.");
        }

        return (consumer, messageId);
    }

    private void RequireOwnCompany(Guid companyId)
    {
        if (companyId != CompanyId)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.CompanyMismatch, "The source belongs to another company.");
        }
    }

    private Journal RequireJournal(Guid journalId) =>
        _journals.TryGetValue(journalId, out var journal)
            ? journal
            : throw new AccountingDomainException(
                AccountingErrorCodes.JournalNotFound, "No such journal in this company's ledger.");

    private void RequireNewJournalId(Guid journalId)
    {
        if (_journals.ContainsKey(journalId))
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference, "A journal id is used once.");
        }
    }
}

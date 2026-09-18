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
    private readonly Dictionary<(string Consumer, string MessageId), PostingOutcome> _inbox = [];
    private readonly Dictionary<(Guid SourceId, string Purpose), Journal> _originals = [];
    private readonly Dictionary<(Guid SourceId, string Purpose), PostingException> _exceptions = [];
    private readonly Dictionary<(Guid SourceId, string Purpose), PendingValuation> _pending = [];
    private readonly Dictionary<Guid, Journal> _journals = [];
    private readonly Dictionary<Guid, Journal> _reversalsByOriginal = [];
    private readonly Dictionary<Guid, Journal> _corrections = [];

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
        if (request.Lines is null || request.Lines.Any(line => line is null))
        {
            throw new ArgumentException("A posting request needs its proposed lines.", nameof(request));
        }

        var delivery = Delivery(request.Consumer, request.MessageId);
        if (_inbox.TryGetValue(delivery, out var recorded))
        {
            return Replayed(recorded);
        }

        var key = (request.Source.SourceId, request.Source.PostingPurpose);
        if (_originals.TryGetValue(key, out var existing))
        {
            // Another message, or a newer rule version, for a fact already posted: no second original.
            return Record(delivery, new PostingOutcome(PostingOutcomeKind.AlreadyPosted, Journal: existing));
        }

        // Each queue reflects the LATEST delivery of the fact: an exception entry while some line
        // has no mapping, a pending entry while some amount is unknown. A delivery with both gaps
        // keeps both entries current; one that closes a gap removes that entry.
        var missing = request.Lines
            .Where(line => line.Account is null)
            .Select(line => line.MappingKey)
            .ToArray();
        var unvalued = request.Lines.Count(line => line.Amount is null);
        if (missing.Length > 0)
        {
            var keys = Array.AsReadOnly(missing);
            var queued = _exceptions.TryGetValue(key, out var open)
                ? open with { PostingRuleVersion = request.PostingRuleVersion, MissingMappingKeys = keys }
                : new PostingException(request.Source, request.PostingRuleVersion, keys, request.MessageId);
            _exceptions[key] = queued;
            SyncPending(key, request, unvalued);
            return Record(delivery, new PostingOutcome(PostingOutcomeKind.ExceptionQueued, Exception: queued));
        }

        if (unvalued > 0)
        {
            var pending = SyncPending(key, request, unvalued);
            // Every line of this delivery is mapped, so an earlier missing-mapping entry is stale.
            _exceptions.Remove(key);
            return Record(delivery, new PostingOutcome(PostingOutcomeKind.WaitingValuation, Pending: pending));
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
        return Record(delivery, new PostingOutcome(PostingOutcomeKind.Posted, Journal: journal));
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
        if (_inbox.TryGetValue(delivery, out var recorded))
        {
            return Replayed(recorded);
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
            return Record(delivery, new PostingOutcome(PostingOutcomeKind.AlreadyReversed, Journal: earlier));
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
        return Record(delivery, new PostingOutcome(PostingOutcomeKind.Posted, Journal: reversal));
    }

    /// <summary>
    /// Posts a correction as a NEW balanced journal linked to the journal it corrects. The
    /// corrected journal stays as it is, even when its period is closed; the correction itself
    /// must land in an open period. It is not an original, so it does not compete for the
    /// source's single original.
    /// <para>
    /// <paramref name="correctionId"/> is the correction's business identity — the correction
    /// decision itself, not its delivery. The same correction delivered again under another
    /// message id answers <see cref="PostingOutcomeKind.AlreadyPosted"/> with the journal it
    /// already produced; a different correction of the same journal gets its own id and posts.
    /// Reusing a correction id for a different journal is refused, and so is the same id with
    /// different content: other lines (compared as a set of account, debit and credit) or other
    /// business dates (transaction, accounting, value). The recording instant, the posting rule
    /// version and the journal id a redelivery proposes are delivery details, not content.
    /// </para>
    /// </summary>
    public PostingOutcome Correct(
        string consumer,
        string messageId,
        Guid correctionId,
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
        var correctionLines = lines.ToArray();
        if (correctionLines.Any(line => line is null))
        {
            throw new ArgumentException("A journal line cannot be null.", nameof(lines));
        }

        var delivery = Delivery(consumer, messageId);
        if (_inbox.TryGetValue(delivery, out var recorded))
        {
            return Replayed(recorded);
        }

        if (correctionId == Guid.Empty)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference, "A correction needs its own business identity.");
        }

        var target = RequireJournal(correctedJournalId);
        if (_corrections.TryGetValue(correctionId, out var earlier))
        {
            if (earlier.LinkedJournalId != target.Id)
            {
                throw new AccountingDomainException(
                    AccountingErrorCodes.CorrectionIdentityConflict,
                    "This correction id already corrected another journal.");
            }

            if (!SameContent(earlier, correctionLines, dates))
            {
                throw new AccountingDomainException(
                    AccountingErrorCodes.CorrectionContentConflict,
                    "This correction id already carries other lines or dates.");
            }

            return Record(delivery, new PostingOutcome(PostingOutcomeKind.AlreadyPosted, Journal: earlier));
        }

        var correction = Journal.Post(
            correctionJournalId,
            JournalKind.Correction,
            target.Source,
            postingRuleVersion,
            target.CurrencyId,
            dates,
            correctionLines,
            period,
            linkedJournalId: target.Id);

        RequireNewJournalId(correction.Id);
        _journals[correction.Id] = correction;
        _corrections[correctionId] = correction;
        return Record(delivery, new PostingOutcome(PostingOutcomeKind.Posted, Journal: correction));
    }

    private static (string, string) Delivery(string consumer, string messageId)
    {
        if (!AccountingText.IsCanonical(consumer) || !AccountingText.IsCanonical(messageId))
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference,
                "A delivery needs a consumer and a message id without surrounding whitespace.");
        }

        return (consumer, messageId);
    }

    /// <summary>
    /// A redelivered message changes nothing and answers what the first delivery recorded: its
    /// journal, queue entry or pending entry, under <see cref="PostingOutcomeKind.DuplicateMessage"/>.
    /// </summary>
    private static PostingOutcome Replayed(PostingOutcome recorded) =>
        recorded with { Kind = PostingOutcomeKind.DuplicateMessage };

    private PostingOutcome Record((string, string) delivery, PostingOutcome outcome)
    {
        _inbox[delivery] = outcome;
        return outcome;
    }

    /// <summary>
    /// Keeps the pending-valuation entry equal to this delivery's facts: removed when every amount
    /// is known, otherwise created or refreshed with this delivery's rule version and count. The
    /// first message that raised it stays recorded.
    /// </summary>
    private PendingValuation? SyncPending((Guid, string) key, PostingRequest request, int unvalued)
    {
        if (unvalued == 0)
        {
            _pending.Remove(key);
            return null;
        }

        var pending = _pending.TryGetValue(key, out var waiting)
            ? waiting with { PostingRuleVersion = request.PostingRuleVersion, LinesAwaitingAmount = unvalued }
            : new PendingValuation(request.Source, request.PostingRuleVersion, unvalued, request.MessageId);
        _pending[key] = pending;
        return pending;
    }

    private static bool SameContent(Journal earlier, JournalLine[] lines, PostingDates dates) =>
        earlier.Dates.TransactionDate == dates.TransactionDate
        && earlier.Dates.AccountingDate == dates.AccountingDate
        && earlier.Dates.ValueDate == dates.ValueDate
        && Canonical(earlier.Lines).SequenceEqual(Canonical(lines));

    private static IEnumerable<JournalLine> Canonical(IEnumerable<JournalLine> lines) =>
        lines.OrderBy(line => line.Account.CompanyId)
            .ThenBy(line => line.Account.AccountId)
            .ThenBy(line => line.Debit)
            .ThenBy(line => line.Credit);

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

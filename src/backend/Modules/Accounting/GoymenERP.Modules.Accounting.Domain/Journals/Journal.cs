using System.Collections.ObjectModel;
using GoymenERP.Modules.Accounting.Domain.Periods;

namespace GoymenERP.Modules.Accounting.Domain.Journals;

/// <summary>
/// A posted, balanced journal. It is never edited or deleted: a mistake is answered by a NEW
/// reversal or correction journal that links back to this one (plan §7.6, R4). A journal is an
/// accounting record of a source; it is not a second bank or stock movement.
/// </summary>
public sealed class Journal
{
    private readonly ReadOnlyCollection<JournalLine> _lines;

    private Journal(
        Guid id,
        JournalKind kind,
        SourceReference source,
        string postingRuleVersion,
        Guid currencyId,
        PostingDates dates,
        JournalLine[] lines,
        Guid? linkedJournalId)
    {
        Id = id;
        Kind = kind;
        Source = source;
        PostingRuleVersion = postingRuleVersion;
        CurrencyId = currencyId;
        Dates = dates;
        _lines = Array.AsReadOnly(lines);
        LinkedJournalId = linkedJournalId;
    }

    public Guid Id { get; }

    public Guid CompanyId => Source.CompanyId;

    public JournalKind Kind { get; }

    public SourceReference Source { get; }

    public string PostingRuleVersion { get; }

    /// <summary>The single currency of every line. Multi-currency and FX are not in this slice.</summary>
    public Guid CurrencyId { get; }

    public PostingDates Dates { get; }

    public IReadOnlyList<JournalLine> Lines => _lines;

    /// <summary>For a reversal or correction: the journal it answers. Null for an original.</summary>
    public Guid? LinkedJournalId { get; }

    public decimal TotalDebit => _lines.Sum(line => line.Debit);

    public decimal TotalCredit => _lines.Sum(line => line.Credit);

    internal static Journal Post(
        Guid id,
        JournalKind kind,
        SourceReference source,
        string postingRuleVersion,
        Guid currencyId,
        PostingDates dates,
        IEnumerable<JournalLine> lines,
        AccountingPeriod period,
        Guid? linkedJournalId)
    {
        if (id == Guid.Empty || currencyId == Guid.Empty || string.IsNullOrWhiteSpace(postingRuleVersion))
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference,
                "A journal needs an id, a currency and a posting rule version.");
        }

        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(lines);
        var array = lines.ToArray();
        if (array.Any(line => line is null))
        {
            throw new ArgumentException("A journal line cannot be null.", nameof(lines));
        }

        if (array.Length < 2)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.JournalTooFewLines, "A journal needs at least two lines.");
        }

        if (array.Any(line => line.Account.CompanyId != source.CompanyId))
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.CompanyMismatch,
                "Every line's account must belong to the journal's company.");
        }

        if (array.Sum(line => line.Debit) != array.Sum(line => line.Credit))
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.JournalUnbalanced, "Total debit must equal total credit exactly.");
        }

        period.EnsureAcceptsPosting(source.CompanyId, dates.AccountingDate);
        return new Journal(id, kind, source, postingRuleVersion, currencyId, dates, array, linkedJournalId);
    }

    internal IEnumerable<JournalLine> MirroredLines() => _lines.Select(line => line.Mirrored());

    /// <summary>
    /// Rebuilds a journal that was posted and stored earlier (ACC-3). Its shape is checked again:
    /// identities, at least two one-sided lines of the journal's company, exact balance, and the
    /// link its kind requires. The period is NOT checked: a stored journal may sit in a period
    /// that has closed since, and it stays exactly as it was posted.
    /// </summary>
    public static Journal Restore(
        Guid id,
        JournalKind kind,
        SourceReference source,
        string postingRuleVersion,
        Guid currencyId,
        PostingDates dates,
        IEnumerable<JournalLine> lines,
        Guid? linkedJournalId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(lines);
        if (id == Guid.Empty || currencyId == Guid.Empty || string.IsNullOrWhiteSpace(postingRuleVersion)
            || !Enum.IsDefined(kind) || (kind == JournalKind.Original) != (linkedJournalId is null)
            || linkedJournalId == Guid.Empty)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference,
                "A stored journal needs an id, a currency, a rule version and the link its kind requires.");
        }

        var array = lines.ToArray();
        if (array.Any(line => line is null))
        {
            throw new ArgumentException("A journal line cannot be null.", nameof(lines));
        }

        if (array.Length < 2)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.JournalTooFewLines, "A journal needs at least two lines.");
        }

        if (array.Any(line => line.Account.CompanyId != source.CompanyId))
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.CompanyMismatch,
                "Every line's account must belong to the journal's company.");
        }

        if (array.Sum(line => line.Debit) != array.Sum(line => line.Credit))
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.JournalUnbalanced, "Total debit must equal total credit exactly.");
        }

        return new Journal(id, kind, source, postingRuleVersion, currencyId, dates, array, linkedJournalId);
    }
}

namespace GoymenERP.Modules.Accounting.Domain.Journals;

/// <summary>
/// An account of the company's chart. Opaque on purpose: this slice owns no real chart of
/// accounts (T22's real accounts and policy are a dependency), so an account is only a company
/// and an identity, never a code or a default.
/// </summary>
public readonly record struct AccountRef
{
    public AccountRef(Guid companyId, Guid accountId)
    {
        if (companyId == Guid.Empty || accountId == Guid.Empty)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference, "An account needs a company and an id.");
        }

        CompanyId = companyId;
        AccountId = accountId;
    }

    public Guid CompanyId { get; }

    public Guid AccountId { get; }
}

/// <summary>
/// The business source a journal accounts for. <see cref="PostingPurpose"/> separates the
/// different journals one source may legitimately need; (company, source, purpose) is the key
/// that allows exactly one ORIGINAL journal, independent of any message identity.
/// </summary>
public sealed record SourceReference
{
    public SourceReference(Guid companyId, Guid sourceId, string postingPurpose)
    {
        if (companyId == Guid.Empty || sourceId == Guid.Empty || !AccountingText.IsCanonical(postingPurpose))
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference,
                "A source needs a company, an id and a posting purpose without surrounding whitespace.");
        }

        CompanyId = companyId;
        SourceId = sourceId;
        PostingPurpose = postingPurpose;
    }

    public Guid CompanyId { get; }

    public Guid SourceId { get; }

    public string PostingPurpose { get; }
}

/// <summary>
/// The dates a posting carries (Ö5), kept apart: none is derived from another. The accounting
/// date decides the period; the transaction/document date and the value date are recorded as
/// given; the recording instant is when it was entered, and it must be given in UTC. An unset
/// date (<see cref="DateOnly.MinValue"/>) is refused rather than taken as a real one.
/// </summary>
public sealed record PostingDates
{
    public PostingDates(
        DateOnly transactionDate,
        DateOnly accountingDate,
        DateOnly? valueDate,
        DateTimeOffset recordedAtUtc)
    {
        if (transactionDate == DateOnly.MinValue || accountingDate == DateOnly.MinValue
            || valueDate == DateOnly.MinValue || recordedAtUtc == default)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidDates, "Posting dates must be set; none is assumed.");
        }

        if (recordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidDates, "The recording instant must be given in UTC.");
        }

        TransactionDate = transactionDate;
        AccountingDate = accountingDate;
        ValueDate = valueDate;
        RecordedAtUtc = recordedAtUtc;
    }

    public DateOnly TransactionDate { get; }

    public DateOnly AccountingDate { get; }

    public DateOnly? ValueDate { get; }

    public DateTimeOffset RecordedAtUtc { get; }
}

/// <summary>
/// The identifying texts (posting purpose, consumer, message id) are compared ordinally and
/// exactly. Surrounding whitespace is refused rather than trimmed, so "x" and "x " can never
/// become two identities, and nothing is silently rewritten.
/// </summary>
internal static class AccountingText
{
    public static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);
}

public enum JournalKind
{
    Original = 1,
    Reversal = 2,
    Correction = 3,
}

/// <summary>
/// One side of one account. Exactly one of debit/credit is positive and the other is zero; no
/// negative amounts. Amounts are compared exactly: no scale or rounding is applied here, because
/// neither is decided for this slice (the Settings money profile owns them).
/// </summary>
public sealed record JournalLine
{
    public JournalLine(AccountRef account, decimal debit, decimal credit)
    {
        if (account == default)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference, "A line needs an account.");
        }

        var oneSided = (debit > 0m && credit == 0m) || (credit > 0m && debit == 0m);
        if (!oneSided)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.JournalLineInvalid,
                "A line is either a positive debit or a positive credit, never both, zero or negative.");
        }

        Account = account;
        Debit = debit;
        Credit = credit;
    }

    public AccountRef Account { get; }

    public decimal Debit { get; }

    public decimal Credit { get; }

    internal JournalLine Mirrored() => new(Account, Credit, Debit);
}

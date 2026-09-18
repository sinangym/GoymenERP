namespace GoymenERP.Modules.Accounting.Domain;

/// <summary>
/// A rule of the accounting domain was broken. The code is stable and machine-readable; the
/// message is for diagnostics only.
/// </summary>
public sealed class AccountingDomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Stable codes raised by <see cref="AccountingDomainException"/>.</summary>
public static class AccountingErrorCodes
{
    public const string InvalidReference = "accounting_invalid_reference";
    public const string CompanyMismatch = "accounting_company_mismatch";
    public const string JournalTooFewLines = "accounting_journal_too_few_lines";
    public const string JournalLineInvalid = "accounting_journal_line_invalid";
    public const string JournalUnbalanced = "accounting_journal_unbalanced";
    public const string PeriodClosed = "accounting_period_closed";
    public const string PeriodDoesNotCover = "accounting_period_does_not_cover_date";
    public const string PeriodInvalid = "accounting_period_invalid";
    public const string JournalNotFound = "accounting_journal_not_found";
    public const string JournalAlreadyReversed = "accounting_journal_already_reversed";
    public const string ReversalOfReversal = "accounting_reversal_of_reversal";
}

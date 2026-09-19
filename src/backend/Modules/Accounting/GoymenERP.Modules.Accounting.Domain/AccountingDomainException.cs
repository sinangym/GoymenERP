namespace GoymenERP.Modules.Accounting.Domain;

/// <summary>
/// A rule of the accounting domain was broken. The code is stable and machine-readable; the
/// message is for diagnostics only.
/// </summary>
public sealed class AccountingDomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Stable codes raised by <see cref="AccountingDomainException"/>: each names the rule that
/// refused, so a test or a later application layer tells WHICH rule held without parsing the
/// message. A null argument stays an <see cref="ArgumentNullException"/>: a caller error, not a
/// rule.
/// </summary>
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
    public const string ReversalOfReversal = "accounting_reversal_of_reversal";
    public const string InvalidDates = "accounting_invalid_dates";
    public const string PeriodAlreadyClosed = "accounting_period_already_closed";

    /// <summary>Ö4: the period's preparer tried to approve its own opening.</summary>
    public const string PeriodSelfApproval = "accounting_period_self_approval";

    /// <summary>Only a proposed period is approved (it is already open or closed).</summary>
    public const string PeriodNotProposed = "accounting_period_not_proposed";

    /// <summary>Only an open period is closed (a proposal is not open yet).</summary>
    public const string PeriodNotOpen = "accounting_period_not_open";
    public const string CorrectionIdentityConflict = "accounting_correction_identity_conflict";
    public const string CorrectionContentConflict = "accounting_correction_content_conflict";
}

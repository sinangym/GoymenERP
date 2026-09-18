namespace GoymenERP.Modules.Accounting.Domain.Periods;

public enum AccountingPeriodStatus
{
    Open = 1,
    Closed = 2,
}

/// <summary>
/// One company's accounting period (Ö5: posting only into an authorised open period). A closed
/// period accepts no new journal; a correction for something recorded there is a NEW journal
/// dated in an open period. Re-opening a period is an approval workflow (Ö4) that this slice does
/// not model, so there is deliberately no Reopen here.
/// </summary>
public sealed class AccountingPeriod
{
    public AccountingPeriod(Guid id, Guid companyId, DateOnly startsOn, DateOnly endsOn)
    {
        if (id == Guid.Empty || companyId == Guid.Empty)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference, "A period needs an id and a company.");
        }

        if (endsOn < startsOn)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.PeriodInvalid, "A period cannot end before it starts.");
        }

        Id = id;
        CompanyId = companyId;
        StartsOn = startsOn;
        EndsOn = endsOn;
        Status = AccountingPeriodStatus.Open;
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public DateOnly StartsOn { get; }

    public DateOnly EndsOn { get; }

    public AccountingPeriodStatus Status { get; private set; }

    /// <summary>Who closed the period; null while it is open.</summary>
    public Guid? ClosedByUserId { get; private set; }

    /// <summary>When the period was closed, in UTC; null while it is open.</summary>
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public bool Covers(DateOnly date) => date >= StartsOn && date <= EndsOn;

    /// <summary>
    /// Closes the period and records who closed it and when. Closing is an authorised act (Ö5);
    /// checking that authority is the caller's permission check, not this record's. A second close
    /// is refused rather than silently overwriting the first one's trace.
    /// </summary>
    public void Close(Guid closedByUserId, DateTimeOffset closedAtUtc)
    {
        if (closedByUserId == Guid.Empty || closedAtUtc == default || closedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference, "Closing needs an actor and a UTC instant.");
        }

        if (Status == AccountingPeriodStatus.Closed)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.PeriodAlreadyClosed, "The period is already closed.");
        }

        Status = AccountingPeriodStatus.Closed;
        ClosedByUserId = closedByUserId;
        ClosedAtUtc = closedAtUtc;
    }

    /// <summary>
    /// Throws unless this is the same company's open period and it covers the accounting date.
    /// The caller resolves which period applies; this never picks one on its own.
    /// </summary>
    public void EnsureAcceptsPosting(Guid companyId, DateOnly accountingDate)
    {
        if (companyId != CompanyId)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.CompanyMismatch, "The period belongs to another company.");
        }

        if (!Covers(accountingDate))
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.PeriodDoesNotCover,
                "The accounting date is outside the given period.");
        }

        if (Status != AccountingPeriodStatus.Open)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.PeriodClosed, "The period is closed to new postings.");
        }
    }
}

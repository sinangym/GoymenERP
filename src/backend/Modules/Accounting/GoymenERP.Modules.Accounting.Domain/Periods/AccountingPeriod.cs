namespace GoymenERP.Modules.Accounting.Domain.Periods;

public enum AccountingPeriodStatus
{
    Open = 1,
    Closed = 2,

    /// <summary>Prepared, not yet approved (Ö4): it covers its dates but accepts no posting.</summary>
    Proposed = 3,
}

/// <summary>
/// One company's accounting period (Ö5: posting only into an authorised open period). A closed
/// period accepts no new journal; a correction for something recorded there is a NEW journal
/// dated in an open period.
/// <list type="bullet">
/// <item><b>Opening is maker≠checker (Ö4, #66 5722637822: "dönem açma … hazırlayandan farklı tek
/// yetkili onayında").</b> A period is born <see cref="AccountingPeriodStatus.Proposed"/> by its
/// preparer (<see cref="Propose"/>) and becomes open only by <see cref="Approve"/> of ONE other
/// authorised person; the preparer's own approval is refused. There is no other way to an open period.</item>
/// <item><b>Closing</b> is one authorised act (Ö5); Ö4 does not list it, so no second person is added.</item>
/// <item>No reopen, and no withdraw or reject of a proposal: neither is decided (Ö4 names only the
/// approval; NEEDS-BUSINESS #66 5742097485). They are not modelled.</item>
/// </list>
/// </summary>
public sealed class AccountingPeriod
{
    private AccountingPeriod(Guid id, Guid companyId, DateOnly startsOn, DateOnly endsOn,
        Guid preparedByUserId, DateTimeOffset preparedAtUtc)
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

        RequireActorAndInstant(preparedByUserId, preparedAtUtc, "Proposing");
        Id = id;
        CompanyId = companyId;
        StartsOn = startsOn;
        EndsOn = endsOn;
        PreparedByUserId = preparedByUserId;
        PreparedAtUtc = preparedAtUtc;
        Status = AccountingPeriodStatus.Proposed;
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public DateOnly StartsOn { get; }

    public DateOnly EndsOn { get; }

    public AccountingPeriodStatus Status { get; private set; }

    /// <summary>Who prepared (proposed) the period.</summary>
    public Guid PreparedByUserId { get; }

    /// <summary>When the period was proposed, in UTC.</summary>
    public DateTimeOffset PreparedAtUtc { get; }

    /// <summary>Who approved the opening (never the preparer); null while it is proposed.</summary>
    public Guid? ApprovedByUserId { get; private set; }

    /// <summary>When the opening was approved, in UTC; null while it is proposed.</summary>
    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    /// <summary>Who closed the period; null while it is not closed.</summary>
    public Guid? ClosedByUserId { get; private set; }

    /// <summary>When the period was closed, in UTC; null while it is not closed.</summary>
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public bool Covers(DateOnly date) => date >= StartsOn && date <= EndsOn;

    /// <summary>
    /// Proposes a new period: it covers its dates (so no other period of the company may overlap it)
    /// but accepts no posting until someone other than <paramref name="preparedByUserId"/> approves it.
    /// </summary>
    public static AccountingPeriod Propose(Guid id, Guid companyId, DateOnly startsOn, DateOnly endsOn,
        Guid preparedByUserId, DateTimeOffset preparedAtUtc) =>
        new(id, companyId, startsOn, endsOn, preparedByUserId, preparedAtUtc);

    /// <summary>
    /// Rebuilds a stored period: proposed; open (with its approval); or closed (with its approval and
    /// its close). A trace with only one half of a pair, or a close without an approval, is refused,
    /// as <see cref="Approve"/> and <see cref="Close"/> would refuse it.
    /// </summary>
    public static AccountingPeriod Restore(Guid id, Guid companyId, DateOnly startsOn, DateOnly endsOn,
        Guid preparedByUserId, DateTimeOffset preparedAtUtc,
        Guid? approvedByUserId, DateTimeOffset? approvedAtUtc,
        Guid? closedByUserId, DateTimeOffset? closedAtUtc)
    {
        var period = new AccountingPeriod(id, companyId, startsOn, endsOn, preparedByUserId, preparedAtUtc);
        if (approvedByUserId is null && approvedAtUtc is null)
        {
            return closedByUserId is null && closedAtUtc is null
                ? period
                : throw new AccountingDomainException(
                    AccountingErrorCodes.InvalidReference, "A stored close needs a stored approval.");
        }

        if (approvedByUserId is not { } approvedBy || approvedAtUtc is not { } approvedAt)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference, "A stored approval needs both its actor and its instant.");
        }

        period.Approve(approvedBy, approvedAt);
        if (closedByUserId is null && closedAtUtc is null)
        {
            return period;
        }

        if (closedByUserId is not { } closedBy || closedAtUtc is not { } closedAt)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference, "A stored close needs both its actor and its instant.");
        }

        period.Close(closedBy, closedAt);
        return period;
    }

    /// <summary>
    /// Approves a proposed opening (Ö4): one authorised person other than the preparer. The period is
    /// then open for posting. Checking that the approver holds the authority is the caller's; this
    /// record enforces only that it is someone else and that the period is still a proposal.
    /// </summary>
    public void Approve(Guid approvedByUserId, DateTimeOffset approvedAtUtc)
    {
        RequireActorAndInstant(approvedByUserId, approvedAtUtc, "Approving");
        if (Status != AccountingPeriodStatus.Proposed)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.PeriodNotProposed, "Only a proposed period is approved.");
        }

        if (approvedByUserId == PreparedByUserId)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.PeriodSelfApproval, "The preparer cannot approve their own period (Ö4).");
        }

        Status = AccountingPeriodStatus.Open;
        ApprovedByUserId = approvedByUserId;
        ApprovedAtUtc = approvedAtUtc;
    }

    /// <summary>
    /// Closes an open period and records who closed it and when. Closing is an authorised act (Ö5);
    /// checking that authority is the caller's permission check, not this record's. A second close
    /// is refused rather than silently overwriting the first one's trace.
    /// </summary>
    public void Close(Guid closedByUserId, DateTimeOffset closedAtUtc)
    {
        RequireActorAndInstant(closedByUserId, closedAtUtc, "Closing");
        if (Status == AccountingPeriodStatus.Closed)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.PeriodAlreadyClosed, "The period is already closed.");
        }

        if (Status != AccountingPeriodStatus.Open)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.PeriodNotOpen, "Only an open period is closed; a proposal is not open yet.");
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

        if (Status == AccountingPeriodStatus.Closed)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.PeriodClosed, "The period is closed to new postings.");
        }

        if (Status != AccountingPeriodStatus.Open)
        {
            // Ö4: a proposal accepts no posting until another authorised person approves it.
            throw new AccountingDomainException(
                AccountingErrorCodes.PeriodNotOpen, "The period is proposed and not approved yet.");
        }
    }

    private static void RequireActorAndInstant(Guid actor, DateTimeOffset instant, string act)
    {
        if (actor == Guid.Empty || instant == default || instant.Offset != TimeSpan.Zero)
        {
            throw new AccountingDomainException(
                AccountingErrorCodes.InvalidReference, $"{act} needs an actor and a UTC instant.");
        }
    }
}

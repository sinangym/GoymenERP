namespace GoymenERP.Modules.Accounting.Domain.Periods;

/// <summary>FNS-D42 and the subsequent human decision: the opening authority proposes undoing
/// a particular close; a different approval authority decides. Approval reverses that close's
/// carry voucher in the same host transaction. This object never opens a period by itself.</summary>
public sealed record AccountingPeriodReopening
{
    private AccountingPeriodReopening() { }

    public Guid Id { get; private init; }
    public Guid CompanyId { get; private init; }
    public Guid PeriodId { get; private init; }
    public Guid ClosureId { get; private init; }
    public long ClosedPeriodVersion { get; private init; }
    public Guid ProposedBy { get; private init; }
    public DateTimeOffset ProposedAtUtc { get; private init; }
    public string Reason { get; private init; } = string.Empty;
    public Guid? ApprovedBy { get; private init; }
    public DateTimeOffset? ApprovedAtUtc { get; private init; }
    public long Version => ApprovedBy is null ? 1 : 2;

    public static AccountingPeriodReopening Propose(Guid id, AccountingPeriod period, Guid closureId,
        long closedPeriodVersion, Guid actor, DateTimeOffset atUtc, string reason)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (id == Guid.Empty || closureId == Guid.Empty || closedPeriodVersion < 1 || actor == Guid.Empty
            || atUtc == default || atUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(reason)
            || reason.Length > 1000 || reason != reason.Trim())
            throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                "A reopening proposal requires its exact closure, version, actor, UTC instant and reason.");
        if (period.Status != AccountingPeriodStatus.Closed)
            throw new AccountingDomainException("accounting_period_not_closed", "Only a closed period can be proposed for reopening.");
        return new AccountingPeriodReopening { Id = id, CompanyId = period.CompanyId, PeriodId = period.Id,
            ClosureId = closureId, ClosedPeriodVersion = closedPeriodVersion, ProposedBy = actor,
            ProposedAtUtc = atUtc, Reason = reason };
    }

    public AccountingPeriodReopening Approve(Guid actor, DateTimeOffset atUtc)
    {
        if (actor == Guid.Empty || atUtc == default || atUtc.Offset != TimeSpan.Zero || atUtc < ProposedAtUtc)
            throw new AccountingDomainException(AccountingErrorCodes.InvalidReference, "Approval requires an actor and valid UTC instant.");
        if (ApprovedBy is not null)
            throw new AccountingDomainException("accounting_period_reopening_already_approved", "The reopening was already approved.");
        if (actor == ProposedBy)
            throw new AccountingDomainException(AccountingErrorCodes.PeriodSelfApproval, "The reopening preparer cannot approve it.");
        return this with { ApprovedBy = actor, ApprovedAtUtc = atUtc };
    }

    public static AccountingPeriodReopening Restore(Guid id, AccountingPeriod closedPeriod, Guid closureId,
        long closedPeriodVersion, Guid proposedBy, DateTimeOffset proposedAtUtc, string reason,
        Guid? approvedBy, DateTimeOffset? approvedAtUtc)
    {
        var proposal = Propose(id, closedPeriod, closureId, closedPeriodVersion, proposedBy, proposedAtUtc, reason);
        return (approvedBy, approvedAtUtc) switch
        {
            (null, null) => proposal,
            ({ } actor, { } at) => proposal.Approve(actor, at),
            _ => throw new AccountingDomainException(AccountingErrorCodes.InvalidReference, "A stored approval requires both actor and instant."),
        };
    }
}

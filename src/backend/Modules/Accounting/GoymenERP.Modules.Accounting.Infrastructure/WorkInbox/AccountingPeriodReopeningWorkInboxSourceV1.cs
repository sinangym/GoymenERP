using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32): the proposals to reopen a closed accounting period (FNS-D42), as Accounting lists them for the
/// Work inbox. A proposal waits while it has no approval and the close it names is still the period's current close (the
/// approve command requires the closed period at that version and the close not yet reopened); it waits since it was
/// proposed. It is approved through the native command from the Work purpose view by someone other than its proposer
/// (<c>AccountingPeriodReopening.Approve</c>: "the reopening preparer cannot approve it"); no other user is refused.
/// <para>
/// ROL-D12: reopening is approved under its own permission, <c>accounting.periods.reopen</c>. The approve command accepts
/// only a company-wide grant (D11: no scoped approver).
/// </para>
/// </summary>
internal sealed partial class AccountingPeriodReopeningWorkInboxSourceV1(
    AccountingDbContext dbContext,
    AccountingWorkInboxFence fence,
    IAccountingPeriodDecisionReadV1? owner = null) : IWorkInboxSourceV1
{
    public const string ModuleName = "accounting";
    public const string KindName = "accounting.period-reopening.v1";
    public const string ScreenPath = "/onaylarim/accounting.period-reopening.v1";
    public const string NativeScreenPath = "/muhasebe/donemler";

    /// <summary>A closed period is reopened (its carry voucher reversed) only with a different person's approval.</summary>
    public const string ReopeningRequestedRule = "accounting.period-reopening-requested";

    /// <summary>A later period is still closed; approval would be refused until that period is reopened first.</summary>
    public const string LaterPeriodClosedRule = "accounting.period-reopening-later-period-closed";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => AccountingPermissionManifestV1.PeriodsReopen;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        fence.ListAsync(context, ApproverPermission, maxCount,
            async token =>
            {
                var company = context.CompanyId;
                var pending = await (
                        from proposal in dbContext.PeriodReopenings.AsNoTracking()
                        join close in dbContext.PeriodClosures.AsNoTracking() on proposal.ClosureId equals close.Id
                        join period in dbContext.Periods.AsNoTracking() on proposal.PeriodId equals period.Id
                        where proposal.CompanyId == company && close.CompanyId == company && period.CompanyId == company
                            && proposal.ApprovedBy == null && proposal.ApprovedAtUtc == null
                            && close.PeriodId == period.Id && close.ReopeningId == null
                            && period.Status == (int)AccountingPeriodStatus.Closed
                            && close.ClosedPeriodVersion == period.Version
                        orderby proposal.ProposedAtUtc, proposal.Id
                        select new Pending(proposal, period.StartsOn, period.EndsOn))
                    .Take(maxCount + 1)
                    .ToListAsync(token);
                var latestClosedEnd = await (
                        from close in dbContext.PeriodClosures.AsNoTracking()
                        join period in dbContext.Periods.AsNoTracking() on close.PeriodId equals period.Id
                        where close.CompanyId == company && period.CompanyId == company && close.ReopeningId == null
                        select (DateOnly?)period.EndsOn)
                    .MaxAsync(token);
                return (pending, latestClosedEnd);
            },
            snapshot => AccountingWorkInboxFence.Page(snapshot.pending, maxCount,
                row => Item(row, snapshot.latestClosedEnd)),
            cancellationToken);

    private static WorkInboxItemV1? Item(Pending row, DateOnly? latestClosedEnd) =>
        Item(row, latestClosedEnd is { } end && end > row.EndsOn);

    private static WorkInboxItemV1? Item(Pending row, bool laterPeriodClosed)
    {
        var proposal = row.Proposal;
        if (proposal.Id == Guid.Empty || proposal.PeriodId == Guid.Empty || proposal.ClosureId == Guid.Empty
            || proposal.ProposedBy == Guid.Empty || string.IsNullOrWhiteSpace(proposal.Reason) || row.EndsOn < row.StartsOn)
        {
            return null;
        }

        return IdentityItem(proposal.Id, proposal.PeriodId, proposal.ClosureId, proposal.ProposedBy, proposal.ProposedAtUtc,
            row.StartsOn, row.EndsOn, laterPeriodClosed);
    }

    private static WorkInboxItemV1? IdentityItem(Guid proposalId, Guid periodId, Guid closureId, Guid proposedBy,
        DateTimeOffset proposedAt, DateOnly startsOn, DateOnly endsOn, bool laterPeriodClosed)
    {
        if (proposalId == Guid.Empty || periodId == Guid.Empty || closureId == Guid.Empty || proposedBy == Guid.Empty || endsOn < startsOn) return null;
        var rules = new List<WorkApprovalRuleResultV1>(2) { new(ReopeningRequestedRule, null, null, null, null) };
        if (laterPeriodClosed) rules.Add(new(LaterPeriodClosedRule, null, null, null, null));
        var label = AccountingWorkInboxFence.PeriodLabel(startsOn, endsOn);
        return new WorkInboxItemV1(ModuleName, KindName, proposalId, label, proposedAt.ToUniversalTime(),
            AccountingWorkInboxFence.Link(ScreenPath, proposalId), AccountingWorkInboxFence.Actor(proposedBy), rules,
            [
                new WorkApprovalReferenceV1("period", label, periodId, NativeScreenPath),
                new WorkApprovalReferenceV1("period-closure", AccountingWorkInboxFence.ShortReference(closureId), closureId, null),
            ]);
    }

    private sealed record Pending(AccountingPeriodReopeningRow Proposal, DateOnly StartsOn, DateOnly EndsOn);
}

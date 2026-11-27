using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

internal sealed partial class AccountingPeriodReopeningWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (owner is null) throw new InvalidOperationException("Period reopening decision owner unavailable.");
        var answer = await owner.ReadReopeningDecisionAsync(itemId, Fence, token); await Fence(token);
        if (answer is null) throw new InvalidOperationException("Period reopening answer unavailable.");
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Status == AccountingApiStatus.NotFound && answer.Resource is null) return null;
        if (answer.Status != AccountingApiStatus.Ok || answer.Resource is not { } detail || detail.Reopening is not { } proposal
            || proposal.CompanyId != context.CompanyId || proposal.Id != itemId || !ValidDetail(detail)
            || detail.Period.IsOwnProposal != WorkInboxRulesV1.SameUser(AccountingWorkInboxFence.Actor(detail.Period.PreparedByUserId), context.UserId)
            || DecisionItem(proposal, detail.Period.StartsOn, detail.Period.EndsOn, detail.LaterPeriodClosed!.Value) is not { } item)
            throw new InvalidOperationException("Period reopening facts unavailable.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        var approved = proposal.ApprovedBy is not null;
        var pending = !approved && detail.Closure.ReopeningId is null && detail.Period.Status == "Closed"
            && detail.Closure.ClosedPeriodVersion == detail.Period.Version;
        return new(context.CompanyId, item, proposal.Version.ToString(CultureInfo.InvariantCulture), pending)
        {
            AccountingPeriodReopeningProof = new(context.CompanyId, itemId, proposal.PeriodId, proposal.ClosureId,
                proposal.ProposedBy, proposal.ProposedAtUtc, detail.Period.StartsOn, detail.Period.EndsOn, proposal.Version,
                proposal.ClosedPeriodVersion, detail.Period.Version, detail.Period.Status, approved, pending,
                detail.LaterPeriodClosed.Value, detail.SourceFactsDigest, detail.PreviewDigest),
        };
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refusal = await fence.CheckAsync(context, ApproverPermission, ct); ct.ThrowIfCancellationRequested();
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Period reopening authority unavailable.");
        }
    }
    internal static bool ValidDetail(AccountingPeriodReopeningDecisionDetailV1 detail)
    {
        if (detail.Reopening is not { } proposal || detail.Period is not { } period || detail.Closure is not { } closure
            || detail.LaterPeriodClosed is null || !AccountingPeriodOpeningWorkInboxSourceV1.ValidView(period)
            || proposal.Id == Guid.Empty || proposal.CompanyId != period.CompanyId || proposal.PeriodId != period.PeriodId
            || proposal.ClosureId != closure.Id || closure.CompanyId != period.CompanyId || closure.PeriodId != period.PeriodId
            || proposal.ClosedPeriodVersion <= 0 || proposal.ClosedPeriodVersion != closure.ClosedPeriodVersion
            || proposal.ProposedBy == Guid.Empty || closure.CarryVoucherId == Guid.Empty || closure.TargetPeriodId == Guid.Empty
            || proposal.ApprovedBy.HasValue != proposal.ApprovedAtUtc.HasValue || proposal.Version <= 0
            || proposal.ApprovedBy == proposal.ProposedBy || proposal.Version != (proposal.ApprovedBy.HasValue ? 2 : 1)) return false;
        if (proposal.ApprovedBy is not null && (closure.ReopeningId != proposal.Id || closure.ReopenedBy != proposal.ApprovedBy
            || closure.ReopenedAtUtc != proposal.ApprovedAtUtc)) return false;
        var source = new AccountingPeriodReopeningDecisionSourceV1(AccountingPeriodOpeningWorkInboxSourceV1.RestoreView(period), proposal, closure)
            { LaterPeriodClosed = detail.LaterPeriodClosed };
        return detail.SourceFactsDigest == AccountingApiService.PeriodReopeningSourceFactsDigest(source)
            && detail.PreviewDigest == AccountingApiService.PeriodReopeningPreviewDigest(proposal, period, closure);
    }
    internal static WorkInboxItemV1? DecisionItem(AccountingPeriodReopeningViewV1 proposal, DateOnly startsOn, DateOnly endsOn, bool laterClosed) =>
        Item(new Pending(new AccountingPeriodReopeningRow { Id = proposal.Id, CompanyId = proposal.CompanyId, PeriodId = proposal.PeriodId,
            ClosureId = proposal.ClosureId, ClosedPeriodVersion = proposal.ClosedPeriodVersion, ProposedBy = proposal.ProposedBy,
            ProposedAtUtc = proposal.ProposedAtUtc, Reason = proposal.Reason, ApprovedBy = proposal.ApprovedBy,
            ApprovedAtUtc = proposal.ApprovedAtUtc, Version = proposal.Version }, startsOn, endsOn), laterClosed);
}

/// <summary>The actual owner's dates and later-closed fact drive the existing formatter.</summary>
public static class AccountingPeriodReopeningDecisionBindingV1
{
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, AccountingPeriodReopeningViewV1? original,
        AccountingPeriodApprovalReceiptV1? receipt)
    {
        if (original is null || receipt is null || receipt.PeriodStartsOn is not { } starts || receipt.PeriodEndsOn is not { } ends
            || receipt.LaterPeriodClosed is not { } later || ends < starts
            || receipt.CommandName != "accounting.period.reopening.approve.v1" || receipt.CompanyId != original.CompanyId
            || receipt.PeriodId != original.PeriodId || receipt.ReopeningId != original.Id || original.ProposedBy == receipt.ActorId
            || original.ApprovedBy != receipt.ActorId || original.ApprovedAtUtc != receipt.ApprovedAtUtc
            || receipt.ExpectedVersion < 1 || receipt.ExpectedVersion == long.MaxValue || receipt.ResultVersion != receipt.ExpectedVersion + 1
            || receipt.ResultVersion != original.Version || receipt.ExpectedPeriodVersion != original.ClosedPeriodVersion
            || AccountingPeriodReopeningWorkInboxSourceV1.DecisionItem(original, starts, ends, later) is not { } expected) return false;
        // Dates/later-closed come from the genuine owner receipt body, not the snapshot's own proof.
        return AccountingPeriodOpeningDecisionBindingV1.SameItem(item, expected);
    }

    public static bool MatchesDecisionItem(WorkInboxItemV1? item, AccountingPeriodReopeningDecisionDetailV1? detail) =>
        detail is not null && AccountingPeriodReopeningWorkInboxSourceV1.ValidDetail(detail)
        && AccountingPeriodReopeningWorkInboxSourceV1.DecisionItem(detail.Reopening, detail.Period.StartsOn, detail.Period.EndsOn,
            detail.LaterPeriodClosed!.Value) is { } expected && AccountingPeriodOpeningDecisionBindingV1.SameItem(item, expected);
}

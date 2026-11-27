using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

internal sealed partial class AccountingPeriodOpeningWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (owner is null) throw new InvalidOperationException("Period opening decision owner unavailable.");
        var answer = await owner.ReadOpeningDecisionAsync(itemId, Fence, token); await Fence(token);
        if (answer is null) throw new InvalidOperationException("Period opening answer unavailable.");
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Status == AccountingApiStatus.NotFound && answer.Resource is null) return null;
        if (answer.Status != AccountingApiStatus.Ok || answer.Resource is not { } detail || detail.Period is not { } view
            || view.CompanyId != context.CompanyId || view.PeriodId != itemId
            || view.IsOwnProposal != WorkInboxRulesV1.SameUser(AccountingWorkInboxFence.Actor(view.PreparedByUserId), context.UserId)
            || !ValidDetail(detail)
            || DecisionItem(view) is not { } item) throw new InvalidOperationException("Period opening facts unavailable.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        return new(context.CompanyId, item, view.Version.ToString(CultureInfo.InvariantCulture), view.Status == "Proposed")
        {
            AccountingPeriodOpeningProof = new(context.CompanyId, itemId, view.PreparedByUserId, view.PreparedAtUtc,
                view.StartsOn, view.EndsOn, view.Status, view.Version, detail.SourceFactsDigest, detail.PreviewDigest),
        };
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refusal = await fence.CheckAsync(context, ApproverPermission, ct); ct.ThrowIfCancellationRequested();
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Period opening authority unavailable.");
        }
    }
    internal static bool ValidDetail(AccountingPeriodOpeningDecisionDetailV1 detail) => detail.Period is { } view
        && ValidView(view) && detail.SourceFactsDigest == AccountingApiService.PeriodOpeningSourceFactsDigest(view)
        && detail.PreviewDigest == AccountingApiService.PeriodOpeningPreviewDigest(view);
    internal static bool ValidView(PeriodViewV1 view)
    {
        if (view.PeriodId == Guid.Empty || view.CompanyId == Guid.Empty || view.Version <= 0 || view.Status is not ("Proposed" or "Open" or "Closed")) return false;
        try
        {
            var source = RestoreView(view);
            return source.Period.Status.ToString() == view.Status;
        }
        catch (AccountingDomainException) { return false; }
    }
    internal static AccountingPeriodRecord RestoreView(PeriodViewV1 view) => new(AccountingPeriod.Restore(view.PeriodId, view.CompanyId,
        view.StartsOn, view.EndsOn, view.PreparedByUserId, view.PreparedAtUtc, view.ApprovedByUserId, view.ApprovedAtUtc,
        view.ClosedByUserId, view.ClosedAtUtc), view.Version);
    internal static WorkInboxItemV1? DecisionItem(PeriodViewV1 view) => !ValidView(view) ? null
        : IdentityItem(view.PeriodId, view.StartsOn, view.EndsOn, view.PreparedByUserId, view.PreparedAtUtc);
    internal static WorkInboxItemV1? ProofItem(WorkAccountingPeriodOpeningProofV1 proof) =>
        IdentityItem(proof.PeriodId, proof.StartsOn, proof.EndsOn, proof.PreparedBy, proof.PreparedAtUtc);
}

/// <summary>Owner formatter equivalence for actual native opening views, never snapshot self-certification.</summary>
public static class AccountingPeriodOpeningDecisionBindingV1
{
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkAccountingPeriodOpeningProofV1? proof) =>
        item is not null && proof is not null && proof.CompanyId != Guid.Empty && proof.Version > 0
        && proof.Status is "Proposed" or "Open" or "Closed"
        && proof.SourceFactsDigest == AccountingApiService.PeriodOpeningSourceFactsDigest(proof.PeriodId, proof.CompanyId,
            proof.StartsOn, proof.EndsOn, proof.PreparedBy, proof.PreparedAtUtc)
        && AccountingPeriodOpeningWorkInboxSourceV1.ProofItem(proof) is { } expected && SameItem(item, expected);

    public static bool MatchesDecisionItem(WorkInboxItemV1? item, AccountingPeriodOpeningDecisionDetailV1? detail) =>
        detail is not null && AccountingPeriodOpeningWorkInboxSourceV1.ValidDetail(detail) && MatchesDecisionItem(item, detail.Period);
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, PeriodViewV1? view) => view is not null
        && AccountingPeriodOpeningWorkInboxSourceV1.DecisionItem(view) is { } expected && SameItem(item, expected);
    internal static bool SameItem(WorkInboxItemV1? item, WorkInboxItemV1 expected) => item is not null
        && item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId
        && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc
        && item.PreparedBy == expected.PreparedBy && item.Link == expected.Link && item.Scope is null
        && item.Rules is not null && item.References is not null && item.Rules.SequenceEqual(expected.Rules)
        && item.References.SequenceEqual(expected.References)
        && (item.ExcludedDeciders ?? []).SequenceEqual(expected.ExcludedDeciders ?? []);
}

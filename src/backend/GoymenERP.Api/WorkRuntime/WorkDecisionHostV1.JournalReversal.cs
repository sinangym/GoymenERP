using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool JournalReversalVersion(string text, out int version) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version)
        && version > 0 && version < int.MaxValue && text == version.ToString(CultureInfo.InvariantCulture);

    private static bool ValidJournalReversalRequest(WorkDecisionRequestV1 request) => request.Approve && request.Reason is null
        && JournalReversalVersion(request.ExpectedVersion, out _) && request.AccountingPeriodReopening is null
        && request.NormalJournalDraft is null && request.ManufacturingPlanReapproval is null && request.InventoryTransferLoss is null
        && request.LogisticsPlanExcess is null && request.InventoryTransferOverage is null && request.SupplierReturnCostException is null
        && request.LogisticsCustomerReturn is null && request.ProductPackageStandard is null;

    private static bool ValidJournalReversalProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != JournalReversal) return snapshot.JournalReversalProof is null;
        return snapshot.JournalReversalProof is { } p && p.CompanyId == snapshot.CompanyId
            && p.ProposalId == snapshot.Item.ItemId && p.ProposalId != Guid.Empty && p.SourceJournalId != Guid.Empty
            && p.PeriodId != Guid.Empty && p.ProposedBy != Guid.Empty && snapshot.Item.PreparedBy == p.ProposedBy.ToString("D")
            && p.ProposedAtUtc.Offset == TimeSpan.Zero && p.ProposedAtUtc == snapshot.Item.WaitingSinceUtc
            && p.Version > 0 && snapshot.SourceVersion == p.Version.ToString(CultureInfo.InvariantCulture)
            && (snapshot.Pending ? p.Status == "Pending" : p.Status == "Approved")
            && p.CancellationSourceActorId != Guid.Empty && NormalJournalDigest(p.SourceFactsDigest) && NormalJournalDigest(p.PreviewDigest)
            && AccountingJournalReversalDecisionBindingV1.MatchesDecisionProof(snapshot.Item, p);
    }

    private async Task JournalReversalFenceAsync(WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(JournalReversal, AccountingJournalReversalPermissionNamesV1.Approve, snapshot.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }

    private async Task<WorkJournalReversalDecisionPreviewV1> JournalReversalHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (accountingJournalReversals is null) throw new WorkDecisionException(503);
        AccountingApiResult<AccountingJournalReversalDecisionDetailV1> answer;
        try { answer = await accountingJournalReversals.ReadDecisionAsync(snapshot.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.JournalReversalProof!;
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null || answer.OriginalCorrelationId is not null
            || answer.Resource is not { } detail || detail.Proposal is not { } view
            || view.CompanyId != snapshot.CompanyId || view.ProposalId != p.ProposalId || view.SourceJournalId != p.SourceJournalId
            || view.PeriodId != p.PeriodId || view.ProposedBy != p.ProposedBy || view.ProposedAtUtc != p.ProposedAtUtc
            || view.Version != p.Version || view.Status != p.Status || !detail.CancellationSourceActorProofEstablished
            || detail.CancellationSourceActorId != p.CancellationSourceActorId
            || detail.SourceFactsDigest != p.SourceFactsDigest || detail.PreviewDigest != p.PreviewDigest
            || AccountingJournalReversalServiceV1.JournalReversalSourceFactsDigest(view) != p.SourceFactsDigest
            || AccountingJournalReversalServiceV1.JournalReversalPreviewDigest(view) != p.PreviewDigest
            || !AccountingJournalReversalDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail)) throw new WorkDecisionException(503);
        return new(view, detail.SourceFactsDigest, detail.PreviewDigest);
        Task Fence(CancellationToken ct) => JournalReversalFenceAsync(snapshot, actor, ct);
    }

    private async Task<WorkDecisionResultV1> DecideJournalReversalAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (accountingJournalReversals is null) throw new WorkDecisionException(503);
        var expected = int.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture);
        AccountingApiResult<AccountingJournalReversalProposalViewV1> answer;
        try { answer = await accountingJournalReversals.ApproveAsync(id, expected, key, token); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await JournalReversalFenceAsync(snapshot, actor, token);
        RequireAccountingPeriodAnswer(answer);
        var p = snapshot.JournalReversalProof!;
        if (answer.Resource is not { } view || view.ApprovalReceipt is not { } receipt
            || view.ProposalId != id || view.CompanyId != snapshot.CompanyId || view.SourceJournalId != p.SourceJournalId
            || view.PeriodId != p.PeriodId || view.ProposedBy != p.ProposedBy || view.ProposedAtUtc != p.ProposedAtUtc
            || view.Status != "Approved" || view.Version != expected + 1 || view.DecisionReason is not null
            || view.ReversalJournalId is not { } reversal || reversal == Guid.Empty || reversal == view.SourceJournalId
            || !Guid.TryParse(actor, out var checker) || checker == Guid.Empty || checker == p.ProposedBy || view.DecidedBy != checker
            || view.DecidedAtUtc is not { } approved || approved.Offset != TimeSpan.Zero
            || receipt.CompanyId != snapshot.CompanyId || receipt.ProposalId != id || receipt.SourceJournalId != p.SourceJournalId
            || receipt.ReversalJournalId != reversal || receipt.PeriodId != p.PeriodId
            || receipt.CommandName != AccountingJournalReversalCommandNamesV1.Approve
            || receipt.IdempotencyKeyHash != AccountingJournalReversalServiceV1.JournalReversalApprovalKeyHash(key)
            || receipt.PayloadHash != AccountingJournalReversalServiceV1.JournalReversalApprovalPayloadHash(id, expected)
            || receipt.ActorId != checker || receipt.ExpectedVersion != expected || receipt.ResultVersion != expected + 1
            || receipt.ApprovedAtUtc != approved || !receipt.CancellationSourceActorProofEstablished
            || receipt.CancellationSourceActorId != p.CancellationSourceActorId || receipt.CancellationSourceActorId == checker
            || receipt.SourceFactsDigest != p.SourceFactsDigest
            || AccountingJournalReversalServiceV1.JournalReversalSourceFactsDigest(view) != p.SourceFactsDigest
            || receipt.OriginalResultDigest != AccountingJournalReversalServiceV1.JournalReversalOriginalResultDigest(answer)
            || !NormalJournalDigest(receipt.OriginalResultDigest) || string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
            || !AccountingJournalReversalDecisionBindingV1.MatchesDecisionItem(snapshot.Item, view, receipt)
            || (answer.IsReplay ? answer.OriginalCorrelationId != receipt.OriginalCorrelationId
                : answer.OriginalCorrelationId is not null || receipt.OriginalCorrelationId != correlation.CorrelationId
                    || !snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion)
            || !snapshot.Pending && (p.Status != "Approved" || p.Version != view.Version)) throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, JournalReversal, id, "Approved", answer.IsReplay, receipt.OriginalCorrelationId);
    }
}

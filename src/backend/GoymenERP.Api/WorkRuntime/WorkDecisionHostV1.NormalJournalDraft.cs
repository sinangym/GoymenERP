using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool NormalJournalDigest(string? text) => text is { Length: 64 }
        && text.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');
    private static bool NormalJournalVersion(string text, out long version) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version > 0
        && version.ToString(CultureInfo.InvariantCulture) == text;

    private static bool ValidNormalJournalDraftRequest(WorkDecisionRequestV1 request) =>
        request.Approve && request.Reason is null && NormalJournalVersion(request.ExpectedVersion, out _)
        && request.NormalJournalDraft is { PeriodId: var period } && period != Guid.Empty
        && request.ManufacturingPlanReapproval is null && request.InventoryTransferLoss is null && request.LogisticsPlanExcess is null
        && request.InventoryTransferOverage is null && request.SupplierReturnCostException is null
        && request.LogisticsCustomerReturn is null && request.ProductPackageStandard is null;

    private static bool ValidNormalJournalDraftProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != NormalJournalDraft) return snapshot.NormalJournalDraftProof is null;
        return snapshot.NormalJournalDraftProof is { } p && p.CompanyId == snapshot.CompanyId && p.DraftId == snapshot.Item.ItemId
            && p.DraftId != Guid.Empty && p.PreparedBy != Guid.Empty && snapshot.Item.PreparedBy == p.PreparedBy.ToString("D")
            && p.PreparedAtUtc.Offset == TimeSpan.Zero && p.PreparedAtUtc == snapshot.Item.WaitingSinceUtc
            && p.Version > 0 && snapshot.SourceVersion == p.Version.ToString(CultureInfo.InvariantCulture)
            && (snapshot.Pending ? p.Status == "Draft" : p.Status == "Approved")
            && NormalJournalDigest(p.SourceFactsDigest) && NormalJournalDigest(p.PreviewDigest)
            && snapshot.Item.Scope is null && snapshot.Item.ExcludedDeciders is not { Count: > 0 }
            && snapshot.Item.Rules.Count is 1 or 2 && snapshot.Item.Rules[0].RuleCode == "accounting.normal-journal-draft-approval-required"
            && (snapshot.Item.Rules.Count == 1 || snapshot.Item.Rules[1].RuleCode == "accounting.credit-journal-maker-checker")
            && snapshot.Item.Rules.All(x => x.Actual is null && x.Threshold is null && x.Deviation is null && x.Unit is null)
            && snapshot.Item.References.Count == 2;
    }

    private async Task<NormalJournalDraftDecisionDetailV1> NormalJournalDraftHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 snapshot,
        string actor, CancellationToken token)
    {
        if (normalJournalDrafts is null) throw new WorkDecisionException(503);
        AccountingApiResult<NormalJournalDraftDecisionDetailV1> answer;
        try { answer = await normalJournalDrafts.ReadDecisionAsync(snapshot.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.NormalJournalDraftProof!;
        if (answer.Status != AccountingApiStatus.Ok || answer.Resource is not { } detail || detail.Draft is not { } view
            || view.CompanyId != snapshot.CompanyId || view.DraftId != p.DraftId || view.Version != p.Version || view.Status != p.Status
            || view.PreparedByUserId != p.PreparedBy || view.PreparedAtUtc != p.PreparedAtUtc || detail.IsCreditSource is null
            || detail.SourceFactsDigest != p.SourceFactsDigest || detail.PreviewDigest != p.PreviewDigest
            || NormalJournalDraftServiceV1.NormalPreviewDigest(view) != p.PreviewDigest
            || !AccountingNormalJournalDraftDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail)) throw new WorkDecisionException(503);
        return detail;
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await RequireDecisionAuthorityAsync(NormalJournalDraft, NormalJournalPermissionNamesV1.DraftApprove, snapshot.CompanyId, actor, ct);
            ct.ThrowIfCancellationRequested();
        }
    }

    private async Task<WorkDecisionResultV1> DecideNormalJournalDraftAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (normalJournalDrafts is null) throw new WorkDecisionException(503);
        var expected = long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture); var period = request.NormalJournalDraft!.PeriodId;
        AccountingApiResult<NormalJournalDraftViewV1> answer;
        try { answer = await normalJournalDrafts.ApproveAsync(id, expected, period, key, token); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(NormalJournalDraft, NormalJournalPermissionNamesV1.DraftApprove, snapshot.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
        if (answer is null || !Enum.IsDefined(answer.Status)) throw new WorkDecisionException(503);
        if (answer.Status != AccountingApiStatus.Ok)
            throw new WorkDecisionException(answer.Status switch { AccountingApiStatus.Denied => 403, AccountingApiStatus.Unavailable => 503,
                AccountingApiStatus.Conflict => 409, AccountingApiStatus.NotFound => 404, AccountingApiStatus.Invalid => 400, _ => 422 },
                answer.Code, answer.IsReplay, answer.OriginalCorrelationId);
        var p = snapshot.NormalJournalDraftProof!;
        var payload = NormalJournalDraftServiceV1.ApprovalPayloadHash(id, expected, period);
        if (answer.Resource is not { } view || view.ApprovalReceipt is not { } receipt || receipt.IsCreditSource is null
            || answer.Code is not null || answer.Field is not null || answer.ExpectedVersion is not null || answer.CurrentVersion is not null
            || expected == long.MaxValue || view.Version != expected + 1 || view.Status != "Approved"
            || view.DraftId != id || view.CompanyId != snapshot.CompanyId || view.PeriodId != period || view.PostedJournalId != id
            || view.PreparedByUserId != p.PreparedBy || view.PreparedAtUtc != p.PreparedAtUtc
            || !Guid.TryParse(actor, out var approver) || view.ApprovedByUserId != approver || approver == p.PreparedBy
            || view.ApprovedAtUtc is not { } approvedAt || approvedAt.Offset != TimeSpan.Zero
            || receipt.CompanyId != snapshot.CompanyId || receipt.DraftId != id || receipt.CommandName != NormalJournalCommandNamesV1.ApproveDraft
            || receipt.IdempotencyKeyHash != NormalJournalDraftServiceV1.ApprovalKeyHash(key) || receipt.PayloadHash != payload
            || receipt.ActorId != approver || receipt.ExpectedVersion != expected || receipt.ResultVersion != expected + 1
            || receipt.PeriodId != period || receipt.PostedJournalId != id || receipt.ApprovedAtUtc != approvedAt
            || receipt.SourceFactsDigest != p.SourceFactsDigest || string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
            || receipt.OriginalResultDigest != NormalJournalDraftServiceV1.ApprovalOriginalResultDigest(answer)
            || !AccountingNormalJournalDraftDecisionBindingV1.MatchesDecisionItem(snapshot.Item,
                new(view, receipt.SourceFactsDigest, NormalJournalDraftServiceV1.NormalPreviewDigest(view)) { IsCreditSource = receipt.IsCreditSource })
            || (answer.IsReplay ? answer.OriginalCorrelationId != receipt.OriginalCorrelationId
                : answer.OriginalCorrelationId is not null || receipt.OriginalCorrelationId != correlation.CorrelationId
                    || !snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion)
            || !snapshot.Pending && (p.Status != "Approved" || p.Version != view.Version)) throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, NormalJournalDraft, id, "Approved", answer.IsReplay, receipt.OriginalCorrelationId);
    }
}

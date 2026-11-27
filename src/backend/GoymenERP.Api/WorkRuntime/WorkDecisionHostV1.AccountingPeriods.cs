using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool AccountingPeriodVersion(string value, out long version) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out version)
        && version > 0 && version < long.MaxValue && value == version.ToString(CultureInfo.InvariantCulture);
    private static bool ValidAccountingPeriodRequest(string kind, WorkDecisionRequestV1 request)
    {
        if (kind is not (AccountingPeriodOpening or AccountingPeriodReopening)) return request.AccountingPeriodReopening is null;
        return request.Approve && request.Reason is null && AccountingPeriodVersion(request.ExpectedVersion, out _)
            && (kind == AccountingPeriodOpening ? request.AccountingPeriodReopening is null
                : request.AccountingPeriodReopening is { } selected && AccountingPeriodVersion(selected.ExpectedPeriodVersion, out _))
            && request.NormalJournalDraft is null && request.ManufacturingPlanReapproval is null
            && request.InventoryTransferLoss is null && request.InventoryTransferOverage is null
            && request.LogisticsPlanExcess is null && request.SupplierReturnCostException is null
            && request.LogisticsCustomerReturn is null && request.ProductPackageStandard is null;
    }
    private static bool ValidAccountingPeriodProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind is not (AccountingPeriodOpening or AccountingPeriodReopening))
            return snapshot.AccountingPeriodOpeningProof is null && snapshot.AccountingPeriodReopeningProof is null;
        if (snapshot.Item.Scope is not null || snapshot.Item.ExcludedDeciders is { Count: > 0 }
            || snapshot.Item.Rules.Any(x => x.Threshold is not null || x.Actual is not null || x.Deviation is not null || x.Unit is not null)) return false;
        if (kind == AccountingPeriodOpening)
            return snapshot.AccountingPeriodReopeningProof is null && snapshot.AccountingPeriodOpeningProof is { } p
                && p.CompanyId == snapshot.CompanyId && p.PeriodId == snapshot.Item.ItemId && p.PeriodId != Guid.Empty
                && p.PreparedBy != Guid.Empty && snapshot.Item.PreparedBy == p.PreparedBy.ToString("D")
                && p.PreparedAtUtc.Offset == TimeSpan.Zero && p.PreparedAtUtc == snapshot.Item.WaitingSinceUtc
                && p.StartsOn <= p.EndsOn && p.Version > 0 && snapshot.SourceVersion == p.Version.ToString(CultureInfo.InvariantCulture)
                && p.Status is "Proposed" or "Open" or "Closed" && snapshot.Pending == (p.Status == "Proposed")
                && NormalJournalDigest(p.SourceFactsDigest) && NormalJournalDigest(p.PreviewDigest)
                && AccountingPeriodOpeningDecisionBindingV1.MatchesDecisionProof(snapshot.Item, p);
        return snapshot.AccountingPeriodOpeningProof is null && snapshot.AccountingPeriodReopeningProof is { } r
            && r.CompanyId == snapshot.CompanyId && r.ProposalId == snapshot.Item.ItemId && r.ProposalId != Guid.Empty
            && r.PeriodId != Guid.Empty && r.ClosureId != Guid.Empty && r.PreparedBy != Guid.Empty
            && snapshot.Item.PreparedBy == r.PreparedBy.ToString("D") && r.PreparedAtUtc.Offset == TimeSpan.Zero
            && snapshot.Item.WaitingSinceUtc == r.PreparedAtUtc && r.StartsOn <= r.EndsOn
            && r.Version > 0 && r.ClosedPeriodVersion > 0 && r.PeriodVersion > 0
            && snapshot.SourceVersion == r.Version.ToString(CultureInfo.InvariantCulture) && snapshot.Pending == r.Pending
            && r.PeriodStatus is "Proposed" or "Open" or "Closed" && !(r.Pending && r.Approved)
            && NormalJournalDigest(r.SourceFactsDigest) && NormalJournalDigest(r.PreviewDigest)
            && AccountingPeriodReopeningDecisionBindingV1.MatchesDecisionProof(snapshot.Item, r);
    }
    private async Task AccountingPeriodFenceAsync(string kind, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(kind, kind == AccountingPeriodOpening ? AccountingPermissionManifestV1.PeriodsApprove
            : AccountingPermissionManifestV1.PeriodsReopen, snapshot.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }
    private async Task<WorkAccountingPeriodOpeningPreviewV1> AccountingPeriodOpeningHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (accountingPeriods is null) throw new WorkDecisionException(503);
        AccountingApiResult<AccountingPeriodOpeningDecisionDetailV1> answer;
        try { answer = await accountingPeriods.ReadOpeningDecisionAsync(snapshot.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.AccountingPeriodOpeningProof!;
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null || answer.OriginalCorrelationId is not null
            || answer.Resource is not { } detail || detail.Period is not { } v
            || v.CompanyId != snapshot.CompanyId || v.PeriodId != p.PeriodId || v.Version != p.Version || v.Status != p.Status
            || v.PreparedByUserId != p.PreparedBy || v.PreparedAtUtc != p.PreparedAtUtc || v.StartsOn != p.StartsOn || v.EndsOn != p.EndsOn
            || detail.SourceFactsDigest != p.SourceFactsDigest || detail.PreviewDigest != p.PreviewDigest
            || AccountingApiService.PeriodOpeningSourceFactsDigest(v) != p.SourceFactsDigest
            || AccountingApiService.PeriodOpeningPreviewDigest(v) != p.PreviewDigest
            || !AccountingPeriodOpeningDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail)) throw new WorkDecisionException(503);
        return WorkAccountingPeriodOpeningPreviewV1.FromNative(detail);
        Task Fence(CancellationToken ct) => AccountingPeriodFenceAsync(AccountingPeriodOpening, snapshot, actor, ct);
    }
    private async Task<WorkAccountingPeriodReopeningPreviewV1> AccountingPeriodReopeningHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (accountingPeriods is null) throw new WorkDecisionException(503);
        AccountingApiResult<AccountingPeriodReopeningDecisionDetailV1> answer;
        try { answer = await accountingPeriods.ReadReopeningDecisionAsync(snapshot.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.AccountingPeriodReopeningProof!;
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null || answer.OriginalCorrelationId is not null
            || answer.Resource is not { } detail || detail.Reopening is not { } v
            || detail.Period is not { } period || detail.Closure is not { } closure || v.CompanyId != snapshot.CompanyId
            || v.Id != p.ProposalId || v.PeriodId != p.PeriodId || v.ClosureId != p.ClosureId || v.Version != p.Version
            || v.ClosedPeriodVersion != p.ClosedPeriodVersion || v.ProposedBy != p.PreparedBy || v.ProposedAtUtc != p.PreparedAtUtc
            || (v.ApprovedBy is not null) != p.Approved || period.PeriodId != p.PeriodId || period.CompanyId != snapshot.CompanyId
            || period.StartsOn != p.StartsOn || period.EndsOn != p.EndsOn || period.Version != p.PeriodVersion || period.Status != p.PeriodStatus
            || closure.Id != p.ClosureId || closure.CompanyId != snapshot.CompanyId || closure.PeriodId != p.PeriodId
            || p.Pending != (v.ApprovedBy is null && closure.ReopeningId is null && period.Status == "Closed" && closure.ClosedPeriodVersion == period.Version)
            || detail.LaterPeriodClosed != p.LaterPeriodClosed || detail.SourceFactsDigest != p.SourceFactsDigest
            || detail.PreviewDigest != p.PreviewDigest
            || AccountingApiService.PeriodReopeningPreviewDigest(v, period, closure) != p.PreviewDigest
            || !AccountingPeriodReopeningDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail)) throw new WorkDecisionException(503);
        return WorkAccountingPeriodReopeningPreviewV1.FromNative(detail);
        Task Fence(CancellationToken ct) => AccountingPeriodFenceAsync(AccountingPeriodReopening, snapshot, actor, ct);
    }
    private async Task<WorkDecisionResultV1> DecideAccountingPeriodAsync(string kind, Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (accountingPeriods is null) throw new WorkDecisionException(503);
        var expected = long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture);
        if (!Guid.TryParse(actor, out var currentActor) || currentActor == Guid.Empty) throw new WorkDecisionException(503);
        if (kind == AccountingPeriodOpening)
        {
            AccountingApiResult<PeriodViewV1> answer;
            try { answer = await accountingPeriods.ApprovePeriodAsync(id, new(expected), key, token); }
            catch (Exception e) when (e is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
            await AccountingPeriodFenceAsync(kind, snapshot, actor, token); RequireAccountingPeriodAnswer(answer);
            var p = snapshot.AccountingPeriodOpeningProof!;
            if (answer.Resource is not { } v || v.ApprovalReceipt is not { } receipt || v.CompanyId != snapshot.CompanyId
                || v.PeriodId != id || v.Status != "Open" || v.Version != expected + 1 || v.IsOwnProposal
                || v.ClosedByUserId is not null || v.ClosedAtUtc is not null || v.PreparedByUserId != p.PreparedBy
                || v.PreparedAtUtc != p.PreparedAtUtc || v.StartsOn != p.StartsOn || v.EndsOn != p.EndsOn
                || v.ApprovedByUserId is not { } approver || approver != currentActor || approver == p.PreparedBy
                || v.ApprovedAtUtc is not { } approved || approved.Offset != TimeSpan.Zero
                || !ValidAccountingPeriodReceipt(receipt, snapshot.CompanyId, id, null, AccountingCommandNames.ApprovePeriod,
                    AccountingApiService.PeriodOpeningApprovalPayloadHash(id, expected), expected, null, key, approver, approved,
                    p.SourceFactsDigest, AccountingApiService.PeriodApprovalOriginalResultDigest(answer))
                || !AccountingPeriodOpeningDecisionBindingV1.MatchesDecisionItem(snapshot.Item, v)
                || (answer.IsReplay ? answer.OriginalCorrelationId != receipt.OriginalCorrelationId
                    : answer.OriginalCorrelationId is not null || receipt.OriginalCorrelationId != correlation.CorrelationId
                        || !snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion)) throw new WorkDecisionException(503);
            return new(snapshot.CompanyId, kind, id, "Approved", answer.IsReplay, receipt.OriginalCorrelationId);
        }
        var rp = snapshot.AccountingPeriodReopeningProof!;
        var selected = long.Parse(request.AccountingPeriodReopening!.ExpectedPeriodVersion, CultureInfo.InvariantCulture);
        AccountingApiResult<AccountingPeriodReopeningViewV1> result;
        try { result = await accountingPeriods.ApprovePeriodReopeningAsync(rp.PeriodId, id, new(expected, selected), key, token); }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await AccountingPeriodFenceAsync(kind, snapshot, actor, token); RequireAccountingPeriodAnswer(result);
        if (result.Resource is not { } reopening || reopening.ApprovalReceipt is not { } proof
            || reopening.Id != id || reopening.CompanyId != snapshot.CompanyId || reopening.PeriodId != rp.PeriodId
            || reopening.ClosureId != rp.ClosureId || reopening.ClosedPeriodVersion != rp.ClosedPeriodVersion
            || selected != reopening.ClosedPeriodVersion || reopening.Version != expected + 1
            || reopening.ProposedBy != rp.PreparedBy || reopening.ProposedAtUtc != rp.PreparedAtUtc
            || reopening.ApprovedBy is not { } checker || checker != currentActor || checker == rp.PreparedBy
            || reopening.ApprovedAtUtc is not { } at || at.Offset != TimeSpan.Zero || proof.PeriodStartsOn != rp.StartsOn
            || proof.PeriodEndsOn != rp.EndsOn || proof.LaterPeriodClosed is null
            || !ValidAccountingPeriodReceipt(proof, snapshot.CompanyId, rp.PeriodId, id, "accounting.period.reopening.approve.v1",
                AccountingApiService.PeriodReopeningApprovalPayloadHash(rp.PeriodId, id, new(expected, selected)), expected, selected,
                key, checker, at, rp.SourceFactsDigest, AccountingApiService.PeriodApprovalOriginalResultDigest(result))
            || !AccountingPeriodReopeningDecisionBindingV1.MatchesDecisionItem(snapshot.Item, reopening, proof)
            || (result.IsReplay ? result.OriginalCorrelationId != proof.OriginalCorrelationId
                : result.OriginalCorrelationId is not null || proof.OriginalCorrelationId != correlation.CorrelationId
                    || !snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion || selected != rp.PeriodVersion))
            throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, kind, id, "Approved", result.IsReplay, proof.OriginalCorrelationId);
    }
    private static bool ValidAccountingPeriodReceipt(AccountingPeriodApprovalReceiptV1 receipt, Guid company, Guid period,
        Guid? reopening, string command, string payload, long expected, long? expectedPeriod, string key, Guid actor,
        DateTimeOffset at, string sourceDigest, string resultDigest) => receipt.CompanyId == company && receipt.PeriodId == period
        && receipt.ReopeningId == reopening && receipt.CommandName == command && receipt.ActorId == actor
        && receipt.IdempotencyKeyHash == AccountingApiService.PeriodApprovalKeyHash(key) && receipt.PayloadHash == payload
        && receipt.ExpectedVersion == expected && receipt.ExpectedPeriodVersion == expectedPeriod && receipt.ResultVersion == expected + 1
        && receipt.ApprovedAtUtc == at && receipt.SourceFactsDigest == sourceDigest && NormalJournalDigest(receipt.SourceFactsDigest)
        && receipt.OriginalResultDigest == resultDigest && NormalJournalDigest(receipt.OriginalResultDigest)
        && !string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId);
    private static void RequireAccountingPeriodAnswer<T>(AccountingApiResult<T>? answer) where T : class
    {
        if (answer is null || !Enum.IsDefined(answer.Status)) throw new WorkDecisionException(503);
        if (answer.Status != AccountingApiStatus.Ok)
            throw new WorkDecisionException(answer.Status switch { AccountingApiStatus.Denied => 403, AccountingApiStatus.Unavailable => 503,
                AccountingApiStatus.Conflict => 409, AccountingApiStatus.NotFound => 404, AccountingApiStatus.Invalid => 400, _ => 422 },
                answer.Code, answer.IsReplay, answer.OriginalCorrelationId);
        if (answer.Code is not null || answer.Field is not null || answer.ExpectedVersion is not null || answer.CurrentVersion is not null)
            throw new WorkDecisionException(503);
    }
}

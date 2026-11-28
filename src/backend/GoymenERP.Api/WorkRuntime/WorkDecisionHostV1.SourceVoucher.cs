using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool SourceVoucherVersion(string? text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value > 0 && text == value.ToString(CultureInfo.InvariantCulture);
    private static bool ValidSourceVoucherRequest(string kind, WorkDecisionRequestV1 request) => kind != SourceVoucher
        || request.Approve && request.Reason is null && request.ExpectedVersion == "1"
            && request.InventoryCostCancellation is null && request.PostingAccountMappingSelection is null
            && request.ApprovedSourceCatalogSelection is null && request.AccountingPeriodReopening is null
            && request.NormalJournalDraft is null && request.ProductPackageStandard is null && request.ManufacturingPlanReapproval is null
            && request.InventoryTransferLoss is null && request.InventoryTransferOverage is null && request.LogisticsPlanExcess is null
            && request.SupplierReturnCostException is null && request.LogisticsCustomerReturn is null;
    private static bool ValidSourceVoucherProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot) => kind != SourceVoucher
        ? snapshot.SourceVoucherProof is null
        : snapshot.SourceVoucherProof is { } p && p.CompanyId == snapshot.CompanyId && p.VoucherId == snapshot.Item.ItemId
            && SourceVoucherVersion(snapshot.SourceVersion) && snapshot.Pending == (!p.SourceCancelled && p.Status is "AwaitingApproval" or "PeriodWaiting")
            && AccountingSourceVoucherDecisionBindingV1.MatchesDecisionProof(snapshot.Item, p);
    private async Task SourceVoucherFenceAsync(WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(SourceVoucher, NormalJournalPermissionNamesV1.DraftApprove, snapshot.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }
    private async Task<WorkSourceVoucherPreviewV1> SourceVoucherHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 snapshot,
        string actor, CancellationToken token)
    {
        if (sourceVouchers is null) throw new WorkDecisionException(503);
        AccountingApiResult<AccountingSourceVoucherDecisionDetailV1> answer;
        try { answer = await sourceVouchers.ReadDecisionAsync(snapshot.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.SourceVoucherProof!;
        if (answer is not { Status: AccountingApiStatus.Ok, Resource: { } d, IsReplay: false, Code: null, Field: null,
                ExpectedVersion: null, CurrentVersion: null, OriginalCorrelationId: null }
            || !AccountingSourceVoucherDecisionBindingV1.ValidDetail(d) || d.Voucher is not { } v
            || v.CompanyId != snapshot.CompanyId || v.Id != p.VoucherId || v.SourceDocumentId != p.SourceDocumentId
            || v.InvoiceId != p.InvoiceId || v.SourceActorId != p.SourceActorId || d.CreatedAtUtc != p.CreatedAtUtc
            || v.DocumentKind != p.DocumentKind || v.FinalizedVersion != p.FinalizedVersion || v.PartyId != p.PartyId
            || v.DocumentCurrencyId != p.CurrencyId || v.Status != p.Status || v.OfficialInvoiceNo != p.OfficialInvoiceNo
            || v.Version.ToString(CultureInfo.InvariantCulture) != snapshot.SourceVersion
            || d.SourceCancelled != p.SourceCancelled || d.PeriodOpen != p.PeriodOpen
            || snapshot.Pending != (!d.SourceCancelled && v.Status is "AwaitingApproval" or "PeriodWaiting")
            || d.SourceFactsDigest != p.SourceFactsDigest || d.PreviewDigest != p.PreviewDigest
            || !AccountingSourceVoucherDecisionBindingV1.MatchesDecisionItem(snapshot.Item, d)) throw new WorkDecisionException(503);
        return WorkSourceVoucherPreviewV1.FromNative(d);
        Task Fence(CancellationToken ct) => SourceVoucherFenceAsync(snapshot, actor, ct);
    }
    private async Task<WorkDecisionResultV1> DecideSourceVoucherAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (sourceVouchers is null) throw new WorkDecisionException(503);
        var p = snapshot.SourceVoucherProof!;
        AccountingApiResult<AccountingSourceVoucherViewV1> answer;
        // Exactly one guarded native command. No second purpose GET or current-period substitution on POST.
        try { answer = await sourceVouchers.ApproveAsync(id, 1, key, token); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await SourceVoucherFenceAsync(snapshot, actor, token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Resource is null) { RequireAccountingPeriodAnswer(answer); throw new WorkDecisionException(503); }
        // Missing/invalid proof suppresses release; it does not assert rollback of a native approval that already committed.
        if (answer is not { Status: AccountingApiStatus.Ok, Resource: { ApprovalReceipt: { } receipt } v,
                Code: null, Field: null, ExpectedVersion: null, CurrentVersion: null }
            || v.CompanyId != snapshot.CompanyId || v.Id != id || v.Status != "Posted" || v.Version != 2
            || v.SourceDocumentId != p.SourceDocumentId || v.InvoiceId != p.InvoiceId || v.SourceActorId != p.SourceActorId
            || v.DocumentKind != p.DocumentKind || v.FinalizedVersion != p.FinalizedVersion || v.PartyId != p.PartyId
            || v.DocumentCurrencyId != p.CurrencyId || v.OfficialInvoiceNo != p.OfficialInvoiceNo
            || v.PeriodId is not { } originalPeriod || originalPeriod == Guid.Empty || v.ApprovedAtUtc is not { Offset: var offset } || offset != TimeSpan.Zero
            || v.CancellationId is not null || v.CancellationReason is not null || v.ReversalProposal is not null
            || !Guid.TryParse(actor, out var checker) || checker == Guid.Empty || checker == p.SourceActorId || v.ApprovedBy != checker
            || receipt.CompanyId != snapshot.CompanyId || receipt.VoucherId != id || receipt.SourceDocumentId != p.SourceDocumentId
            || receipt.InvoiceId != p.InvoiceId || receipt.SourceActorId != p.SourceActorId || receipt.CreatedAtUtc != p.CreatedAtUtc
            || receipt.CommandName != AccountingSourceVoucherServiceV1.SourceVoucherApproveCommand
            || receipt.IdempotencyKeyHash != AccountingSourceVoucherServiceV1.SourceVoucherApprovalKeyHash(key)
            || receipt.PayloadHash != AccountingSourceVoucherServiceV1.SourceVoucherApprovalPayloadHash(id, 1)
            || receipt.ActorId != checker || receipt.ExpectedVersion != 1 || receipt.ResultVersion != 2
            || receipt.JournalId != id || v.JournalId != receipt.JournalId || v.ApprovedAtUtc != receipt.ApprovedAtUtc
            || receipt.SourceFactsDigest != p.SourceFactsDigest
            || receipt.OriginalResultDigest != AccountingSourceVoucherServiceV1.SourceVoucherOriginalResultDigest(answer)
            || !NormalJournalDigest(receipt.OriginalResultDigest) || string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
            || !AccountingSourceVoucherDecisionBindingV1.MatchesOriginalDecisionItem(snapshot.Item, v, receipt)
            || (answer.IsReplay ? answer.OriginalCorrelationId != receipt.OriginalCorrelationId
                : answer.OriginalCorrelationId is not null || receipt.OriginalCorrelationId != correlation.CorrelationId
                    || !snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion)) throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, SourceVoucher, id, v.Status, answer.IsReplay, receipt.OriginalCorrelationId);
    }
}

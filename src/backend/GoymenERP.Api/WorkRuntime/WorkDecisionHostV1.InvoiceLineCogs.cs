using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidInvoiceLineCogsRequest(string kind, WorkDecisionRequestV1 request) => kind != InvoiceLineCogs
        || request.Approve && request.Reason is null && request.ExpectedVersion == "1"
            && request.PostingAccountMappingSelection is null && request.ApprovedSourceCatalogSelection is null
            && request.AccountingPeriodReopening is null && request.NormalJournalDraft is null && request.ProductPackageStandard is null
            && request.ManufacturingPlanReapproval is null && request.InventoryTransferLoss is null && request.InventoryTransferOverage is null
            && request.LogisticsPlanExcess is null && request.SupplierReturnCostException is null && request.LogisticsCustomerReturn is null;
    private static bool ValidInvoiceLineCogsProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot) => kind != InvoiceLineCogs
        ? snapshot.InvoiceLineCogsProof is null
        : snapshot.InvoiceLineCogsProof is { } p && p.CompanyId == snapshot.CompanyId && p.ProposalId == snapshot.Item.ItemId
            && snapshot.SourceVersion == (p.Status == "Prepared" ? "1" : "2") && (p.Status != "Approved" || !snapshot.Pending)
            && AccountingInvoiceLineCogsDecisionBindingV1.MatchesDecisionProof(snapshot.Item, p);
    private async Task InvoiceLineCogsFenceAsync(WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(InvoiceLineCogs, AccountingJournalReversalPermissionNamesV1.Approve, snapshot.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }
    private async Task<WorkInvoiceLineCogsPreviewV1> InvoiceLineCogsHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (invoiceLineCogs is null) throw new WorkDecisionException(503);
        AccountingApiResult<InvoiceLineCogsDecisionDetailV1> answer;
        try { answer = await invoiceLineCogs.ReadDecisionAsync(snapshot.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.InvoiceLineCogsProof!;
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null || answer.OriginalCorrelationId is not null
            || answer.Resource is not { } detail || detail.Proposal is not { } v || v.CompanyId != snapshot.CompanyId || v.ProposalId != p.ProposalId
            || v.InvoiceId != p.InvoiceId || v.InvoiceLineId != p.InvoiceLineId || v.FinalizedVersion != p.FinalizedVersion || v.ExpectedLineVersion != p.ExpectedLineVersion
            || v.PreparedBy != p.PreparedBy || v.PreparedAtUtc != p.PreparedAtUtc || v.Status != p.Status || v.Version.ToString(CultureInfo.InvariantCulture) != snapshot.SourceVersion
            || snapshot.Pending != (v.Status == "Prepared" && detail.CurrentLineVersion == v.ExpectedLineVersion)
            || detail.SourceFactsDigest != p.SourceFactsDigest || detail.PreviewDigest != p.PreviewDigest
            || !AccountingInvoiceLineCogsDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail)) throw new WorkDecisionException(503);
        return WorkInvoiceLineCogsPreviewV1.FromNative(detail);
        Task Fence(CancellationToken ct) => InvoiceLineCogsFenceAsync(snapshot, actor, ct);
    }
    private async Task<WorkDecisionResultV1> DecideInvoiceLineCogsAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (invoiceLineCogs is null) throw new WorkDecisionException(503);
        var p = snapshot.InvoiceLineCogsProof!; var expected = long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture);
        AccountingApiResult<InvoiceLineCogsCorrectionViewV1> answer;
        try { answer = await invoiceLineCogs.ApproveAsync(p.InvoiceId, p.InvoiceLineId, id, new(expected), key, token); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await InvoiceLineCogsFenceAsync(snapshot, actor, token); RequireAccountingPeriodAnswer(answer);
        if (answer.Resource is not { Status: "Approved", Version: 2 } v || v.ApprovalReceipt is not { } receipt
            || v.CompanyId != snapshot.CompanyId || v.ProposalId != id || v.InvoiceId != p.InvoiceId || v.InvoiceLineId != p.InvoiceLineId
            || v.FinalizedVersion != p.FinalizedVersion || v.ExpectedLineVersion != p.ExpectedLineVersion || v.PreparedBy != p.PreparedBy || v.PreparedAtUtc != p.PreparedAtUtc
            || !Guid.TryParse(actor, out var checker) || checker == Guid.Empty || checker == p.PreparedBy || v.ApprovedBy != checker
            || receipt.CompanyId != snapshot.CompanyId || receipt.ProposalId != id || receipt.InvoiceId != p.InvoiceId || receipt.InvoiceLineId != p.InvoiceLineId
            || receipt.PreparedBy != p.PreparedBy || receipt.PreparedAtUtc != p.PreparedAtUtc || receipt.ActorId != checker
            || receipt.CommandName != InvoiceLineCogsCorrectionServiceV1.CogsApproveCommand
            || receipt.IdempotencyKeyHash != InvoiceLineCogsCorrectionServiceV1.CogsApprovalKeyHash(key)
            || receipt.PayloadHash != InvoiceLineCogsCorrectionServiceV1.CogsApprovalPayloadHash(p.InvoiceId, p.InvoiceLineId, id, 1)
            || receipt.ExpectedVersion != 1 || receipt.ResultVersion != 2 || receipt.SourceFactsDigest != p.SourceFactsDigest
            || receipt.OriginalResultDigest != InvoiceLineCogsCorrectionServiceV1.CogsOriginalResultDigest(answer)
            || !NormalJournalDigest(receipt.OriginalResultDigest) || string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
            || !AccountingInvoiceLineCogsDecisionBindingV1.MatchesDecisionItem(snapshot.Item, v, receipt)
            || (answer.IsReplay ? answer.OriginalCorrelationId != receipt.OriginalCorrelationId
                : answer.OriginalCorrelationId is not null || receipt.OriginalCorrelationId != correlation.CorrelationId
                    || !snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion)) throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, InvoiceLineCogs, id, "Approved", answer.IsReplay, receipt.OriginalCorrelationId);
    }
}

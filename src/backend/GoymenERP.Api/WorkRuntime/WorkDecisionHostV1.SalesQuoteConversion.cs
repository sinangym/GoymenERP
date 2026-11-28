using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidSalesQuoteConversionRequest(string kind, WorkDecisionRequestV1 r) => kind != SalesQuoteConversion
        ? r.SalesQuoteConversion is null
        : r.SalesQuoteConversion is { QuoteId: var quote, RequestNo: > 0, SubjectRevision: >= 0 } selection && quote != Guid.Empty
            && SalesQuoteConversionDecisionBindingV1.TryVersion(selection.RequestVersion, out _)
            && SalesQuoteConversionDecisionBindingV1.TryVersion(r.ExpectedVersion, out var version) && version < long.MaxValue
            && (r.Approve ? r.Reason is null : !string.IsNullOrWhiteSpace(r.Reason) && r.Reason.Length <= SalesApprovalRequest.MaximumTextLength
                && r.ExpectedVersion == selection.RequestVersion)
            && r.IdentityAccessGrant is null && r.InventoryCostCancellation is null && r.PostingAccountMappingSelection is null
            && r.ApprovedSourceCatalogSelection is null && r.AccountingPeriodReopening is null && r.NormalJournalDraft is null
            && r.ProductPackageStandard is null && r.LogisticsCustomerReturn is null && r.SupplierReturnCostException is null
            && r.InventoryTransferOverage is null && r.LogisticsPlanExcess is null && r.InventoryTransferLoss is null && r.ManufacturingPlanReapproval is null;

    private static bool ValidSalesQuoteConversionProof(string kind, WorkApprovalDecisionSnapshotV1 s) => kind != SalesQuoteConversion
        ? s.SalesQuoteConversionProof is null
        : s.SalesQuoteConversionProof is { } p && s.CompanyId == p.CompanyId && s.Item.ItemId == p.RequestId
            && s.SourceVersion == p.QuoteVersion && s.Pending == (p.RequestStatus == "Pending")
            && SalesQuoteConversionDecisionBindingV1.MatchesDecisionProof(s.Item, p);

    private async Task SalesQuoteConversionFenceAsync(WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(SalesQuoteConversion, SalesPermissionManifestV1.QuotesExpiredConversionApprove, s.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }

    private async Task<WorkSalesQuoteConversionDecisionPreviewV1> SalesQuoteConversionHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (salesQuoteConversions is null) throw new WorkDecisionException(503);
        SalesQuoteDecisionReadResultV1 answer;
        try { answer = await salesQuoteConversions.ReadQuoteConversionDecisionAsync(s.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is { State: SalesQuoteDecisionReadStateV1.Denied, Detail: null }) throw new WorkDecisionException(403);
        if (answer is not { State: SalesQuoteDecisionReadStateV1.Found, Detail: { } detail }
            || !SalesQuoteConversionDecisionBindingV1.MatchesDecisionSnapshot(s, detail)) throw new WorkDecisionException(503);
        var view = WorkSalesQuoteConversionDecisionPreviewV1.FromNative(detail);
        if (view.Quote.DueTermsDisclosure == "Visible")
        {
            // Optional native price visibility is observed LAST, after all other awaited authority fences.
            var price = await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.OrdersPriceRead, s.CompanyId, token);
            token.ThrowIfCancellationRequested();
            if (!company.IsResolved || company.CompanyId != s.CompanyId || !user.IsAuthenticated || user.UserId != actor || !user.CanAccessCompany(s.CompanyId))
                throw new WorkDecisionException(403);
            if (!price.IsGranted || !price.IsCompanyWide) throw new WorkDecisionException(503);
        }
        return view;
        Task Fence(CancellationToken ct) => SalesQuoteConversionFenceAsync(s, actor, ct);
    }

    private async Task<WorkDecisionResultV1> DecideSalesQuoteConversionAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (salesQuoteConversions is null) throw new WorkDecisionException(503);
        var selection = request.SalesQuoteConversion!; var p = s.SalesQuoteConversionProof!;
        if (selection.QuoteId != p.QuoteId || selection.RequestNo != p.RequestNo || selection.SubjectRevision != p.SubjectRevision
            || !SalesQuoteConversionDecisionBindingV1.TryVersion(selection.RequestVersion, out var selectedRequestVersion)
            || !SalesQuoteConversionDecisionBindingV1.TryVersion(p.RequestVersion, out var currentRequestVersion) || selectedRequestVersion > currentRequestVersion)
            throw new WorkDecisionException(503);
        var target = new SalesQuoteConversionTargetV1(id, selection.QuoteId, selection.RequestNo, selection.SubjectRevision, selection.RequestVersion);
        var metadata = new SalesCommandMetadata(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow);
        string original; bool replay; SalesQuoteOriginalDecisionReceiptV1? receipt; bool matches;
        try
        {
            if (request.Approve)
            {
                var body = new SalesQuoteExpiryApprovalRequest(request.ExpectedVersion);
                var answer = await salesQuoteConversions.ApproveSelectedQuoteConversionAsync(target, body, metadata, token);
                await Fence(token);
                if (answer is null) throw new WorkDecisionException(503);
                original = answer.OriginalCorrelationId; replay = answer.IsReplay; receipt = answer.OriginalDecisionReceipt;
                matches = SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(s, target, answer, body, key, actor);
            }
            else
            {
                var body = new SalesDraftStateCommandRequest(request.ExpectedVersion, request.Reason);
                var answer = await salesQuoteConversions.RejectSelectedQuoteConversionAsync(target, body, metadata, token);
                await Fence(token);
                if (answer is null) throw new WorkDecisionException(503);
                original = answer.CorrelationId; replay = answer.IsReplay; receipt = answer.OriginalDecisionReceipt;
                matches = SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(s, target, answer, body, key, actor);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException refusal)
        { await Fence(token); throw new WorkDecisionException(refusal.Status, refusal.Code, refusal.IsReplay, refusal.OriginalCorrelationId); }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch (Exception error) when (error is InvalidOperationException or JsonException)
        { await Fence(token); throw new WorkDecisionException(503); }
        if (!matches || receipt is null || string.IsNullOrWhiteSpace(original) || original.Length > 128 || original.Trim() != original
            || !replay && original != correlation.CorrelationId) throw new WorkDecisionException(503);
        // Missing/mismatched proof after native success is an unknown outcome, never a claim of rollback.
        return new(s.CompanyId, SalesQuoteConversion, id, receipt.OriginalState, replay, original);
        Task Fence(CancellationToken ct) => SalesQuoteConversionFenceAsync(s, actor, ct);
    }
}

using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidSalesRemainderChangeRequest(string kind, WorkDecisionRequestV1 r) => kind != SalesRemainderChange
        || SalesRequestDecisionBindingV1.TryVersion(r.ExpectedVersion, out var n) && n < long.MaxValue
            && (r.Approve ? r.Reason is null : r.Reason is null || r.Reason.Length <= SalesOrderDraft.MaximumRemainderReasonLength)
            && r.SalesPriceFloor is null && r.SalesDiscount is null && r.SalesQuoteConversion is null && r.IdentityAccessGrant is null && r.InventoryCostCancellation is null
            && r.PostingAccountMappingSelection is null && r.ApprovedSourceCatalogSelection is null && r.AccountingPeriodReopening is null
            && r.NormalJournalDraft is null && r.ProductPackageStandard is null && r.LogisticsCustomerReturn is null
            && r.SupplierReturnCostException is null && r.InventoryTransferOverage is null && r.LogisticsPlanExcess is null
            && r.InventoryTransferLoss is null && r.ManufacturingPlanReapproval is null;

    private static bool ValidSalesRemainderChangeProof(string kind, WorkApprovalDecisionSnapshotV1 s) => kind != SalesRemainderChange
        ? s.SalesRemainderChangeProof is null
        : s.SalesRemainderChangeProof is { } p && s.CompanyId == p.CompanyId && s.Item.ItemId == p.DraftId
            && SalesRequestDecisionBindingV1.TryVersion(s.SourceVersion, out var n) && n == p.SelectedExpectedVersion
            && (s.Pending ? p.RequestState == "Pending" && p.ObservedCurrentVersion == n : p.RequestState is "Approved" or "Rejected")
            && SalesRemainderChangeDecisionBindingV1.MatchesDecisionProof(s.Item, p);

    private async Task SalesRemainderChangeFenceAsync(WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(SalesRemainderChange, SalesPermissionManifestV1.OrdersApprove, s.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }

    private async Task<WorkSalesRemainderChangeDecisionPreviewV1> SalesRemainderChangeHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        // Exactly the command's already configured scoped instance, never a separately resolved raw read owner.
        if (salesOrders is not ISalesRemainderDecisionReadV1 reader) throw new WorkDecisionException(503);
        SalesRemainderDecisionReadResultV1 answer;
        try { answer = await reader.ReadRemainderDecisionAsync(s.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        // The owner performs its final consumer + native permission/persona fence before returning.
        // Do not add another authority await after that SAT-D2 persona snapshot has been frozen.
        token.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || user.UserId != actor || !company.IsResolved || company.CompanyId != s.CompanyId || !user.CanAccessCompany(s.CompanyId))
            throw new WorkDecisionException(403);
        if (answer is { State: SalesRemainderDecisionReadStateV1.Denied, Detail: null }) throw new WorkDecisionException(403);
        if (answer is not { State: SalesRemainderDecisionReadStateV1.Found, Detail: { } detail }
            || !SalesRemainderChangeDecisionBindingV1.MatchesDecisionSnapshot(s, detail)) throw new WorkDecisionException(503);
        return WorkSalesRemainderChangeDecisionPreviewV1.FromNative(detail);
        Task Fence(CancellationToken ct) => SalesRemainderChangeFenceAsync(s, actor, ct);
    }

    private async Task<WorkDecisionResultV1> DecideSalesRemainderChangeAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (salesOrders is not ISalesRemainderDecisionReadV1 || s.SalesRemainderChangeProof is not { } p) throw new WorkDecisionException(503);
        var body = new SalesDraftRemainderDecisionRequest(request.ExpectedVersion, request.Reason);
        var metadata = new SalesCommandMetadata(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow);
        SalesOrderDraftResponse answer;
        try
        {
            // No human GET or new current-maker selection here. Versioned source chose this exact DraftId/ChangeNo/N.
            answer = request.Approve
                ? await salesOrders.ApproveRemainderChangeAsync(id, p.ChangeNo, body, metadata, token)
                : await salesOrders.RejectRemainderChangeAsync(id, p.ChangeNo, body, metadata, token);
            await Fence(token);
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException refusal)
        { await Fence(token); throw new WorkDecisionException(refusal.Status, refusal.Code, refusal.IsReplay, refusal.OriginalCorrelationId); }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch (Exception failure) when (failure is InvalidOperationException or JsonException)
        { await Fence(token); throw new WorkDecisionException(503); }
        if (!SalesRemainderChangeDecisionBindingV1.MatchesOriginalDecisionReceipt(s, answer, body, key, actor, request.Approve)
            || answer?.OriginalRemainderDecisionReceipt is not { } receipt || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId)
            || answer.OriginalCorrelationId != answer.OriginalCorrelationId.Trim() || answer.OriginalCorrelationId.Length > 128
            || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId) throw new WorkDecisionException(503);
        // The immutable decision is N→N+1. Native replay's CURRENT draft DTO may be N+k; never relabel it as original N+1.
        // A missing/invalid proof after native completion is unknown, never evidence of rollback.
        return new(s.CompanyId, SalesRemainderChange, id, receipt.OriginalChange.State, answer.IsReplay, answer.OriginalCorrelationId);
        Task Fence(CancellationToken ct) => SalesRemainderChangeFenceAsync(s, actor, ct);
    }
}

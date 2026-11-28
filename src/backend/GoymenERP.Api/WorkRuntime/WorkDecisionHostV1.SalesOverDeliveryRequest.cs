using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidSalesOverDeliveryRequestRequest(string kind, WorkDecisionRequestV1 r) => kind != SalesOverDeliveryRequest
        || SalesRequestDecisionBindingV1.TryVersion(r.ExpectedVersion, out var n) && n < long.MaxValue
            && (r.Approve ? r.Reason is null : !string.IsNullOrWhiteSpace(r.Reason) && r.Reason.Length <= SalesApprovalRequest.MaximumTextLength)
            && r.SalesPriceFloor is null && r.SalesQuoteConversion is null && r.IdentityAccessGrant is null && r.InventoryCostCancellation is null
            && r.PostingAccountMappingSelection is null && r.ApprovedSourceCatalogSelection is null && r.AccountingPeriodReopening is null
            && r.NormalJournalDraft is null && r.ProductPackageStandard is null && r.LogisticsCustomerReturn is null
            && r.SupplierReturnCostException is null && r.InventoryTransferOverage is null && r.LogisticsPlanExcess is null
            && r.InventoryTransferLoss is null && r.ManufacturingPlanReapproval is null;

    private static bool ValidSalesOverDeliveryRequestProof(string kind, WorkApprovalDecisionSnapshotV1 s) => kind != SalesOverDeliveryRequest
        ? s.SalesOverDeliveryRequestProof is null
        : s.SalesOverDeliveryRequestProof is { } p && s.CompanyId == p.CompanyId && s.Item.ItemId == p.RequestId
            && SalesRequestDecisionBindingV1.TryVersion(s.SourceVersion, out var n)
            && (s.Pending ? p.RequestStatus == "Pending" && p.RequestVersion == n
                : p.RequestStatus is "Approved" or "Rejected" && (p.RequestVersion == n || n < long.MaxValue && p.RequestVersion == n + 1))
            && SalesOverDeliveryRequestDecisionBindingV1.MatchesDecisionProof(s.Item, p);

    private async Task SalesOverDeliveryRequestFenceAsync(WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(SalesOverDeliveryRequest, SalesPermissionManifestV1.OrdersOverDeliveryApprove, s.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }

    private async Task<WorkSalesOverDeliveryRequestDecisionPreviewV1> SalesOverDeliveryRequestHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (salesOverDeliveryRequests is null) throw new WorkDecisionException(503);
        SalesOverDeliveryRequestDecisionReadResultV1 answer;
        try { answer = await salesOverDeliveryRequests.ReadOverDeliveryRequestDecisionAsync(s.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is { State: SalesOverDeliveryRequestDecisionReadStateV1.Denied, Detail: null }) throw new WorkDecisionException(403);
        if (answer is not { State: SalesOverDeliveryRequestDecisionReadStateV1.Found, Detail: { } d }
            || !SalesOverDeliveryRequestDecisionBindingV1.MatchesDecisionSnapshot(s, d)) throw new WorkDecisionException(503);
        return WorkSalesOverDeliveryRequestDecisionPreviewV1.FromNative(d);
        Task Fence(CancellationToken ct) => SalesOverDeliveryRequestFenceAsync(s, actor, ct);
    }

    private async Task<WorkDecisionResultV1> DecideSalesOverDeliveryRequestAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (salesOverDeliveryRequests is null || s.SalesOverDeliveryRequestProof is not { } p) throw new WorkDecisionException(503);
        // The source selected this numbered request's original version. Never substitute an order version or a later maker.
        var target = SalesOverDeliveryRequestDecisionBindingV1.Target(p, request.ExpectedVersion);
        var body = new SalesDraftStateCommandRequest(request.ExpectedVersion, request.Reason);
        var metadata = new SalesCommandMetadata(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow);
        SalesApprovalRequestCommandResult answer;
        try
        {
            answer = request.Approve
                ? await salesOverDeliveryRequests.ApproveSelectedOverDeliveryRequestAsync(target, body, metadata, token)
                : await salesOverDeliveryRequests.RejectSelectedOverDeliveryRequestAsync(target, body, metadata, token);
            await Fence(token);
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException refusal)
        { await Fence(token); throw new WorkDecisionException(refusal.Status, refusal.Code, refusal.IsReplay, refusal.OriginalCorrelationId); }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch (Exception failure) when (failure is InvalidOperationException or JsonException)
        { await Fence(token); throw new WorkDecisionException(503); }
        if (!SalesOverDeliveryRequestDecisionBindingV1.MatchesOriginalDecisionReceipt(s, answer, body, key, actor, request.Approve)
            || answer?.OriginalOverDeliveryReceipt is not { } receipt || string.IsNullOrWhiteSpace(answer.CorrelationId)
            || answer.CorrelationId.Trim() != answer.CorrelationId || answer.CorrelationId.Length > 128
            || !answer.IsReplay && answer.CorrelationId != correlation.CorrelationId) throw new WorkDecisionException(503);
        // A missing/invalid proof after the native commit is unknown, not evidence that effects were rolled back.
        return new(s.CompanyId, SalesOverDeliveryRequest, id, receipt.OriginalRequest.Status, answer.IsReplay, answer.CorrelationId);
        Task Fence(CancellationToken ct) => SalesOverDeliveryRequestFenceAsync(s, actor, ct);
    }
}

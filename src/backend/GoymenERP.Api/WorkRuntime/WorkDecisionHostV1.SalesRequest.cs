using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using NativeSalesRequest = GoymenERP.Modules.Sales.Domain.Requests.SalesRequest;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidSalesRequestRequest(string kind, WorkDecisionRequestV1 request) => kind != SalesRequest
        || SalesRequestDecisionBindingV1.TryVersion(request.ExpectedVersion, out var version) && version < long.MaxValue
            && (request.Approve ? request.Reason is null : !string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length <= NativeSalesRequest.MaximumReasonLength)
            && request.ManufacturingPlanReapproval is null && request.InventoryTransferLoss is null && request.InventoryTransferOverage is null
            && request.LogisticsPlanExcess is null && request.SupplierReturnCostException is null && request.LogisticsCustomerReturn is null
            && request.ProductPackageStandard is null && request.NormalJournalDraft is null && request.AccountingPeriodReopening is null
            && request.ApprovedSourceCatalogSelection is null && request.PostingAccountMappingSelection is null && request.InventoryCostCancellation is null;

    private static bool ValidSalesRequestProof(string kind, WorkApprovalDecisionSnapshotV1 s) => kind != SalesRequest
        ? s.SalesRequestProof is null
        : s.SalesRequestProof is { } p && p.CompanyId == s.CompanyId && p.RequestId == s.Item.ItemId
            && s.SourceVersion == p.Version && s.Pending == (p.State == "Submitted")
            && SalesRequestDecisionBindingV1.MatchesDecisionProof(s.Item, p);

    private async Task SalesRequestFenceAsync(WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(SalesRequest, SalesPermissionManifestV1.RequestsApprove, s.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }

    private async Task<WorkSalesRequestDecisionPreviewV1> SalesRequestHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 s,
        string actor, CancellationToken token)
    {
        if (salesRequests is not ISalesRequestDecisionReadV1 reader) throw new WorkDecisionException(503);
        SalesRequestDecisionReadResultV1 answer;
        try { answer = await reader.ReadRequestDecisionAsync(s.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is { State: SalesRequestDecisionReadStateV1.Denied, Detail: null }) throw new WorkDecisionException(403);
        if (answer is not { State: SalesRequestDecisionReadStateV1.Found, Detail: { } detail }
            || !SalesRequestDecisionBindingV1.MatchesDecisionSnapshot(s, detail)) throw new WorkDecisionException(503);
        return WorkSalesRequestDecisionPreviewV1.FromNative(detail);
        Task Fence(CancellationToken ct) => SalesRequestFenceAsync(s, actor, ct);
    }

    private async Task<WorkDecisionResultV1> DecideSalesRequestAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (salesRequests is null) throw new WorkDecisionException(503);
        var body = new SalesDraftStateCommandRequest(request.ExpectedVersion, request.Reason);
        var metadata = new SalesCommandMetadata(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow);
        SalesRequestResponse answer;
        try
        {
            answer = request.Approve
                ? await salesRequests.ApproveRequestAsync(id, body, metadata, token)
                : await salesRequests.RejectRequestAsync(id, body, metadata, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException refusal)
        { await Fence(token); throw new WorkDecisionException(refusal.Status, refusal.Code, refusal.IsReplay, refusal.OriginalCorrelationId); }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch (Exception error) when (error is InvalidOperationException or JsonException)
        { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        var command = request.Approve ? SalesRequestCommands.ApproveV1 : SalesRequestCommands.RejectV1;
        if (answer is null || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId) || answer.OriginalCorrelationId.Length > 128
            || answer.OriginalCorrelationId != answer.OriginalCorrelationId.Trim()
            || !SalesRequestDecisionBindingV1.MatchesOriginalDecisionReceipt(s, answer, command, body, key, actor)
            || !answer.IsReplay && (s.SourceVersion != request.ExpectedVersion || answer.OriginalCorrelationId != correlation.CorrelationId
                || !(request.Approve ? s.SalesRequestProof!.ApprovalSupported : s.SalesRequestProof!.RejectionSupported)))
            throw new WorkDecisionException(503);
        // A post-native mismatch is an unknown outcome, not rollback. Current replay Detail is not the original decision.
        return new(s.CompanyId, SalesRequest, id, answer.OriginalDecisionReceipt!.State, answer.IsReplay, answer.OriginalCorrelationId);
        Task Fence(CancellationToken ct) => SalesRequestFenceAsync(s, actor, ct);
    }
}

using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidSalesRiskExceptionRequest(string kind, WorkDecisionRequestV1 r) => kind != SalesRiskException
        || SalesRiskExceptionDecisionBindingV1.TryVersion(r.ExpectedVersion, out var version) && version < long.MaxValue
            && (r.Approve ? r.Reason is null : !string.IsNullOrWhiteSpace(r.Reason) && r.Reason.Length <= SalesShipmentRiskException.MaximumReasonLength)
            && r.SalesDiscount is null && r.SalesPriceFloor is null && r.SalesQuoteConversion is null && r.IdentityAccessGrant is null
            && r.InventoryCostCancellation is null && r.PostingAccountMappingSelection is null && r.ApprovedSourceCatalogSelection is null
            && r.AccountingPeriodReopening is null && r.NormalJournalDraft is null && r.ProductPackageStandard is null
            && r.LogisticsCustomerReturn is null && r.SupplierReturnCostException is null && r.InventoryTransferOverage is null
            && r.LogisticsPlanExcess is null && r.InventoryTransferLoss is null && r.ManufacturingPlanReapproval is null;

    private static bool ValidSalesRiskExceptionProof(string kind, WorkApprovalDecisionSnapshotV1 s) => kind != SalesRiskException
        ? s.SalesRiskExceptionProof is null
        : s.SalesRiskExceptionProof is { } p && s.CompanyId == p.CompanyId && s.Item.ItemId == p.ExceptionId
            && s.SourceVersion == p.Version && s.Pending == p.Pending && SalesRiskExceptionDecisionBindingV1.MatchesDecisionProof(s.Item, p);

    private async Task SalesRiskExceptionFenceAsync(WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(SalesRiskException, SalesPermissionManifestV1.OrdersRiskExceptionApprove, s.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }

    private async Task<WorkSalesRiskExceptionDecisionPreviewV1> SalesRiskExceptionHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (salesRiskExceptions is not ISalesRiskExceptionDecisionReadV1 reader || s.SalesRiskExceptionProof is not { Historical: false })
            throw new WorkDecisionException(503);
        SalesRiskExceptionDecisionReadResultV1 answer;
        try { answer = await reader.ReadRiskExceptionDecisionAsync(s.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is { State: SalesRiskExceptionDecisionReadStateV1.Denied, Detail: null }) throw new WorkDecisionException(403);
        if (answer is not { State: SalesRiskExceptionDecisionReadStateV1.Found, Detail: { } detail }
            || !SalesRiskExceptionDecisionBindingV1.MatchesDecisionSnapshot(s, detail)) throw new WorkDecisionException(503);
        return WorkSalesRiskExceptionDecisionPreviewV1.FromNative(detail);
        Task Fence(CancellationToken ct) => SalesRiskExceptionFenceAsync(s, actor, ct);
    }

    private async Task<WorkDecisionResultV1> DecideSalesRiskExceptionAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (salesRiskExceptions is not ISalesRiskExceptionDecisionReadV1 || s.SalesRiskExceptionProof is not { } p)
            throw new WorkDecisionException(503);
        if (request.ExpectedVersion != p.Version) throw new WorkDecisionException(409, "work_decision_version_conflict");
        var body = new SalesDraftStateCommandRequest(request.ExpectedVersion, request.Reason);
        var metadata = new SalesCommandMetadata(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow);
        var command = request.Approve ? SalesRiskExceptionCommands.ApproveV1 : SalesRiskExceptionCommands.RejectV1;
        SalesRiskExceptionCommandResult answer;
        try
        {
            // Work's exception GUID selects the native order/no target; neither order version nor today's readiness is an expected version.
            answer = request.Approve
                ? await salesRiskExceptions.ApproveRiskExceptionAsync(p.OrderId, p.ExceptionNo, body, metadata, token)
                : await salesRiskExceptions.RejectRiskExceptionAsync(p.OrderId, p.ExceptionNo, body, metadata, token);
            await Fence(token);
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException refusal)
        { await Fence(token); throw new WorkDecisionException(refusal.Status, refusal.Code, refusal.IsReplay, refusal.OriginalCorrelationId); }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch (Exception failure) when (failure is InvalidOperationException or JsonException)
        { await Fence(token); throw new WorkDecisionException(503); }
        if (answer?.Exception is null || !SalesRiskExceptionDecisionBindingV1.MatchesOriginalDecisionReceipt(s, answer, command, body, key, actor)
            || answer?.OriginalDecisionReceipt is not { } receipt || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId)
            || answer.OriginalCorrelationId.Trim() != answer.OriginalCorrelationId || answer.OriginalCorrelationId.Length > 128
            || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId) throw new WorkDecisionException(503);
        // A proof refusal after native commit suppresses the response; it does not claim a rollback or a new decision.
        return new(s.CompanyId, SalesRiskException, id, receipt.After.State, answer.IsReplay, answer.OriginalCorrelationId);
        Task Fence(CancellationToken ct) => SalesRiskExceptionFenceAsync(s, actor, ct);
    }
}

using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidSalesContractDiscountRequest(string kind, WorkDecisionRequestV1 r) => kind != SalesContractDiscount
        || SalesContractDiscountDecisionDigestsV1.TryVersion(r.ExpectedVersion, out var version) && version < long.MaxValue
            // Work freezes the canonical reason it can later prove from immutable native Audit. The legacy native API still accepts raw padded reasons.
            && (r.Approve ? r.Reason is null : !string.IsNullOrWhiteSpace(r.Reason) && r.Reason == r.Reason.Trim() && r.Reason.Length <= SalesContract.MaximumReasonLength)
            && r.SalesDiscount is null && r.SalesPriceFloor is null && r.SalesQuoteConversion is null && r.IdentityAccessGrant is null
            && r.InventoryCostCancellation is null && r.PostingAccountMappingSelection is null && r.ApprovedSourceCatalogSelection is null
            && r.AccountingPeriodReopening is null && r.NormalJournalDraft is null && r.ProductPackageStandard is null
            && r.LogisticsCustomerReturn is null && r.SupplierReturnCostException is null && r.InventoryTransferOverage is null
            && r.LogisticsPlanExcess is null && r.InventoryTransferLoss is null && r.ManufacturingPlanReapproval is null;

    private static bool ValidSalesContractDiscountProof(string kind, WorkApprovalDecisionSnapshotV1 s) => kind != SalesContractDiscount
        ? s.SalesContractDiscountProof is null
        : s.SalesContractDiscountProof is { } p && s.CompanyId == p.CompanyId && s.Item.ItemId == p.ContractId
            && s.SourceVersion == p.Version && s.Pending == p.Pending && SalesContractDiscountDecisionBindingV1.MatchesDecisionProof(s.Item, p);

    private async Task SalesContractDiscountFenceAsync(WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(SalesContractDiscount, SalesPermissionManifestV1.ContractsDiscountApprove, s.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }
    private async Task<WorkSalesContractDiscountDecisionPreviewV1> SalesContractDiscountHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (salesContracts is not ISalesContractDiscountDecisionReadV1 reader || s.SalesContractDiscountProof is not { Historical: false }) throw new WorkDecisionException(503);
        SalesContractDiscountDecisionReadResultV1 answer;
        try { answer = await reader.ReadContractDiscountDecisionAsync(s.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is { State: SalesContractDiscountDecisionReadStateV1.Denied, Detail: null }) throw new WorkDecisionException(403);
        if (answer is not { State: SalesContractDiscountDecisionReadStateV1.Found, Detail: { } detail }) throw new WorkDecisionException(503);
        var view = WorkSalesContractDiscountDecisionPreviewV1.FromNative(detail);
        if (!SalesContractDiscountDecisionBindingV1.MatchesDecisionSnapshot(s, detail)) throw new WorkDecisionException(503);
        if (view.Contract.AmountDisclosure == SalesPriceDisclosure.Visible)
        {
            // Last async measurement; a reduced disclosure requires a fresh card, never a stale captured amount.
            var disclosed = false;
            try
            {
                var price = await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.OrdersPriceRead, s.CompanyId, token);
                disclosed = price.IsGranted && price.IsCompanyWide;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception failure) when (failure is InvalidOperationException or TimeoutException or System.Data.Common.DbException) { }
            token.ThrowIfCancellationRequested();
            if (!company.IsResolved || company.CompanyId != s.CompanyId || !user.IsAuthenticated || user.UserId != actor || !user.CanAccessCompany(s.CompanyId)) throw new WorkDecisionException(403);
            if (!disclosed) throw new WorkDecisionException(503);
        }
        return view;
        Task Fence(CancellationToken ct) => SalesContractDiscountFenceAsync(s, actor, ct);
    }
    private async Task<WorkDecisionResultV1> DecideSalesContractDiscountAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (salesContracts is not ISalesContractDiscountDecisionReadV1 || s.SalesContractDiscountProof is not { } p || p.ContractId != id) throw new WorkDecisionException(503);
        if (request.ExpectedVersion != p.Version) throw new WorkDecisionException(409, "work_decision_version_conflict");
        // No rewriting of native body/hash, version or reason. The canonical Work transport was checked before source IO.
        var body = new SalesDraftStateCommandRequest(request.ExpectedVersion, request.Reason);
        var metadata = new SalesCommandMetadata(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow);
        var command = request.Approve ? SalesContractCommands.ApproveDiscountV1 : SalesContractCommands.RejectDiscountV1;
        SalesContractCommandResult answer;
        try
        {
            answer = request.Approve ? await salesContracts.ApproveContractDiscountAsync(id, body, metadata, token)
                : await salesContracts.RejectContractDiscountAsync(id, body, metadata, token);
            await Fence(token);
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException refusal)
        { await Fence(token); throw new WorkDecisionException(refusal.Status, refusal.Code, refusal.IsReplay, refusal.OriginalCorrelationId); }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch (Exception failure) when (failure is InvalidOperationException or JsonException)
        { await Fence(token); throw new WorkDecisionException(503); }
        if (!SalesContractDiscountDecisionBindingV1.MatchesOriginalDecisionReceipt(s, answer, command, body, key, actor)
            || answer?.OriginalDecisionReceipt is not { } receipt || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId)
            || answer.OriginalCorrelationId != answer.OriginalCorrelationId.Trim() || answer.OriginalCorrelationId.Length > 128
            || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId) throw new WorkDecisionException(503);
        // Unknown after a native commit means response suppression. It does not undo or repeat that decision.
        return new(s.CompanyId, SalesContractDiscount, id, receipt.After.Contract.State.ToString(), answer.IsReplay, answer.OriginalCorrelationId);
        Task Fence(CancellationToken ct) => SalesContractDiscountFenceAsync(s, actor, ct);
    }
}

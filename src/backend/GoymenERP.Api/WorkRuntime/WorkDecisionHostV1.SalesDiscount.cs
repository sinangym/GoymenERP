using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidSalesDiscountRequest(string kind, WorkDecisionRequestV1 r) => kind != SalesDiscount
        ? r.SalesDiscount is null
        : r.Approve && r.Reason is null && SalesDiscountDecisionBindingV1.TryVersion(r.ExpectedVersion, out var version) && version < long.MaxValue
            && r.SalesDiscount?.Lines is { Count: > 0 and <= SalesOrderDraft.MaximumLines } lines
            && lines.All(x => x is not null && x.LineId != Guid.Empty && !string.IsNullOrWhiteSpace(x.Reason)
                && x.Reason.Trim().Length <= SalesOrderDraft.MaximumRejectionReasonLength)
            && lines.Select(x => x.LineId).Distinct().Count() == lines.Count
            && r.SalesPriceFloor is null && r.SalesQuoteConversion is null && r.IdentityAccessGrant is null && r.InventoryCostCancellation is null
            && r.PostingAccountMappingSelection is null && r.ApprovedSourceCatalogSelection is null && r.AccountingPeriodReopening is null
            && r.NormalJournalDraft is null && r.ProductPackageStandard is null && r.LogisticsCustomerReturn is null
            && r.SupplierReturnCostException is null && r.InventoryTransferOverage is null && r.LogisticsPlanExcess is null
            && r.InventoryTransferLoss is null && r.ManufacturingPlanReapproval is null;

    private static bool ValidSalesDiscountProof(string kind, WorkApprovalDecisionSnapshotV1 s) => kind != SalesDiscount
        ? s.SalesDiscountProof is null
        : s.SalesDiscountProof is { } p && s.CompanyId == p.CompanyId && s.Item.ItemId == p.DraftId
            && s.SourceVersion == p.Version && s.Pending == p.Pending && SalesDiscountDecisionBindingV1.MatchesDecisionProof(s.Item, p);

    private async Task SalesDiscountFenceAsync(WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(SalesDiscount, SalesPermissionManifestV1.OrdersDiscountExceptionApprove, s.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }

    private async Task<WorkSalesDiscountDecisionPreviewV1> SalesDiscountHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (salesOrders is not ISalesDiscountDecisionReadV1 reader || s.SalesDiscountProof is not { Historical: false })
            throw new WorkDecisionException(503);
        SalesDiscountDecisionReadResultV1 answer;
        try { answer = await reader.ReadDiscountDecisionAsync(s.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is { State: SalesDiscountDecisionReadStateV1.Denied, Detail: null }) throw new WorkDecisionException(403);
        if (answer is not { State: SalesDiscountDecisionReadStateV1.Found, Detail: { } detail }) throw new WorkDecisionException(503);
        var view = WorkSalesDiscountDecisionPreviewV1.FromNative(detail);
        if (!SalesDiscountDecisionBindingV1.MatchesDecisionSnapshot(s, detail)) throw new WorkDecisionException(503);
        if (view.PriceDisclosure == SalesPriceDisclosure.Visible)
        {
            // This is the last await. Withholding a changed card requires a fresh GET; no price authority is a co-grant.
            var disclosed = false;
            try
            {
                var price = await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.OrdersPriceRead, s.CompanyId, token);
                disclosed = price.IsGranted && price.IsCompanyWide;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception failure) when (failure is InvalidOperationException or TimeoutException or System.Data.Common.DbException)
            { /* An unreadable optional disclosure cannot release previously captured prices. */ }
            token.ThrowIfCancellationRequested();
            if (!company.IsResolved || company.CompanyId != s.CompanyId || !user.IsAuthenticated || user.UserId != actor || !user.CanAccessCompany(s.CompanyId))
                throw new WorkDecisionException(403);
            if (!disclosed) throw new WorkDecisionException(503);
        }
        return view;
        Task Fence(CancellationToken ct) => SalesDiscountFenceAsync(s, actor, ct);
    }

    private async Task<WorkDecisionResultV1> DecideSalesDiscountAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (salesOrders is not ISalesDiscountDecisionReadV1 || s.SalesDiscountProof is not { } p)
            throw new WorkDecisionException(503);
        var lines = request.SalesDiscount!.Lines;
        if (lines.Count != p.Lines.Count || lines.Any(x => p.Lines.All(y => y.LineId != x.LineId)))
            throw new WorkDecisionException(400, "work_decision_invalid");
        // Preserve raw canonical expected-version text, line order and literal reasons in the native hash.
        var body = new SalesDraftDiscountExceptionApprovalRequest(request.ExpectedVersion, Array.AsReadOnly(lines.ToArray()));
        var metadata = new SalesCommandMetadata(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow);
        SalesOrderDraftResponse answer;
        try
        {
            answer = await salesOrders.ApproveDiscountExceptionsAsync(id, body, metadata, token);
            await Fence(token);
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException refusal)
        { await Fence(token); throw new WorkDecisionException(refusal.Status, refusal.Code, refusal.IsReplay, refusal.OriginalCorrelationId); }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch (Exception failure) when (failure is InvalidOperationException or JsonException)
        { await Fence(token); throw new WorkDecisionException(503); }
        if (!SalesDiscountDecisionBindingV1.MatchesOriginalDecisionReceipt(s, answer, body, key, actor)
            || answer?.OriginalDiscountApprovalReceipt is not { } receipt || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId)
            || answer.OriginalCorrelationId.Length > 128 || answer.OriginalCorrelationId.Trim() != answer.OriginalCorrelationId
            || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId)
            throw new WorkDecisionException(503);
        // Proof failure after a committed native answer is unknown; it is never a rollback assertion.
        return new(s.CompanyId, SalesDiscount, id, receipt.OriginalResult.Visible.Draft.ApprovalState, answer.IsReplay, answer.OriginalCorrelationId);
        Task Fence(CancellationToken ct) => SalesDiscountFenceAsync(s, actor, ct);
    }
}

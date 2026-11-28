using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

internal sealed partial class SalesContractDiscountWorkInboxSourceV1 : IWorkApprovalDecisionVersionSourceV1
{
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token) => ReadPurposeAsync(context, itemId, null, token);
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 context, Guid itemId, string expectedProposalVersion, CancellationToken token) => ReadPurposeAsync(context, itemId, expectedProposalVersion, token);
    private async Task<WorkApprovalDecisionSnapshotV1?> ReadPurposeAsync(WorkInboxSourceContextV1 context, Guid id, string? selected, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token); if (id == Guid.Empty) return null;
        if (owner is null) throw new InvalidOperationException("Sözleşme karar okuma sahibi kayıtlı değil.");
        if (selected is not null && (!SalesContractDiscountDecisionDigestsV1.TryVersion(selected, out var v) || v == long.MaxValue))
            throw new InvalidOperationException("Sözleşme karar sürümü geçersiz.");
        var answer = await owner.ReadContractDiscountDecisionAsync(id, Fence, token); await Fence(token);
        if (answer is { State: SalesContractDiscountDecisionReadStateV1.Denied, Detail: null }) throw new UnauthorizedAccessException();
        if (answer is { State: SalesContractDiscountDecisionReadStateV1.NotFound, Detail: null }) return null;
        if (answer is not { State: SalesContractDiscountDecisionReadStateV1.Found, Detail: { } detail } || detail.CompanyId != context.CompanyId
            || detail.Contract?.Id != id || !SalesContractService.MatchesContractDiscountDecisionDetail(detail)) throw new InvalidOperationException("Sözleşme karar olguları doğrulanamadı.");
        WorkSalesContractDiscountProofV1 p;
        WorkInboxItemV1? item;
        if (selected is not null && selected != detail.Contract.Version)
        {
            var original = await owner.ReadContractDiscountOriginalVersionAsync(id, selected, Fence, token); await Fence(token);
            if (original is { State: SalesContractDiscountDecisionReadStateV1.Denied, Receipt: null }) throw new UnauthorizedAccessException();
            if (original is not { State: SalesContractDiscountDecisionReadStateV1.Found, Receipt: { } receipt } || receipt.CompanyId != context.CompanyId
                || receipt.ContractId != id || receipt.ExpectedVersion != selected || receipt.ActorId != context.UserId) throw new InvalidOperationException("Özgün sözleşme karar makbuzu yok.");
            p = SalesContractDiscountDecisionBindingV1.Proof(receipt); item = SalesContractDiscountDecisionBindingV1.Item(receipt);
        }
        else
        {
            // Source-level awaits may have reduced PriceRead after the owner's final disclosure. Never upgrade or release stale numbers.
            var prices = await AllowedAsync(context, SalesPermissionManifestV1.OrdersPriceRead, token);
            token.ThrowIfCancellationRequested(); if (!SameContext(context)) throw new UnauthorizedAccessException();
            if (!prices && detail.Contract.AmountDisclosure == SalesPriceDisclosure.Visible)
                detail = SalesContractService.ProjectContractDiscountDecision(detail.Source!, detail.ObservedBusinessDate!.Value, SalesPriceDisclosure.Withheld);
            p = SalesContractDiscountDecisionBindingV1.Proof(detail); item = SalesContractDiscountDecisionBindingV1.Item(detail);
        }
        if (item is null || !SalesContractDiscountDecisionBindingV1.MatchesDecisionProof(item, p)) throw new InvalidOperationException("Sözleşme karar kartı doğrulanamadı.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        return new(context.CompanyId, item, p.Version, p.Pending) { SalesContractDiscountProof = p };
        async Task Fence(CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (!await DecisionAllowedAsync(context, ct)) throw new UnauthorizedAccessException(); ct.ThrowIfCancellationRequested(); }
    }
}

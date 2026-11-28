using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Application;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

internal sealed partial class SalesDiscountExceptionWorkInboxSourceV1 : IWorkApprovalDecisionVersionSourceV1
{
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token) =>
        ReadPurposeAsync(context, itemId, null, token);
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        string expectedProposalVersion, CancellationToken token) => ReadPurposeAsync(context, itemId, expectedProposalVersion, token);

    private async Task<WorkApprovalDecisionSnapshotV1?> ReadPurposeAsync(WorkInboxSourceContextV1 context, Guid id, string? selected, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (id == Guid.Empty) return null;
        if (owner is null) throw new InvalidOperationException("İskonto karar okuma sahibi kayıtlı değil.");
        if (selected is not null)
        {
            if (!SalesDiscountDecisionBindingV1.TryVersion(selected, out var version) || version == long.MaxValue)
                throw new InvalidOperationException("Özgün iskonto karar sürümü geçersiz.");
            var original = await owner.ReadDiscountOriginalVersionAsync(id, selected, Fence, token); await Fence(token);
            if (original is { State: SalesDiscountDecisionReadStateV1.Denied, Receipt: null }) throw new UnauthorizedAccessException();
            if (original is { State: SalesDiscountDecisionReadStateV1.Found, Receipt: { } receipt })
            {
                var proof = SalesDiscountDecisionBindingV1.Proof(receipt);
                var item = SalesDiscountDecisionBindingV1.Item(receipt);
                if (proof.CompanyId != context.CompanyId || proof.DraftId != id || proof.Version != selected || receipt.ActorId != context.UserId || item is null)
                    throw new InvalidOperationException("Özgün iskonto makbuzu doğrulanamadı.");
                return new(context.CompanyId, item, selected, false) { SalesDiscountProof = proof };
            }
            if (original is not { State: SalesDiscountDecisionReadStateV1.NotFound, Receipt: null })
                throw new InvalidOperationException("Özgün iskonto makbuzu kullanılamıyor.");
            if (original.CurrentVersion is null) return null;
            if (original.CurrentVersion.Value.ToString(CultureInfo.InvariantCulture) != selected)
                throw new InvalidOperationException("Geçmiş iskonto kararının özgün makbuzu yok.");
        }
        var answer = await owner.ReadDiscountDecisionAsync(id, Fence, token); await Fence(token);
        if (answer is { State: SalesDiscountDecisionReadStateV1.Denied, Detail: null }) throw new UnauthorizedAccessException();
        if (answer is { State: SalesDiscountDecisionReadStateV1.NotFound, Detail: null }) return null;
        if (answer is not { State: SalesDiscountDecisionReadStateV1.Found, Detail: { } detail }
            || detail.CompanyId != context.CompanyId || detail.DraftId != id || selected is not null && detail.Version != selected
            || !SalesDraftService.MatchesDiscountDecisionDetail(detail))
            throw new InvalidOperationException("İskonto karar olguları doğrulanamadı.");
        // No await follows this price disclosure. Only reduce visibility after the final module/owner fences.
        var showPrices = detail.PriceDisclosure == SalesPriceDisclosure.Visible
            && await AllowedAsync(context, SalesPermissionManifestV1.OrdersPriceRead, token);
        token.ThrowIfCancellationRequested(); if (!SameContext(context)) throw new UnauthorizedAccessException();
        if (!showPrices) detail = SalesDraftService.ProjectDiscountDecision(detail.Source!, SalesPriceDisclosure.Withheld);
        if (SalesDiscountDecisionBindingV1.Item(detail) is not { } current) throw new InvalidOperationException("İskonto kartı doğrulanamadı.");
        var currentProof = SalesDiscountDecisionBindingV1.Proof(detail);
        return new(context.CompanyId, current, detail.Version, currentProof.Pending) { SalesDiscountProof = currentProof };

        async Task Fence(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!await DecisionAllowedAsync(context, cancellation)) throw new UnauthorizedAccessException();
            cancellation.ThrowIfCancellationRequested();
        }
    }
}

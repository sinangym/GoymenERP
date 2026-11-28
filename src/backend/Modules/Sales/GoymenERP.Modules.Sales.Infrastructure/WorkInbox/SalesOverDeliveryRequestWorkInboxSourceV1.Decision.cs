using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

internal sealed partial class SalesOverDeliveryRequestWorkInboxSourceV1 : IWorkApprovalDecisionVersionSourceV1
{
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token) => ReadSelectedAsync(context, itemId, null, token);
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 context, Guid itemId, string expectedProposalVersion, CancellationToken token) => ReadSelectedAsync(context, itemId, expectedProposalVersion, token);
    private async Task<WorkApprovalDecisionSnapshotV1?> ReadSelectedAsync(WorkInboxSourceContextV1 context, Guid id, string? selected, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (id == Guid.Empty) return null;
        if (owner is null) throw new InvalidOperationException("Fazla teslim karar okuma sahibi kayıtlı değil.");
        if (selected is not null)
        {
            if (!SalesRequestDecisionBindingV1.TryVersion(selected, out var n) || n == long.MaxValue) throw new InvalidOperationException("Özgün talep sürümü geçersiz.");
            var original = await owner.ReadOverDeliveryRequestOriginalVersionAsync(id, selected, Fence, token); await Fence(token);
            if (original is { State: SalesOverDeliveryRequestDecisionReadStateV1.Denied, Receipt: null }) throw new UnauthorizedAccessException();
            if (original is { State: SalesOverDeliveryRequestDecisionReadStateV1.Found, Receipt: { } receipt })
            {
                if (receipt.CompanyId != context.CompanyId || receipt.Target.RequestId != id || receipt.ExpectedRequestVersion != n || receipt.ActorId != context.UserId
                    || SalesOverDeliveryRequestDecisionBindingV1.Item(receipt) is not { } oldItem) throw new InvalidOperationException("Özgün fazla teslim makbuzu doğrulanamadı.");
                return new(context.CompanyId, oldItem, selected, false) { SalesOverDeliveryRequestProof = SalesOverDeliveryRequestDecisionBindingV1.Proof(receipt) };
            }
            if (original is not { State: SalesOverDeliveryRequestDecisionReadStateV1.NotFound, Receipt: null }) throw new InvalidOperationException("Özgün fazla teslim makbuzu kullanılamıyor.");
            if (original.CurrentVersion is null) return null;
            if (original.CurrentVersion.Value != n) throw new InvalidOperationException("Geçmiş fazla teslim kararının özgün makbuzu yok.");
        }
        var answer = await owner.ReadOverDeliveryRequestDecisionAsync(id, Fence, token); await Fence(token);
        if (answer is { State: SalesOverDeliveryRequestDecisionReadStateV1.Denied, Detail: null }) throw new UnauthorizedAccessException();
        if (answer is { State: SalesOverDeliveryRequestDecisionReadStateV1.NotFound, Detail: null }) return null;
        if (answer is not { State: SalesOverDeliveryRequestDecisionReadStateV1.Found, Detail: { } detail } || detail.CompanyId != context.CompanyId
            || detail.RequestId != id || selected is not null && detail.Request.Version != selected
            || SalesOverDeliveryRequestDecisionBindingV1.Item(detail) is not { } item) throw new InvalidOperationException("Fazla teslim karar olguları doğrulanamadı.");
        var proof = SalesOverDeliveryRequestDecisionBindingV1.Proof(detail);
        return new(context.CompanyId, item, proof.RequestVersion.ToString(CultureInfo.InvariantCulture), proof.RequestStatus == "Pending") { SalesOverDeliveryRequestProof = proof };
        async Task Fence(CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); if (!await DecisionAllowedAsync(context, cancellation)) throw new UnauthorizedAccessException(); cancellation.ThrowIfCancellationRequested(); }
    }
}

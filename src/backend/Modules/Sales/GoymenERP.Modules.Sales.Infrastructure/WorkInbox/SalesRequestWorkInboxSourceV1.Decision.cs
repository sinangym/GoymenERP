using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

internal sealed partial class SalesRequestWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(cancellationToken);
        if (itemId == Guid.Empty) return null;
        if (owner is null) throw new InvalidOperationException("Satış talebi karar okuma sahibi kayıtlı değil.");
        var answer = await owner.ReadRequestDecisionAsync(itemId, Fence, cancellationToken); await Fence(cancellationToken);
        if (answer is { State: SalesRequestDecisionReadStateV1.Denied, Detail: null }) throw new UnauthorizedAccessException();
        if (answer is { State: SalesRequestDecisionReadStateV1.NotFound, Detail: null }) return null;
        if (answer is not { State: SalesRequestDecisionReadStateV1.Found, Detail: { } detail }
            || detail.Detail?.Request is not { } request || request.CompanyId != context.CompanyId || request.Id != itemId
            || SalesRequestDecisionBindingV1.Item(detail) is not { } item)
            throw new InvalidOperationException("Satış talebi karar olguları doğrulanamadı.");
        return new(context.CompanyId, item, request.Version, request.State == "Submitted")
        { SalesRequestProof = SalesRequestDecisionBindingV1.Proof(detail) };

        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!await DecisionAllowedAsync(context, token)) throw new UnauthorizedAccessException();
            token.ThrowIfCancellationRequested();
        }
    }
}

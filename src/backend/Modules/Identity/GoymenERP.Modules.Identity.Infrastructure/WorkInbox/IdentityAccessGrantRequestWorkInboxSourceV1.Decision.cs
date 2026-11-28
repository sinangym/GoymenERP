using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Modules.Identity.Infrastructure.WorkInbox;

internal sealed partial class IdentityAccessGrantRequestWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await Fence(cancellationToken);
        if (itemId == Guid.Empty) return null;
        if (owner is null) throw new InvalidOperationException("Yetki talebi karar okuma sahibi kayıtlı değil.");
        var readContext = new IdentityAccessGrantDecisionReadContextV1(context.CompanyId, context.UserId, context.CorrelationId, context.CausationId);
        var detail = await owner.ReadDecisionAsync(itemId, readContext, Fence, cancellationToken);
        await Fence(cancellationToken);
        if (detail is null) return null;
        if (detail.Request?.RequestId != itemId || IdentityAccessGrantDecisionBindingV1.Item(context.CompanyId, detail) is not { } item)
            throw new InvalidOperationException("Yetki talebi karar olguları doğrulanamadı.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        return new(context.CompanyId, item, detail.Request.Version, detail.Request.Status == "Pending")
        { IdentityAccessGrantProof = IdentityAccessGrantDecisionBindingV1.Proof(context.CompanyId, detail) };

        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (context.GrantScope is not null || !Guid.TryParse(context.UserId, out var actor) || actor == Guid.Empty)
                throw new UnauthorizedAccessException();
            var refused = await CheckAsync(context, token);
            token.ThrowIfCancellationRequested();
            if (refused?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refused is not null) throw new InvalidOperationException("Yetki talebi güncel yetkisi doğrulanamadı.");
        }
    }

    internal static WorkInboxItemV1? DecisionItem(WorkIdentityAccessGrantProofV1 proof) => TryItem(
        new(proof.RequestId, proof.RequestedBy, proof.TargetUserId, proof.RoleId, proof.RequiresSecondApproval,
            proof.RequestedAtUtc, proof.ValidUntilUtc, proof.RoleName, proof.TargetName), out var item) ? item : null;
}

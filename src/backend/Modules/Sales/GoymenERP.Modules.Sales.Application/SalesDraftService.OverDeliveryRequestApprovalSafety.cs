using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService
{
    private bool overDeliveryRequestApprovalSafety;
    // Root composition enables only after reciprocal marker/Restore fences are connected.
    public SalesDraftService WithOverDeliveryRequestApprovalSafety()
    { overDeliveryRequestApprovalSafety = true; return this; }

    private async Task<SalesApprovalRequestCommandResult> RunOverDeliveryRequestApprovalAsync(Guid id, int no,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, bool approve, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        token.ThrowIfCancellationRequested();
        var boundary = store as ISalesOverDeliveryRequestApprovalStoreV1;
        var cleanBefore = boundary?.OverDeliveryRequestApprovalEntryIsClean == true;
        var permission = bindings.OverDeliveryApprovePermission;
        var scope = await RequireAsync(permission, token); token.ThrowIfCancellationRequested();
        _ = ExpectedVersion(request.ExpectedVersion); // Native authority, then shape, then entry refusal.
        if (boundary is null || !cleanBefore || !boundary.OverDeliveryRequestApprovalEntryIsClean)
            throw new SalesDraftServiceException(503, "service_unavailable");
        var command = approve ? SalesApprovalRequestCommands.ApproveOverDeliveryV1 : SalesApprovalRequestCommands.RejectOverDeliveryV1;
        var hash = SalesApprovalRequestHash.Compute(command, id, no, request);
        var envelope = Envelope(scope, metadata, permission!);
        try
        {
            return await boundary.RunOverDeliveryRequestApprovalAsync(envelope, hash, command, cleanBefore,
                ct => approve ? ApproveOverDeliveryRequestCoreAsync(id, no, request, metadata, ct)
                    : RejectApprovalRequestAsync(GoymenERP.Modules.Sales.Domain.ApprovalRequests.SalesApprovalRequestKind.OverDelivery,
                        id, no, request, command, permission, metadata, OverDeliveryRequestRejectedAudit, ct),
                async ct =>
                {
                    var replay = await FindApprovalReplayAsync(scope, permission, envelope, hash, command, ct);
                    return replay is null ? null : await ApprovalReplayedAsync(scope, permission, replay, ct);
                }, async ct => { scope = await RequireStillAsync(scope, permission, ct); ct.ThrowIfCancellationRequested(); }, token);
        }
        catch (SalesDraftStoreException refusal) { throw Store(refusal); }
    }
}

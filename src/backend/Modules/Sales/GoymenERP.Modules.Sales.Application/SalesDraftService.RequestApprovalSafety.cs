using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService
{
    private bool requestApprovalSafety;

    // Monotonic runtime composition: all aliases of this same scoped core use the protected request decisions.
    // Directly constructed legacy policy fixtures remain synthetic core evidence, not transaction acceptance.
    public SalesDraftService WithRequestApprovalSafety()
    {
        requestApprovalSafety = true;
        return this;
    }

    private async Task<SalesRequestResponse> RunRequestApprovalAsync(Guid requestId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, string commandName, Func<CancellationToken, Task<SalesRequestResponse>> execute,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        token.ThrowIfCancellationRequested();
        var boundary = requests as ISalesRequestApprovalStoreV1;
        var cleanBefore = boundary?.RequestApprovalEntryIsClean == true;
        var permission = bindings.RequestApprovePermission;
        var scope = await RequireAsync(permission, token); // Native 401/403/permission setup precedence.
        token.ThrowIfCancellationRequested();
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        if (boundary is null || !cleanBefore || !boundary.RequestApprovalEntryIsClean)
            throw new SalesDraftServiceException(503, "service_unavailable");
        var hash = SalesRequestHash.Compute(commandName, requestId, request);
        var envelope = Envelope(scope, metadata, permission!);
        try
        {
            return await boundary.RunRequestApprovalAsync(envelope, hash, commandName, cleanBefore, execute,
                async ct =>
                {
                    var replay = await FindRequestReplayAsync(scope, permission, envelope, hash, commandName, ct);
                    return replay is null ? null : RequestReplayed(replay);
                },
                async ct => { scope = await RequireStillAsync(scope, permission, ct); ct.ThrowIfCancellationRequested(); }, token);
        }
        catch (SalesDraftStoreException exception) { throw Store(exception); }
    }
}

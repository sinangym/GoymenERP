using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchaseServiceOrderServiceV1
{
    // Read/revalidate only. The parent separately proves the genuine historical approval; this method cannot approve a Draft.
    public async Task<PurchaseServiceOrderApprovalPreparationV1> ReadRetainedApprovalPreparationAsync(Guid serviceId, string expectedVersion,
        Func<CancellationToken, Task> freshOwnerFence, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(freshOwnerFence); token.ThrowIfCancellationRequested();
        var permission = PurchasingPermissionManifestV1.OrdersApprove; var captured = await RequireAsync(permission, token);
        var scope = captured with { ApprovalPreparationFence = Fence };
        await Fence(token);
        try
        {
            var original = FreezeOwner(await store.ReadForApprovalPreparationAsync(serviceId, Fence, token) ?? throw Failure(404));
            if (original.CompanyId != captured.Company || original.OrderId != serviceId || original.Status != PurchaseServiceOrderStatus.Approved
                || original.ApprovedBy is null || PurchaseMixedOrderMembershipV1.SelectedChildVersion(expectedVersion) != original.Version)
                throw new PurchaseServiceOrderException(PurchaseServiceOrder.VersionConflict);
            _ = await PrepareApprovalStateAsync(scope, original, token); await Fence(token);
            var final = FreezeOwner(await store.ReadForApprovalPreparationAsync(serviceId, Fence, token) ?? throw Failure(503));
            await Fence(token);
            if (!SameOwner(original, final)) throw new PurchaseServiceOrderException(PurchaseServiceOrder.VersionConflict);
            return new(captured.Company, captured.Actor, original); // Never rewrite the old approval or supplier snapshots.
        }
        catch (PurchaseServiceOrderException failure) { throw new PurchasingServiceException(409, failure.Code); }
        catch (SettingsPrerequisiteException) { throw Failure(503); }
        catch (ArgumentException) { throw Failure(422); }
        catch (OverflowException) { throw Failure(422); }
        catch (InvalidOperationException) { await Fence(token); throw Failure(503); }
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); await StillAsync(captured, permission, ct); ct.ThrowIfCancellationRequested();
            await freshOwnerFence(ct); ct.ThrowIfCancellationRequested(); await StillAsync(captured, permission, ct); ct.ThrowIfCancellationRequested();
        }
    }
}

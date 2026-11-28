using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchaseServiceOrderServiceV1
{
    public async Task<PurchaseServiceOrderApprovalPreparationV1> ReadApprovalPreparationAsync(Guid serviceId, string expectedVersion,
        Func<CancellationToken, Task> freshOwnerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshOwnerFence);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = PurchasingPermissionManifestV1.OrdersApprove;
        var captured = await RequireAsync(permission, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = captured with { ApprovalPreparationFence = Fence };
        await Fence(cancellationToken);
        if (serviceId == Guid.Empty) throw Failure(400);
        try
        {
            var observed = await store.ReadForApprovalPreparationAsync(serviceId, Fence, cancellationToken);
            await Fence(cancellationToken);
            var original = FreezeOwner(observed ?? throw Failure(404));
            if (original.CompanyId != captured.Company || original.OrderId != serviceId) throw Failure(503);
            var prepared = await PrepareApprovalStateAsync(scope, original, cancellationToken);
            // Pure native guards only: this approved copy is discarded, never written or represented as an approval.
            _ = PurchaseServiceOrder.Approve(prepared, Version(expectedVersion), captured.Actor, clock.GetUtcNow());
            await Fence(cancellationToken);
            var current = await store.ReadForApprovalPreparationAsync(serviceId, Fence, cancellationToken);
            await Fence(cancellationToken);
            var latest = FreezeOwner(current ?? throw Failure(503));
            if (!SameOwner(original, latest)) throw new PurchaseServiceOrderException(PurchaseServiceOrder.VersionConflict);
            return new(captured.Company, captured.Actor, prepared);
        }
        catch (PurchaseServiceOrderException failure) { throw new PurchasingServiceException(409, failure.Code); }
        catch (SettingsPrerequisiteException) { throw Failure(503); }
        catch (ArgumentException) { throw Failure(422); }
        catch (OverflowException) { throw Failure(422); }
        catch (InvalidOperationException)
        {
            await Fence(cancellationToken);
            throw Failure(503);
        }

        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await StillAsync(captured, permission, token); token.ThrowIfCancellationRequested();
            await freshOwnerFence(token); token.ThrowIfCancellationRequested();
            await StillAsync(captured, permission, token); token.ThrowIfCancellationRequested();
        }
    }

    private async Task<PurchaseServiceOrderState> PrepareApprovalStateAsync(Scope scope, PurchaseServiceOrderState value, CancellationToken ct)
    {
        var supplier = await SupplierAsync(scope, value.SupplierPartyId, value.CompanyAccountId, value.Kind, PurchasingPermissionManifestV1.OrdersApprove, ct);
        foreach (var line in value.PriceRevision == 0 ? value.Lines : [])
        {
            var type = await ReadAsync(scope, PurchasingPermissionManifestV1.OrdersApprove,
                () => referenceLists.GetAsync(scope.Company, SettingsReferenceListKindV1.ServiceType, line.ServiceTypeId, ct), ct);
            if (type.Outcome == SettingsReferenceListLookupOutcomeV1.Unavailable) throw Failure(503);
            if (type.Outcome != SettingsReferenceListLookupOutcomeV1.Found || type.Entry?.Active != true || type.Entry.Id != line.ServiceTypeId)
                throw new PurchaseServiceOrderException(PurchaseServiceOrder.StateConflict);
            if (type.Entry.Version != line.ServiceTypeVersion) throw new PurchaseServiceOrderException(PurchaseServiceOrder.VersionConflict);
        }
        // A price-only revision retains the original approved material identities. A later stock movement
        // cannot invalidate already performed service or require the same label to be physically sent again.
        if (!await ReadAsync(scope, PurchasingPermissionManifestV1.OrdersApprove,
            () => store.HasApprovedPhysicalReferencesAsync(value, ct), ct))
        {
            await VerifyPhysicalAsync(scope, PurchasingPermissionManifestV1.OrdersApprove, value.BusinessDate, value.Materials, value.ExpectedOutputs, ct);
        }
        if (value.Kind == PurchaseServiceOrderKind.OwnMaterialSubcontract)
            await VerifyTaxSnapshotsAsync(scope, value, ct);
        return value with { PartyVersion = supplier.PartyVersion, AccountVersion = supplier.AccountVersion };
    }

    // Reuse the actual native disclosure/quantity/tax/money formatter without a second priced projection.
    internal static PurchaseServiceOrderViewV1 ApprovalPreviewView(PurchaseServiceOrderState value, bool prices) => View(value, prices);

    private static PurchaseServiceOrderState FreezeOwner(PurchaseServiceOrderState value)
    {
        if (value.Money is null || value.Lines is null || value.Materials is null || value.ExpectedOutputs is null || value.Acceptances is null
            || value.Lines.Any(x => x is null) || value.Materials.Any(x => x is null)
            || value.ExpectedOutputs.Any(x => x is null) || value.Acceptances.Any(x => x is null)) throw Failure(503);
        try { PurchaseServiceOrder.Validate(value); }
        catch (ArgumentException) { throw Failure(503); }
        // Detach all collection containers before the first reference/provider await.
        return value with { Lines = Array.AsReadOnly(value.Lines.ToArray()), Materials = Array.AsReadOnly(value.Materials.ToArray()),
            ExpectedOutputs = Array.AsReadOnly(value.ExpectedOutputs.ToArray()), Acceptances = Array.AsReadOnly(value.Acceptances.ToArray()) };
    }

    private static bool SameOwner(PurchaseServiceOrderState a, PurchaseServiceOrderState b) =>
        (a with { Lines = b.Lines, Materials = b.Materials, ExpectedOutputs = b.ExpectedOutputs, Acceptances = b.Acceptances }) == b
        && a.Lines.SequenceEqual(b.Lines) && a.Materials.SequenceEqual(b.Materials)
        && a.ExpectedOutputs.SequenceEqual(b.ExpectedOutputs) && a.Acceptances.SequenceEqual(b.Acceptances);
}

using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchaseServiceOrderServiceV1
{
    // A Draft setter only for a genuine linked supplier-revision origin verified by the actual store.
    // Existing Approved-only RevisePrices and its native wire/hash remain unchanged.
    public Task<PurchaseServiceOrderResponseV1> SetSupplierRevisionPricesAsync(Guid id, RevisePurchaseServiceOrderPricesV1 request,
        PurchasingCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CommandAsync(id, PurchaseServiceOrderCommandsV1.SetSupplierRevisionPrices, PurchasingPermissionManifestV1.OrdersRevise,
            request, request.Reason, metadata, (scope, old, _) =>
            {
                if (request.Lines is not { Count: > 0 and <= 100 } || request.Lines.Any(x => x is null)
                    || request.Lines.Select(x => x.LineId).Distinct().Count() != request.Lines.Count) throw new ArgumentException();
                return Task.FromResult(PurchaseServiceOrder.SetSupplierRevisionPrices(old ?? throw Failure(404), Version(request.ExpectedVersion),
                    request.Lines.ToDictionary(x => x.LineId, x => Decimal(x.UnitPrice, zero: true)), scope.Actor, request.Reason, clock.GetUtcNow()));
            }, token);
    }
    public async Task<PurchaseServiceSupplierRevisionPreparationV1> ReadSupplierRevisionPreparationAsync(Guid serviceId, string expectedVersion,
        Guid supplierPartyId, Guid companyAccountId, string reason, Func<CancellationToken, Task> freshOwnerFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshOwnerFence); cancellationToken.ThrowIfCancellationRequested();
        const string permission = PurchasingPermissionManifestV1.OrdersRevise;
        var captured = await RequireAsync(permission, cancellationToken); await Fence(cancellationToken);
        var scope = captured with { ApprovalPreparationFence = Fence };
        try
        {
            var facts = await store.ReadSupplierRevisionFactsAsync(serviceId, Fence, cancellationToken) ?? throw Failure(404);
            await Fence(cancellationToken);
            if (facts.Original is null) throw Failure(503);
            var original = FreezeOwner(facts.Original);
            if (original.CompanyId != captured.Company || original.OrderId != serviceId) throw Failure(503);
            var supplier = await SupplierAsync(scope, supplierPartyId, companyAccountId, original.Kind, permission, cancellationToken);
            var candidate = PurchaseServiceOrder.PrepareSupplierRevision(original, Version(expectedVersion), supplierPartyId, companyAccountId,
                supplier.PartyVersion, supplier.AccountVersion, captured.Actor, reason, clock.GetUtcNow(), facts.InvoiceHistory);
            // Actual existing reference/tax/physical validators, under the native revision permission. No approval copy or write.
            candidate = await PrepareApprovalStateAsync(scope, candidate, cancellationToken, permission);
            await Fence(cancellationToken);
            var latest = await store.ReadSupplierRevisionFactsAsync(serviceId, Fence, cancellationToken) ?? throw Failure(503);
            await Fence(cancellationToken);
            if (latest.Original is null) throw Failure(503);
            if (latest.InvoiceHistory != facts.InvoiceHistory || !SameOwner(original, FreezeOwner(latest.Original)))
                throw new PurchaseServiceOrderException(PurchaseServiceOrder.VersionConflict);
            return new(captured.Company, captured.Actor, original, candidate);
        }
        catch (PurchaseServiceOrderException failure) { throw new PurchasingServiceException(409, failure.Code); }
        catch (PurchasingRuleException failure) { throw new PurchasingServiceException(failure.Code == PurchaseOrder.SupplierRevisionInvoiceUnavailable ? 503 : 409, failure.Code); }
        catch (SettingsPrerequisiteException) { throw Failure(503); }
        catch (ArgumentException) { throw Failure(422); }
        catch (OverflowException) { throw Failure(422); }
        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); await StillAsync(captured, permission, token); token.ThrowIfCancellationRequested();
            await freshOwnerFence(token); token.ThrowIfCancellationRequested(); await StillAsync(captured, permission, token); token.ThrowIfCancellationRequested();
        }
    }
}

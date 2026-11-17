using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using static GoymenERP.Modules.Inventory.Application.InventorySubcontractDeliveryContractV1;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Existing physical-allocation write authority; no general Inventory read or Sales grant is inferred.</summary>
public sealed class InventorySubcontractAllocationReleaseServiceV1(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IInventorySubcontractAllocationReleaseStoreV1 store)
    : IInventorySubcontractAllocationReleaseServiceV1
{
    public async Task<InventorySubcontractAllocationReleaseCandidatesV1> ListAsync(string? cursor, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken); Guid? after = null;
        if (cursor is not null)
        {
            if (!Guid.TryParseExact(cursor, "D", out var value) || value == Guid.Empty || value.ToString("D") != cursor)
                throw Error(400, "invalid_cursor");
            after = value;
        }
        var result = await FencedAsync(() => store.ListAsync(after, ct => StillAsync(scope, ct), cancellationToken), scope, cancellationToken);
        if (result is null || result.Items is null || result.Items.Count > 50 || result.Items.Any(x => x is null || x.Allocation is null
                || x.Allocation.CompanyId != scope.CompanyId || x.Allocation.DemandKind != InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine))
            throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
        return result;
    }

    public async Task<InventorySubcontractAllocationReleaseResultV1> ReleaseAsync(Guid allocationId,
        ReleaseInventorySubcontractAllocationV1 request, InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (allocationId == Guid.Empty || request is null || metadata is null || !Text(request.Reason, 1000)
            || !Text(metadata.IdempotencyKey, 128) || !Text(metadata.CorrelationId, 128) || !Text(metadata.SourceChannel, 128)
            || metadata.CausationId is not null && !Text(metadata.CausationId, 128)) throw Error(400, InventoryAllocationCodesV1.ValidationFailed);
        try { _ = Version(request.ExpectedAllocationVersion); }
        catch (InventoryDefiniteShipmentException) { throw Error(400, InventoryAllocationCodesV1.ValidationFailed); }
        var actual = metadata with { ActorId = scope.ActorId, PermissionCode = ErpPermissions.InventoryPhysicalAllocationWrite,
            RoleCodes = scope.Roles };
        var result = await FencedAsync(() => store.ReleaseAsync(allocationId, request, actual, ct => StillAsync(scope, ct), cancellationToken), scope, cancellationToken);
        if (result?.Release?.Allocation is not { } value || value.CompanyId != scope.CompanyId || value.AllocationId != allocationId
            || value.DemandKind != InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine || value.Status != "Released" || value.RemainingKg != "0"
            || result.Release.ReleaseReason != request.Reason) throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
        return result;
    }

    private async Task<T> FencedAsync<T>(Func<Task<T>> action, Scope scope, CancellationToken token)
    {
        try { var value = await action(); await StillAsync(scope, token); return value; }
        catch (OperationCanceledException) { throw; }
        catch (InventoryAllocationException) { await StillAsync(scope, token); throw; }
        catch (InventoryDefiniteShipmentException error)
        {
            await StillAsync(scope, token);
            throw Error(error.Status, error.Status == 503 ? InventorySubcontractAllocationReleaseV1.Unavailable : error.Code);
        }
        catch { await StillAsync(scope, token); throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable); }
    }
    private async Task StillAsync(Scope scope, CancellationToken token) => _ = await RequireAsync(scope, token);
    private async Task<Scope> RequireAsync(Scope? expected, CancellationToken token)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw Error(expected is null ? 401 : 403, expected is null ? "unauthenticated" : "forbidden");
        if (!company.IsResolved || company.CompanyId == Guid.Empty) throw Error(403, "forbidden");
        var scope = new Scope(company.CompanyId, user.UserId, []);
        if (expected is not null && (expected.CompanyId != scope.CompanyId || expected.ActorId != scope.ActorId)) throw Error(403, "forbidden");
        Identity(scope); PermissionScope permission;
        try { permission = await authority.RevalidatePermissionScopeAsync(ErpPermissions.InventoryPhysicalAllocationWrite, scope.CompanyId, token); }
        catch (OperationCanceledException) { throw; }
        catch { Identity(scope); throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable); }
        token.ThrowIfCancellationRequested(); Identity(scope);
        if (!permission.IsGranted || !permission.IsCompanyWide) throw Error(403, "forbidden");
        return scope with { Roles = [.. permission.RoleCodes.Order(StringComparer.Ordinal)] };
    }
    private void Identity(Scope scope)
    {
        if (!company.IsResolved || company.CompanyId != scope.CompanyId || !user.IsAuthenticated || user.UserId != scope.ActorId
            || !user.CanAccessCompany(scope.CompanyId)) throw Error(403, "forbidden");
    }
    private static InventoryAllocationException Error(int status, string code) => new(status, code);
    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> Roles);
}

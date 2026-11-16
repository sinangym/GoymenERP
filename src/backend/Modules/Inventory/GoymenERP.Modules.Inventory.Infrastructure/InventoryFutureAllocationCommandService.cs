using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>The user boundary adds no transaction; the Inventory writer owns locks, command replay, audit and outbox.</summary>
public sealed class InventoryFutureAllocationCommandService(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IInventoryFutureAllocationWriterV1 writer) : IInventoryFutureAllocationCommandV1
{
    public Task<InventoryFutureAllocationResultV1> CreateAsync(CreateFutureSupplyAllocationV1 request,
        InventoryCommandMetadata transport, CancellationToken cancellationToken = default) => ExecuteAsync(
            (metadata, fence) => writer.CreateAsync(request, metadata, fence, cancellationToken), transport, cancellationToken);

    public Task<InventoryFutureAllocationResultV1> ReleaseAsync(Guid allocationId, ReleaseFutureSupplyAllocationV1 request,
        InventoryCommandMetadata transport, CancellationToken cancellationToken = default) => ExecuteAsync(
            (metadata, fence) => writer.ReleaseAsync(allocationId, request, metadata, fence, cancellationToken), transport, cancellationToken);

    private async Task<InventoryFutureAllocationResultV1> ExecuteAsync(
        Func<InventoryCommandMetadata, IInventoryFutureAllocationAuthorityFenceV1, Task<InventoryFutureAllocationResultV1>> operation,
        InventoryCommandMetadata transport, CancellationToken cancellationToken)
    {
        var fence = new InventoryFutureAllocationUserFence(company, user, authority, ErpPermissions.InventoryFutureAllocationWrite);
        await fence.RequireStillAsync(cancellationToken);
        var scope = fence.LastScope;
        var metadata = transport with { ActorId = fence.ActorId, PermissionCode = ErpPermissions.InventoryFutureAllocationWrite,
            RoleCodes = scope.RoleCodes.Order(StringComparer.Ordinal).ToArray() };
        InventoryFutureAllocationResultV1 result;
        try { result = await operation(metadata, fence); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await fence.RequireStillAsync(cancellationToken);
            throw;
        }
        await fence.RequireStillAsync(cancellationToken);
        if (result.Allocation.CompanyId != fence.CompanyId)
            throw new InventoryAllocationException(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
        return result;
    }
}

internal sealed class InventoryFutureAllocationUserFence(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, string permission) : IInventoryFutureAllocationAuthorityFenceV1
{
    public Guid CompanyId { get; } = company.CompanyId;
    public string ActorId { get; } = user.UserId ?? string.Empty;
    public PermissionScope LastScope { get; private set; } = PermissionScope.Denied;

    public async Task RequireStillAsync(CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated) throw new InventoryAllocationException(401, "unauthenticated");
        if (!Matches()) throw new InventoryAllocationException(403, "forbidden");
        if (!ErpPermissions.IsKnown(permission)) throw new InventoryAllocationException(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
        LastScope = await authority.RevalidatePermissionScopeAsync(permission, CompanyId, cancellationToken);
        if (!LastScope.IsGranted || !LastScope.IsCompanyWide || !Matches()) throw new InventoryAllocationException(403, "forbidden");
    }

    private bool Matches() => CompanyId != Guid.Empty && company.IsResolved && company.CompanyId == CompanyId
        && !string.IsNullOrWhiteSpace(ActorId) && user.IsAuthenticated && string.Equals(user.UserId, ActorId, StringComparison.Ordinal)
        && user.CanAccessCompany(CompanyId);
}

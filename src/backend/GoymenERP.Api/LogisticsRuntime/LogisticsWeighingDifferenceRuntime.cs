using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.LogisticsRuntime;

/// <summary>
/// STK-S24 permission fence of the weighing-difference record: the server-resolved company, the authenticated actor and a
/// fresh, company-wide grant of the exact permission (the transfer outgoing-document fence).
/// </summary>
internal sealed class LogisticsWeighingDifferenceScope(
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority) : ILogisticsWeighingDifferenceScopeV1
{
    public async Task<LogisticsWeighingDifferenceScopeV1> RequireAsync(string permission,
        CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId))
            throw new LogisticsWeighingDifferenceServiceException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
            throw new LogisticsWeighingDifferenceServiceException(403, "forbidden");
        if (string.IsNullOrWhiteSpace(permission) || !ErpPermissions.IsKnown(permission))
            throw new LogisticsWeighingDifferenceServiceException(503, "permission_unregistered");

        var companyId = company.CompanyId;
        var actorId = user.UserId;
        var measured = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(companyId, actorId))
            throw new LogisticsWeighingDifferenceServiceException(403, "forbidden");
        return new(companyId, actorId, [.. measured.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    public async Task RequireStillAsync(LogisticsWeighingDifferenceScopeV1 scope, string permission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId
            || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
            throw new LogisticsWeighingDifferenceServiceException(403, "forbidden");
    }

    private bool Matches(Guid companyId, string actorId) => user.IsAuthenticated
        && string.Equals(user.UserId, actorId, StringComparison.Ordinal)
        && company.IsResolved && company.CompanyId == companyId && user.CanAccessCompany(companyId);
}

/// <summary>
/// Host composition seam: Inventory owns the physical balances, Logistics consumes them through its own port. The adapter only
/// reads; any failure other than cancellation answers "unreadable" so the record is refused, never evaluated on a guess.
/// </summary>
internal sealed class InventoryWeighingDifferenceStockAdapter(IInventoryProductPhysicalStockReadPortV1 inventory)
    : ILogisticsWeighingDifferenceStockReadPortV1
{
    public async Task<LogisticsWeighingDifferenceStockReadV1> ReadAsync(Guid companyId,
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        InventoryProductPhysicalStockReadV1 read;
        try
        {
            read = await inventory.ReadAsync(companyId, productIds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return LogisticsWeighingDifferenceStockReadV1.Unreadable;
        }

        return read is { IsReadable: true }
            ? new LogisticsWeighingDifferenceStockReadV1(true, read.PhysicalKgByProduct)
            : LogisticsWeighingDifferenceStockReadV1.Unreadable;
    }
}

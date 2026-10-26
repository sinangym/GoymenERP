using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Infrastructure;

internal sealed record InventoryPhysicalAllocationReadRecord(
    InventoryPhysicalAllocationViewV1 Allocation,
    long LabelVersion);

internal sealed record InventoryPhysicalAllocationReadWindow(
    IReadOnlyList<InventoryPhysicalAllocationReadRecord> Records,
    bool HasMore);

internal interface IInventoryPhysicalAllocationReader
{
    Task<InventoryPhysicalAllocationReadWindow> SearchAsync(
        Guid companyId,
        Guid salesOrderId,
        Guid salesOrderLineId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Null when the label is unknown in the company.</summary>
    Task<InventoryLabelPhysicalAllocationsV1?> ReadLabelAsync(
        Guid companyId,
        Guid labelId,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>Optional exact capability; existing bounded-reader adapters remain valid.</summary>
internal interface IInventoryPhysicalAllocationExactReader
{
    Task<InventoryPhysicalAllocationExactReadV1?> ReadExactAsync(
        Guid companyId,
        Guid allocationId,
        CancellationToken cancellationToken);
}

internal sealed class InventoryPhysicalAllocationReadService(
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IInventoryPhysicalAllocationReader? reader) : IInventoryPhysicalAllocationReadsV1, IInventoryPhysicalAllocationExactReadsV1
{
    public async Task<InventoryPhysicalAllocationExactReadV1> ReadAsync(
        Guid allocationId,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (allocationId == Guid.Empty)
        {
            throw Error(400, InventoryPhysicalAllocationReadCodesV1.InvalidQuery);
        }

        var source = reader as IInventoryPhysicalAllocationExactReader
            ?? throw Error(503, InventoryPhysicalAllocationReadCodesV1.Unavailable);
        var answer = await FencedAsync(
            () => source.ReadExactAsync(scope.CompanyId, allocationId, cancellationToken), scope, cancellationToken)
            ?? throw Error(404, InventoryPhysicalAllocationReadCodesV1.NotFound);
        if (answer.Allocation is not { } allocation)
        {
            throw Error(503, InventoryPhysicalAllocationReadCodesV1.Unavailable);
        }

        if (allocation.AllocationId != allocationId || allocation.DemandId == Guid.Empty || allocation.DemandLineId == Guid.Empty
            || !PositiveVersion(answer.LabelVersion)
            || !Enum.TryParse<StockAllocationStatus>(allocation.Status, false, out var status) || !Enum.IsDefined(status)
            || status.ToString() != allocation.Status
            || !Enum.TryParse<StockPartyKind>(answer.OwnerKind, false, out var owner) || !Enum.IsDefined(owner)
            || owner.ToString() != answer.OwnerKind || answer.OwnerId == Guid.Empty
            || !Enum.TryParse<StockPartyKind>(answer.CustodianKind, false, out var custodian) || !Enum.IsDefined(custodian)
            || custodian.ToString() != answer.CustodianKind || answer.CustodianId == Guid.Empty || answer.LocationId == Guid.Empty
            || (status == StockAllocationStatus.Released) != (answer.ReleasedAtUtc is not null)
            || (answer.ReleasedAtUtc is null) != (answer.ReleaseReason is null)
            || answer.ReleasedAtUtc is { } released && (released.Offset != TimeSpan.Zero || released < allocation.CreatedAtUtc
                || released > allocation.UpdatedAtUtc || string.IsNullOrWhiteSpace(answer.ReleaseReason)))
        {
            throw Error(503, InventoryPhysicalAllocationReadCodesV1.Unavailable);
        }

        _ = Item(new(allocation, long.Parse(answer.LabelVersion, CultureInfo.InvariantCulture)), scope.CompanyId,
            new(allocation.DemandId, allocation.DemandLineId));
        return answer;
    }

    public async Task<InventoryPhysicalAllocationReadsV1> SearchAsync(
        InventoryPhysicalAllocationReadQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (query is null || query.SalesOrderId == Guid.Empty || query.SalesOrderLineId == Guid.Empty
            || query.Limit is < 1 or > 100)
        {
            throw Error(400, InventoryPhysicalAllocationReadCodesV1.InvalidQuery);
        }

        var source = reader ?? throw Error(503, InventoryPhysicalAllocationReadCodesV1.Unavailable);
        var window = await FencedAsync(
            () => source.SearchAsync(
                scope.CompanyId,
                query.SalesOrderId,
                query.SalesOrderLineId,
                query.Limit,
                cancellationToken),
            scope,
            cancellationToken);

        return new InventoryPhysicalAllocationReadsV1(
            [.. window.Records.Select(record => Item(record, scope.CompanyId, query))],
            window.HasMore);
    }

    /// <summary>The label's newest allocations (at most <see cref="LabelWindow"/>) with release facts and change history.</summary>
    public const int LabelWindow = 50;

    public async Task<InventoryLabelPhysicalAllocationsV1> ReadLabelAsync(
        Guid labelId,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (labelId == Guid.Empty)
        {
            throw Error(400, InventoryPhysicalAllocationReadCodesV1.InvalidQuery);
        }

        var source = reader ?? throw Error(503, InventoryPhysicalAllocationReadCodesV1.Unavailable);
        var answer = await FencedAsync(
            () => source.ReadLabelAsync(scope.CompanyId, labelId, LabelWindow, cancellationToken),
            scope,
            cancellationToken)
            ?? throw Error(404, InventoryPhysicalAllocationReadCodesV1.NotFound);
        if (answer.LabelId != labelId || !PositiveVersion(answer.LabelVersion) || answer.Items.Count > LabelWindow
            || answer.Items.Select(item => item.Allocation.AllocationId).Distinct().Count() != answer.Items.Count
            || answer.Items.Any(item => item.Allocation.CompanyId != scope.CompanyId
                || item.Allocation.LabelId != labelId
                || !string.Equals(item.Allocation.DemandKind, InventoryAllocationDemandKindsV1.SalesOrderLine,
                    StringComparison.Ordinal)
                || !Enum.TryParse<StockAllocationStatus>(item.Allocation.Status, false, out var status)
                || !Enum.IsDefined(status)
                || !Quantity(item.Allocation.AllocatedKg, positive: true, out var allocated)
                || !Quantity(item.Allocation.RemainingKg, positive: false, out var remaining)
                || remaining > allocated
                || status == StockAllocationStatus.Active && remaining <= 0m
                || status != StockAllocationStatus.Active && remaining != 0m
                || (status == StockAllocationStatus.Released) != (item.ReleasedAtUtc is not null)
                || (item.ReleasedAtUtc is null) != (item.ReleaseReason is null)
                || item.Changes.Any(entry => entry.SourceAllocationId != item.Allocation.AllocationId
                    && entry.TargetAllocationId != item.Allocation.AllocationId)))
        {
            throw Error(503, InventoryPhysicalAllocationReadCodesV1.Unavailable);
        }

        return answer;
    }

    private static InventoryPhysicalAllocationReadItemV1 Item(
        InventoryPhysicalAllocationReadRecord record,
        Guid companyId,
        InventoryPhysicalAllocationReadQueryV1 query)
    {
        var allocation = record.Allocation;
        if (record.LabelVersion <= 0
            || allocation.CompanyId != companyId
            || allocation.AllocationId == Guid.Empty
            || allocation.LabelId == Guid.Empty
            || allocation.ProductId == Guid.Empty
            || !string.Equals(
                allocation.DemandKind,
                InventoryAllocationDemandKindsV1.SalesOrderLine,
                StringComparison.Ordinal)
            || allocation.DemandId != query.SalesOrderId
            || allocation.DemandLineId != query.SalesOrderLineId
            || string.IsNullOrWhiteSpace(allocation.DemandProductVersion)
            || string.IsNullOrWhiteSpace(allocation.Reason)
            || allocation.CreatedAtUtc == default
            || allocation.CreatedAtUtc.Offset != TimeSpan.Zero
            || allocation.UpdatedAtUtc.Offset != TimeSpan.Zero
            || allocation.UpdatedAtUtc < allocation.CreatedAtUtc
            || !PositiveVersion(allocation.Version)
            || !Enum.TryParse<StockAllocationStatus>(allocation.Status, false, out var status)
            || !Enum.IsDefined(status)
            || !Quantity(allocation.AllocatedKg, positive: true, out var allocated)
            || !Quantity(allocation.RemainingKg, positive: false, out var remaining)
            || remaining > allocated
            || status == StockAllocationStatus.Active && remaining <= 0m
            || status != StockAllocationStatus.Active && remaining != 0m)
        {
            throw Error(503, InventoryPhysicalAllocationReadCodesV1.Unavailable);
        }

        return new InventoryPhysicalAllocationReadItemV1(
            allocation,
            record.LabelVersion.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<T> FencedAsync<T>(Func<Task<T>> read, Scope scope, CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RequireAsync(scope, cancellationToken);
            if (exception is InventoryPhysicalAllocationReadException)
            {
                throw;
            }

            throw Error(503, InventoryPhysicalAllocationReadCodesV1.Unavailable, exception);
        }

        await RequireAsync(scope, cancellationToken);
        return result;
    }

    private async Task<Scope> RequireAsync(Scope? expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId))
        {
            throw Error(expected is null ? 401 : 403, expected is null
                ? InventoryPhysicalAllocationReadCodesV1.Unauthenticated
                : InventoryPhysicalAllocationReadCodesV1.Forbidden);
        }

        if (!company.IsResolved)
        {
            throw Error(403, InventoryPhysicalAllocationReadCodesV1.Forbidden);
        }

        var scope = new Scope(company.CompanyId, user.UserId);
        if (expected is not null && expected != scope)
        {
            throw Error(403, InventoryPhysicalAllocationReadCodesV1.Forbidden);
        }

        RequireIdentity(scope);
        PermissionScope grant;
        try
        {
            grant = await authority.RevalidatePermissionScopeAsync(
                ErpPermissions.InventoryPhysicalAllocationWrite,
                scope.CompanyId,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            RequireIdentity(scope);
            throw Error(503, InventoryPhysicalAllocationReadCodesV1.Unavailable, exception);
        }

        cancellationToken.ThrowIfCancellationRequested();
        RequireIdentity(scope);
        if (!grant.IsGranted || !grant.IsCompanyWide)
        {
            throw Error(403, InventoryPhysicalAllocationReadCodesV1.Forbidden);
        }

        return scope;
    }

    private void RequireIdentity(Scope scope)
    {
        if (!company.IsResolved || company.CompanyId != scope.CompanyId || scope.CompanyId == Guid.Empty
            || !user.IsAuthenticated || user.UserId != scope.ActorId || !user.CanAccessCompany(scope.CompanyId))
        {
            throw Error(403, InventoryPhysicalAllocationReadCodesV1.Forbidden);
        }
    }

    private static bool PositiveVersion(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.All(char.IsAsciiDigit)
        && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version > 0;

    private static bool Quantity(string? value, bool positive, out decimal quantity)
    {
        quantity = 0m;
        return !string.IsNullOrEmpty(value)
            && value.Length <= 40
            && value.Count(character => character == '.') <= 1
            && value.All(character => char.IsAsciiDigit(character) || character == '.')
            && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out quantity)
            && decimal.Round(quantity, 3, MidpointRounding.ToZero) == quantity
            && (positive ? quantity > 0m : quantity >= 0m);
    }

    private static InventoryPhysicalAllocationReadException Error(
        int status,
        string code,
        Exception? exception = null) => new(status, code, exception);

    private sealed record Scope(Guid CompanyId, string ActorId);
}

using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Infrastructure;

internal sealed record InventoryActiveAllocationRecord(
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    string Block,
    decimal RemainingKg);

internal sealed record InventoryStockPositionRecord(
    long Version,
    MaterialLabel Label,
    MaterialLot Lot,
    IReadOnlyList<StockBalance> Balances,
    IReadOnlyList<InventoryActiveAllocationRecord> ActiveAllocations);

internal sealed record InventoryStockPositionWindow(
    IReadOnlyList<InventoryStockPositionRecord> Records,
    bool HasMore,
    InventoryStockPositionPosition? LastPosition = null);

internal sealed record InventoryStockPositionPosition(string LabelCode, Guid LabelId);

internal sealed record InventoryStockPositionReadQuery(
    string Q,
    int Limit,
    Guid? ProductId,
    Guid? LocationId,
    InventoryLabelClassificationV1? Classification,
    StockPartyKind? OwnerKind,
    Guid? OwnerId,
    InventoryStockPositionPosition? After);

internal interface IInventoryStockPositionCursorCodec
{
    bool IsAvailable { get; }
    string Protect(Guid companyId, string actorId, InventoryStockPositionReadQuery query,
        InventoryStockPositionPosition position);
    InventoryStockPositionPosition Unprotect(Guid companyId, string actorId,
        InventoryStockPositionReadQuery query, string cursor);
}

internal interface IInventoryStockPositionReader
{
    Task<InventoryStockPositionWindow> SearchAsync(
        Guid companyId,
        InventoryStockPositionReadQuery query,
        CancellationToken cancellationToken);
}

internal sealed class InventoryStockPositionService(
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IInventoryStockPositionReader? reader,
    IInventoryStockPositionCursorCodec? cursors = null) : IInventoryStockPositionsV1
{
    public async Task<InventoryStockPositionsV1> SearchAsync(
        InventoryStockPositionQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        var normalizedQ = query?.Q?.Trim();
        var normalizedOwnerKind = query?.OwnerKind?.Trim();
        if (query is null || query.Limit is < 1 or > 100 || normalizedQ?.Length > MaterialLabel.MaximumCodeLength
            || query.ProductId == Guid.Empty || query.LocationId == Guid.Empty || query.OwnerId == Guid.Empty
            || (query.OwnerId.HasValue != !string.IsNullOrEmpty(normalizedOwnerKind))
            || query.Classification is { } classification && !Enum.IsDefined(classification)
            || !TryOwnerKind(normalizedOwnerKind, out var ownerKind))
        {
            throw Error(400, InventoryStockPositionCodesV1.InvalidQuery);
        }

        var source = reader ?? throw Error(503, InventoryStockPositionCodesV1.Unavailable);
        var readQuery = new InventoryStockPositionReadQuery(
            normalizedQ ?? string.Empty,
            query.Limit,
            query.ProductId,
            query.LocationId,
            query.Classification,
            ownerKind,
            query.OwnerId,
            null);
        if (query.Cursor is not null)
        {
            if (string.IsNullOrWhiteSpace(query.Cursor))
            {
                throw Error(400, InventoryStockPositionCodesV1.InvalidQuery);
            }
            if (cursors is not { IsAvailable: true })
            {
                throw Error(503, InventoryStockPositionCodesV1.Unavailable);
            }

            readQuery = readQuery with
            {
                After = cursors.Unprotect(scope.CompanyId, scope.ActorId, readQuery, query.Cursor),
            };
        }

        var window = await FencedAsync(
            () => source.SearchAsync(scope.CompanyId, readQuery, cancellationToken),
            scope,
            cancellationToken);
        string? nextCursor = null;
        if (window.HasMore)
        {
            if (window.LastPosition is null || cursors is not { IsAvailable: true })
            {
                throw Error(503, InventoryStockPositionCodesV1.Unavailable);
            }

            nextCursor = cursors.Protect(scope.CompanyId, scope.ActorId, readQuery, window.LastPosition);
        }

        return new InventoryStockPositionsV1(
            [.. window.Records.Select(record => View(record, scope.CompanyId))],
            window.HasMore,
            nextCursor);
    }

    private static InventoryStockPositionV1 View(InventoryStockPositionRecord record, Guid companyId)
    {
        var label = record.Label;
        if (record.Version <= 0 || label.CompanyId != companyId || record.Lot.CompanyId != companyId
            || record.Lot.Id != label.LotId || record.Lot.ProductId != label.ProductId
            || record.Balances.Count == 0
            || record.Balances.Any(balance => balance.Position.CompanyId != companyId
                || balance.Position.LabelId != label.Id || balance.Position.ProductId != label.ProductId))
        {
            throw Error(503, InventoryStockPositionCodesV1.Unavailable);
        }

        try
        {
            _ = StockLedger.FromStoredBalances(record.Balances);
        }
        catch (InventoryRuleException exception)
        {
            throw Error(503, InventoryStockPositionCodesV1.Unavailable, exception);
        }

        var nonZero = record.Balances.Where(balance => balance.Quantity.Value != 0m).ToArray();
        if (nonZero.Length == 0)
        {
            throw Error(503, InventoryStockPositionCodesV1.Unavailable);
        }

        var place = nonZero[0].Position;
        var free = nonZero.Where(balance => balance.Position.Block == BlockState.Free)
            .Sum(balance => balance.Quantity.Value);
        var blocked = nonZero.Where(balance => balance.Position.Block == BlockState.Blocked)
            .Sum(balance => balance.Quantity.Value);

        foreach (var allocation in record.ActiveAllocations)
        {
            if (allocation.RemainingKg <= 0m
                || allocation.Block != nameof(BlockState.Free)
                || allocation.OwnerKind != place.Owner.Kind.ToString()
                || allocation.OwnerId != place.Owner.Id
                || allocation.CustodianKind != place.Custodian.Kind.ToString()
                || allocation.CustodianId != place.Custodian.Id
                || allocation.LocationId != place.LocationId)
            {
                throw Error(503, InventoryStockPositionCodesV1.Unavailable);
            }
        }

        var allocated = record.ActiveAllocations.Sum(allocation => allocation.RemainingKg);
        if (allocated > free)
        {
            throw Error(503, InventoryStockPositionCodesV1.Unavailable);
        }

        return new InventoryStockPositionV1(
            companyId,
            label.Id,
            record.Version.ToString(CultureInfo.InvariantCulture),
            label.Code,
            label.ProductId,
            label.ProductVersion,
            label.Classification?.ToString(),
            new InventoryLotV1(record.Lot.Id, record.Lot.HeatNumber, record.Lot.CoilNumber),
            label.HeatNumber,
            label.CoilNumber,
            label.DocumentStatus.ToString(),
            place.Owner.Kind.ToString(),
            place.Owner.Id,
            place.Custodian.Kind.ToString(),
            place.Custodian.Id,
            place.LocationId,
            Kilograms.Of(free + blocked).ToString(),
            Kilograms.Of(free).ToString(),
            Kilograms.Of(blocked).ToString(),
            Kilograms.Of(allocated).ToString(),
            Kilograms.Of(free - allocated).ToString());
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
            if (exception is InventoryStockPositionException)
            {
                throw;
            }

            throw Error(503, InventoryStockPositionCodesV1.Unavailable, exception);
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
                ? InventoryStockPositionCodesV1.Unauthenticated
                : InventoryStockPositionCodesV1.Forbidden);
        }

        if (!company.IsResolved)
        {
            throw Error(403, InventoryStockPositionCodesV1.Forbidden);
        }

        var scope = new Scope(company.CompanyId, user.UserId);
        if (expected is not null && expected != scope)
        {
            throw Error(403, InventoryStockPositionCodesV1.Forbidden);
        }

        foreach (var permission in new[] { ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead })
        {
            RequireIdentity(scope);
            PermissionScope grant;
            try
            {
                grant = await authority.RevalidatePermissionScopeAsync(permission, scope.CompanyId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                RequireIdentity(scope);
                throw Error(503, InventoryStockPositionCodesV1.Unavailable, exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            RequireIdentity(scope);
            if (!grant.IsGranted || !grant.IsCompanyWide)
            {
                throw Error(403, InventoryStockPositionCodesV1.Forbidden);
            }
        }

        return scope;
    }

    private void RequireIdentity(Scope scope)
    {
        if (!company.IsResolved || company.CompanyId != scope.CompanyId || scope.CompanyId == Guid.Empty
            || !user.IsAuthenticated || user.UserId != scope.ActorId || !user.CanAccessCompany(scope.CompanyId))
        {
            throw Error(403, InventoryStockPositionCodesV1.Forbidden);
        }
    }

    private static InventoryStockPositionException Error(int status, string code, Exception? exception = null) =>
        new(status, code, exception);

    private static bool TryOwnerKind(string? value, out StockPartyKind? kind)
    {
        kind = null;
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        if (!Enum.TryParse<StockPartyKind>(value, ignoreCase: false, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            return false;
        }

        kind = parsed;
        return true;
    }

    private sealed record Scope(Guid CompanyId, string ActorId);
}

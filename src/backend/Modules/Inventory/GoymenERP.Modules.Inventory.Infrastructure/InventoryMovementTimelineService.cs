using System.Data.SqlTypes;
using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Infrastructure;

internal sealed record InventoryMovementTimelinePosition(DateTimeOffset OccurredAtUtc, Guid MovementId);

internal sealed record InventoryMovementTimelineReadQuery(
    int Limit,
    InventoryMovementTimelinePosition? After);

internal sealed record InventoryMovementTimelineWindow(
    IReadOnlyList<InventoryMovementV1> Items,
    bool HasMore,
    InventoryMovementTimelinePosition? LastPosition);

internal interface IInventoryMovementTimelineReader
{
    Task<InventoryMovementTimelineWindow> SearchAsync(
        Guid companyId,
        InventoryMovementTimelineReadQuery query,
        CancellationToken cancellationToken);
}

internal interface IInventoryMovementTimelineCursorCodec
{
    bool IsAvailable { get; }
    string Protect(Guid companyId, string actorId, int limit, InventoryMovementTimelinePosition position);
    InventoryMovementTimelinePosition Unprotect(Guid companyId, string actorId, int limit, string cursor);
}

internal sealed class InventoryMovementTimelineService(
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IInventoryMovementTimelineReader? reader,
    IInventoryMovementTimelineCursorCodec? cursors = null) : IInventoryMovementTimelineV1
{
    public async Task<InventoryMovementTimelinePageV1> SearchAsync(
        InventoryMovementTimelineQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (query is null || query.Limit is < 1 or > 100)
        {
            throw Error(400, InventoryMovementTimelineCodesV1.InvalidQuery);
        }

        InventoryMovementTimelinePosition? after = null;
        if (query.Cursor is not null)
        {
            if (string.IsNullOrWhiteSpace(query.Cursor))
            {
                throw Error(400, InventoryMovementTimelineCodesV1.InvalidQuery);
            }
            if (cursors is not { IsAvailable: true })
            {
                throw Error(503, InventoryMovementTimelineCodesV1.Unavailable);
            }

            after = cursors.Unprotect(scope.CompanyId, scope.ActorId, query.Limit, query.Cursor);
        }

        var source = reader ?? throw Error(503, InventoryMovementTimelineCodesV1.Unavailable);
        var window = await FencedAsync(
            () => source.SearchAsync(scope.CompanyId,
                new InventoryMovementTimelineReadQuery(query.Limit, after), cancellationToken),
            scope,
            cancellationToken);
        ValidateWindow(window, query.Limit);

        string? nextCursor = null;
        if (window.HasMore)
        {
            if (window.LastPosition is null || cursors is not { IsAvailable: true })
            {
                throw Error(503, InventoryMovementTimelineCodesV1.Unavailable);
            }
            nextCursor = cursors.Protect(scope.CompanyId, scope.ActorId, query.Limit, window.LastPosition);
        }

        return new InventoryMovementTimelinePageV1(window.Items, nextCursor);
    }

    private static void ValidateWindow(InventoryMovementTimelineWindow? window, int limit)
    {
        if (window is null || window.Items is null || window.Items.Count > limit
            || window.HasMore != (window.LastPosition is not null)
            || window.Items.Count == 0 && window.LastPosition is not null)
        {
            throw Error(503, InventoryMovementTimelineCodesV1.Unavailable);
        }

        var movementIds = new HashSet<Guid>();
        InventoryMovementTimelinePosition? previous = null;
        foreach (var movement in window.Items)
        {
            if (!ValidMovement(movement) || !movementIds.Add(movement.MovementId))
            {
                throw Error(503, InventoryMovementTimelineCodesV1.Unavailable);
            }

            var current = new InventoryMovementTimelinePosition(movement.OccurredAtUtc, movement.MovementId);
            if (previous is not null && (current.OccurredAtUtc > previous.OccurredAtUtc
                || current.OccurredAtUtc == previous.OccurredAtUtc
                    && CompareSqlServerUniqueIdentifiers(current.MovementId, previous.MovementId) >= 0))
            {
                throw Error(503, InventoryMovementTimelineCodesV1.Unavailable);
            }
            previous = current;
        }

        if (window.LastPosition is not null
            && (previous is null || window.LastPosition != previous))
        {
            throw Error(503, InventoryMovementTimelineCodesV1.Unavailable);
        }
    }

    private static int CompareSqlServerUniqueIdentifiers(Guid left, Guid right) =>
        new SqlGuid(left).CompareTo(new SqlGuid(right));

    private static bool ValidMovement(InventoryMovementV1? movement)
    {
        if (movement is null || movement.MovementId == Guid.Empty || movement.OccurredAtUtc.Offset != TimeSpan.Zero
            || !Enum.TryParse<StockMovementKind>(movement.Kind, out var kind) || !Enum.IsDefined(kind)
            || string.IsNullOrWhiteSpace(movement.SourceKind)
            || movement.SourceKind.Length > StockSourceReference.MaximumKindLength
            || !string.Equals(movement.SourceKind, movement.SourceKind.Trim(), StringComparison.Ordinal)
            || movement.SourceId == Guid.Empty
            || !Enum.TryParse<StockSourceGrain>(movement.SourceGrain, out var grain) || !Enum.IsDefined(grain)
            || (grain == StockSourceGrain.Whole) != (movement.SourceLineId is null)
            || movement.SourceLineId == Guid.Empty
            || movement.Lines is null || movement.Lines.Count == 0)
        {
            return false;
        }

        var sequences = new HashSet<int>();
        foreach (var line in movement.Lines)
        {
            if (line is null || line.Sequence <= 0 || !sequences.Add(line.Sequence)
                || !Enum.TryParse<StockDirection>(line.Direction, out var direction) || !Enum.IsDefined(direction)
                || line.LabelId == Guid.Empty || string.IsNullOrWhiteSpace(line.LabelCode)
                || line.LabelCode.Length > MaterialLabel.MaximumCodeLength
                || !string.Equals(line.LabelCode, line.LabelCode.Trim(), StringComparison.Ordinal)
                || line.LabelCode.Any(char.IsControl)
                || !Enum.TryParse<StockPartyKind>(line.OwnerKind, out var ownerKind) || !Enum.IsDefined(ownerKind)
                || !Enum.TryParse<StockPartyKind>(line.CustodianKind, out var custodianKind)
                || !Enum.IsDefined(custodianKind)
                || line.OwnerId == Guid.Empty || line.CustodianId == Guid.Empty
                || line.LocationId == Guid.Empty || line.ProductId == Guid.Empty
                || !Enum.TryParse<BlockState>(line.Block, out var block) || !Enum.IsDefined(block)
                || !CanonicalQuantity(line.QuantityKg)
                || kind is StockMovementKind.Receipt or StockMovementKind.Return or StockMovementKind.TransferAccept
                    && direction != StockDirection.In
                || kind == StockMovementKind.Issue && (direction != StockDirection.Out || block == BlockState.Blocked)
                || kind == StockMovementKind.TransferDispatch && direction != StockDirection.Out
                || kind == StockMovementKind.Scrap && direction != StockDirection.Out)
            {
                return false;
            }
        }

        if (!movement.Lines.Select(line => line.Sequence).SequenceEqual(
                movement.Lines.Select(line => line.Sequence).Order())
            || ((kind is StockMovementKind.Scrap or StockMovementKind.CountAdjustment)
                && movement.Lines.Count != 1))
        {
            return false;
        }

        return kind is not (StockMovementKind.Block or StockMovementKind.Release)
            || ValidBlockChange(kind, movement.Lines);
    }

    private static bool ValidBlockChange(StockMovementKind kind, IReadOnlyList<InventoryMovementLineV1> lines)
    {
        if (lines.Count != 2)
        {
            return false;
        }

        var from = lines.SingleOrDefault(line => line.Direction == nameof(StockDirection.Out));
        var to = lines.SingleOrDefault(line => line.Direction == nameof(StockDirection.In));
        var fromBlock = kind == StockMovementKind.Block ? nameof(BlockState.Free) : nameof(BlockState.Blocked);
        var toBlock = kind == StockMovementKind.Block ? nameof(BlockState.Blocked) : nameof(BlockState.Free);
        return from is not null && to is not null
            && from.Block == fromBlock && to.Block == toBlock
            && from.LabelId == to.LabelId && from.LabelCode == to.LabelCode
            && from.OwnerKind == to.OwnerKind && from.OwnerId == to.OwnerId
            && from.CustodianKind == to.CustodianKind && from.CustodianId == to.CustodianId
            && from.LocationId == to.LocationId && from.ProductId == to.ProductId
            && from.QuantityKg == to.QuantityKg;
    }

    private static bool CanonicalQuantity(string value)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0m)
        {
            return false;
        }

        try
        {
            return string.Equals(Kilograms.Positive(parsed).ToString(), value, StringComparison.Ordinal);
        }
        catch (InventoryRuleException)
        {
            return false;
        }
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
            if (exception is InventoryMovementTimelineException)
            {
                throw;
            }
            throw Error(503, InventoryMovementTimelineCodesV1.Unavailable, exception);
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
                ? InventoryMovementTimelineCodesV1.Unauthenticated
                : InventoryMovementTimelineCodesV1.Forbidden);
        }
        if (!company.IsResolved)
        {
            throw Error(403, InventoryMovementTimelineCodesV1.Forbidden);
        }

        var scope = new Scope(company.CompanyId, user.UserId);
        if (expected is not null && expected != scope)
        {
            throw Error(403, InventoryMovementTimelineCodesV1.Forbidden);
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
                throw Error(503, InventoryMovementTimelineCodesV1.Unavailable, exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            RequireIdentity(scope);
            if (!grant.IsGranted || !grant.IsCompanyWide)
            {
                throw Error(403, InventoryMovementTimelineCodesV1.Forbidden);
            }
        }
        return scope;
    }

    private void RequireIdentity(Scope scope)
    {
        if (!company.IsResolved || company.CompanyId != scope.CompanyId || scope.CompanyId == Guid.Empty
            || !user.IsAuthenticated || user.UserId != scope.ActorId || !user.CanAccessCompany(scope.CompanyId))
        {
            throw Error(403, InventoryMovementTimelineCodesV1.Forbidden);
        }
    }

    private static InventoryMovementTimelineException Error(int statusCode, string code, Exception? exception = null) =>
        new(statusCode, code, exception);

    private sealed record Scope(Guid CompanyId, string ActorId);
}

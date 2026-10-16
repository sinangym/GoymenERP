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
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Modules.Inventory.Infrastructure;

internal sealed record InventoryActiveAllocationRecord(
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    string Block,
    decimal RemainingKg);

internal sealed record InventoryActivePhysicalHoldRecord(
    int OwnerKind,
    string SourceKind,
    Guid SourceId,
    string PositionOwnerKind,
    Guid PositionOwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    Guid ProductId,
    decimal QuantityKg);

internal sealed record InventoryStockPositionRecord(
    long Version,
    MaterialLabel Label,
    MaterialLot Lot,
    IReadOnlyList<StockBalance> Balances,
    IReadOnlyList<InventoryActiveAllocationRecord> ActiveAllocations,
    IReadOnlyList<InventoryActivePhysicalHoldRecord> ActivePhysicalHolds);

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
    Guid? ProductFamilyId,
    decimal? ThicknessMm,
    decimal? ThicknessMinMm,
    decimal? ThicknessMaxMm,
    decimal? WidthMm,
    decimal? WidthMinMm,
    decimal? WidthMaxMm,
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
    IInventoryStockPositionCursorCodec? cursors = null,
    IInventoryStockProductFilterV1? productFilter = null) : IInventoryStockPositionsV1
{
    private const int MaximumTypedFilterCandidateWindow = 1000;
    private const int TypedFilterCandidateBatchSize = 100;

    public async Task<InventoryStockPositionsV1> SearchAsync(
        InventoryStockPositionQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        var normalizedQ = query?.Q?.Trim();
        var normalizedOwnerKind = query?.OwnerKind?.Trim();
        if (query is null || query.Limit is < 1 or > 100 || normalizedQ?.Length > MaterialLabel.MaximumCodeLength
            || query.ProductId == Guid.Empty || query.LocationId == Guid.Empty || query.OwnerId == Guid.Empty
            || query.ProductFamilyId == Guid.Empty
            || (query.OwnerId.HasValue != !string.IsNullOrEmpty(normalizedOwnerKind))
            || query.Classification is { } classification && !Enum.IsDefined(classification)
            || !TryOwnerKind(normalizedOwnerKind, out var ownerKind)
            || !ValidTypedProductFilter(query))
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
            query.ProductFamilyId,
            query.ThicknessMm,
            query.ThicknessMinMm,
            query.ThicknessMaxMm,
            query.WidthMm,
            query.WidthMinMm,
            query.WidthMaxMm,
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

        if (query.HasTypedProductFilter)
        {
            return await SearchTypedAsync(query, readQuery, source, scope, cancellationToken);
        }

        var window = await ReadWindowAsync(source, readQuery, scope, cancellationToken);
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

    private async Task<InventoryStockPositionsV1> SearchTypedAsync(
        InventoryStockPositionQueryV1 query,
        InventoryStockPositionReadQuery readQuery,
        IInventoryStockPositionReader source,
        Scope scope,
        CancellationToken cancellationToken)
    {
        var filter = productFilter ?? throw Error(503, InventoryStockPositionCodesV1.Unavailable);
        var matches = new List<InventoryStockPositionV1>(query.Limit);
        var scanned = 0;
        InventoryStockPositionPosition? lastProcessed = readQuery.After;
        var hasContinuation = false;
        var cursorBindingQuery = readQuery;
        var scanQuery = readQuery with { Limit = TypedFilterCandidateBatchSize };

        while (matches.Count < query.Limit && scanned < MaximumTypedFilterCandidateWindow)
        {
            var window = await ReadWindowAsync(source, scanQuery, scope, cancellationToken);
            if (window.Records.Count == 0)
            {
                if (window.HasMore) throw Error(503, InventoryStockPositionCodesV1.Unavailable);
                return new(matches, false);
            }

            var views = window.Records.Select(record => View(record, scope.CompanyId)).ToArray();
            var selections = views
                .Where(item => !string.IsNullOrWhiteSpace(item.ProductVersion))
                .Select(item => new InventoryStockProductSelectionV1(
                    item.LabelId, item.ProductId, item.ProductVersion!))
                .ToArray();
            var matched = selections.Length == 0
                ? new HashSet<Guid>()
                : await FencedAsync(
                    () => filter.MatchAsync(new(
                        scope.CompanyId,
                        scope.ActorId,
                        selections,
                        new(query.ProductFamilyId, query.ThicknessMm, query.ThicknessMinMm,
                            query.ThicknessMaxMm, query.WidthMm, query.WidthMinMm, query.WidthMaxMm)),
                        cancellationToken),
                    scope,
                    cancellationToken);
            if (matched.Any(labelId => labelId == Guid.Empty
                    || selections.All(selection => selection.LabelId != labelId)))
            {
                throw Error(503, InventoryStockPositionCodesV1.Unavailable);
            }

            for (var index = 0; index < views.Length; index++)
            {
                var item = views[index];
                scanned++;
                lastProcessed = new(item.LabelCode, item.LabelId);
                if (matched.Contains(item.LabelId)) matches.Add(item);

                var unprocessedInWindow = index + 1 < views.Length;
                if (matches.Count == query.Limit || scanned == MaximumTypedFilterCandidateWindow)
                {
                    hasContinuation = unprocessedInWindow || window.HasMore;
                    break;
                }
            }

            if (matches.Count == query.Limit || scanned == MaximumTypedFilterCandidateWindow)
            {
                break;
            }
            if (!window.HasMore)
            {
                return new(matches, false);
            }
            if (window.LastPosition is null || window.LastPosition == scanQuery.After)
            {
                throw Error(503, InventoryStockPositionCodesV1.Unavailable);
            }
            scanQuery = scanQuery with { After = window.LastPosition };
        }

        string? nextCursor = null;
        if (hasContinuation)
        {
            if (lastProcessed is null || cursors is not { IsAvailable: true })
            {
                throw Error(503, InventoryStockPositionCodesV1.Unavailable);
            }
            nextCursor = cursors.Protect(scope.CompanyId, scope.ActorId, cursorBindingQuery, lastProcessed);
        }
        return new(matches, hasContinuation, nextCursor);
    }

    private Task<InventoryStockPositionWindow> ReadWindowAsync(
        IInventoryStockPositionReader source,
        InventoryStockPositionReadQuery query,
        Scope scope,
        CancellationToken cancellationToken) => FencedAsync(
            () => source.SearchAsync(scope.CompanyId, query, cancellationToken),
            scope,
            cancellationToken);

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

        var physical = free + blocked;
        var allocated = record.ActiveAllocations.Sum(allocation => allocation.RemainingKg);
        var activeQualityHold = record.ActivePhysicalHolds.Count > 0;
        if (activeQualityHold)
        {
            if (free != 0m || blocked != physical
                || record.ActivePhysicalHolds.Any(hold =>
                    hold.OwnerKind != InventoryStore.QualityPhysicalHoldOwner
                    || hold.SourceKind != InventoryPhysicalHoldTokensV1.QualityInspectionSource
                    || hold.SourceId == Guid.Empty
                    || hold.PositionOwnerKind != place.Owner.Kind.ToString()
                    || hold.PositionOwnerId != place.Owner.Id
                    || hold.CustodianKind != place.Custodian.Kind.ToString()
                    || hold.CustodianId != place.Custodian.Id
                    || hold.LocationId != place.LocationId
                    || hold.ProductId != label.ProductId
                    || hold.QuantityKg != physical)
                || allocated > physical)
            {
                throw Error(503, InventoryStockPositionCodesV1.Unavailable);
            }

            // A whole-label Quality hold and an earlier physical allocation are overlapping restrictions,
            // not two kilograms to subtract. Keep the public components mutually exclusive: allocation wins
            // its reserved part, the remaining physical kilograms stay blocked, and nothing is available.
            free = allocated;
            blocked = physical - allocated;
        }
        else if (allocated > free)
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
            Kilograms.Of(physical).ToString(),
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
            || !Enum.IsDefined(parsed)
            || !string.Equals(value, parsed.ToString(), StringComparison.Ordinal))
        {
            return false;
        }

        kind = parsed;
        return true;
    }

    private static bool ValidTypedProductFilter(InventoryStockPositionQueryV1 query) =>
        ValidMeasure(query.ThicknessMm)
        && ValidMeasure(query.ThicknessMinMm)
        && ValidMeasure(query.ThicknessMaxMm)
        && ValidMeasure(query.WidthMm)
        && ValidMeasure(query.WidthMinMm)
        && ValidMeasure(query.WidthMaxMm)
        && !(query.ThicknessMm.HasValue
            && (query.ThicknessMinMm.HasValue || query.ThicknessMaxMm.HasValue))
        && !(query.WidthMm.HasValue && (query.WidthMinMm.HasValue || query.WidthMaxMm.HasValue))
        && (!query.ThicknessMinMm.HasValue || !query.ThicknessMaxMm.HasValue
            || query.ThicknessMinMm <= query.ThicknessMaxMm)
        && (!query.WidthMinMm.HasValue || !query.WidthMaxMm.HasValue
            || query.WidthMinMm <= query.WidthMaxMm);

    private static bool ValidMeasure(decimal? value)
    {
        if (!value.HasValue) return true;
        if (value <= 0m) return false;
        var bits = decimal.GetBits(value.Value);
        var scale = (bits[3] >> 16) & 0x7F;
        return scale <= 3;
    }

    private sealed record Scope(Guid CompanyId, string ActorId);
}

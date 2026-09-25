using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Infrastructure;

internal sealed record InventoryLabelDetailRecord(
    long Version,
    MaterialLabel Label,
    MaterialLot Lot,
    IReadOnlyList<StockBalance> Balances,
    InventoryLabelMovementTimelineV1 MovementTimeline);

internal interface IInventoryLabelDetailReader
{
    Task<InventoryLabelDetailRecord?> ReadAsync(
        Guid companyId,
        Guid labelId,
        CancellationToken cancellationToken);
}

internal sealed class InventoryLabelDetailService(
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IInventoryLabelDetailReader? reader,
    IInventoryLabelGenealogyReaderV1? genealogyReader) : IInventoryLabelDetailsV1
{
    public async Task<InventoryLabelDetailV1> GetAsync(
        Guid labelId,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (labelId == Guid.Empty)
        {
            throw Error(404, InventoryLabelDetailCodesV1.NotFound);
        }

        var source = reader ?? throw Error(503, InventoryLabelDetailCodesV1.Unavailable);
        var genealogySource = genealogyReader ?? throw Error(503, InventoryLabelDetailCodesV1.Unavailable);
        var record = await FencedAsync(
            () => source.ReadAsync(scope.CompanyId, labelId, cancellationToken),
            scope,
            cancellationToken);
        if (record is null)
        {
            throw Error(404, InventoryLabelDetailCodesV1.NotFound);
        }

        var genealogy = await FencedAsync(
            () => genealogySource.ReadAsync(labelId, cancellationToken: cancellationToken),
            scope,
            cancellationToken);
        if (genealogy is null)
        {
            throw Error(503, InventoryLabelDetailCodesV1.Unavailable);
        }

        var confirmation = await FencedAsync(
            () => source.ReadAsync(scope.CompanyId, labelId, cancellationToken),
            scope,
            cancellationToken);
        if (confirmation is null || confirmation.Version != record.Version
            || !SameMovementTimeline(record.MovementTimeline, confirmation.MovementTimeline))
        {
            throw Error(409, InventoryLabelDetailCodesV1.ReadConflict);
        }

        var genealogyConfirmation = await FencedAsync(
            () => genealogySource.ReadAsync(labelId, cancellationToken: cancellationToken),
            scope,
            cancellationToken);
        if (genealogyConfirmation is null || !SameGenealogy(genealogy, genealogyConfirmation))
        {
            throw Error(409, InventoryLabelDetailCodesV1.ReadConflict);
        }

        return View(record, genealogy, scope.CompanyId, labelId);
    }

    private static InventoryLabelDetailV1 View(
        InventoryLabelDetailRecord record,
        InventoryLabelGenealogyV1 genealogy,
        Guid companyId,
        Guid labelId)
    {
        var label = record.Label;
        if (record.Version <= 0 || label.Id != labelId || label.CompanyId != companyId
            || record.Lot.CompanyId != companyId || record.Lot.Id != label.LotId
            || record.Lot.ProductId != label.ProductId
            || record.Balances.Any(balance => balance.Position.CompanyId != companyId
                || balance.Position.LabelId != label.Id
                || balance.Position.ProductId != label.ProductId)
            || !ValidGenealogy(genealogy, label)
            || !ValidMovementTimeline(record.MovementTimeline, label))
        {
            throw Error(503, InventoryLabelDetailCodesV1.Unavailable);
        }

        try
        {
            _ = StockLedger.FromStoredBalances(record.Balances);
            return new InventoryLabelDetailV1(
                record.Version.ToString(CultureInfo.InvariantCulture),
                InventoryReadModelsV1.LabelView(label, record.Lot),
                InventoryReadModelsV1.Balances(label.Id, record.Balances),
                genealogy,
                record.MovementTimeline);
        }
        catch (InventoryRuleException exception)
        {
            throw Error(503, InventoryLabelDetailCodesV1.Unavailable, exception);
        }
        catch (ArgumentException exception)
        {
            throw Error(503, InventoryLabelDetailCodesV1.Unavailable, exception);
        }
    }

    private static bool ValidGenealogy(InventoryLabelGenealogyV1 genealogy, MaterialLabel label)
    {
        if (genealogy.LabelId != label.Id
            || !string.Equals(genealogy.LabelCode, label.Code, StringComparison.Ordinal)
            || genealogy.Edges is null)
        {
            return false;
        }

        foreach (var edge in genealogy.Edges)
        {
            if (edge is null || edge.TransformationId == Guid.Empty
                || edge.OccurredAtUtc.Offset != TimeSpan.Zero
                || edge.SourceLabelId == Guid.Empty || edge.OutputLabelId == Guid.Empty
                || string.IsNullOrWhiteSpace(edge.SourceCode) || string.IsNullOrWhiteSpace(edge.OutputCode)
                || edge.Kind is not (nameof(InventoryLabelTransformationKind.Split)
                    or nameof(InventoryLabelTransformationKind.Merge))
                || !PositiveVersion(edge.SourceLabelVersion)
                || !PositiveVersion(edge.OutputLabelVersion)
                || !CanonicalQuantity(edge.SourceQuantityKg)
                || !CanonicalQuantity(edge.OutputQuantityKg))
            {
                return false;
            }
        }

        foreach (var transformation in genealogy.Edges.GroupBy(edge => edge.TransformationId))
        {
            var first = transformation.First();
            var pairs = new HashSet<(Guid SourceLabelId, Guid OutputLabelId)>();
            var sources = new Dictionary<Guid, (string Code, string Version, string QuantityKg)>();
            var outputs = new Dictionary<Guid, (string Code, string Version, string QuantityKg)>();
            foreach (var edge in transformation)
            {
                if (!string.Equals(edge.Kind, first.Kind, StringComparison.Ordinal)
                    || edge.OccurredAtUtc != first.OccurredAtUtc
                    || !pairs.Add((edge.SourceLabelId, edge.OutputLabelId))
                    || !SameLabelFacts(sources, edge.SourceLabelId, edge.SourceCode,
                        edge.SourceLabelVersion, edge.SourceQuantityKg)
                    || !SameLabelFacts(outputs, edge.OutputLabelId, edge.OutputCode,
                        edge.OutputLabelVersion, edge.OutputQuantityKg))
                {
                    return false;
                }
            }

            if (sources.Keys.Intersect(outputs.Keys).Any()
                || first.Kind == nameof(InventoryLabelTransformationKind.Split)
                    && (sources.Count != 1 || outputs.Count < 2)
                || first.Kind == nameof(InventoryLabelTransformationKind.Merge)
                    && (sources.Count < 2 || outputs.Count != 1)
                || !SameQuantityTotal(
                    sources.Values.Select(facts => facts.QuantityKg),
                    outputs.Values.Select(facts => facts.QuantityKg)))
            {
                return false;
            }
        }

        var connectedLabels = new HashSet<Guid> { label.Id };
        var remaining = genealogy.Edges.ToList();
        while (remaining.Count > 0)
        {
            var connected = remaining.Where(edge =>
                connectedLabels.Contains(edge.SourceLabelId) || connectedLabels.Contains(edge.OutputLabelId)).ToArray();
            if (connected.Length == 0)
            {
                return false;
            }

            foreach (var edge in connected)
            {
                connectedLabels.Add(edge.SourceLabelId);
                connectedLabels.Add(edge.OutputLabelId);
                remaining.Remove(edge);
            }
        }

        return true;
    }

    private static bool SameLabelFacts(
        IDictionary<Guid, (string Code, string Version, string QuantityKg)> labels,
        Guid labelId,
        string code,
        string version,
        string quantityKg)
    {
        var facts = (code, version, quantityKg);
        if (labels.TryGetValue(labelId, out var existing))
        {
            return existing == facts;
        }

        labels.Add(labelId, facts);
        return true;
    }

    private static bool SameQuantityTotal(IEnumerable<string> sources, IEnumerable<string> outputs) =>
        TryQuantityTotal(sources, out var sourceTotal)
        && TryQuantityTotal(outputs, out var outputTotal)
        && sourceTotal == outputTotal;

    private static bool TryQuantityTotal(IEnumerable<string> values, out decimal total)
    {
        total = 0m;
        try
        {
            foreach (var value in values)
            {
                if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    return false;
                }

                total = checked(total + parsed);
            }

            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool PositiveVersion(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;

    private static bool SameGenealogy(InventoryLabelGenealogyV1 left, InventoryLabelGenealogyV1 right) =>
        left.LabelId == right.LabelId
        && string.Equals(left.LabelCode, right.LabelCode, StringComparison.Ordinal)
        && left.WindowFull == right.WindowFull
        && left.Edges.SequenceEqual(right.Edges);

    private static bool SameMovementTimeline(
        InventoryLabelMovementTimelineV1? left,
        InventoryLabelMovementTimelineV1? right) =>
        left is not null && right is not null
        && left.Entries is not null && right.Entries is not null
        && left.LabelId == right.LabelId
        && left.WindowFull == right.WindowFull
        && left.Entries.Count == right.Entries.Count
        && left.Entries.Zip(right.Entries).All(pair =>
            pair.First is not null && pair.Second is not null
            && pair.First.Lines is not null && pair.Second.Lines is not null
            && pair.First with { Lines = [] } == pair.Second with { Lines = [] }
            && pair.First.Lines.SequenceEqual(pair.Second.Lines));

    private static bool ValidMovementTimeline(
        InventoryLabelMovementTimelineV1? timeline,
        MaterialLabel label)
    {
        if (timeline is null || timeline.LabelId != label.Id
            || timeline.Entries is null || timeline.Entries.Count > 100)
        {
            return false;
        }

        var movementIds = new HashSet<Guid>();
        DateTimeOffset? previous = null;
        foreach (var movement in timeline.Entries)
        {
            if (movement is null || movement.MovementId == Guid.Empty || !movementIds.Add(movement.MovementId)
                || movement.OccurredAtUtc.Offset != TimeSpan.Zero
                || previous is not null && movement.OccurredAtUtc > previous
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

            previous = movement.OccurredAtUtc;
            var sequences = new HashSet<int>();
            foreach (var line in movement.Lines)
            {
                if (line is null || line.Sequence <= 0 || !sequences.Add(line.Sequence)
                    || !Enum.TryParse<StockDirection>(line.Direction, out var direction) || !Enum.IsDefined(direction)
                    || !Enum.TryParse<StockPartyKind>(line.OwnerKind, out var ownerKind) || !Enum.IsDefined(ownerKind)
                    || !Enum.TryParse<StockPartyKind>(line.CustodianKind, out var custodianKind)
                    || !Enum.IsDefined(custodianKind)
                    || line.OwnerId == Guid.Empty || line.CustodianId == Guid.Empty
                    || line.LocationId == Guid.Empty || line.ProductId != label.ProductId
                    || !Enum.TryParse<BlockState>(line.Block, out var block) || !Enum.IsDefined(block)
                    || !CanonicalQuantity(line.QuantityKg)
                    || kind is StockMovementKind.Receipt or StockMovementKind.Return && direction != StockDirection.In
                    || kind == StockMovementKind.Issue && (direction != StockDirection.Out || block == BlockState.Blocked))
                {
                    return false;
                }
            }

            if (!movement.Lines.Select(line => line.Sequence).SequenceEqual(
                    movement.Lines.Select(line => line.Sequence).Order()))
            {
                return false;
            }

            if (kind is StockMovementKind.Block or StockMovementKind.Release
                && !ValidBlockChange(kind, movement.Lines))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidBlockChange(
        StockMovementKind kind,
        IReadOnlyList<InventoryLabelMovementLineV1> lines)
    {
        if (lines.Count != 2)
        {
            return false;
        }

        var fromLines = lines.Where(line => line.Direction == nameof(StockDirection.Out)).ToArray();
        var toLines = lines.Where(line => line.Direction == nameof(StockDirection.In)).ToArray();
        if (fromLines.Length != 1 || toLines.Length != 1)
        {
            return false;
        }

        var from = fromLines[0];
        var to = toLines[0];
        var fromBlock = kind == StockMovementKind.Block ? nameof(BlockState.Free) : nameof(BlockState.Blocked);
        var toBlock = kind == StockMovementKind.Block ? nameof(BlockState.Blocked) : nameof(BlockState.Free);
        return from.Block == fromBlock && to.Block == toBlock
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

    private async Task<T> FencedAsync<T>(
        Func<Task<T>> read,
        Scope scope,
        CancellationToken cancellationToken)
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
            if (exception is InventoryLabelDetailException)
            {
                throw;
            }

            throw Error(503, InventoryLabelDetailCodesV1.Unavailable, exception);
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
                ? InventoryLabelDetailCodesV1.Unauthenticated
                : InventoryLabelDetailCodesV1.Forbidden);
        }

        if (!company.IsResolved)
        {
            throw Error(403, InventoryLabelDetailCodesV1.Forbidden);
        }

        var scope = new Scope(company.CompanyId, user.UserId);
        if (expected is not null && expected != scope)
        {
            throw Error(403, InventoryLabelDetailCodesV1.Forbidden);
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
                throw Error(503, InventoryLabelDetailCodesV1.Unavailable, exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            RequireIdentity(scope);
            if (!grant.IsGranted || !grant.IsCompanyWide)
            {
                throw Error(403, InventoryLabelDetailCodesV1.Forbidden);
            }
        }

        return scope;
    }

    private void RequireIdentity(Scope scope)
    {
        if (!company.IsResolved || company.CompanyId != scope.CompanyId || scope.CompanyId == Guid.Empty
            || !user.IsAuthenticated || user.UserId != scope.ActorId || !user.CanAccessCompany(scope.CompanyId))
        {
            throw Error(403, InventoryLabelDetailCodesV1.Forbidden);
        }
    }

    private static InventoryLabelDetailException Error(int statusCode, string code, Exception? exception = null) =>
        new(statusCode, code, exception);

    private sealed record Scope(Guid CompanyId, string ActorId);
}

using System.Data.Common;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

internal enum InventoryCostLineageOutcome { Value = 1, NotFound = 2, Unreadable = 3 }

internal sealed record InventoryCostLineagePositionSnapshot(
    StockOwner Owner, StockCustodian Custodian, Guid LocationId, BlockState Block, decimal QuantityKg);

internal sealed record InventoryCostLineageReturnSnapshot(Guid ReturnId, Guid MovementId, int LineNumber,
    Guid SourceIssueMovementId, decimal QuantityKg, DateTimeOffset RecordedAtUtc);

internal sealed record InventoryCostLineageShipmentSnapshot(Guid ShipmentId, Guid MovementId, int LineNumber,
    Guid LoadingInstructionLineId, Guid LabelId, Guid ProductId, string ProductVersion, StockOwner Owner,
    StockCustodian Custodian, Guid LocationId, BlockState Block, decimal QuantityKg, ExactCostRatio RootShare,
    ExactCostRatio NetRootQuantityKg, IReadOnlyList<InventoryCostLineageReturnSnapshot> Returns, DateTimeOffset RecordedAtUtc);

internal sealed class InventoryCostLineageLabelSnapshot
{
    internal InventoryCostLineageLabelSnapshot(Guid labelId, long version, string productVersion, ExactCostRatio rootShare,
        IEnumerable<InventoryCostLineagePositionSnapshot> positions)
    {
        LabelId = labelId;
        Version = version;
        ProductVersion = productVersion;
        RootShare = rootShare;
        Positions = Array.AsReadOnly(positions.ToArray());
    }

    public Guid LabelId { get; }
    public long Version { get; }
    public string ProductVersion { get; }
    public ExactCostRatio RootShare { get; }
    public IReadOnlyList<InventoryCostLineagePositionSnapshot> Positions { get; }
}

internal sealed class InventoryCostLineageSnapshot
{
    internal InventoryCostLineageSnapshot(InventoryCostLineageOutcome outcome, Guid companyId, Guid productId, Guid rootLabelId,
        IEnumerable<InventoryCostLineageLabelSnapshot> labels, ExactCostRatio? remainingKg, ExactCostRatio? shippedKg,
        ExactCostRatio? customerReturnedKg, string? reason, IEnumerable<InventoryCostLineageShipmentSnapshot>? shipments = null)
    {
        Outcome = outcome;
        CompanyId = companyId;
        ProductId = productId;
        RootLabelId = rootLabelId;
        Labels = Array.AsReadOnly(labels.ToArray());
        RemainingKg = remainingKg;
        ShippedKg = shippedKg;
        CustomerReturnedKg = customerReturnedKg;
        Reason = reason;
        Shipments = Array.AsReadOnly((shipments ?? []).ToArray());
    }

    public InventoryCostLineageOutcome Outcome { get; }
    public Guid CompanyId { get; }
    public Guid ProductId { get; }
    public Guid RootLabelId { get; }
    public IReadOnlyList<InventoryCostLineageLabelSnapshot> Labels { get; }
    public ExactCostRatio? RemainingKg { get; }
    public ExactCostRatio? ShippedKg { get; }
    public ExactCostRatio? CustomerReturnedKg { get; }
    public ExactCostRatio? NetShippedKg => ShippedKg is not null && CustomerReturnedKg is not null
        ? ShippedKg.Subtract(CustomerReturnedKg) : null;
    public string? Reason { get; }
    public IReadOnlyList<InventoryCostLineageShipmentSnapshot> Shipments { get; }
}

/// <summary>
/// Inventory-owned, company-fenced, read-only material lineage. A connected split/merge component is
/// loaded, including a merge's other sources and their ancestors, without scanning unrelated product
/// history. Manufacturing is a boundary: its input issue/output receipt already carry physical kg.
/// The caller owns receipt-cohort admission, its transaction and subsequent label-version fencing.
/// </summary>
internal static class InventoryCostLineageReader
{
    internal const int MaximumMovementLines = InventoryManufacturingConsumedCostLineageEvidenceV1.MaximumMovementLines;
    internal const int MaximumBalanceRows = 65536;
    private const int QueryBatchSize = 256;
    internal const string UnreadableReason = "inventory_cost_lineage_source_unreadable";

    internal static async Task<InventoryCostLineageSnapshot> ReadAsync(InventoryDbContext context,
        Guid companyId, Guid productId, Guid rootLabelId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || productId == Guid.Empty || rootLabelId == Guid.Empty)
            return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
        try
        {
            var root = await context.Labels.AsNoTracking().Where(row => row.CompanyId == companyId && row.Id == rootLabelId)
                .Select(row => new InventoryCostLineageLabelFact(row.CompanyId, row.Id, row.ProductId, row.ProductVersion, row.Version))
                .SingleOrDefaultAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (root is null) return Failed(InventoryCostLineageOutcome.NotFound, companyId, productId, rootLabelId);
            if (!ValidLabel(root, companyId, productId)) return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);

            var labels = new Dictionary<Guid, InventoryCostLineageLabelFact> { [rootLabelId] = root };
            var seenEvents = new HashSet<Guid>();
            var headers = new List<InventoryCostLineageTransformationFact>();
            var inputs = new List<InventoryCostLineageEdgeFact>();
            var outputs = new List<InventoryCostLineageEdgeFact>();
            var frontier = new HashSet<Guid> { rootLabelId };
            while (frontier.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var discoveredEvents = new HashSet<Guid>();
                foreach (var batch in frontier.Order().Chunk(QueryBatchSize))
                {
                    var incoming = await context.LabelTransformationInputs.AsNoTracking()
                        .Where(row => row.CompanyId == companyId && batch.Contains(row.LabelId))
                        .Select(row => row.TransformationId).Distinct()
                        .Take(InventoryCostLineageShares.MaximumTransformations + 1).ToListAsync(cancellationToken);
                    var outgoing = await context.LabelTransformationOutputs.AsNoTracking()
                        .Where(row => row.CompanyId == companyId && batch.Contains(row.LabelId))
                        .Select(row => row.TransformationId).Distinct()
                        .Take(InventoryCostLineageShares.MaximumTransformations + 1).ToListAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    discoveredEvents.UnionWith(incoming);
                    discoveredEvents.UnionWith(outgoing);
                    if (discoveredEvents.Count > InventoryCostLineageShares.MaximumTransformations)
                        return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
                }

                discoveredEvents.ExceptWith(seenEvents);
                if (seenEvents.Count + discoveredEvents.Count > InventoryCostLineageShares.MaximumTransformations)
                    return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
                var nextLabels = new HashSet<Guid>();
                foreach (var batch in discoveredEvents.Order().Chunk(QueryBatchSize))
                {
                    var found = await context.LabelTransformations.AsNoTracking()
                        .Where(row => row.CompanyId == companyId && batch.Contains(row.Id))
                        .Select(row => new InventoryCostLineageTransformationFact(row.CompanyId, row.Id, row.Kind))
                        .Take(batch.Length + 1).ToListAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (found.Count != batch.Length || found.Any(row => !KnownKind(row.Kind)))
                        return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
                    seenEvents.UnionWith(batch);
                    // Do not load a production edge's other labels: they may legitimately be a different product.
                    var repackIds = found.Where(row => row.Kind != (int)InventoryLabelTransformationKind.Manufacturing)
                        .Select(row => row.TransformationId).ToArray();
                    headers.AddRange(found.Where(row => row.Kind != (int)InventoryLabelTransformationKind.Manufacturing));
                    if (repackIds.Length == 0) continue;
                    var loadedInputs = await context.LabelTransformationInputs.AsNoTracking()
                        .Where(row => row.CompanyId == companyId && repackIds.Contains(row.TransformationId))
                        .OrderBy(row => row.TransformationId).ThenBy(row => row.Sequence)
                        .Select(row => new InventoryCostLineageEdgeFact(row.CompanyId, row.TransformationId, row.Sequence,
                            row.LabelId, row.LabelVersion, row.QuantityKg))
                        .Take(InventoryCostLineageShares.MaximumQuantityRows - inputs.Count - outputs.Count + 1)
                        .ToListAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    inputs.AddRange(loadedInputs);
                    if (inputs.Count + outputs.Count > InventoryCostLineageShares.MaximumQuantityRows)
                        return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
                    var loadedOutputs = await context.LabelTransformationOutputs.AsNoTracking()
                        .Where(row => row.CompanyId == companyId && repackIds.Contains(row.TransformationId))
                        .OrderBy(row => row.TransformationId).ThenBy(row => row.Sequence)
                        .Select(row => new InventoryCostLineageEdgeFact(row.CompanyId, row.TransformationId, row.Sequence,
                            row.LabelId, row.LabelVersion, row.QuantityKg))
                        .Take(InventoryCostLineageShares.MaximumQuantityRows - inputs.Count - outputs.Count + 1)
                        .ToListAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    outputs.AddRange(loadedOutputs);
                    if (inputs.Count + outputs.Count > InventoryCostLineageShares.MaximumQuantityRows)
                        return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
                    nextLabels.UnionWith(loadedInputs.Select(row => row.LabelId));
                    nextLabels.UnionWith(loadedOutputs.Select(row => row.LabelId));
                }

                nextLabels.ExceptWith(labels.Keys);
                if (nextLabels.Contains(Guid.Empty) || labels.Count + nextLabels.Count > InventoryCostLineageShares.MaximumQuantityRows + 1)
                    return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
                foreach (var batch in nextLabels.Order().Chunk(QueryBatchSize))
                {
                    var found = await context.Labels.AsNoTracking().Where(row => row.CompanyId == companyId && batch.Contains(row.Id))
                        .Select(row => new InventoryCostLineageLabelFact(row.CompanyId, row.Id, row.ProductId, row.ProductVersion, row.Version))
                        .Take(batch.Length + 1).ToListAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (found.Count != batch.Length || found.Any(row => !ValidLabel(row, companyId, productId)))
                        return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
                    foreach (var label in found) labels.Add(label.LabelId, label);
                }

                frontier = nextLabels;
            }

            var lines = new List<InventoryCostLineageMovementLineFact>();
            var balances = new List<InventoryCostLineageBalanceFact>();
            foreach (var batch in labels.Keys.Order().Chunk(QueryBatchSize))
            {
                lines.AddRange(await context.MovementLines.AsNoTracking()
                    .Where(row => row.CompanyId == companyId && batch.Contains(row.LabelId))
                    .OrderBy(row => row.MovementId).ThenBy(row => row.LineNumber)
                    .Select(row => new InventoryCostLineageMovementLineFact(row.CompanyId, row.MovementId, row.LineNumber,
                        row.LabelId, row.ProductId, row.Direction, row.OwnerKind, row.OwnerId, row.CustodianKind, row.CustodianId,
                        row.LocationId, row.Block, row.QuantityKg))
                    .Take(MaximumMovementLines - lines.Count + 1).ToListAsync(cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                if (lines.Count > MaximumMovementLines) return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
                balances.AddRange(await context.Balances.AsNoTracking()
                    .Where(row => row.CompanyId == companyId && batch.Contains(row.LabelId))
                    .OrderBy(row => row.Id)
                    .Select(row => new InventoryCostLineageBalanceFact(row.CompanyId, row.LabelId, row.ProductId,
                        row.OwnerKind, row.OwnerId, row.CustodianKind, row.CustodianId, row.LocationId, row.Block, row.QuantityKg))
                    .Take(MaximumBalanceRows - balances.Count + 1).ToListAsync(cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                if (balances.Count > MaximumBalanceRows) return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
            }

            var movements = new List<InventoryCostLineageMovementFact>();
            foreach (var batch in lines.Select(row => row.MovementId).Distinct().Order().Chunk(QueryBatchSize))
            {
                var found = await context.Movements.AsNoTracking().Where(row => row.CompanyId == companyId && batch.Contains(row.Id))
                    .Select(row => new InventoryCostLineageMovementFact(row.CompanyId, row.Id, row.Kind, row.SourceKind,
                        row.SourceId, row.SourceLineId, row.SourceGrain, row.RecordedAtUtc)).Take(batch.Length + 1).ToListAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (found.Count != batch.Length) return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
                movements.AddRange(found);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Compute(companyId, productId, rootLabelId, labels.Values.ToArray(), headers, inputs, outputs,
                movements, lines, balances, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or ArgumentException or OverflowException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
        }
    }

    /// <summary>Pure reconciliation of the admitted bounded owner facts; also used by source model tests.</summary>
    internal static InventoryCostLineageSnapshot Compute(Guid companyId, Guid productId, Guid rootLabelId,
        IReadOnlyList<InventoryCostLineageLabelFact> labelFacts, IReadOnlyList<InventoryCostLineageTransformationFact> headers,
        IReadOnlyList<InventoryCostLineageEdgeFact> inputs, IReadOnlyList<InventoryCostLineageEdgeFact> outputs,
        IReadOnlyList<InventoryCostLineageMovementFact> movementFacts, IReadOnlyList<InventoryCostLineageMovementLineFact> lines,
        IReadOnlyList<InventoryCostLineageBalanceFact> balances, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InventoryCostLineageSnapshot Fault() => Failed(InventoryCostLineageOutcome.Unreadable, companyId, productId, rootLabelId);
        if (companyId == Guid.Empty || productId == Guid.Empty || rootLabelId == Guid.Empty
            || labelFacts.Count > InventoryCostLineageShares.MaximumQuantityRows + 1
            || headers.Count > InventoryCostLineageShares.MaximumTransformations
            || inputs.Count + outputs.Count > InventoryCostLineageShares.MaximumQuantityRows
            || lines.Count > MaximumMovementLines || movementFacts.Count > MaximumMovementLines || balances.Count > MaximumBalanceRows)
            return Fault();
        if (labelFacts.Count == 0)
            return headers.Count + inputs.Count + outputs.Count + movementFacts.Count + lines.Count + balances.Count == 0
                ? Failed(InventoryCostLineageOutcome.NotFound, companyId, productId, rootLabelId) : Fault();
        if (labelFacts.Any(row => row is null || !ValidLabel(row, companyId, productId))
            || labelFacts.Select(row => row.LabelId).Distinct().Count() != labelFacts.Count) return Fault();
        var labels = labelFacts.ToDictionary(row => row.LabelId);
        if (!labels.ContainsKey(rootLabelId)) return Fault();
        if (headers.Any(row => row is null || row.CompanyId != companyId || row.TransformationId == Guid.Empty || !KnownKind(row.Kind))
            || headers.Select(row => row.TransformationId).Distinct().Count() != headers.Count) return Fault();
        var headerMap = headers.ToDictionary(row => row.TransformationId);
        if (inputs.Concat(outputs).Any(row => row is null || row.CompanyId != companyId || row.LabelId == Guid.Empty
                || row.Sequence < 1 || row.LabelVersion < 1 || row.QuantityKg <= 0m || !headerMap.ContainsKey(row.TransformationId))) return Fault();
        var repackIds = headers.Where(row => row.Kind != (int)InventoryLabelTransformationKind.Manufacturing)
            .Select(row => row.TransformationId).ToHashSet();
        var repackInputs = inputs.Where(row => repackIds.Contains(row.TransformationId)).ToArray();
        var repackOutputs = outputs.Where(row => repackIds.Contains(row.TransformationId)).ToArray();
        if (repackInputs.Any(row => !labels.TryGetValue(row.LabelId, out var label) || label.Version <= row.LabelVersion)
            || repackOutputs.Any(row => !labels.TryGetValue(row.LabelId, out var label) || label.Version < row.LabelVersion)) return Fault();

        var transformations = new List<InventoryCostLineageTransformation>();
        var adjacent = labels.Keys.ToDictionary(id => id, _ => new HashSet<Guid>());
        var physical = labels.Keys.ToDictionary(id => id, _ => ExactCostRatio.Of(0m));
        foreach (var header in headers.Where(row => repackIds.Contains(row.TransformationId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var from = repackInputs.Where(row => row.TransformationId == header.TransformationId).OrderBy(row => row.Sequence).ToArray();
            var to = repackOutputs.Where(row => row.TransformationId == header.TransformationId).OrderBy(row => row.Sequence).ToArray();
            if (!Consecutive(from) || !Consecutive(to)) return Fault();
            transformations.Add(new InventoryCostLineageTransformation(header.TransformationId, (InventoryLabelTransformationKind)header.Kind,
                from.Select(row => new InventoryCostLineageQuantity(row.LabelId, row.QuantityKg)),
                to.Select(row => new InventoryCostLineageQuantity(row.LabelId, row.QuantityKg))));
            foreach (var input in from)
            {
                physical[input.LabelId] = physical[input.LabelId].Subtract(ExactCostRatio.Of(input.QuantityKg));
                foreach (var output in to)
                {
                    adjacent[input.LabelId].Add(output.LabelId);
                    adjacent[output.LabelId].Add(input.LabelId);
                }
            }
            foreach (var output in to) physical[output.LabelId] = physical[output.LabelId].Add(ExactCostRatio.Of(output.QuantityKg));
        }

        var connected = new HashSet<Guid> { rootLabelId };
        var pending = new Queue<Guid>([rootLabelId]);
        while (pending.TryDequeue(out var queuedLabel))
            foreach (var neighbor in adjacent[queuedLabel]) if (connected.Add(neighbor)) pending.Enqueue(neighbor);
        if (connected.Count != labels.Count
            || !InventoryCostLineageShares.TryResolve(rootLabelId, transformations, out var coefficients, out _)) return Fault();

        if (movementFacts.Any(row => row is null || row.CompanyId != companyId || row.MovementId == Guid.Empty
                || !Enum.IsDefined((StockMovementKind)row.Kind) || string.IsNullOrWhiteSpace(row.SourceKind) || row.SourceId == Guid.Empty
                || (row.SourceGrain == (int)StockSourceGrain.Whole ? row.SourceLineId is not null
                    : row.SourceGrain != (int)StockSourceGrain.Line || row.SourceLineId is null || row.SourceLineId == Guid.Empty))
            || movementFacts.Select(row => row.MovementId).Distinct().Count() != movementFacts.Count) return Fault();
        var movements = movementFacts.ToDictionary(row => row.MovementId);
        var lineKeys = new HashSet<(Guid Movement, int Line)>();
        var referenced = new HashSet<Guid>();
        var shippedByLabel = labels.Keys.ToDictionary(id => id, _ => ExactCostRatio.Of(0m));
        var returnsByLabel = labels.Keys.ToDictionary(id => id, _ => ExactCostRatio.Of(0m));
        var shipments = new Dictionary<Guid, InventoryCostLineageMovementLineFact>();
        var returns = new List<InventoryCostLineageMovementLineFact>();
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line is null || line.CompanyId != companyId || line.ProductId != productId || !labels.ContainsKey(line.LabelId)
                || line.LineNumber < 1 || line.QuantityKg <= 0m || !ValidPosition(line.OwnerKind, line.OwnerId, line.CustodianKind,
                    line.CustodianId, line.LocationId, line.Block) || !lineKeys.Add((line.MovementId, line.LineNumber))
                || !movements.TryGetValue(line.MovementId, out var movement) || !ShapeMatches(movement.Kind, line.Direction)) return Fault();
            referenced.Add(line.MovementId);
            physical[line.LabelId] = physical[line.LabelId].Add(ExactCostRatio.Of(
                line.Direction == (int)StockDirection.In ? line.QuantityKg : -line.QuantityKg));
            if (string.Equals(movement.SourceKind, InventoryLabelDispositionReader.DefiniteShipmentSourceKind, StringComparison.Ordinal))
            {
                if (movement.Kind != (int)StockMovementKind.Issue || line.Direction != (int)StockDirection.Out
                    || movement.SourceGrain != (int)StockSourceGrain.Line || line.LineNumber != 1 || !shipments.TryAdd(line.MovementId, line)) return Fault();
                shippedByLabel[line.LabelId] = shippedByLabel[line.LabelId].Add(ExactCostRatio.Of(line.QuantityKg));
            }
            if (string.Equals(movement.SourceKind, InventoryCustomerReturnContractV1.SourceKind, StringComparison.Ordinal))
            {
                if (movement.Kind != (int)StockMovementKind.Return || line.Direction != (int)StockDirection.In
                    || movement.SourceGrain != (int)StockSourceGrain.Line || line.LineNumber != 1) return Fault();
                returns.Add(line);
                returnsByLabel[line.LabelId] = returnsByLabel[line.LabelId].Add(ExactCostRatio.Of(line.QuantityKg));
            }
        }
        if (referenced.Count != movements.Count) return Fault();
        var returnedByIssue = new Dictionary<Guid, ExactCostRatio>();
        foreach (var returned in returns)
        {
            var issueId = movements[returned.MovementId].SourceId;
            if (!shipments.TryGetValue(issueId, out var issue) || issue.LabelId != returned.LabelId) return Fault();
            var already = returnedByIssue.GetValueOrDefault(issueId) ?? ExactCostRatio.Of(0m);
            var total = already.Add(ExactCostRatio.Of(returned.QuantityKg));
            if (total.CompareTo(ExactCostRatio.Of(issue.QuantityKg)) > 0) return Fault();
            returnedByIssue[issueId] = total;
        }

        var actual = labels.Keys.ToDictionary(id => id, _ => ExactCostRatio.Of(0m));
        var positions = labels.Keys.ToDictionary(id => id, _ => new List<InventoryCostLineagePositionSnapshot>());
        var positionKeys = new HashSet<(Guid Label, int OwnerKind, Guid Owner, int CustodianKind, Guid Custodian, Guid Location, int Block)>();
        foreach (var balance in balances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (balance is null || balance.CompanyId != companyId || balance.ProductId != productId || !labels.ContainsKey(balance.LabelId)
                || balance.QuantityKg < 0m || !ValidPosition(balance.OwnerKind, balance.OwnerId, balance.CustodianKind,
                    balance.CustodianId, balance.LocationId, balance.Block)
                || !positionKeys.Add((balance.LabelId, balance.OwnerKind, balance.OwnerId, balance.CustodianKind,
                    balance.CustodianId, balance.LocationId, balance.Block))) return Fault();
            actual[balance.LabelId] = actual[balance.LabelId].Add(ExactCostRatio.Of(balance.QuantityKg));
            positions[balance.LabelId].Add(new InventoryCostLineagePositionSnapshot(new StockOwner((StockPartyKind)balance.OwnerKind, balance.OwnerId),
                new StockCustodian((StockPartyKind)balance.CustodianKind, balance.CustodianId), balance.LocationId, (BlockState)balance.Block, balance.QuantityKg));
        }

        var snapshots = new List<InventoryCostLineageLabelSnapshot>();
        var remainingKg = ExactCostRatio.Of(0m);
        var shippedKg = ExactCostRatio.Of(0m);
        var returnedKg = ExactCostRatio.Of(0m);
        foreach (var label in labels.Values.OrderBy(row => row.LabelId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (physical[label.LabelId].CompareTo(actual[label.LabelId]) != 0
                || positions[label.LabelId].Where(row => row.QuantityKg > 0m)
                    .Select(row => (row.Owner, row.Custodian, row.LocationId)).Distinct().Take(2).Count() > 1) return Fault();
            var share = coefficients.GetValueOrDefault(label.LabelId) ?? ExactCostRatio.Of(0m);
            remainingKg = remainingKg.Add(actual[label.LabelId].Multiply(share));
            shippedKg = shippedKg.Add(shippedByLabel[label.LabelId].Multiply(share));
            returnedKg = returnedKg.Add(returnsByLabel[label.LabelId].Multiply(share));
            snapshots.Add(new InventoryCostLineageLabelSnapshot(label.LabelId, label.Version, label.ProductVersion!, share,
                positions[label.LabelId].OrderBy(row => row.LocationId).ThenBy(row => row.Owner.Kind).ThenBy(row => row.Owner.Id)
                    .ThenBy(row => row.Custodian.Kind).ThenBy(row => row.Custodian.Id).ThenBy(row => row.Block)));
        }
        if (shippedKg.Subtract(returnedKg).Sign < 0) return Fault();
        // Keep every actual positive-share issue, including fully returned ones. These are physical source facts,
        // not an allocation of the generation's rounded consumed TL amount to a shipment or invoice.
        var shipmentSnapshots = shipments.Values.OrderBy(row => row.MovementId).ThenBy(row => row.LineNumber)
            .Where(row => coefficients.TryGetValue(row.LabelId, out var share) && share.Sign > 0)
            .Select(issue =>
            {
                var movement = movements[issue.MovementId];
                var share = coefficients[issue.LabelId];
                var returned = returnedByIssue.GetValueOrDefault(issue.MovementId) ?? ExactCostRatio.Of(0m);
                var returnSnapshots = returns.Where(row => movements[row.MovementId].SourceId == issue.MovementId)
                    .OrderBy(row => row.MovementId).ThenBy(row => row.LineNumber)
                    .Select(row => new InventoryCostLineageReturnSnapshot(movements[row.MovementId].SourceLineId!.Value,
                        row.MovementId, row.LineNumber, issue.MovementId, row.QuantityKg, movements[row.MovementId].RecordedAtUtc)).ToArray();
                return new InventoryCostLineageShipmentSnapshot(movement.SourceId, issue.MovementId, issue.LineNumber,
                    movement.SourceLineId!.Value, issue.LabelId, issue.ProductId, labels[issue.LabelId].ProductVersion!,
                    new StockOwner((StockPartyKind)issue.OwnerKind, issue.OwnerId), new StockCustodian((StockPartyKind)issue.CustodianKind, issue.CustodianId),
                    issue.LocationId, (BlockState)issue.Block, issue.QuantityKg, share,
                    ExactCostRatio.Of(issue.QuantityKg).Subtract(returned).Multiply(share), Array.AsReadOnly(returnSnapshots), movement.RecordedAtUtc);
            }).ToArray();
        return new InventoryCostLineageSnapshot(InventoryCostLineageOutcome.Value, companyId, productId, rootLabelId, snapshots, remainingKg, shippedKg, returnedKg,
            null, shipmentSnapshots);
    }

    private static bool Consecutive(InventoryCostLineageEdgeFact[] edges) =>
        edges.Select((row, index) => row.Sequence == index + 1).All(value => value);
    private static bool ValidLabel(InventoryCostLineageLabelFact label, Guid company, Guid product) =>
        label.CompanyId == company && label.LabelId != Guid.Empty && label.ProductId == product && label.Version >= 1
        && !string.IsNullOrWhiteSpace(label.ProductVersion);
    private static bool KnownKind(int kind) => kind is (int)InventoryLabelTransformationKind.Split
        or (int)InventoryLabelTransformationKind.Merge or (int)InventoryLabelTransformationKind.Manufacturing;
    private static bool ValidPosition(int ownerKind, Guid owner, int custodianKind, Guid custodian, Guid location, int block) =>
        owner != Guid.Empty && custodian != Guid.Empty && location != Guid.Empty
        && (ownerKind is (int)StockPartyKind.Company or (int)StockPartyKind.Counterparty)
        && (custodianKind is (int)StockPartyKind.Company or (int)StockPartyKind.Counterparty)
        && (block is (int)BlockState.Free or (int)BlockState.Blocked);
    private static bool ShapeMatches(int kind, int direction) => (direction is (int)StockDirection.In or (int)StockDirection.Out)
        && ((StockMovementKind)kind switch
        {
            StockMovementKind.Receipt or StockMovementKind.Return or StockMovementKind.TransferAccept
                or StockMovementKind.TransferDispatchCancellation or StockMovementKind.TransferReceiptOverage => direction == (int)StockDirection.In,
            StockMovementKind.Issue or StockMovementKind.Scrap or StockMovementKind.TransferDispatch
                or StockMovementKind.ManufacturingOutputReversal or StockMovementKind.TransferReceiptShortage
                or StockMovementKind.SupplierReturnExit or StockMovementKind.PurchaseAcceptanceReversal => direction == (int)StockDirection.Out,
            _ => true,
        });
    private static InventoryCostLineageSnapshot Failed(InventoryCostLineageOutcome outcome, Guid company, Guid product, Guid root) =>
        new InventoryCostLineageSnapshot(outcome, company, product, root, [], null, null, null, outcome == InventoryCostLineageOutcome.Unreadable ? UnreadableReason : null);
}

internal sealed record InventoryCostLineageLabelFact(Guid CompanyId, Guid LabelId, Guid ProductId, string? ProductVersion, long Version);
internal sealed record InventoryCostLineageTransformationFact(Guid CompanyId, Guid TransformationId, int Kind);
internal sealed record InventoryCostLineageEdgeFact(Guid CompanyId, Guid TransformationId, int Sequence, Guid LabelId, long LabelVersion, decimal QuantityKg);
internal sealed record InventoryCostLineageMovementFact(Guid CompanyId, Guid MovementId, int Kind, string SourceKind,
    Guid SourceId, Guid? SourceLineId, int SourceGrain, DateTimeOffset RecordedAtUtc = default);
internal sealed record InventoryCostLineageMovementLineFact(Guid CompanyId, Guid MovementId, int LineNumber, Guid LabelId, Guid ProductId,
    int Direction, int OwnerKind, Guid OwnerId, int CustodianKind, Guid CustodianId, Guid LocationId, int Block, decimal QuantityKg);
internal sealed record InventoryCostLineageBalanceFact(Guid CompanyId, Guid LabelId, Guid ProductId, int OwnerKind, Guid OwnerId,
    int CustodianKind, Guid CustodianId, Guid LocationId, int Block, decimal QuantityKg);

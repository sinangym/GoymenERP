using System.Data;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>URT-D31/D36/D56 / SAT-D260: a recognized cost epoch is not moved back by
/// rewriting a transfer. Its genuine cancellation or completed linked reverse comes first.
/// The append-only recognition JSON owns the observation; no historical source is backfilled.</summary>
internal static class InventoryManufacturingTransferReversalProof
{
    internal const string RequiredCode = "inventory_manufacturing_cost_transfer_reversal_required";
    internal const string UnavailableCode = "inventory_manufacturing_cost_transfer_source_unreadable";
    private const int Limit = InventoryCostLineageShares.MaximumQuantityRows;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal sealed record Position(Guid LocationId, int OwnerKind, Guid OwnerId, int CustodianKind,
        Guid CustodianId, InventoryExactCostRatioV1 QuantityKg);
    internal sealed record Label(Guid LabelId, long Version, string ProductVersion);
    internal sealed record Dispatch(Guid TransferId, Guid LineId, Guid LabelId, Guid MovementId, long LabelVersion, decimal QuantityKg);
    internal sealed record Epoch(Guid CompanyId, Guid GenerationId, Guid OutputId, Guid ReceiptMovementId,
        Guid RootLabelId, Guid ProductId, string SourceVersion, IReadOnlyList<Label> Labels,
        IReadOnlyList<Position> Positions, IReadOnlyList<Dispatch> Dispatches);

    internal static async Task<Epoch> CaptureAsync(InventoryDbContext db, InventoryCostLineageSnapshot lineage,
        InventoryManufacturingCostGenerationOutputV1 output, Guid generation, string sourceVersion, CancellationToken token)
    {
        RequireTransaction(db);
        var facts = await ReadAsync(db, lineage, token);
        return new(lineage.CompanyId, generation, output.OutputId, output.ReceiptMovementId, output.LabelId, lineage.ProductId, sourceVersion,
            lineage.Labels.Where(x => x.RootShare.Sign > 0).OrderBy(x => x.LabelId)
                .Select(x => new Label(x.LabelId, x.Version, x.ProductVersion)).ToArray(), Positions(lineage), facts.Dispatches);
    }

    internal static async Task RequireReversibleAsync(InventoryDbContext db, InventoryCostLineageSnapshot lineage,
        InventoryManufacturingCostGenerationOutputV1 output, Guid generation, string sourceVersion,
        IReadOnlyList<InventoryManufacturingCostPositionEffectV1> originalPositions, CancellationToken token)
    {
        RequireTransaction(db);
        var facts = await ReadAsync(db, lineage, token);
        var recognitions = await db.MovementCostRecognitions.AsNoTracking().Where(x => x.CompanyId == lineage.CompanyId
            && x.GenerationId == generation && x.MovementId == output.ReceiptMovementId && x.LineNumber == 1)
            .Select(x => new { x.SourceVersion, x.ResultJson }).Take(3).ToListAsync(token);
        if (recognitions.Count is < 1 or > 2 || recognitions.Any(x => x.SourceVersion != sourceVersion)) throw Unavailable();
        Epoch? epoch = null;
        bool? hasEpoch = null;
        foreach (var row in recognitions)
        {
            Epoch? parsed;
            try
            {
                using var json = JsonDocument.Parse(row.ResultJson);
                parsed = json.RootElement.TryGetProperty("transferScope", out var value)
                    ? value.Deserialize<Epoch>(Json) : null;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException) { throw Unavailable(); }
            if (hasEpoch is { } expected && expected != (parsed is not null)) throw Unavailable();
            hasEpoch = parsed is not null;
            if (parsed is null)
            {
                // A genuinely empty, locked transfer history is sufficient for legacy no-transfer
                // generations. The mutable current location never reconstructs an old epoch.
                if (facts.Dispatches.Count != 0) throw Unavailable();
                continue;
            }
            if (epoch is not null && JsonSerializer.Serialize(epoch, Json) != JsonSerializer.Serialize(parsed, Json)) throw Unavailable();
            epoch = parsed;
        }
        if (epoch is null)
        {
            // No-transfer legacy can use its actual frozen included/location/quantity facts.
            // It cannot recover a missing epoch from a transfer that happened to end here.
            foreach (var group in Positions(lineage).GroupBy(x => (x.LocationId,
                Included: x.OwnerKind == (int)StockPartyKind.Company && x.OwnerId == lineage.CompanyId
                    && x.CustodianKind == (int)StockPartyKind.Company && x.CustodianId == lineage.CompanyId)))
            {
                var original = originalPositions.SingleOrDefault(x => x.LocationId == group.Key.LocationId
                    && x.IncludedInMovingAverage == group.Key.Included) ?? throw Unavailable();
                var quantity = group.Aggregate(ExactCostRatio.Of(0m), (sum, x) => sum.Add(x.QuantityKg.Value()));
                if (quantity.CompareTo(InventoryExactCostRatioV1.Resolve(original.QuantityKg, original.QuantityKgRatio)) > 0) throw Unavailable();
            }
            return;
        }
        if (epoch.CompanyId != lineage.CompanyId || epoch.GenerationId != generation || epoch.OutputId != output.OutputId
            || epoch.ReceiptMovementId != output.ReceiptMovementId || epoch.RootLabelId != output.LabelId
            || epoch.ProductId != lineage.ProductId || epoch.SourceVersion != sourceVersion
            || epoch.Labels is null || epoch.Positions is null || epoch.Dispatches is null
            || epoch.Labels.Any(x => x is null) || epoch.Positions.Any(x => x is null) || epoch.Dispatches.Any(x => x is null)
            || epoch.Labels.Count is < 1 or > Limit || epoch.Positions.Count > Limit || epoch.Dispatches.Count > Limit
            || epoch.Labels.Select(x => x.LabelId).Distinct().Count() != epoch.Labels.Count
            || epoch.Dispatches.Select(x => (x.TransferId, x.LineId)).Distinct().Count() != epoch.Dispatches.Count) throw Unavailable();
        foreach (var label in epoch.Labels)
        {
            var current = lineage.Labels.SingleOrDefault(x => x.LabelId == label.LabelId);
            if (label.LabelId == Guid.Empty || label.Version < 1 || current is null || current.RootShare.Sign <= 0
                || current.Version <= label.Version || current.ProductVersion != label.ProductVersion) throw Unavailable();
        }
        foreach (var observed in epoch.Dispatches)
        {
            if (!facts.Dispatches.Contains(observed) || !epoch.Labels.Any(x => x.LabelId == observed.LabelId
                && x.Version >= observed.LabelVersion)) throw Unavailable();
        }
        RequireUndo(epoch, facts);
        // Real transfer undo must return the still-present root share to the recognized
        // owner/custodian/location scope. A same-location unrelated receipt is no proof.
        RequirePositionScope(epoch.Positions, Positions(lineage));
    }

    internal static void RequireUndo(Epoch epoch, Facts facts)
    {
        var originals = epoch.Dispatches.Select(x => (x.TransferId, x.LineId)).ToHashSet();
        var later = facts.Dispatches.Where(x => !originals.Contains((x.TransferId, x.LineId)))
            .OrderBy(x => x.LabelId).ThenBy(x => x.LabelVersion).ToArray();
        var restored = new HashSet<(Guid, Guid)>();
        foreach (var dispatch in later)
        {
            if (restored.Contains((dispatch.TransferId, dispatch.LineId))) continue;
            if (facts.Cancelled(dispatch)) { restored.Add((dispatch.TransferId, dispatch.LineId)); continue; }
            var reversal = facts.CompletedReverse(dispatch);
            if (reversal is null || originals.Contains((reversal.TransferId, reversal.LineId))) throw Required();
            restored.Add((dispatch.TransferId, dispatch.LineId)); restored.Add((reversal.TransferId, reversal.LineId));
        }
    }

    private static Position[] Positions(InventoryCostLineageSnapshot lineage) => lineage.Labels.Where(x => x.RootShare.Sign > 0)
        .SelectMany(label => label.Positions.Where(x => x.QuantityKg > 0m).Select(x => new
        { x.LocationId, OwnerKind = (int)x.Owner.Kind, OwnerId = x.Owner.Id, CustodianKind = (int)x.Custodian.Kind,
            CustodianId = x.Custodian.Id, Quantity = label.RootShare.Multiply(x.QuantityKg) }))
        .GroupBy(x => (x.LocationId, x.OwnerKind, x.OwnerId, x.CustodianKind, x.CustodianId))
        .OrderBy(x => x.Key.LocationId).ThenBy(x => x.Key.OwnerKind).ThenBy(x => x.Key.OwnerId)
        .ThenBy(x => x.Key.CustodianKind).ThenBy(x => x.Key.CustodianId)
        .Select(x => new Position(x.Key.LocationId, x.Key.OwnerKind, x.Key.OwnerId, x.Key.CustodianKind, x.Key.CustodianId,
            InventoryExactCostRatioV1.From(x.Aggregate(ExactCostRatio.Of(0m), (sum, item) => sum.Add(item.Quantity))))).ToArray();

    internal static void RequirePositionScope(IReadOnlyList<Position> original, IReadOnlyList<Position> current)
    {
        static (Guid, int, Guid, int, Guid) Key(Position x) => (x.LocationId, x.OwnerKind, x.OwnerId, x.CustodianKind, x.CustodianId);
        try
        {
            if (original.Select(Key).Distinct().Count() != original.Count || original.Any(x => x.QuantityKg.Value().Sign <= 0)) throw Unavailable();
            foreach (var position in current)
            {
                var source = original.SingleOrDefault(x => Key(x) == Key(position));
                if (source is null || position.QuantityKg.Value().Sign <= 0
                    || position.QuantityKg.Value().CompareTo(source.QuantityKg.Value()) > 0) throw Unavailable();
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NullReferenceException or FormatException or OverflowException) { throw Unavailable(); }
    }

    private static async Task<Facts> ReadAsync(InventoryDbContext db, InventoryCostLineageSnapshot lineage, CancellationToken token)
    {
        var labels = lineage.Labels.Where(x => x.RootShare.Sign > 0).Select(x => x.LabelId).Distinct().ToArray();
        if (labels.Length is < 1 or > Limit) throw Unavailable();
        var lines = await db.TransferLines.AsNoTracking().Where(x => x.CompanyId == lineage.CompanyId && labels.Contains(x.LabelId))
            .Take(Limit + 1).ToListAsync(token);
        if (lines.Count > Limit) throw Unavailable();
        var ids = lines.Select(x => x.TransferId).Distinct().ToArray();
        var lineIds = lines.Select(x => x.LineId).Distinct().ToArray();
        var headers = await db.Transfers.AsNoTracking().Where(x => x.CompanyId == lineage.CompanyId && ids.Contains(x.Id)).ToListAsync(token);
        var dispatches = await db.TransferDispatches.AsNoTracking().Where(x => x.CompanyId == lineage.CompanyId && ids.Contains(x.TransferId)).ToListAsync(token);
        var actual = await db.TransferDispatchLines.AsNoTracking().Where(x => x.CompanyId == lineage.CompanyId && ids.Contains(x.TransferId) && lineIds.Contains(x.LineId)).Take(Limit + 1).ToListAsync(token);
        var cancellations = await db.TransferDispatchCancellations.AsNoTracking().Where(x => x.CompanyId == lineage.CompanyId && ids.Contains(x.TransferId)).ToListAsync(token);
        var acceptances = await db.TransferAcceptances.AsNoTracking().Where(x => x.CompanyId == lineage.CompanyId && ids.Contains(x.TransferId) && lineIds.Contains(x.LineId)).Take(Limit + 1).ToListAsync(token);
        var links = await db.TransferReversalLines.AsNoTracking().Where(x => x.CompanyId == lineage.CompanyId && ids.Contains(x.TransferId) && lineIds.Contains(x.LineId)).Take(Limit + 1).ToListAsync(token);
        var reverseHeaders = await db.TransferReversals.AsNoTracking().Where(x => x.CompanyId == lineage.CompanyId && ids.Contains(x.TransferId)).ToListAsync(token);
        var buckets = await db.TransferBuckets.AsNoTracking().Where(x => x.CompanyId == lineage.CompanyId && ids.Contains(x.TransferId)).Take(Limit + 1).ToListAsync(token);
        var movementIds = dispatches.Select(x => x.MovementId).Concat(cancellations.Select(x => x.MovementId)).Concat(acceptances.Select(x => x.MovementId)).Distinct().ToArray();
        var movements = await db.Movements.AsNoTracking().Where(x => x.CompanyId == lineage.CompanyId && movementIds.Contains(x.Id)).Take(Limit + 1).ToListAsync(token);
        var movementLines = await db.MovementLines.AsNoTracking().Where(x => x.CompanyId == lineage.CompanyId && movementIds.Contains(x.MovementId)).Take(Limit + 1).ToListAsync(token);
        if (actual.Count > Limit || acceptances.Count > Limit || links.Count > Limit || buckets.Count > Limit || movements.Count > Limit || movementLines.Count > Limit) throw Unavailable();
        var result = new Facts(lineage.CompanyId, lineage.ProductId, headers, lines, dispatches, actual, cancellations, acceptances, links, reverseHeaders, buckets, movements, movementLines);
        result.Validate(); return result;
    }

    internal sealed class Facts(Guid company, Guid product, IReadOnlyList<InventoryTransferRow> headers, IReadOnlyList<InventoryTransferLineRow> lines,
        IReadOnlyList<InventoryTransferDispatchRow> dispatches, IReadOnlyList<InventoryTransferDispatchLineRow> actual,
        IReadOnlyList<InventoryTransferDispatchCancellationRow> cancellations, IReadOnlyList<InventoryTransferAcceptanceRow> acceptances,
        IReadOnlyList<InventoryTransferReversalLineRow> links, IReadOnlyList<InventoryTransferReversalRow> reverseHeaders,
        IReadOnlyList<InventoryTransferBucketRow> buckets, IReadOnlyList<MovementRow> movements, IReadOnlyList<MovementLineRow> movementLines)
    {
        internal IReadOnlyList<Dispatch> Dispatches { get; private set; } = [];
        internal void Validate()
        {
            if (company == Guid.Empty || product == Guid.Empty || headers.Cast<IInventoryTenantRow>().Concat(lines).Concat(dispatches)
                .Concat(actual).Concat(cancellations).Concat(acceptances).Concat(links).Concat(reverseHeaders).Concat(buckets)
                .Concat(movements).Concat(movementLines).Any(x => x.CompanyId != company)) throw Unavailable();
            var result = new List<Dispatch>();
            foreach (var line in lines)
            {
                var header = headers.SingleOrDefault(x => x.Id == line.TransferId) ?? throw Unavailable();
                if (!Enum.IsDefined((InventoryTransferStatus)header.Status)) throw Unavailable();
                var dispatch = dispatches.SingleOrDefault(x => x.TransferId == line.TransferId);
                if (dispatch is null)
                { if (header.Status != (int)InventoryTransferStatus.Planned) throw Unavailable(); continue; }
                if (header.Status == (int)InventoryTransferStatus.Planned) throw Unavailable();
                var measured = actual.SingleOrDefault(x => x.TransferId == line.TransferId && x.LineId == line.LineId) ?? throw Unavailable();
                if (measured.ActualKg <= 0 || line.ExpectedLabelVersion is < 1 or long.MaxValue
                    || measured.LabelVersionAfterDispatch != line.ExpectedLabelVersion + 1 || measured.ActualKg != BucketQuantity(line)
                    || buckets.Where(x => x.TransferId == line.TransferId && x.LineId == line.LineId).Any(x => x.LabelId != line.LabelId
                        || x.ProductId != product || x.QuantityKg <= 0 || x.LocationId != header.SourceLocationId
                        || x.CustodianKind != header.SourceCustodianKind || x.CustodianId != header.SourceCustodianId)) throw Unavailable();
                RequireMovement(line, dispatch.MovementId, StockMovementKind.TransferDispatch, false, null);
                result.Add(new(line.TransferId, line.LineId, line.LabelId, dispatch.MovementId, measured.LabelVersionAfterDispatch, measured.ActualKg));
            }
            Dispatches = result.OrderBy(x => x.TransferId).ThenBy(x => x.LineId).ToArray();
        }

        internal bool Cancelled(Dispatch dispatch)
        {
            var header = headers.Single(x => x.Id == dispatch.TransferId);
            var cancellation = cancellations.SingleOrDefault(x => x.TransferId == dispatch.TransferId);
            if (cancellation is null)
            { if (header.Status == (int)InventoryTransferStatus.DispatchCancelled) throw Unavailable(); return false; }
            if (header.Status != (int)InventoryTransferStatus.DispatchCancelled || acceptances.Any(x => x.TransferId == dispatch.TransferId)) throw Unavailable();
            RequireMovement(Line(dispatch), cancellation.MovementId, StockMovementKind.TransferDispatchCancellation, false, null);
            return true;
        }

        internal Dispatch? CompletedReverse(Dispatch original)
        {
            var acceptance = acceptances.SingleOrDefault(x => x.TransferId == original.TransferId && x.LineId == original.LineId);
            if (acceptance is null)
            {
                if (headers.Single(x => x.Id == original.TransferId).Status is (int)InventoryTransferStatus.Accepted or (int)InventoryTransferStatus.Closed)
                    throw Unavailable();
                return null;
            }
            if (headers.Single(x => x.Id == original.TransferId).Status is not ((int)InventoryTransferStatus.PartiallyAccepted
                or (int)InventoryTransferStatus.Accepted or (int)InventoryTransferStatus.Closed)) throw Unavailable();
            RequireMovement(Line(original), acceptance.MovementId, StockMovementKind.TransferAccept, true, original.LineId);
            var candidates = links.Where(x => x.OriginalTransferId == original.TransferId && x.OriginalLineId == original.LineId)
                .Select(x => Dispatches.SingleOrDefault(d => d.TransferId == x.TransferId && d.LineId == x.LineId)).Where(x => x is not null).Cast<Dispatch>().ToArray();
            var completed = new List<Dispatch>();
            foreach (var reverse in candidates)
            {
                if (Cancelled(reverse)) continue;
                var returned = acceptances.SingleOrDefault(x => x.TransferId == reverse.TransferId && x.LineId == reverse.LineId);
                if (returned is null) continue;
                var from = headers.Single(x => x.Id == original.TransferId); var to = headers.Single(x => x.Id == reverse.TransferId);
                if (!reverseHeaders.Any(x => x.TransferId == reverse.TransferId && x.OriginalTransferId == original.TransferId)
                    || to.Status is not ((int)InventoryTransferStatus.PartiallyAccepted or (int)InventoryTransferStatus.Accepted or (int)InventoryTransferStatus.Closed)
                    || reverse.LabelId != original.LabelId || reverse.LabelVersion <= original.LabelVersion || reverse.QuantityKg != original.QuantityKg
                    || to.SourceLocationId != from.DestinationLocationId || to.SourceCustodianKind != from.DestinationCustodianKind || to.SourceCustodianId != from.DestinationCustodianId
                    || to.DestinationLocationId != from.SourceLocationId || to.DestinationCustodianKind != from.SourceCustodianKind || to.DestinationCustodianId != from.SourceCustodianId) throw Unavailable();
                RequireMovement(Line(reverse), returned.MovementId, StockMovementKind.TransferAccept, true, reverse.LineId);
                if (!SameBuckets(Line(original), Line(reverse))) throw Unavailable();
                completed.Add(reverse);
            }
            if (completed.Count > 1) throw Unavailable();
            return completed.SingleOrDefault();
        }

        private InventoryTransferLineRow Line(Dispatch dispatch) => lines.Single(x => x.TransferId == dispatch.TransferId && x.LineId == dispatch.LineId);
        private decimal BucketQuantity(InventoryTransferLineRow line) => buckets.Where(x => x.TransferId == line.TransferId && x.LineId == line.LineId).Sum(x => x.QuantityKg);
        private bool SameBuckets(InventoryTransferLineRow original, InventoryTransferLineRow reverse)
        {
            var left = buckets.Where(x => x.TransferId == original.TransferId && x.LineId == original.LineId)
                .Select(x => (x.OwnerKind, x.OwnerId, x.Block, x.QuantityKg)).Order().ToArray();
            var right = buckets.Where(x => x.TransferId == reverse.TransferId && x.LineId == reverse.LineId)
                .Select(x => (x.OwnerKind, x.OwnerId, x.Block, x.QuantityKg)).Order().ToArray();
            return left.SequenceEqual(right);
        }
        private void RequireMovement(InventoryTransferLineRow line, Guid movementId, StockMovementKind kind, bool target, Guid? sourceLine)
        {
            var movement = movements.SingleOrDefault(x => x.Id == movementId);
            var header = headers.Single(x => x.Id == line.TransferId);
            if (movement is null || movement.Kind != (int)kind || movement.SourceKind != InventoryTransfer.SourceKind
                || movement.SourceId != line.TransferId || movement.SourceLineId != sourceLine) throw Unavailable();
            var expected = buckets.Where(x => x.TransferId == line.TransferId && x.LineId == line.LineId).Select(x =>
                (x.OwnerKind, x.OwnerId, CustodianKind: target ? header.DestinationCustodianKind : x.CustodianKind,
                    CustodianId: target ? header.DestinationCustodianId : x.CustodianId, Location: target ? header.DestinationLocationId : x.LocationId,
                    x.Block, x.QuantityKg)).Order().ToArray();
            var direction = (int)(kind == StockMovementKind.TransferDispatch ? StockDirection.Out : StockDirection.In);
            var recorded = movementLines.Where(x => x.MovementId == movementId && x.LabelId == line.LabelId).ToArray();
            if (expected.Length == 0 || recorded.Any(x => x.ProductId != product || x.Direction != direction)
                || !expected.SequenceEqual(recorded.Select(x => (x.OwnerKind, x.OwnerId, x.CustodianKind, x.CustodianId, x.LocationId, x.Block, x.QuantityKg)).Order())) throw Unavailable();
        }
    }

    private static void RequireTransaction(InventoryDbContext db)
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable) throw Unavailable();
    }
    private static InventoryManufacturingCostGenerationException Required() => new(409, RequiredCode);
    private static InventoryManufacturingCostGenerationException Unavailable() => new(503, UnavailableCode);
}

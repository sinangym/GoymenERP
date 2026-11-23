using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Receipt-specific, price-free source attribution. Causal label versions order immutable epochs.</summary>
public sealed class InventorySupplierReturnCostSourceReader(InventoryDbContext db, ICompanyContext company)
    : IInventorySupplierReturnCostSourceReadPortV1
{
    internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<InventorySupplierReturnCostSourcesV1> GetAsync(Guid companyId, Guid acceptanceId,
        InventorySupplierReturnPurchaseSourceV1 purchaseSource, CancellationToken cancellationToken = default) =>
        (await ReadAsync(companyId, acceptanceId, purchaseSource, cancellationToken)).Preview;

    internal async Task<Snapshot> ReadAsync(Guid companyId, Guid acceptanceId,
        InventorySupplierReturnPurchaseSourceV1 purchaseSource, CancellationToken token)
    {
        try { return await ReadCoreAsync(companyId, acceptanceId, purchaseSource, token); }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or FormatException or OverflowException)
        { throw Unavailable(); }
    }

    // Only the cost-pool owner supplies completed, not-yet-saved recognition effects. They are not public source
    // rows: the application identity does not exist until the whole invoice is ready to commit atomically.
    internal async Task<StockAttribution> ReadStockAttributionAsync(Guid companyId, Guid acceptanceId,
        InventorySupplierReturnPurchaseSourceV1 purchaseSource,
        IReadOnlyList<PurchaseInvoiceReceiptCostEffectRow> stagedEffects, CancellationToken token)
    {
        var current = await ReadAsync(companyId, acceptanceId, purchaseSource, token);
        // A finalized invoice admits each acceptance once. More than one staged effect is not a supported owner plan.
        if (stagedEffects.Count > 1) throw Unavailable();
        var pendingStock = current.PendingStockKg;
        foreach (var effect in stagedEffects)
        {
            var receipt = InventoryPurchaseInvoiceCostStoreV1.FirstCostReceipt(effect);
            var physical = InventoryPurchaseInvoiceCostStoreV1.FirstCostPhysicalSource(effect);
            var basis = physical.PendingBasis;
            if (effect.CompanyId != companyId || effect.AcceptanceId != acceptanceId
                || effect.AcceptedQuantity != purchaseSource.AcceptedQuantity
                || physical.CompanyId != companyId || physical.AcceptanceId != acceptanceId
                || physical.RootLabelId != current.Preview.LabelId || physical.Labels.Count != 1
                || physical.Labels[0].VersionBefore != current.LabelVersion
                || physical.Labels[0].VersionAfter != checked(current.LabelVersion + 1)
                || physical.AllocationBasis != "explicit-supplier-return-source" || basis is null
                || basis.RootQuantityKg.Value().CompareTo(current.PendingRootKg) != 0
                || basis.StockQuantityKg.Value().CompareTo(current.PendingStockKg) != 0
                || basis.PurchaseQuantity.Value().CompareTo(current.PendingQuantity) != 0
                || current.PendingRootKg.Sign <= 0 || current.PendingQuantity.Sign <= 0
                || effect.MatchedQuantity <= 0m || ExactCostRatio.Of(effect.MatchedQuantity).CompareTo(current.PendingQuantity) > 0)
                throw Unavailable();
            var invoicedKg = current.PendingRootKg.Multiply(effect.MatchedQuantity).Divide(current.PendingQuantity);
            // ALS-D122/D123/D124 (golden EK1 E26, E28): a piece receipt's invoice takes the pending kg rounded to three decimals,
            // the last one the whole pending kg (the cost store's rule over the same pending figures); with the switch closed the
            // store keeps the exact share above. Either way the recorded kilograms are one of these two deterministic values.
            if (current.PieceReceipt && receipt.InvoicedPhysicalKg.Value().CompareTo(invoicedKg) != 0)
                invoicedKg = PurchaseInvoiceKilogramShare.RemainingShare(current.PendingRootKg, current.PendingQuantity,
                    effect.MatchedQuantity);
            var stockKg = invoicedKg.Multiply(current.PendingStockKg).Divide(current.PendingRootKg);
            if (receipt.InvoicedPhysicalKg.Value().CompareTo(invoicedKg) != 0
                || receipt.ConsumedQuantityKg.Value().CompareTo(invoicedKg.Subtract(stockKg)) != 0)
                throw Unavailable();
            pendingStock = pendingStock.Subtract(stockKg);
        }
        if (pendingStock.Sign < 0 || pendingStock.CompareTo(current.StockKg) > 0) throw Unavailable();
        return new StockAttribution(current.StockKg, pendingStock);
    }

    private async Task<Snapshot> ReadCoreAsync(Guid companyId, Guid acceptanceId,
        InventorySupplierReturnPurchaseSourceV1 purchaseSource, CancellationToken token)
    {
        RequireCompany(companyId);
        if (purchaseSource.AcceptedQuantity <= 0m || string.IsNullOrWhiteSpace(purchaseSource.SourceVersion)
            || purchaseSource.ReceiptAdjustments is null || purchaseSource.ReceiptAdjustments.Count > 10_000
            || purchaseSource.ReceiptAdjustments.Any(x => x is null || x.AdjustmentId == Guid.Empty || x.UndoQuantity <= 0m)
            || purchaseSource.ReceiptAdjustments.Select(x => x.AdjustmentId).Distinct().Count() != purchaseSource.ReceiptAdjustments.Count)
            throw Unavailable();
        var source = await InventoryGoodsReceiptSourceProjection.ReadAsync(db, companyId, acceptanceId, token)
            ?? throw Unavailable();
        var receipt = source.OriginalRequest;
        var lineage = await InventoryCostLineageReader.ReadAsync(db, companyId, receipt.ProductId, receipt.LabelId, token);
        // Existing mixed-root ancestry has no per-invoice identity. Never relabel its historic shipments.
        if (lineage.Outcome != InventoryCostLineageOutcome.Value || lineage.RemainingKg is null || lineage.NetShippedKg is null
            || lineage.Labels.Count != 1 || lineage.Labels[0].LabelId != receipt.LabelId
            || lineage.Labels[0].RootShare.CompareTo(ExactCostRatio.Of(1m)) != 0)
            throw Unavailable();
        var label = lineage.Labels[0];
        var effects = await db.PurchaseInvoiceReceiptCostEffects.AsNoTracking().Where(x => x.AcceptanceId == acceptanceId).ToListAsync(token);
        var eventIds = effects.Select(x => x.SourceEventId).ToArray();
        var applications = await db.PurchaseInvoiceCostApplications.AsNoTracking().Where(x => eventIds.Contains(x.SourceEventId))
            .ToDictionaryAsync(x => x.SourceEventId, token);
        var exits = await (from movement in db.Movements.AsNoTracking()
            join stamp in db.MovementCostStamps.AsNoTracking() on movement.Id equals stamp.MovementId
            where movement.SourceId == receipt.MovementId && movement.Kind == (int)StockMovementKind.SupplierReturnExit
                && stamp.LineNumber == 1 && stamp.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira
            select new { movement.Id, stamp.EvidenceJson }).ToListAsync(token);
        var proofs = exits.Select(x => new { x.Id, Proof = ReadProof(x.EvidenceJson) }).Where(x => x.Proof is not null).ToArray();
        var reversals = await db.GoodsReceiptReversals.AsNoTracking().Where(x => x.AcceptanceId == acceptanceId).ToListAsync(token);
        if (reversals.Count != purchaseSource.ReceiptAdjustments.Count
            || reversals.Any(x => x.OriginalMovementId != receipt.MovementId || x.LabelId != receipt.LabelId
                || !Enum.IsDefined((InventoryGoodsReceiptReversalKindV1)x.Kind)
                || !purchaseSource.ReceiptAdjustments.Any(a => a.AdjustmentId == x.Id
                    && a.IsSupplierReturn == (x.Kind == (int)InventoryGoodsReceiptReversalKindV1.SupplierReturn)))) throw Unavailable();
        foreach (var entry in proofs)
        {
            // Inventory evidence cannot stand in for the Purchasing owner's immutable undo fact.
            var reversal = reversals.SingleOrDefault(x => x.MovementId == entry.Id);
            if (reversal is null || reversal.Kind != (int)InventoryGoodsReceiptReversalKindV1.SupplierReturn) throw Unavailable();
            var adjustment = purchaseSource.ReceiptAdjustments.Single(x => x.AdjustmentId == reversal.Id);
            var proof = entry.Proof!;
            if (proof.PurchaseSource.AcceptedQuantity != purchaseSource.AcceptedQuantity
                || proof.Selected.Aggregate(ExactCostRatio.Of(0m), (sum, x) => sum.Add(x.QuantityKg.Value()))
                    .CompareTo(ExactCostRatio.Of(reversal.QuantityKg)) != 0
                || proof.Selected.Aggregate(ExactCostRatio.Of(0m), (sum, x) => sum.Add(x.UndoQuantity.Value()))
                    .CompareTo(ExactCostRatio.Of(adjustment.UndoQuantity)) != 0) throw Unavailable();
        }
        var states = new Dictionary<Guid, Cohort>();
        var pendingRoot = ExactCostRatio.Of(decimal.Parse(receipt.QuantityKg, CultureInfo.InvariantCulture));
        var pendingQuantity = ExactCostRatio.Of(purchaseSource.AcceptedQuantity);
        var knownWhole = false;
        if (effects.Count == 1 && !HasFirstReceipt(effects[0].ResultJson))
        {
            var effect = effects[0];
            var recognized = JsonSerializer.Deserialize<InventoryPurchaseInvoiceCostReceiptResultV1>(effect.ResultJson, Json)
                ?? throw Unavailable();
            var originalStamp = await db.MovementCostStamps.AsNoTracking().SingleOrDefaultAsync(x => x.MovementId == receipt.MovementId
                && x.LineNumber == 1 && x.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira, token);
            // One complete invoice has no intra-receipt allocation ambiguity. This restores the ordinary known-cost
            // path without inventing a causal epoch for older partial invoice effects or assigning a historical return.
            if (originalStamp?.Status != (int)InventoryMovementCostStampStatusV1.Known
                || effect.AcceptedQuantity != purchaseSource.AcceptedQuantity || effect.MatchedQuantity != effect.AcceptedQuantity
                || recognized.AcceptanceId != acceptanceId || recognized.MovementId != receipt.MovementId
                || recognized.InvoiceLineId != effect.InvoiceLineId || recognized.ReceiptCostStampVersion != originalStamp.StampVersion
                || recognized.InvoicedKg.Value().CompareTo(pendingRoot) != 0
                || !applications.TryGetValue(effect.SourceEventId, out var application)
                || reversals.Any(x => proofs.All(p => p.Id != x.MovementId))
                || proofs.Any(x => x.Proof!.Before.PendingQuantityKg.Value().Sign != 0
                    || x.Proof.Before.InvoicedSources.Count != 1 || x.Proof.Before.InvoicedSources[0].SourceEventId != effect.SourceEventId))
                throw Unavailable();
            states.Add(effect.SourceEventId, new Cohort(effect.SourceEventId, application.InvoiceId, effect.InvoiceLineId,
                pendingRoot, pendingRoot, pendingQuantity));
            pendingRoot = ExactCostRatio.Of(0m); pendingQuantity = ExactCostRatio.Of(0m); knownWhole = true;
        }
        foreach (var reversal in reversals.Where(x => proofs.All(p => p.Id != x.MovementId)))
        {
            // Legacy returns are unambiguous only when every existing recognition already contains that undo.
            if (effects.Any(x => !(InventoryPurchaseInvoiceCostStoreV1.FirstCostPhysicalSource(x).ReceiptReversals ?? [])
                    .Any(r => r.ReversalId == reversal.Id))) throw Unavailable();
            pendingRoot = pendingRoot.Subtract(ExactCostRatio.Of(reversal.QuantityKg));
            pendingQuantity = pendingQuantity.Subtract(ExactCostRatio.Of(purchaseSource.ReceiptAdjustments.Single(x => x.AdjustmentId == reversal.Id).UndoQuantity));
        }
        var pendingStock = pendingRoot;
        var stock = states.Values.Aggregate(pendingRoot, (sum, x) => sum.Add(x.StockKg));
        var epochs = new List<Epoch>();
        foreach (var effect in effects)
        {
            if (knownWhole) continue;
            var physical = InventoryPurchaseInvoiceCostStoreV1.FirstCostPhysicalSource(effect);
            var saved = InventoryPurchaseInvoiceCostStoreV1.FirstCostReceipt(effect);
            if (physical.Labels.Count != 1 || physical.RootLabelId != receipt.LabelId || physical.Labels[0].LabelId != receipt.LabelId
                || effect.AcceptedQuantity != purchaseSource.AcceptedQuantity || !applications.TryGetValue(effect.SourceEventId, out var application))
                throw Unavailable();
            epochs.Add(new Epoch(physical.Labels[0].VersionAfter, physical.RemainingRootQuantityKg.Value(), effect, saved, application.InvoiceId, null));
        }
        foreach (var entry in proofs)
        {
            var proof = entry.Proof!;
            if (proof.AcceptanceId != acceptanceId || proof.Before.LabelId != receipt.LabelId
                || proof.LabelVersionAfter != proof.LabelVersionBefore + 1) throw Unavailable();
            var beforeStock = proof.Before.InvoicedSources.Aggregate(proof.Before.PendingQuantityKg.Value(),
                (sum, x) => sum.Add(x.AvailableQuantityKg.Value()));
            epochs.Add(new Epoch(proof.LabelVersionAfter, beforeStock, null, null, Guid.Empty, proof));
        }
        if (epochs.Select(x => x.Version).Distinct().Count() != epochs.Count || epochs.Any(x => x.Version > label.Version)) throw Unavailable();
        foreach (var epoch in epochs.OrderBy(x => x.Version))
        {
            ScaleStock(epoch.BeforeStock);
            if (epoch.Effect is { } effect)
            {
                var saved = epoch.Receipt!;
                var rootKg = saved.InvoicedPhysicalKg.Value();
                var stockKg = rootKg.Subtract(saved.ConsumedQuantityKg.Value());
                pendingRoot = pendingRoot.Subtract(rootKg);
                pendingStock = pendingStock.Subtract(stockKg);
                pendingQuantity = pendingQuantity.Subtract(ExactCostRatio.Of(effect.MatchedQuantity));
                states.Add(effect.SourceEventId, new Cohort(effect.SourceEventId, epoch.InvoiceId, effect.InvoiceLineId,
                    rootKg, stockKg, ExactCostRatio.Of(effect.MatchedQuantity)));
            }
            else
            {
                foreach (var selected in epoch.Return!.Selected)
                {
                    var kg = selected.QuantityKg.Value(); var quantity = selected.UndoQuantity.Value();
                    if (kg.Sign < 0 || quantity.Sign < 0 || (kg.Sign == 0) != (quantity.Sign == 0)) throw Unavailable();
                    if (selected.SourceEventId is { } id)
                    {
                        if (!states.TryGetValue(id, out var state)) throw Unavailable();
                        states[id] = state with { RootKg = state.RootKg.Subtract(kg), StockKg = state.StockKg.Subtract(kg),
                            Quantity = state.Quantity.Subtract(quantity) };
                    }
                    else { pendingRoot = pendingRoot.Subtract(kg); pendingStock = pendingStock.Subtract(kg); pendingQuantity = pendingQuantity.Subtract(quantity); }
                    stock = stock.Subtract(kg);
                }
            }
            Validate();
        }
        // A customer return can restore a prior, different cohort. Its original source needs finer attribution;
        // do not silently apply the current fraction to that immutable issue.
        if (proofs.Length != 0 && lineage.Shipments.SelectMany(x => x.Returns).Any()) throw Unavailable();
        ScaleStock(lineage.RemainingKg);
        Validate();
        var totalRoot = states.Values.Aggregate(pendingRoot, (sum, x) => sum.Add(x.RootKg));
        var totalQuantity = states.Values.Aggregate(pendingQuantity, (sum, x) => sum.Add(x.Quantity));
        var ownerQuantity = purchaseSource.ReceiptAdjustments.Aggregate(ExactCostRatio.Of(purchaseSource.AcceptedQuantity),
            (remaining, x) => remaining.Subtract(ExactCostRatio.Of(x.UndoQuantity)));
        if (totalRoot.CompareTo(lineage.RemainingKg.Add(lineage.NetShippedKg)) != 0
            || totalQuantity.CompareTo(ownerQuantity) != 0) throw Unavailable();
        // A sole source is unambiguous across free/blocked positions. Multiple surviving cohorts need position
        // attribution before a selected block can be returned; do not manufacture it from global proportions.
        if (label.Positions.Count(x => x.QuantityKg > 0m) > 1
            && states.Values.Count(x => x.StockKg.Sign > 0) + (pendingStock.Sign > 0 ? 1 : 0) > 1) throw Unavailable();
        var rows = states.Values.OrderBy(x => x.EventId).Select(x => new InventorySupplierReturnInvoicedCostSourceV1(
            x.EventId, x.InvoiceId, x.InvoiceLineId, Ratio(x.StockKg), Ratio(x.Quantity), x.RootKg.CompareTo(x.StockKg) == 0)).ToArray();
        var versionEvidence = JsonSerializer.Serialize(new { companyId, acceptanceId, receipt.MovementId,
            label.Version, purchaseSource, pendingRoot = Ratio(pendingRoot), pendingQuantity = Ratio(pendingQuantity),
            pendingStock = Ratio(pendingStock), rows, effects = effects.OrderBy(x => x.SourceEventId).Select(x => new { x.SourceEventId, x.ResultJson }),
            returns = proofs.OrderBy(x => x.Id).Select(x => new { x.Id, x.Proof }) }, Json);
        var version = "supplier-return-sources.v1/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versionEvidence)));
        RequireCompany(companyId);
        return new Snapshot(new InventorySupplierReturnCostSourcesV1(companyId, acceptanceId, receipt.LabelId, version, Ratio(pendingStock), Ratio(pendingQuantity), rows,
            pendingRoot.CompareTo(pendingStock) == 0),
            pendingRoot, pendingStock, pendingQuantity, lineage.RemainingKg, label.Version,
            states.ToDictionary(x => x.Key, x => new SourceRemainder(x.Value.RootKg, x.Value.Quantity)), receipt.PieceCount is not null);

        void ScaleStock(ExactCostRatio next)
        {
            if (next.Sign < 0 || next.CompareTo(stock) > 0) throw Unavailable();
            if (stock.Sign == 0) { if (next.Sign != 0) throw Unavailable(); return; }
            var fraction = next.Divide(stock);
            pendingStock = pendingStock.Multiply(fraction);
            foreach (var pair in states.ToArray()) states[pair.Key] = pair.Value with { StockKg = pair.Value.StockKg.Multiply(fraction) };
            stock = next;
        }
        void Validate()
        {
            if (pendingRoot.Sign < 0 || pendingStock.Sign < 0 || pendingQuantity.Sign < 0 || pendingStock.CompareTo(pendingRoot) > 0
                || (pendingRoot.Sign == 0) != (pendingQuantity.Sign == 0)
                || states.Values.Any(x => x.RootKg.Sign < 0 || x.StockKg.Sign < 0 || x.Quantity.Sign < 0 || x.StockKg.CompareTo(x.RootKg) > 0
                    || (x.RootKg.Sign == 0) != (x.Quantity.Sign == 0))
                || states.Values.Aggregate(pendingStock, (sum, x) => sum.Add(x.StockKg)).CompareTo(stock) != 0) throw Unavailable();
        }
    }

    internal static InventorySupplierReturnAllocationProofV1? ReadProof(string json) =>
        JsonSerializer.Deserialize<AllocationEvidence>(json, Json)?.SupplierReturnAllocation;
    private static bool HasFirstReceipt(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("receipt", out _);
    }

    internal static async Task<InventorySupplierReturnPurchaseSourceV1?> ReadStoredPurchaseSourceAsync(
        InventoryDbContext db, Guid receiptMovementId, Guid acceptanceId, CancellationToken token)
    {
        var evidence = await (from movement in db.Movements.AsNoTracking()
            join stamp in db.MovementCostStamps.AsNoTracking() on movement.Id equals stamp.MovementId
            where movement.SourceId == receiptMovementId && movement.Kind == (int)StockMovementKind.SupplierReturnExit
                && stamp.LineNumber == 1 && stamp.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira
            select new { movement.Id, stamp.EvidenceJson }).ToListAsync(token);
        var proofs = evidence.Select(x => new { x.Id, Proof = ReadProof(x.EvidenceJson) }).Where(x => x.Proof is not null).ToArray();
        if (proofs.Length == 0) return null;
        var latest = proofs.OrderByDescending(x => x.Proof!.LabelVersionAfter).First().Proof!;
        var adjustments = latest.PurchaseSource.ReceiptAdjustments.ToDictionary(x => x.AdjustmentId);
        var reversals = await db.GoodsReceiptReversals.AsNoTracking().Where(x => x.AcceptanceId == acceptanceId).ToListAsync(token);
        foreach (var reversal in reversals.Where(x => !adjustments.ContainsKey(x.Id)))
        {
            var proof = proofs.SingleOrDefault(x => x.Id == reversal.MovementId)?.Proof ?? throw Unavailable();
            var quantity = proof.Selected.Aggregate(ExactCostRatio.Of(0m), (sum, x) => sum.Add(x.UndoQuantity.Value()));
            if (!quantity.TryToDecimal(out var scalar) || scalar <= 0m) throw Unavailable();
            adjustments.Add(reversal.Id, new InventoryPurchaseInvoiceReceiptAdjustmentV1(reversal.Id, scalar, true));
        }
        return new InventorySupplierReturnPurchaseSourceV1(latest.PurchaseSource.AcceptedQuantity, latest.SourceVersion,
            adjustments.Values.OrderBy(x => x.AdjustmentId).ToArray());
    }
    internal static InventoryExactCostRatioV1 Ratio(ExactCostRatio value) => InventoryExactCostRatioV1.From(value);
    private void RequireCompany(Guid id)
    {
        if (!company.IsResolved || id == Guid.Empty || company.CompanyId != id) throw Unavailable();
    }
    internal static InventorySupplierReturnExitException Unavailable() =>
        InventorySupplierReturnExitException.Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
    internal sealed record Snapshot(InventorySupplierReturnCostSourcesV1 Preview, ExactCostRatio PendingRootKg,
        ExactCostRatio PendingStockKg, ExactCostRatio PendingQuantity, ExactCostRatio StockKg, long LabelVersion,
        IReadOnlyDictionary<Guid, SourceRemainder> Roots, bool PieceReceipt = false);
    internal sealed record SourceRemainder(ExactCostRatio RootKg, ExactCostRatio Quantity);
    internal sealed record StockAttribution(ExactCostRatio StockKg, ExactCostRatio PendingStockKg);
    private sealed record Cohort(Guid EventId, Guid InvoiceId, Guid InvoiceLineId, ExactCostRatio RootKg, ExactCostRatio StockKg, ExactCostRatio Quantity);
    private sealed record Epoch(long Version, ExactCostRatio BeforeStock, PurchaseInvoiceReceiptCostEffectRow? Effect,
        InventoryPurchaseFirstCostReceiptV1? Receipt, Guid InvoiceId, InventorySupplierReturnAllocationProofV1? Return);
    private sealed record AllocationEvidence(InventorySupplierReturnAllocationProofV1? SupplierReturnAllocation);
}

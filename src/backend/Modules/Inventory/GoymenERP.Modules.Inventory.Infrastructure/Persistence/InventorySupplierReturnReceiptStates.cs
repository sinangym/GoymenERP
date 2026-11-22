using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// AK-8b (SAT-D433 step 3, K3-B2, TI3): the recorded facts of one goods receipt that a supplier return of a foreign source label
/// needs — what earlier returns took, and what is left of its invoiced and invoice-pending parts. Company-fenced and read-only.
/// The parts come from the recognised invoice effects and from the attribution each earlier exit recorded with its movement; a
/// history that cannot be attributed exactly leaves <see cref="Facts.Cohorts"/> null and is never estimated.
/// </summary>
internal static class InventorySupplierReturnReceiptStates
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    internal sealed record Facts(Guid GoodsAcceptanceId, Guid ReceiptMovementId, Guid RootLabelId, Guid ProductId,
        DateTimeOffset ReceivedAtUtc, decimal ReceivedKg, decimal ReturnedKg, SupplierReturnCohortState? Cohorts,
        IReadOnlyDictionary<Guid, (Guid InvoiceId, Guid InvoiceLineId)> Invoices);

    /// <summary>The whole-acceptance receipt of <paramref name="acceptanceId"/>, or null when it is not exactly one valid receipt.</summary>
    internal static async Task<Facts?> ReadAsync(InventoryDbContext db, Guid companyId, Guid acceptanceId, CancellationToken token)
    {
        var receipts = await (from movement in db.Movements.AsNoTracking()
            join line in db.MovementLines.AsNoTracking() on movement.Id equals line.MovementId
            where movement.CompanyId == companyId && line.CompanyId == companyId
                && movement.Kind == (int)StockMovementKind.Receipt
                && movement.SourceKind == InventoryGoodsReceiptContractV1.SourceKind && movement.SourceId == acceptanceId
            select new { movement.Id, movement.SourceGrain, movement.SourceLineId, movement.RecordedAtUtc, line.Direction,
                line.QuantityKg, line.LabelId, line.ProductId }).Take(2).ToListAsync(token);
        if (receipts.Count != 1 || receipts[0].SourceGrain != (int)StockSourceGrain.Whole || receipts[0].SourceLineId is not null
            || receipts[0].Direction != (int)StockDirection.In || receipts[0].QuantityKg <= 0m)
            return null;
        var receipt = receipts[0];
        var (returned, cohorts, invoices) = await ReadCohortsAsync(db, companyId, acceptanceId, receipt.Id, receipt.QuantityKg, token);
        return new Facts(acceptanceId, receipt.Id, receipt.LabelId, receipt.ProductId, receipt.RecordedAtUtc, receipt.QuantityKg,
            returned, cohorts, invoices);
    }

    internal static async Task<(decimal ReturnedKg, SupplierReturnCohortState? Cohorts,
        IReadOnlyDictionary<Guid, (Guid InvoiceId, Guid InvoiceLineId)> Invoices)> ReadCohortsAsync(InventoryDbContext db, Guid companyId,
        Guid acceptanceId, Guid receiptMovementId, decimal receivedKg, CancellationToken token)
    {
        var exits = await (from movement in db.Movements.AsNoTracking()
            join line in db.MovementLines.AsNoTracking() on movement.Id equals line.MovementId
            where movement.CompanyId == companyId && line.CompanyId == companyId && movement.SourceId == receiptMovementId
                && (movement.Kind == (int)StockMovementKind.SupplierReturnExit
                    && movement.SourceKind == InventorySupplierReturnExitContractV1.SourceKind
                    || movement.Kind == (int)StockMovementKind.PurchaseAcceptanceReversal
                    && movement.SourceKind == InventoryGoodsReceiptReversalCodesV1.SourceKind)
            select new { movement.Id, line.QuantityKg }).ToListAsync(token);
        var returned = exits.Sum(row => row.QuantityKg);
        var exitIds = exits.Select(row => row.Id).Distinct().ToArray();
        var evidence = await db.MovementCostStamps.AsNoTracking().Where(row => row.CompanyId == companyId
                && exitIds.Contains(row.MovementId) && row.LineNumber == 1 && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira)
            .Select(row => new { row.MovementId, row.EvidenceJson }).ToListAsync(token);
        var reversals = await db.GoodsReceiptReversals.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.AcceptanceId == acceptanceId)
            .Select(row => new { row.Id, row.MovementId }).ToListAsync(token);
        var effects = await db.PurchaseInvoiceReceiptCostEffects.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.AcceptanceId == acceptanceId).ToListAsync(token);
        var eventIds = effects.Select(row => row.SourceEventId).ToArray();
        var applications = await db.PurchaseInvoiceCostApplications.AsNoTracking()
            .Where(row => row.CompanyId == companyId && eventIds.Contains(row.SourceEventId))
            .Select(row => new { row.SourceEventId, row.InvoiceId }).ToListAsync(token);
        var invoices = new Dictionary<Guid, (Guid, Guid)>();
        try
        {
            if (exits.GroupBy(row => row.Id).Any(group => group.Count() != 1)) return (returned, null, invoices);
            var cohortEffects = new List<SupplierReturnCohortEffect>();
            foreach (var effect in effects)
            {
                var application = applications.SingleOrDefault(row => row.SourceEventId == effect.SourceEventId);
                if (application is null) return (returned, null, invoices);
                invoices.Add(effect.SourceEventId, (application.InvoiceId, effect.InvoiceLineId));
                if (!HasFirstReceipt(effect.ResultJson))
                {
                    // The older shape: one complete invoice of the whole receipt. Anything partial in that shape is unreadable.
                    if (effect.MatchedQuantity != effect.AcceptedQuantity) return (returned, null, invoices);
                    cohortEffects.Add(new SupplierReturnCohortEffect(effect.SourceEventId, null, []));
                    continue;
                }

                var saved = InventoryPurchaseInvoiceCostStoreV1.FirstCostReceipt(effect);
                var physical = InventoryPurchaseInvoiceCostStoreV1.FirstCostPhysicalSource(effect);
                cohortEffects.Add(new SupplierReturnCohortEffect(effect.SourceEventId, saved.InvoicedPhysicalKg.Value(),
                    (physical.ReceiptReversals ?? []).Select(row => row.ReversalId).ToArray()));
            }

            var cohortExits = exits.Select(row => new SupplierReturnCohortExit(row.Id,
                reversals.SingleOrDefault(reversal => reversal.MovementId == row.Id)?.Id, row.QuantityKg,
                Selection(evidence.SingleOrDefault(item => item.MovementId == row.Id)?.EvidenceJson))).ToArray();
            return (returned, SupplierReturnReceiptCohorts.Resolve(receivedKg, cohortEffects, cohortExits), invoices);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or FormatException
            or OverflowException)
        {
            return (returned, null, invoices);
        }
    }

    /// <summary>
    /// SAT-D444: the location/product moving average in TL per kg when it is known and reconciles with the costed stock; null when
    /// it cannot be read (no pool, an unknown position, a pool that does not match stock).
    /// </summary>
    internal static async Task<decimal?> ReadAverageAsync(InventoryDbContext db, Guid companyId, Guid productId, Guid locationId,
        CancellationToken token)
    {
        var row = await db.AverageCostLedger.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.LocationId == locationId && item.ProductId == productId)
            .SingleOrDefaultAsync(token);
        if (row is null) return null;
        AverageCostPosition position;
        try { position = InventoryAverageCostLedgerRows.PositionOf(row); }
        catch (InvalidOperationException) { return null; }
        var stockKg = await InventoryAverageCostLedgerRows.CostedStock(db, companyId, productId)
            .Where(balance => balance.LocationId == locationId)
            .SumAsync(balance => (decimal?)balance.QuantityKg, token) ?? 0m;
        return position.Status == AverageCostStatus.Known && position.AverageCostPerKg is { } average && average > 0m
            && position.QuantityKg == stockKg ? average : null;
    }

    internal static string Kg(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static IReadOnlyList<SupplierReturnCohortSelection>? Selection(string? evidenceJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson)) return null;
        var evidence = JsonSerializer.Deserialize<Evidence>(evidenceJson, Json);
        if (evidence?.SupplierReturnLineageSelection is { } lineage)
            return lineage.Selected.Select(item => new SupplierReturnCohortSelection(item.SourceEventId, item.QuantityKg.Value())).ToArray();
        if (evidence?.SupplierReturnAllocation is { } allocation)
            return allocation.Selected.Select(item => new SupplierReturnCohortSelection(item.SourceEventId, item.QuantityKg.Value())).ToArray();
        return null;
    }

    private static bool HasFirstReceipt(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("receipt", out _);
    }

    private sealed record Evidence(InventorySupplierReturnAllocationProofV1? SupplierReturnAllocation,
        InventorySupplierReturnLineageSelectionProofV1? SupplierReturnLineageSelection);
}

using System.Globalization;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryStore
{
    private readonly Dictionary<Guid, InventoryPurchaseReceiptCostV1> receiptCostInputs = [];
    private readonly Dictionary<Guid, Guid?> returnIssueCostInputs = [];

    internal void UseReceiptCost(Guid movementId, InventoryPurchaseReceiptCostV1 proof) => receiptCostInputs.Add(movementId, proof);
    internal void UseReturnIssueCost(Guid movementId, Guid? issueMovementId) => returnIssueCostInputs.Add(movementId, issueMovementId);

    /// <summary>
    /// Called after physical-domain validation, before changing the tracked balances. Stamp and cost-pool updates are part
    /// of the same Inventory save. Missing valuations never discard a physical event. A partial priced pool is private:
    /// existing read ports require its kg to equal all physical kg, so no partial average is published as a complete value.
    /// </summary>
    private async Task StageMovementCostsAsync(StockMovement movement, InventoryCommandMetadata metadata, CancellationToken token)
    {
        if (movement.Kind is StockMovementKind.Block or StockMovementKind.Release) return;
        var currencies = new[] { SettingsCurrencyIdentitiesV1.TurkishLira, ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD") };
        var pools = new Dictionary<(Guid Location, Guid Product, Guid Currency), CostPool>();
        var number = 0;
        foreach (var line in movement.Lines)
        {
            number++;
            foreach (var currency in currencies)
            {
                var position = line.Position;
                var kg = line.Quantity.Value;
                var proof = receiptCostInputs.GetValueOrDefault(movement.Id);
                var sourceIssue = returnIssueCostInputs.GetValueOrDefault(movement.Id);
                var entryCurrency = proof?.Currency ?? await EntryCurrencyAsync(position.LabelId, token);
                var stamp = NewStamp(movement.Id, number, currency, entryCurrency, proof?.EvidenceJson ?? "{}", metadata);
                stamp.SourceIssueMovementId = sourceIssue;
                var isCosted = position.Owner.Kind == StockPartyKind.Company && position.Owner.Id == movement.CompanyId
                    && position.Custodian.Kind == StockPartyKind.Company && position.Custodian.Id == movement.CompanyId;
                if (!isCosted)
                {
                    stamp.Reason = "stock_outside_company_cost_pool";
                    dbContext.MovementCostStamps.Add(stamp);
                    continue;
                }
                var poolKey = (position.LocationId, position.ProductId, currency);
                if (!pools.TryGetValue(poolKey, out var pool))
                    pool = await ReadCostPoolAsync(dbContext, companyContext, position.LocationId, position.ProductId, currency, token);
                AverageCostPosition? afterPosition = pool.Position;
                var afterVersion = pool.Version;
                var costedDelta = 0m;
                var incoming = line.Direction == StockDirection.In;
                decimal? unit = null;
                long? sourceVersion = null;
                if (movement.Kind is StockMovementKind.TransferAccept or StockMovementKind.TransferDispatchCancellation
                    or StockMovementKind.TransferReceiptShortage or StockMovementKind.TransferTransitLoss)
                {
                    var original = await TransferDispatchCostAsync(movement.Source.Id, line.Position, currency, token);
                    if (original is { Status: (int)InventoryMovementCostStampStatusV1.Known })
                    { unit = original.UnitCostPerKg; sourceVersion = original.LedgerVersion; CopyCurrency(stamp, original); }
                }
                else if (incoming && movement.Source.Kind == InventoryGoodsReceiptContractV1.SourceKind && proof is not null)
                {
                    unit = currency == SettingsCurrencyIdentitiesV1.TurkishLira ? proof.TlUnitCostPerKg : proof.UsdUnitCostPerKg;
                    stamp.Reason = currency == SettingsCurrencyIdentitiesV1.TurkishLira ? proof.TlUnavailableReason : proof.UsdUnavailableReason;
                }
                else if (incoming && movement.Source.Kind == InventoryManufacturingRealizationContractV1.InputReturnSourceKind
                    && sourceIssue is { } issue)
                {
                    var original = await dbContext.MovementCostStamps.AsNoTracking().SingleOrDefaultAsync(row => row.MovementId == issue
                        && row.LineNumber == 1 && row.CurrencyId == currency, token);
                    if (original is { Status: (int)InventoryMovementCostStampStatusV1.Known })
                    {
                        unit = original.UnitCostPerKg; sourceVersion = original.LedgerVersion;
                        CopyCurrency(stamp, original);
                    }
                }
                else if (!incoming && (movement.Kind is StockMovementKind.Issue or StockMovementKind.Scrap or StockMovementKind.CountAdjustment
                        or StockMovementKind.TransferDispatch or StockMovementKind.TransferReceiptShortage or StockMovementKind.TransferTransitLoss)
                    && pool.IsComplete && pool.Position is { Status: AverageCostStatus.Known })
                {
                    unit = pool.Position.AverageCostPerKg; sourceVersion = pool.Version;
                }

                // Production receipts are intentionally pending until Manufacturing supplies its real closed-order cost.
                // Transfer/return paths require their original source stamp; current average is never a substitute.
                if (movement.Kind == StockMovementKind.TransferTransitLoss)
                {
                    // Dispatch already removed these kg. The paired transit In/Out is a physical fact with net zero;
                    // blending it into the source average would change that average without adding any stock.
                    if (unit is >= 0m && sourceVersion is >= 1) SetKnown(stamp, unit.Value, kg, sourceVersion.Value);
                    stamp.Reason ??= stamp.Status == (int)InventoryMovementCostStampStatusV1.Known ? null : "dispatch_cost_source_unreadable";
                    dbContext.MovementCostStamps.Add(stamp);
                    continue;
                }
                if (incoming)
                {
                    if (unit is > 0m && pool.IsConsistent)
                    {
                        SetKnown(stamp, unit.Value, kg, sourceVersion ?? checked(pool.Version + 1));
                        AverageCostPosition? after = null;
                        if (stamp.Status == (int)InventoryMovementCostStampStatusV1.Known)
                        {
                            try { after = AverageCostCalculator.Apply(pool.Position, new(AverageCostMovementKind.PurchaseReceipt,
                                kg, pool.CostedKg, unit)); }
                            catch (OverflowException)
                            {
                                stamp.Status = (int)InventoryMovementCostStampStatusV1.Unavailable;
                                stamp.UnitCostPerKg = null; stamp.Amount = null; stamp.LedgerVersion = null;
                                stamp.Reason = "movement_cost_not_representable";
                            }
                        }
                        if (after is not null && stamp.Status == (int)InventoryMovementCostStampStatusV1.Known)
                        {
                            afterVersion = await WriteCostPoolAsync(dbContext, companyContext, timeProvider, position.LocationId, position.ProductId, currency, pool, after, token);
                            afterPosition = after; costedDelta = kg;
                        }
                        else AddPending(movement.Id, number, currency, position, kg, stamp.Reason!);
                    }
                    else
                    {
                        AddPending(movement.Id, number, currency, position, kg, stamp.Reason ?? "entry_cost_not_readable");
                    }
                }
                else if (pool.IsConsistent && !pool.PendingLabels.Contains(position.LabelId)
                    && pool.Position is { Status: AverageCostStatus.Known })
                {
                    var after = AverageCostCalculator.Apply(pool.Position, new(AverageCostMovementKind.Issue, kg, pool.CostedKg));
                    afterVersion = await WriteCostPoolAsync(dbContext, companyContext, timeProvider, position.LocationId, position.ProductId, currency, pool, after, token);
                    afterPosition = after; costedDelta = -kg;
                    if (unit is >= 0m && sourceVersion is >= 1) SetKnown(stamp, unit.Value, kg, sourceVersion.Value);
                }
                var pendingLabels = pool.PendingLabels.ToHashSet();
                if (incoming && costedDelta == 0m) pendingLabels.Add(position.LabelId);
                pools[poolKey] = pool with
                {
                    Position = afterPosition, Version = afterVersion,
                    PhysicalKg = pool.PhysicalKg + (incoming ? kg : -kg), CostedKg = pool.CostedKg + costedDelta,
                    PendingLabels = pendingLabels,
                    TlRow = currency == SettingsCurrencyIdentitiesV1.TurkishLira
                        ? dbContext.AverageCostLedger.Local.SingleOrDefault(r => r.LocationId == position.LocationId && r.ProductId == position.ProductId) : null,
                    ForeignRow = currency != SettingsCurrencyIdentitiesV1.TurkishLira
                        ? dbContext.CurrencyAverageCostLedger.Local.SingleOrDefault(r => r.LocationId == position.LocationId && r.ProductId == position.ProductId && r.CurrencyId == currency) : null,
                };
                stamp.Reason ??= stamp.Status == (int)InventoryMovementCostStampStatusV1.Known ? null : "movement_cost_source_unreadable";
                dbContext.MovementCostStamps.Add(stamp);
            }
        }
    }

    private MovementCostStampRow NewStamp(Guid movement, int line, Guid currency, InventoryIssueCostCurrencyV1 proof,
        string evidence, InventoryCommandMetadata metadata) => new()
    {
        CompanyId = companyContext.CompanyId, MovementId = movement, LineNumber = line, CurrencyId = currency,
        Status = (int)InventoryMovementCostStampStatusV1.Unavailable, StampVersion = 1,
        EntryCurrencyStatus = (int)proof.Status, OriginalCurrencyId = proof.OriginalCurrencyId,
        ForeignUnitCostPerKg = proof.ForeignUnitCostPerKg, EntryExchangeRate = proof.ExchangeRate,
        CurrencySourceVersion = proof.SourceVersion, CurrencyReason = proof.Reason, EvidenceJson = evidence,
        RecordedAtUtc = timeProvider.GetUtcNow(), CorrelationId = metadata.CorrelationId,
    };

    private static void SetKnown(MovementCostStampRow stamp, decimal unit, decimal kg, long version)
    {
        try
        {
            var amount = checked(unit * kg);
            if (Scale(unit) > CostScaleMaximum || Scale(amount) > CostScaleMaximum
                || Math.Abs(unit) >= 100_000_000_000_000_000_000m || Math.Abs(amount) >= 100_000_000_000_000_000_000m)
            { stamp.Reason = "movement_cost_not_representable"; return; }
            stamp.Status = (int)InventoryMovementCostStampStatusV1.Known;
            stamp.UnitCostPerKg = unit; stamp.Amount = amount; stamp.LedgerVersion = version; stamp.Reason = null;
        }
        catch (OverflowException) { stamp.Reason = "movement_cost_not_representable"; }
    }

    private void AddPending(Guid movement, int line, Guid currency, StockPosition position, decimal kg, string reason) =>
        dbContext.PendingMovementCosts.Add(new PendingMovementCostRow
        {
            CompanyId = companyContext.CompanyId, MovementId = movement, LineNumber = line, CurrencyId = currency,
            LabelId = position.LabelId, ProductId = position.ProductId, LocationId = position.LocationId, QuantityKg = kg,
            Reason = reason, RecordedAtUtc = timeProvider.GetUtcNow(),
        });

    private static void CopyCurrency(MovementCostStampRow target, MovementCostStampRow source)
    {
        target.EntryCurrencyStatus = source.EntryCurrencyStatus; target.OriginalCurrencyId = source.OriginalCurrencyId;
        target.ForeignUnitCostPerKg = source.ForeignUnitCostPerKg; target.EntryExchangeRate = source.EntryExchangeRate;
        target.CurrencySourceVersion = source.CurrencySourceVersion; target.CurrencyReason = source.CurrencyReason;
        target.EvidenceJson = source.EvidenceJson;
    }

    private async Task<MovementCostStampRow?> TransferDispatchCostAsync(Guid transferId, StockPosition position,
        Guid currency, CancellationToken token)
    {
        var sources = await (from movement in dbContext.Movements.AsNoTracking()
            join line in dbContext.MovementLines.AsNoTracking() on movement.Id equals line.MovementId
            join stamp in dbContext.MovementCostStamps.AsNoTracking()
                on new { line.CompanyId, line.MovementId, line.LineNumber } equals new { stamp.CompanyId, stamp.MovementId, stamp.LineNumber }
            where movement.Kind == (int)StockMovementKind.TransferDispatch && movement.SourceId == transferId
                && movement.SourceKind == Domain.Transfers.InventoryTransfer.SourceKind && line.LabelId == position.LabelId
                && line.Block == (int)position.Block && line.OwnerKind == (int)position.Owner.Kind && line.OwnerId == position.Owner.Id
                && stamp.CurrencyId == currency
            select stamp).Take(2).ToListAsync(token);
        return sources.Count == 1 ? sources[0] : null;
    }

    private async Task<InventoryIssueCostCurrencyV1> EntryCurrencyAsync(Guid labelId, CancellationToken token)
    {
        var generations = await (from recognition in dbContext.MovementCostRecognitions.AsNoTracking()
            join pending in dbContext.PendingMovementCosts.AsNoTracking()
                on new { recognition.CompanyId, recognition.MovementId, recognition.LineNumber, recognition.CurrencyId }
                equals new { pending.CompanyId, pending.MovementId, pending.LineNumber, pending.CurrencyId }
            join generation in dbContext.ManufacturingCostGenerations.AsNoTracking() on recognition.GenerationId equals (Guid?)generation.Id
            where pending.LabelId == labelId && recognition.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira
                && generation.State == (int)InventoryManufacturingCostGenerationStateV1.Recognized
            select new { generation.Id, generation.Version, generation.LastResultJson }).ToListAsync(token);
        foreach (var generation in generations)
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<InventoryManufacturingCostGenerationResultV1>(generation.LastResultJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if (result?.Outputs.Any(output => output.LabelId == labelId && output.TlUnitCostPerKg is > 0m) == true)
                return new(InventoryIssueCostCurrencyStatusV1.FunctionalCurrency, null, null, null,
                    $"inventory.manufacturing-cost-generation.v1/{generation.Id:D}/{generation.Version}", null);
        }
        // Only an actual original receipt stamp is currency evidence. Legacy/opening/split/merged ambiguity stays explicit.
        var receipts = await (from movement in dbContext.Movements.AsNoTracking()
            join line in dbContext.MovementLines.AsNoTracking() on movement.Id equals line.MovementId
            join stamp in dbContext.MovementCostStamps.AsNoTracking()
                on new { line.CompanyId, line.MovementId, line.LineNumber } equals new { stamp.CompanyId, stamp.MovementId, stamp.LineNumber }
            where line.LabelId == labelId && line.Direction == (int)StockDirection.In && movement.Kind == (int)StockMovementKind.Receipt
                && stamp.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira
            select stamp).Take(2).ToListAsync(token);
        return receipts.Count == 1 ? new((InventoryIssueCostCurrencyStatusV1)receipts[0].EntryCurrencyStatus,
            receipts[0].OriginalCurrencyId, receipts[0].ForeignUnitCostPerKg, receipts[0].EntryExchangeRate,
            receipts[0].CurrencySourceVersion, receipts[0].CurrencyReason)
            : new(InventoryIssueCostCurrencyStatusV1.Unreadable, null, null, null,
                "inventory.entry-currency.v1/unreadable", "entry_currency_source_unreadable");
    }

    internal static async Task<CostPool> ReadCostPoolAsync(InventoryDbContext dbContext, GoymenERP.BuildingBlocks.Multitenancy.ICompanyContext companyContext, Guid location, Guid product, Guid currency, CancellationToken token)
    {
        var physical = await dbContext.Balances.Where(row => row.LocationId == location && row.ProductId == product
            && row.OwnerKind == (int)StockPartyKind.Company && row.OwnerId == companyContext.CompanyId
            && row.CustodianKind == (int)StockPartyKind.Company && row.CustodianId == companyContext.CompanyId).ToListAsync(token);
        physical.AddRange(dbContext.Balances.Local.Where(row => row.LocationId == location && row.ProductId == product
            && row.CompanyId == companyContext.CompanyId && row.OwnerKind == (int)StockPartyKind.Company && row.OwnerId == companyContext.CompanyId
            && row.CustodianKind == (int)StockPartyKind.Company && row.CustodianId == companyContext.CompanyId && !physical.Contains(row)));
        var pending = await dbContext.PendingMovementCosts.Where(row => row.ProductId == product && row.CurrencyId == currency).ToListAsync(token);
        pending.AddRange(dbContext.PendingMovementCosts.Local.Where(row => row.ProductId == product && row.CurrencyId == currency && !pending.Contains(row)));
        if (currency == SettingsCurrencyIdentitiesV1.TurkishLira)
        {
            // An approved opening cost is the explicit valuation of the whole position at that decision. It does not
            // backfill any historical movement stamp, but old pending entries at that position no longer hide its pool.
            var openings = await (from opening in dbContext.OpeningCosts.AsNoTracking()
                join decision in dbContext.OpeningCostDecisions.AsNoTracking() on opening.Id equals decision.OpeningCostId
                where opening.ProductId == product && decision.Decision == 1
                select new { opening.LocationId, decision.DecidedAtUtc }).ToListAsync(token);
            pending.RemoveAll(entry => openings.Any(opening => opening.LocationId == entry.LocationId
                && opening.DecidedAtUtc >= entry.RecordedAtUtc));
        }
        var recognized = await (from recognition in dbContext.MovementCostRecognitions
            join generation in dbContext.ManufacturingCostGenerations on recognition.GenerationId equals (Guid?)generation.Id into generations
            from generation in generations.DefaultIfEmpty()
            where recognition.CurrencyId == currency && (recognition.GenerationId == null || generation!.State == (int)InventoryManufacturingCostGenerationStateV1.Recognized)
            select recognition).ToListAsync(token);
        recognized.AddRange(dbContext.MovementCostRecognitions.Local.Where(row => row.CurrencyId == currency
            && (row.GenerationId == null || dbContext.ManufacturingCostGenerations.Local.Any(g => g.Id == row.GenerationId
                && g.State == (int)InventoryManufacturingCostGenerationStateV1.Recognized)) && !recognized.Contains(row)));
        var pendingLabels = pending.Where(row => !recognized.Any(done => done.MovementId == row.MovementId && done.LineNumber == row.LineNumber))
            .Select(row => row.LabelId).ToHashSet();
        var total = physical.Sum(row => row.QuantityKg);
        var pendingKg = physical.Where(row => pendingLabels.Contains(row.LabelId)).Sum(row => row.QuantityKg);
        if (currency == SettingsCurrencyIdentitiesV1.TurkishLira)
        {
            var row = dbContext.AverageCostLedger.Local.SingleOrDefault(row => row.LocationId == location && row.ProductId == product)
                ?? await dbContext.AverageCostLedger.SingleOrDefaultAsync(row => row.LocationId == location && row.ProductId == product, token);
            return new(row is null ? null : InventoryAverageCostLedgerRows.PositionOf(row), row?.Version ?? 0, total, total - pendingKg, pendingLabels, row, null);
        }
        var foreign = dbContext.CurrencyAverageCostLedger.Local.SingleOrDefault(row => row.LocationId == location && row.ProductId == product && row.CurrencyId == currency)
            ?? await dbContext.CurrencyAverageCostLedger.SingleOrDefaultAsync(row => row.LocationId == location && row.ProductId == product && row.CurrencyId == currency, token);
        var position = foreign is null ? null : foreign.Status == (int)AverageCostStatus.Known
            ? AverageCostPosition.Known(foreign.QuantityKg!.Value, foreign.AverageCostPerKg!.Value)
            : AverageCostPosition.Unreadable((AverageCostUnreadableReason)foreign.UnreadableReason!.Value);
        return new(position, foreign?.Version ?? 0, total, total - pendingKg, pendingLabels, null, foreign);
    }

    internal static Task<long> WriteCostPoolAsync(InventoryDbContext dbContext, GoymenERP.BuildingBlocks.Multitenancy.ICompanyContext companyContext, TimeProvider timeProvider, Guid location, Guid product, Guid currency, CostPool before, AverageCostPosition after, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (currency == SettingsCurrencyIdentitiesV1.TurkishLira)
        {
            var row = before.TlRow ?? new AverageCostLedgerRow { Id = Guid.NewGuid(), CompanyId = companyContext.CompanyId, LocationId = location, ProductId = product };
            var firstChange = dbContext.Entry(row).State is EntityState.Detached or EntityState.Unchanged;
            InventoryAverageCostLedgerRows.Write(row, after);
            if (firstChange) row.Version = checked(row.Version + 1);
            row.UpdatedAtUtc = timeProvider.GetUtcNow();
            if (before.TlRow is null) dbContext.AverageCostLedger.Add(row);
            return Task.FromResult(row.Version);
        }
        var foreign = before.ForeignRow ?? new CurrencyAverageCostLedgerRow { Id = Guid.NewGuid(), CompanyId = companyContext.CompanyId,
            LocationId = location, ProductId = product, CurrencyId = currency };
        var firstForeignChange = dbContext.Entry(foreign).State is EntityState.Detached or EntityState.Unchanged;
        foreign.Status = (int)after.Status; foreign.QuantityKg = after.QuantityKg; foreign.AverageCostPerKg = after.AverageCostPerKg;
        foreign.UnreadableReason = after.UnreadableReason is { } reason ? (int)reason : null;
        if (firstForeignChange) foreign.Version = checked(foreign.Version + 1);
        foreign.UpdatedAtUtc = timeProvider.GetUtcNow();
        if (before.ForeignRow is null) dbContext.CurrencyAverageCostLedger.Add(foreign);
        return Task.FromResult(foreign.Version);
    }

    internal sealed record CostPool(AverageCostPosition? Position, long Version, decimal PhysicalKg, decimal CostedKg,
        IReadOnlySet<Guid> PendingLabels, AverageCostLedgerRow? TlRow, CurrencyAverageCostLedgerRow? ForeignRow)
    {
        public bool IsConsistent => Position is null ? CostedKg == 0m : Position.Status == AverageCostStatus.Known && Position.QuantityKg == CostedKg;
        public bool IsComplete => IsConsistent && CostedKg == PhysicalKg;
    }
}

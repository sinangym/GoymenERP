using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Counts;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryStore
{
    private readonly Dictionary<Guid, InventoryPurchaseReceiptCostV1> receiptCostInputs = [];
    private readonly Dictionary<Guid, Guid?> returnIssueCostInputs = [];
    private readonly Dictionary<Guid, decimal?> supplierReturnCostInputs = [];

    internal void UseReceiptCost(Guid movementId, InventoryPurchaseReceiptCostV1 proof) => receiptCostInputs.Add(movementId, proof);
    internal void UseReturnIssueCost(Guid movementId, Guid? issueMovementId) => returnIssueCostInputs.Add(movementId, issueMovementId);
    // SAT-D411: this value is admitted by Purchasing's actual invoice/manual-exception flow, never a receipt-price fallback.
    internal void UseSupplierReturnCost(Guid movementId, decimal? approvedTlUnitCost) => supplierReturnCostInputs.Add(movementId, approvedTlUnitCost);

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
                if (IsCustomerReturn(movement)) sourceIssue = movement.Source.Id;
                var entryCurrency = proof?.Currency ?? await EntryCurrencyAsync(position.LabelId, token);
                var stamp = NewStamp(movement.Id, number, currency, entryCurrency, proof?.EvidenceJson ?? "{}", metadata);
                stamp.SourceIssueMovementId = sourceIssue;
                if (movement.Source.Kind == InventoryGoodsReceiptContractV1.SourceKind
                    && InventoryFirstEntryRateProof.IsValid(proof?.FirstEntryRate, movement.CompanyId, movement.Source.Id, proof?.PurchaseOrderId))
                    stamp.FirstEntryRateJson = JsonSerializer.Serialize(proof!.FirstEntryRate, CostJson);
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
                ExactAverageCostPool? afterPosition = pool.ExactPosition;
                var afterVersion = pool.Version;
                var costedDelta = ExactCostRatio.Of(0m);
                var incoming = line.Direction == StockDirection.In;
                var originalCostExit = movement.Kind is StockMovementKind.SupplierReturnExit or StockMovementKind.PurchaseAcceptanceReversal
                    or StockMovementKind.TransferReceiptShortage;
                decimal? unit = null;
                ExactCostRatio? exactUnit = null;
                ExactCostRatio? reversalPoolUnit = null;
                ExactCostRatio? restoredPricedKg = null;
                ExactCostRatio? restoredPricedValue = null;
                var partialSourceChanged = false;
                var knownZeroReceipt = false;
                long? sourceVersion = null;
                if (movement.Kind == StockMovementKind.CountAdjustment)
                {
                    var countCost = StockCountCostCalculator.ForDifference(incoming ? kg : -kg,
                        pool.IsComplete && pool.ExactPosition is { } known
                            ? AverageCostPosition.Known(pool.PhysicalKg, known.AverageCostPerKg) : null);
                    if (countCost.Status == StockCountCostStatus.Refused && currency == SettingsCurrencyIdentitiesV1.TurkishLira)
                        throw new InventoryStoreException(InventoryStockCountCodesV1.CostNotCalculated,
                            "Ortalama maliyet hesaplanamadı; sayım eksiği uygulanmadı.");
                    if (countCost.Status == StockCountCostStatus.Known)
                    {
                        unit = countCost.AverageCostPerKg;
                        sourceVersion = pool.Version;
                        knownZeroReceipt = incoming && unit == 0m;
                    }
                    else stamp.Reason = "stock_count_average_unreadable";
                }
                else if (movement.Kind is StockMovementKind.TransferAccept or StockMovementKind.TransferDispatchCancellation
                    or StockMovementKind.TransferReceiptShortage or StockMovementKind.TransferTransitLoss)
                {
                    var original = await TransferDispatchCostAsync(movement.Source.Id, line.Position, currency, token);
                    if (original is { Status: (int)InventoryMovementCostStampStatusV1.Known })
                    {
                        unit = original.UnitCostPerKg; exactUnit = StampUnit(original); sourceVersion = original.LedgerVersion; CopyCurrency(stamp, original);
                        knownZeroReceipt = incoming && exactUnit.Sign == 0;
                    }
                    else if (incoming && original is not null) await RestorePartialSourceAsync(original);
                }
                else if (incoming && movement.Source.Kind == InventoryGoodsReceiptContractV1.SourceKind && proof is not null)
                {
                    unit = currency == SettingsCurrencyIdentitiesV1.TurkishLira ? proof.TlUnitCostPerKg : proof.UsdUnitCostPerKg;
                    stamp.Reason = currency == SettingsCurrencyIdentitiesV1.TurkishLira ? proof.TlUnavailableReason : proof.UsdUnavailableReason;
                    try { exactUnit = AdmittedReceiptUnit(unit,
                        currency == SettingsCurrencyIdentitiesV1.TurkishLira ? proof.TlUnitCostRatio : proof.UsdUnitCostRatio, stamp.Reason); }
                    catch (InvalidOperationException) { unit = null; exactUnit = null; stamp.Reason = "receipt_cost_proof_conflict"; }
                }
                else if (incoming && (movement.Source.Kind == InventoryManufacturingRealizationContractV1.InputReturnSourceKind
                        || IsCustomerReturn(movement))
                    && sourceIssue is { } issue)
                {
                    var original = await dbContext.MovementCostStamps.AsNoTracking().SingleOrDefaultAsync(row => row.MovementId == issue
                        && row.LineNumber == 1 && row.CurrencyId == currency, token);
                    if (original is { Status: (int)InventoryMovementCostStampStatusV1.Known })
                    {
                        unit = original.UnitCostPerKg; sourceVersion = original.LedgerVersion;
                        exactUnit = StampUnit(original);
                        knownZeroReceipt = exactUnit.Sign == 0;
                        CopyCurrency(stamp, original);
                    }
                    else if (original is not null) await RestorePartialSourceAsync(original);
                }
                else if (!incoming && movement.Kind is StockMovementKind.SupplierReturnExit or StockMovementKind.PurchaseAcceptanceReversal)
                {
                    var original = await dbContext.MovementCostStamps.AsNoTracking().SingleOrDefaultAsync(row => row.MovementId == movement.Source.Id
                        && row.LineNumber == 1 && row.CurrencyId == currency, token);
                    if (original is { Status: (int)InventoryMovementCostStampStatusV1.Known })
                    {
                        unit = original.UnitCostPerKg; exactUnit = StampUnit(original); sourceVersion = original.LedgerVersion; CopyCurrency(stamp, original);
                        if (movement.Kind == StockMovementKind.PurchaseAcceptanceReversal && original.PoolEffectJson is { } effectJson)
                        {
                            var effect = JsonSerializer.Deserialize<MovementPoolEffect>(effectJson, CostJson) ?? throw new InvalidOperationException();
                            var originalKg = await dbContext.MovementLines.Where(row => row.MovementId == original.MovementId && row.LineNumber == original.LineNumber)
                                .Select(row => row.QuantityKg).SingleAsync(token);
                            reversalPoolUnit = effect.RecordedValue.Value().Divide(originalKg);
                        }
                    }
                    if (movement.Kind == StockMovementKind.SupplierReturnExit && currency == SettingsCurrencyIdentitiesV1.TurkishLira)
                    {
                        // The supplier invoice / approved manual cost wins over the original receipt price (SAT-D411).
                        unit = supplierReturnCostInputs.GetValueOrDefault(movement.Id);
                        exactUnit = unit is { } approved ? ExactCostRatio.Of(approved) : null;
                        sourceVersion = pool.Version;
                        stamp.EvidenceJson = System.Text.Json.JsonSerializer.Serialize(new { movement.Source, approvedReturnTlUnitCost = unit });
                        stamp.EntryCurrencyStatus = (int)InventoryIssueCostCurrencyStatusV1.FunctionalCurrency;
                        stamp.OriginalCurrencyId = null; stamp.ForeignUnitCostPerKg = null; stamp.EntryExchangeRate = null;
                        stamp.ForeignUnitCostRatioJson = null; stamp.EntryExchangeRateRatioJson = null;
                        stamp.CurrencySourceVersion = $"inventory.supplier-return-approved-cost.v1/{movement.Id:D}";
                        stamp.CurrencyReason = null;
                    }
                }
                else if (!incoming && (movement.Kind is StockMovementKind.Issue or StockMovementKind.Scrap
                        or StockMovementKind.TransferDispatch or StockMovementKind.TransferReceiptShortage or StockMovementKind.TransferTransitLoss)
                    && pool.IsComplete && pool.ExactPosition is not null)
                {
                    unit = pool.ExactPosition.AverageCostPerKg; sourceVersion = pool.Version;
                }
                exactUnit ??= unit is { } scalar ? ExactCostRatio.Of(scalar) : null;

                // Production receipts are intentionally pending until Manufacturing supplies its real closed-order cost.
                // Transfer/return paths require their original source stamp; current average is never a substitute.
                if (movement.Kind == StockMovementKind.TransferTransitLoss)
                {
                    // Dispatch already removed these kg. The paired transit In/Out is a physical fact with net zero;
                    // blending it into the source average would change that average without adding any stock.
                    if (exactUnit is { Sign: >= 0 } && sourceVersion is >= 1) SetKnown(stamp, exactUnit, kg, sourceVersion.Value);
                    stamp.Reason ??= stamp.Status == (int)InventoryMovementCostStampStatusV1.Known ? null : "dispatch_cost_source_unreadable";
                    dbContext.MovementCostStamps.Add(stamp);
                    continue;
                }
                if (incoming)
                {
                    if (partialSourceChanged)
                    {
                        // The old issue was not fully costed. A later generation cannot rewrite that historical return
                        // price or invent a new valuation epoch. Preserve the physical return and the explicit unknown.
                        afterVersion = await WriteCostPoolAsync(dbContext, companyContext, timeProvider, position.LocationId,
                            position.ProductId, currency, pool, null, token, AverageCostUnreadableReason.Return);
                        afterPosition = null; stamp.Reason = "original_partial_issue_cost_no_longer_reconciles";
                        AddPending(movement.Id, number, currency, position, kg, stamp.Reason);
                    }
                    else if (restoredPricedKg is { Sign: > 0 } partialKg && restoredPricedValue is { Sign: >= 0 } partialValue && pool.IsConsistent)
                    {
                        var after = (pool.ExactPosition ?? new(ExactCostRatio.Of(0m), 0m)).Add(partialKg, partialValue);
                        afterVersion = await WriteCostPoolAsync(dbContext, companyContext, timeProvider, position.LocationId,
                            position.ProductId, currency, pool, after, token);
                        afterPosition = after; costedDelta = partialKg;
                        stamp.LedgerVersion = afterVersion;
                        stamp.PoolEffectJson = PoolEffect(pool.ExactPosition, after, partialValue, partialKg);
                        stamp.Reason = "movement_contains_unpriced_lineage";
                    }
                    else if ((exactUnit is { Sign: > 0 } || knownZeroReceipt && exactUnit is { Sign: 0 }) && pool.IsConsistent)
                    {
                        SetKnown(stamp, exactUnit, kg, sourceVersion ?? checked(pool.Version + 1));
                        ExactAverageCostPool? after = null;
                        try
                        {
                            after = (pool.ExactPosition ?? new(ExactCostRatio.Of(0m), 0m)).Add(ExactCostRatio.Of(kg), exactUnit!.Multiply(kg));
                            if (after.AverageCostPerKg >= 100_000_000_000_000m) throw new OverflowException();
                        }
                        catch (OverflowException)
                        {
                            stamp.Status = (int)InventoryMovementCostStampStatusV1.Unavailable;
                            stamp.UnitCostPerKg = null; stamp.Amount = null; stamp.LedgerVersion = null;
                            stamp.UnitCostRatioJson = null; stamp.Reason = "movement_cost_not_representable";
                        }
                        if (after is not null && stamp.Status == (int)InventoryMovementCostStampStatusV1.Known)
                        {
                            afterVersion = await WriteCostPoolAsync(dbContext, companyContext, timeProvider, position.LocationId,
                                position.ProductId, currency, pool, after, token);
                            afterPosition = after; costedDelta = ExactCostRatio.Of(kg);
                            stamp.LedgerVersion = sourceVersion ?? afterVersion;
                            stamp.PoolEffectJson = PoolEffect(pool.ExactPosition, after, exactUnit!.Multiply(kg), costedDelta);
                        }
                        else RecordPending(stamp.Reason!);
                    }
                    else RecordPending(stamp.Reason ?? "entry_cost_not_readable");
                }
                else if (pool.IsConsistent && pool.ExactPosition is not null)
                {
                    var pricedKg = pool.PricedQuantity(position.LabelId, kg);
                    if (pricedKg.Sign > 0)
                    {
                        ExactAverageCostPool? after = null;
                        try
                        {
                            var actualValue = reversalPoolUnit?.Multiply(pricedKg)
                                ?? (originalCostExit && exactUnit is not null ? exactUnit.Multiply(pricedKg) : null);
                            // A purchase return without its admitted original cost never becomes a generic average issue.
                            if (originalCostExit && exactUnit is null) throw new InvalidOperationException();
                            after = pool.ExactPosition.Remove(pricedKg, actualValue);
                            if (after.AverageCostPerKg >= 100_000_000_000_000m) throw new OverflowException();
                        }
                        catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
                        { stamp.Reason = "movement_cost_reversal_unreadable"; }
                        afterVersion = await WriteCostPoolAsync(dbContext, companyContext, timeProvider, position.LocationId,
                            position.ProductId, currency, pool, after, token, AverageCostUnreadableReason.Return);
                        afterPosition = after; costedDelta = ExactCostRatio.Of(0m).Subtract(pricedKg);
                        if (after is not null)
                        {
                            var authoritative = (exactUnit ?? ExactCostRatio.Of(pool.ExactPosition.AverageCostPerKg)).Multiply(costedDelta);
                            stamp.PoolEffectJson = PoolEffect(pool.ExactPosition, after, authoritative, pricedKg);
                            stamp.LedgerVersion = afterVersion;
                            if (pricedKg.CompareTo(ExactCostRatio.Of(kg)) == 0 && exactUnit is { Sign: >= 0 } && sourceVersion is >= 1)
                                SetKnown(stamp, exactUnit, kg, sourceVersion.Value);
                            else stamp.Reason ??= "movement_contains_unpriced_lineage";
                        }
                    }
                }
                var pendingShares = pool.UnpricedShares.ToDictionary(pair => pair.Key, pair => pair.Value);
                if (incoming && costedDelta.Sign == 0 && !InheritedPending()) pendingShares[position.LabelId] = ExactCostRatio.Of(1m);
                pools[poolKey] = pool with
                {
                    ExactPosition = afterPosition, Version = afterVersion,
                    PhysicalKg = pool.PhysicalKg + (incoming ? kg : -kg), CostedKg = pool.CostedKg.Add(costedDelta),
                    UnpricedShares = pendingShares,
                    Reason = afterPosition is null && afterVersion != pool.Version ? AverageCostUnreadableReason.Return : pool.Reason,
                    ExactRow = dbContext.ExactCostPools.Local.SingleOrDefault(row => row.CompanyId == companyContext.CompanyId && row.LocationId == position.LocationId
                        && row.ProductId == position.ProductId && row.CurrencyId == currency),
                    TlRow = currency == SettingsCurrencyIdentitiesV1.TurkishLira
                        ? dbContext.AverageCostLedger.Local.SingleOrDefault(r => r.CompanyId == companyContext.CompanyId && r.LocationId == position.LocationId && r.ProductId == position.ProductId) : null,
                    ForeignRow = currency != SettingsCurrencyIdentitiesV1.TurkishLira
                        ? dbContext.CurrencyAverageCostLedger.Local.SingleOrDefault(r => r.CompanyId == companyContext.CompanyId && r.LocationId == position.LocationId && r.ProductId == position.ProductId && r.CurrencyId == currency) : null,
                };
                stamp.Reason ??= stamp.Status == (int)InventoryMovementCostStampStatusV1.Known ? null : "movement_cost_source_unreadable";
                dbContext.MovementCostStamps.Add(stamp);

                bool InheritedPending() => (movement.Kind is StockMovementKind.TransferAccept or StockMovementKind.TransferDispatchCancellation
                    || movement.Source.Kind == InventoryManufacturingRealizationContractV1.InputReturnSourceKind || IsCustomerReturn(movement))
                    && pool.UnpricedShares.GetValueOrDefault(position.LabelId)?.Sign > 0;
                void RecordPending(string reason)
                {
                    // A transport/return does not create a second unknown material root over the existing cohort.
                    if (!InheritedPending()) AddPending(movement.Id, number, currency, position, kg, reason);
                }
                async Task RestorePartialSourceAsync(MovementCostStampRow original)
                {
                    if (original.PoolEffectJson is null || original.LedgerVersion is not >= 1) return;
                    var effect = JsonSerializer.Deserialize<MovementPoolEffect>(original.PoolEffectJson, CostJson)
                        ?? throw new InvalidOperationException("Kısmi hareket maliyeti kanıtı eksik.");
                    if (effect.PricedQuantityKgRatio is null) return;
                    var originalKg = await dbContext.MovementLines.AsNoTracking().Where(row => row.MovementId == original.MovementId
                        && row.LineNumber == original.LineNumber).Select(row => row.QuantityKg).SingleAsync(token);
                    var priced = effect.PricedQuantityKgRatio.Value().Multiply(kg).Divide(originalKg);
                    var value = effect.RecordedValue.Value().Multiply(-kg).Divide(originalKg);
                    if (priced.Sign <= 0 || priced.CompareTo(ExactCostRatio.Of(kg)) > 0 || value.Sign < 0) return;
                    var originalUnpricedFraction = ExactCostRatio.Of(1m).Subtract(priced.Divide(kg));
                    if (originalUnpricedFraction.CompareTo(pool.UnpricedShares.GetValueOrDefault(position.LabelId, ExactCostRatio.Of(0m))) != 0)
                    { partialSourceChanged = true; CopyCurrency(stamp, original); return; }
                    restoredPricedKg = priced; restoredPricedValue = value; CopyCurrency(stamp, original);
                }
            }
        }

        static bool IsCustomerReturn(StockMovement candidate) => candidate.Kind == StockMovementKind.Return
            && candidate.Source.Kind == InventoryCustomerReturnContractV1.SourceKind;
    }

    private MovementCostStampRow NewStamp(Guid movement, int line, Guid currency, InventoryIssueCostCurrencyV1 proof,
        string evidence, InventoryCommandMetadata metadata)
    {
        // The exact owner rate is retained even when decimal(38,18) cannot represent its scalar.
        InventoryExactCostRatioV1? rateRatio = proof.ExchangeRateRatio;
        decimal? rateScalar = proof.ExchangeRate;
        try
        {
            if (proof.Status == InventoryIssueCostCurrencyStatusV1.Converted)
            {
                var exact = InventoryExactCostRatioV1.Resolve(rateScalar, rateRatio);
                if (exact.Sign <= 0 || proof.OriginalCurrencyId is null || proof.OriginalCurrencyId == Guid.Empty
                    || proof.Reason is not null
                    || InventoryExactCostRatioV1.Resolve(proof.ForeignUnitCostPerKg, proof.ForeignUnitCostRatio).Sign <= 0)
                    throw new InvalidOperationException();
                rateRatio = InventoryExactCostRatioV1.From(exact);
                if (rateScalar is { } value && (Math.Round(value, 18, MidpointRounding.AwayFromZero) != value || value >= 100_000_000_000_000_000_000m))
                    rateScalar = null;
            }
            else if (rateScalar is not null || rateRatio is not null) throw new InvalidOperationException();
            if (proof.Status == InventoryIssueCostCurrencyStatusV1.FunctionalCurrency
                && (proof.OriginalCurrencyId is not null || proof.ForeignUnitCostPerKg is not null || proof.ForeignUnitCostRatio is not null
                    || proof.Reason is not null)) throw new InvalidOperationException();
            if (proof.Status == InventoryIssueCostCurrencyStatusV1.RateMissing)
            {
                if (proof.OriginalCurrencyId is null || proof.OriginalCurrencyId == Guid.Empty || string.IsNullOrWhiteSpace(proof.Reason))
                    throw new InvalidOperationException();
                if (proof.ForeignUnitCostPerKg is not null || proof.ForeignUnitCostRatio is not null)
                    if (InventoryExactCostRatioV1.Resolve(proof.ForeignUnitCostPerKg, proof.ForeignUnitCostRatio).Sign <= 0)
                        throw new InvalidOperationException();
            }
            if (proof.Status == InventoryIssueCostCurrencyStatusV1.Unreadable
                && (proof.OriginalCurrencyId is not null || proof.ForeignUnitCostPerKg is not null || proof.ForeignUnitCostRatio is not null
                    || string.IsNullOrWhiteSpace(proof.Reason))) throw new InvalidOperationException();
            if (!Enum.IsDefined(proof.Status) || string.IsNullOrWhiteSpace(proof.SourceVersion) || proof.SourceVersion.Length > 160)
                throw new InvalidOperationException();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            proof = new(InventoryIssueCostCurrencyStatusV1.Unreadable, null, null, null,
                !string.IsNullOrWhiteSpace(proof.SourceVersion) && proof.SourceVersion.Length <= 160
                    ? proof.SourceVersion : "inventory.entry-currency.v1/unreadable",
                "movement_currency_rate_proof_unreadable");
            rateScalar = null; rateRatio = null;
        }
        return new()
        {
        CompanyId = companyContext.CompanyId, MovementId = movement, LineNumber = line, CurrencyId = currency,
        Status = (int)InventoryMovementCostStampStatusV1.Unavailable, StampVersion = 1,
        EntryCurrencyStatus = (int)proof.Status, OriginalCurrencyId = proof.OriginalCurrencyId,
        ForeignUnitCostPerKg = proof.ForeignUnitCostPerKg,
        EntryExchangeRate = rateScalar,
        EntryExchangeRateRatioJson = rateRatio is null ? null : JsonSerializer.Serialize(rateRatio, CostJson),
        ForeignUnitCostRatioJson = proof.ForeignUnitCostRatio is null ? null : JsonSerializer.Serialize(proof.ForeignUnitCostRatio, CostJson),
        CurrencySourceVersion = proof.SourceVersion, CurrencyReason = proof.Reason, EvidenceJson = evidence,
        RecordedAtUtc = timeProvider.GetUtcNow(), CorrelationId = metadata.CorrelationId,
        };
    }

    private static void SetKnown(MovementCostStampRow stamp, ExactCostRatio unit, decimal kg, long version)
    {
        stamp.Status = (int)InventoryMovementCostStampStatusV1.Known;
        stamp.UnitCostRatioJson = JsonSerializer.Serialize(InventoryExactCostRatioV1.From(unit), CostJson);
        stamp.UnitCostPerKg = unit.TryToDecimal(out var scalar) && scalar < 100_000_000_000_000_000_000m ? scalar : null;
        stamp.Amount = unit.Multiply(kg).TryToDecimal(out var amount) && amount < 100_000_000_000_000_000_000m ? amount : null;
        stamp.LedgerVersion = version; stamp.Reason = null;
    }

    private static readonly JsonSerializerOptions CostJson = new(JsonSerializerDefaults.Web);
    private static ExactCostRatio StampUnit(MovementCostStampRow stamp) => InventoryExactCostRatioV1.Resolve(stamp.UnitCostPerKg,
        stamp.UnitCostRatioJson is { } ratio ? JsonSerializer.Deserialize<InventoryExactCostRatioV1>(ratio, CostJson) : null);

    internal static ExactCostRatio? AdmittedReceiptUnit(decimal? scalar, InventoryExactCostRatioV1? ratio, string? unavailableReason)
    {
        if (unavailableReason is not null)
        {
            if (scalar is not null || ratio is not null) throw new InvalidOperationException("Unavailable receipt cost carries a value.");
            return null;
        }
        if (scalar is null && ratio is null) return null;
        var value = InventoryExactCostRatioV1.Resolve(scalar, ratio);
        if (value.Sign < 0) throw new InvalidOperationException("Receipt unit cost is negative.");
        return value;
    }

    private static string PoolEffect(ExactAverageCostPool? before, ExactAverageCostPool after, ExactCostRatio authoritative,
        ExactCostRatio pricedQuantity)
    {
        var beforeQuantity = before?.QuantityKg ?? ExactCostRatio.Of(0m);
        var beforeValue = before?.Value ?? ExactCostRatio.Of(0m);
        return JsonSerializer.Serialize(new MovementPoolEffect(InventoryExactCostRatioV1.ScalarOf(beforeQuantity), before?.AverageCostPerKg,
            InventoryExactCostRatioV1.ScalarOf(after.QuantityKg), after.AverageCostPerKg, InventoryExactCostRatioV1.From(authoritative),
            InventoryExactCostRatioV1.From(after.Value.Subtract(beforeValue)), InventoryExactCostRatioV1.From(beforeQuantity),
            InventoryExactCostRatioV1.From(after.QuantityKg), InventoryExactCostRatioV1.From(pricedQuantity)), CostJson);
    }
    private sealed record MovementPoolEffect(decimal? BeforeQuantityKg, decimal? BeforeAverageCostPerKg, decimal? AfterQuantityKg,
        decimal AfterAverageCostPerKg, InventoryExactCostRatioV1 AuthoritativeValue, InventoryExactCostRatioV1 RecordedValue,
        InventoryExactCostRatioV1? BeforeQuantityKgRatio = null, InventoryExactCostRatioV1? AfterQuantityKgRatio = null,
        InventoryExactCostRatioV1? PricedQuantityKgRatio = null);

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
        target.ForeignUnitCostRatioJson = source.ForeignUnitCostRatioJson;
        target.EntryExchangeRateRatioJson = source.EntryExchangeRateRatioJson;
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
            receipts[0].CurrencySourceVersion, receipts[0].CurrencyReason,
            receipts[0].ForeignUnitCostRatioJson is { } ratio ? JsonSerializer.Deserialize<InventoryExactCostRatioV1>(ratio, CostJson) : null,
            receipts[0].EntryExchangeRateRatioJson is { } fxRatio ? JsonSerializer.Deserialize<InventoryExactCostRatioV1>(fxRatio, CostJson) : null)
            : new(InventoryIssueCostCurrencyStatusV1.Unreadable, null, null, null,
                "inventory.entry-currency.v1/unreadable", "entry_currency_source_unreadable");
    }

}

using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryStore
{
    internal static async Task<CostPool> ReadCostPoolAsync(InventoryDbContext context, ICompanyContext company,
        Guid location, Guid product, Guid currency, CancellationToken token)
    {
        var physical = await context.Balances.Where(row => row.LocationId == location && row.ProductId == product
            && row.OwnerKind == (int)StockPartyKind.Company && row.OwnerId == company.CompanyId
            && row.CustodianKind == (int)StockPartyKind.Company && row.CustodianId == company.CompanyId).ToListAsync(token);
        physical.AddRange(context.Balances.Local.Where(row => row.CompanyId == company.CompanyId && row.LocationId == location
            && row.ProductId == product && row.OwnerKind == (int)StockPartyKind.Company && row.OwnerId == company.CompanyId
            && row.CustodianKind == (int)StockPartyKind.Company && row.CustodianId == company.CompanyId && !physical.Contains(row)));
        var physicalKg = physical.Sum(row => row.QuantityKg);
        var pending = await context.PendingMovementCosts.Where(row => row.ProductId == product && row.CurrencyId == currency).ToListAsync(token);
        pending.AddRange(context.PendingMovementCosts.Local.Where(row => row.CompanyId == company.CompanyId
            && row.ProductId == product && row.CurrencyId == currency && !pending.Contains(row)));
        DateTimeOffset? openingAt = null;
        if (currency == SettingsCurrencyIdentitiesV1.TurkishLira)
            openingAt = await (from opening in context.OpeningCosts.AsNoTracking()
                join decision in context.OpeningCostDecisions.AsNoTracking() on opening.Id equals decision.OpeningCostId
                where opening.ProductId == product && opening.LocationId == location && decision.Decision == 1
                select (DateTimeOffset?)decision.DecidedAtUtc).MaxAsync(token);
        if (openingAt is { } approved) pending.RemoveAll(row => row.LocationId == location && row.RecordedAtUtc <= approved);
        var recognized = await (from recognition in context.MovementCostRecognitions
            join generation in context.ManufacturingCostGenerations on recognition.GenerationId equals (Guid?)generation.Id into generations
            from generation in generations.DefaultIfEmpty()
            where recognition.CurrencyId == currency && (recognition.GenerationId == null
                || generation!.State == (int)InventoryManufacturingCostGenerationStateV1.Recognized)
            select new { recognition.MovementId, recognition.LineNumber }).ToListAsync(token);
        pending.RemoveAll(row => recognized.Any(done => done.MovementId == row.MovementId && done.LineNumber == row.LineNumber)
            || context.MovementCostRecognitions.Local.Any(done => done.CurrencyId == currency && done.MovementId == row.MovementId
                && done.LineNumber == row.LineNumber && (done.GenerationId == null || context.ManufacturingCostGenerations.Local.Any(
                    generation => generation.Id == done.GenerationId && generation.State == (int)InventoryManufacturingCostGenerationStateV1.Recognized))));
        // BS-1 partial first invoices price only their immutable receipt fraction. The original
        // unavailable stamp/pending entry stays intact; it is not a new whole-receipt recognition.
        var purchasePriced = new Dictionary<Guid, ExactCostRatio>();
        if (currency == SettingsCurrencyIdentitiesV1.TurkishLira && pending.Count != 0)
        {
            var movements = pending.Select(row => row.MovementId).Distinct().ToArray();
            var effects = new List<PurchaseInvoiceReceiptCostEffectRow>();
            foreach (var batch in movements.Chunk(256))
                effects.AddRange(await context.PurchaseInvoiceReceiptCostEffects.Where(row => batch.Contains(row.MovementId))
                    .ToListAsync(token));
            effects.AddRange(context.PurchaseInvoiceReceiptCostEffects.Local.Where(row => row.CompanyId == company.CompanyId
                && movements.Contains(row.MovementId) && !effects.Contains(row)));
            foreach (var group in effects.GroupBy(row => row.MovementId))
            {
                if (group.Any(row => row.AcceptedQuantity <= 0m || row.MatchedQuantity <= 0m
                    || row.AcceptedQuantity != group.First().AcceptedQuantity))
                    throw new InvalidOperationException("purchase_first_cost_share_unreadable");
                var fraction = group.Aggregate(ExactCostRatio.Of(0m), (sum, row) =>
                    sum.Add(ExactCostRatio.Of(row.MatchedQuantity).Divide(row.AcceptedQuantity)));
                if (fraction.CompareTo(ExactCostRatio.Of(1m)) > 0)
                    throw new InvalidOperationException("purchase_first_cost_share_unreadable");
                purchasePriced.Add(group.Key, fraction);
            }
        }
        var shares = new Dictionary<Guid, ExactCostRatio>();
        var readable = true;
        foreach (var root in pending.Select(row => row.LabelId).Distinct().Order())
        {
            var entries = pending.Where(row => row.LabelId == root).ToArray();
            if (entries.Length != 1) { readable = false; continue; }
            var unresolved = ExactCostRatio.Of(1m).Subtract(purchasePriced.GetValueOrDefault(entries[0].MovementId, ExactCostRatio.Of(0m)));
            if (unresolved.Sign == 0) continue;
            var lineage = await InventoryCostLineageReader.ReadAsync(context, company.CompanyId, product, root, token);
            if (lineage.Outcome != InventoryCostLineageOutcome.Value) { readable = false; continue; }
            foreach (var label in lineage.Labels.Where(label => label.RootShare.Sign > 0))
            {
                var share = shares.GetValueOrDefault(label.LabelId, ExactCostRatio.Of(0m)).Add(label.RootShare.Multiply(unresolved));
                if (share.CompareTo(ExactCostRatio.Of(1m)) > 0) readable = false;
                shares[label.LabelId] = share;
            }
        }
        var unpriced = physical.Aggregate(ExactCostRatio.Of(0m), (sum, row) => sum.Add(
            shares.GetValueOrDefault(row.LabelId, ExactCostRatio.Of(0m)).Multiply(row.QuantityKg)));
        var priced = ExactCostRatio.Of(physicalKg).Subtract(unpriced);
        if (priced.Sign < 0) readable = false;
        var tl = currency == SettingsCurrencyIdentitiesV1.TurkishLira
            ? context.AverageCostLedger.Local.SingleOrDefault(row => row.LocationId == location && row.ProductId == product)
                ?? await context.AverageCostLedger.SingleOrDefaultAsync(row => row.LocationId == location && row.ProductId == product, token) : null;
        var foreign = currency != SettingsCurrencyIdentitiesV1.TurkishLira
            ? context.CurrencyAverageCostLedger.Local.SingleOrDefault(row => row.LocationId == location && row.ProductId == product && row.CurrencyId == currency)
                ?? await context.CurrencyAverageCostLedger.SingleOrDefaultAsync(row => row.LocationId == location && row.ProductId == product && row.CurrencyId == currency, token) : null;
        var exact = context.ExactCostPools.Local.SingleOrDefault(row => row.LocationId == location && row.ProductId == product && row.CurrencyId == currency)
            ?? await context.ExactCostPools.SingleOrDefaultAsync(row => row.LocationId == location && row.ProductId == product && row.CurrencyId == currency, token);
        ExactAverageCostPool? position = null;
        AverageCostUnreadableReason? reason = null;
        var version = tl?.Version ?? foreign?.Version ?? 0;
        if (exact is not null && !(openingAt is { } openingDecisionAt && openingDecisionAt > exact.UpdatedAtUtc))
        {
            version = Math.Max(version, exact.Version);
            if (exact.Status == (int)AverageCostStatus.Known && exact.QuantityRatioJson is { } json && exact.AverageCostPerKg is { } average)
                position = new ExactAverageCostPool((JsonSerializer.Deserialize<InventoryExactCostRatioV1>(json, CostJson)
                    ?? throw new InvalidOperationException("Maliyet havuzu tam payı eksik.")).Value(), average);
            else reason = exact.UnreadableReason is { } raw ? (AverageCostUnreadableReason)raw : AverageCostUnreadableReason.LedgerStockMismatch;
        }
        else if (tl is not null || foreign is not null)
        {
            var status = tl?.Status ?? foreign!.Status;
            if (status == (int)AverageCostStatus.Known && (tl?.QuantityKg ?? foreign?.QuantityKg) is { } kg
                && (tl?.AverageCostPerKg ?? foreign?.AverageCostPerKg) is { } average)
                position = new ExactAverageCostPool(ExactCostRatio.Of(kg), average);
            else reason = (AverageCostUnreadableReason)(tl?.UnreadableReason ?? foreign?.UnreadableReason ?? (int)AverageCostUnreadableReason.LedgerStockMismatch);
        }
        // No ledger is a genuinely empty priced pool only when every physical kg has an actual pending source.
        // Legacy unvalued stock without those facts needs its approved opening-cost flow, never a zero-cost seed.
        return new CostPool(position, reason, version, physicalKg, priced, shares, tl, foreign, exact, readable);
    }

    internal static Task<long> WriteCostPoolAsync(InventoryDbContext context, ICompanyContext company, TimeProvider clock,
        Guid location, Guid product, Guid currency, CostPool before, ExactAverageCostPool? after,
        CancellationToken token, AverageCostUnreadableReason? reason = null)
    {
        token.ThrowIfCancellationRequested();
        var version = checked(before.Version + (before.ExactRow is null || context.Entry(before.ExactRow).State == EntityState.Unchanged ? 1 : 0));
        var exact = before.ExactRow ?? new ExactCostPoolRow { CompanyId = company.CompanyId,
            LocationId = location, ProductId = product, CurrencyId = currency };
        exact.Status = after is null ? (int)AverageCostStatus.Unreadable : (int)AverageCostStatus.Known;
        exact.QuantityRatioJson = after is null ? null : JsonSerializer.Serialize(InventoryExactCostRatioV1.From(after.QuantityKg), CostJson);
        exact.AverageCostPerKg = after?.AverageCostPerKg; exact.UnreadableReason = after is null ? (int)(reason ?? AverageCostUnreadableReason.LedgerStockMismatch) : null;
        exact.Version = version; exact.UpdatedAtUtc = clock.GetUtcNow();
        if (before.ExactRow is null) context.ExactCostPools.Add(exact);
        var projected = after is not null && after.QuantityKg.TryToDecimal(out var scalar) && decimal.Round(scalar, 3) == scalar
            ? AverageCostPosition.Known(scalar, after.AverageCostPerKg)
            : AverageCostPosition.Unreadable(reason ?? AverageCostUnreadableReason.LedgerStockMismatch);
        if (currency == SettingsCurrencyIdentitiesV1.TurkishLira)
        {
            var row = before.TlRow ?? new AverageCostLedgerRow { Id = Guid.NewGuid(), CompanyId = company.CompanyId, LocationId = location, ProductId = product };
            InventoryAverageCostLedgerRows.Write(row, projected); row.Version = version; row.UpdatedAtUtc = clock.GetUtcNow();
            if (before.TlRow is null) context.AverageCostLedger.Add(row);
        }
        else
        {
            var row = before.ForeignRow ?? new CurrencyAverageCostLedgerRow { Id = Guid.NewGuid(), CompanyId = company.CompanyId,
                LocationId = location, ProductId = product, CurrencyId = currency };
            row.Status = (int)projected.Status; row.QuantityKg = projected.QuantityKg; row.AverageCostPerKg = projected.AverageCostPerKg;
            row.UnreadableReason = projected.UnreadableReason is { } unreadable ? (int)unreadable : null;
            row.Version = version; row.UpdatedAtUtc = clock.GetUtcNow();
            if (before.ForeignRow is null) context.CurrencyAverageCostLedger.Add(row);
        }
        return Task.FromResult(version);
    }

    internal sealed record CostPool(ExactAverageCostPool? ExactPosition, AverageCostUnreadableReason? Reason, long Version,
        decimal PhysicalKg, ExactCostRatio CostedKg, IReadOnlyDictionary<Guid, ExactCostRatio> UnpricedShares,
        AverageCostLedgerRow? TlRow, CurrencyAverageCostLedgerRow? ForeignRow, ExactCostPoolRow? ExactRow, bool SourceReadable)
    {
        public bool IsConsistent => SourceReadable && Reason is null && (ExactPosition is null ? CostedKg.Sign == 0 : ExactPosition.QuantityKg.CompareTo(CostedKg) == 0);
        public bool IsComplete => IsConsistent && CostedKg.CompareTo(ExactCostRatio.Of(PhysicalKg)) == 0;
        public ExactCostRatio PricedQuantity(Guid labelId, decimal kg) => ExactCostRatio.Of(1m)
            .Subtract(UnpricedShares.GetValueOrDefault(labelId, ExactCostRatio.Of(0m))).Multiply(kg);
    }
}

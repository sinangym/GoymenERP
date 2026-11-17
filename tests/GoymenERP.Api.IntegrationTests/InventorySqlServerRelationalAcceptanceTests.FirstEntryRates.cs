using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real SQL receipt/stamp, repack and isolated owner-reader sources. Purchasing order FX and invoice prices
/// are explicitly synthetic owner proofs; no TCMB connection or real Purchasing authority is claimed. NOT_RUN.</summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private static readonly JsonSerializerOptions FirstFxJson = new(JsonSerializerDefaults.Web);

    [SqlServerFact]
    public async Task A_cached_receipt_subtree_does_not_bypass_the_first_entry_lineage_depth_bound_on_a_deeper_merge_path()
    {
        await using var run = await Run.StartAsync();
        var root = await FirstFxLabelAsync(run, 1000m, 31m);
        var current = root.Label.Id;
        var kg = 1000m;
        // The first merge input caches a ten-level subtree; the second reaches that same subtree much deeper.
        for (var i = 0; i < 10; i++)
        {
            var next = Guid.NewGuid();
            await RepackFirstFxAsync(run, InventoryLabelTransformationKindV1.Split, [current], [(Guid.NewGuid(), 1m), (next, kg - 1m)]);
            current = next; kg -= 1m;
        }
        var shallow = Guid.NewGuid(); var tail = Guid.NewGuid();
        await RepackFirstFxAsync(run, InventoryLabelTransformationKindV1.Split, [current], [(shallow, 1m), (tail, kg - 1m)]);
        current = tail; kg -= 1m;
        for (var i = 0; i < 54; i++)
        {
            var next = Guid.NewGuid();
            await RepackFirstFxAsync(run, InventoryLabelTransformationKindV1.Split, [current], [(Guid.NewGuid(), 1m), (next, kg - 1m)]);
            current = next; kg -= 1m;
        }
        var merged = Guid.NewGuid();
        await RepackFirstFxAsync(run, InventoryLabelTransformationKindV1.Merge, [shallow, current], [(merged, kg + 1m)]);
        var result = await ReadFirstFxAsync(run, CompanyA, CompanyA, merged);
        Assert.Equal(InventoryFirstReceiptExchangeRateStatusV1.Unavailable, result.Status);
        Assert.Equal(InventoryFirstReceiptExchangeRateReasonsV1.Unreadable, result.UnavailableReason);
        Assert.Null(result.Rate); Assert.Null(result.RateRatio); Assert.Empty(result.Sources);
    }

    [SqlServerFact]
    public async Task First_entry_FX_Down_also_refuses_exact_only_issue_currency_rates_without_a_first_entry_snapshot()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddInventoryFirstEntryExchangeRateProof().DownOperations[0]).Sql;
        var label = NewLabel(CompanyA, "SYN-EXACT-FX-DOWN");
        var acceptance = Guid.NewGuid();
        var receipt = Movement(CompanyA, StockMovementKind.Receipt,
            new StockSourceReference(InventoryGoodsReceiptContractV1.SourceKind, acceptance),
            In(Position(CompanyA, await run.LocationOfAsync(CompanyA), label, BlockState.Free), 1m));
        await using (var owner = run.Store(CompanyA))
        {
            await owner.Labels.SaveLabelAsync(label, Metadata(run));
            owner.Store.UseReceiptCost(receipt.Id, new(CompanyA, acceptance, Guid.NewGuid(), Guid.NewGuid(),
                ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"), 1m, 1m, 1m,
                new(InventoryIssueCostCurrencyStatusV1.Converted, ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"), 1m, null,
                    "SENTETIK-exact-only-rate", null, ExchangeRateRatio: new("1", "3")), "SENTETIK-price", "{}", null, null));
            await owner.Store.RecordMovementAsync(receipt, Metadata(run));
        }
        Assert.Equal(51972, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        await using var final = run.Context(CompanyA);
        Assert.Equal(2, await final.MovementCostStamps.CountAsync(x => x.MovementId == receipt.Id
            && x.EntryExchangeRateRatioJson != null && x.FirstEntryRateJson == null));
    }

    [SqlServerFact]
    public async Task First_entry_FX_Down_passes_legacy_rows_and_refuses_real_first_entry_or_exact_issue_rate_provenance()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddInventoryFirstEntryExchangeRateProof().DownOperations[0]).Sql;
        await CostedLabelAsync(run, await run.LocationOfAsync(CompanyA), 1m, 10m, 2m);
        await run.ExecuteAsync(guard); // A plain legacy finite currency proof loses no first-entry or exact-only FX meaning.
        var captured = await FirstFxLabelAsync(run, 1m, 31m);
        Assert.Equal(51972, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        await using var final = run.Context(CompanyA);
        Assert.Equal(2, await final.MovementCostStamps.CountAsync(x => x.MovementId == captured.MovementId && x.FirstEntryRateJson != null));
    }

    [SqlServerFact]
    public async Task First_entry_rate_is_captured_with_the_original_receipt_and_remains_distinct_from_invoice_cost_FX()
    {
        await using var run = await Run.StartAsync();
        var first = await FirstFxLabelAsync(run, 3m, 31.25m);
        var answer = await ReadFirstFxAsync(run, CompanyA, CompanyA, first.Label.Id);
        Assert.Equal(InventoryFirstReceiptExchangeRateStatusV1.Rated, answer.Status);
        Assert.Equal(31.25m, answer.Rate!.Value.Rate);
        Assert.Equal(first.MovementId, Assert.Single(answer.Sources).Entry.MovementId);
        Assert.Equal(first.Snapshot, Assert.Single(answer.Sources).Snapshot);
        await using (var context = run.Context(CompanyA))
        {
            var stamps = await context.MovementCostStamps.AsNoTracking().Where(x => x.MovementId == first.MovementId).ToListAsync();
            Assert.Equal(2, stamps.Count);
            Assert.All(stamps, stamp => Assert.Equal(99m, stamp.EntryExchangeRate)); // Official invoice stock-cost proof.
            Assert.All(stamps, stamp => Assert.Equal(31.25m,
                JsonSerializer.Deserialize<InventoryFirstEntryRateV1>(stamp.FirstEntryRateJson!, FirstFxJson)!.Rate));
        }
        var movement = Movement(CompanyA, StockMovementKind.Issue,
            Out(Position(CompanyA, await run.LocationOfAsync(CompanyA), first.Label, BlockState.Free), 1m));
        await using (var owner = run.Store(CompanyA)) await owner.Store.RecordMovementAsync(movement, Metadata(run));
        var later = await ReadFirstFxAsync(run, CompanyA, CompanyA, first.Label.Id);
        Assert.Equal(answer.SourceVersion, later.SourceVersion);
        Assert.Equal(answer.RateRatio, later.RateRatio);
        Assert.Equal(InventoryFirstReceiptExchangeRateStatusV1.NotFound,
            (await ReadFirstFxAsync(run, CompanyB, CompanyB, first.Label.Id)).Status);
        Assert.Equal(InventoryFirstReceiptExchangeRateStatusV1.NotFound,
            (await ReadFirstFxAsync(run, CompanyA, CompanyB, first.Label.Id)).Status);
    }

    [SqlServerFact]
    public async Task First_entry_merge_split_and_diamond_merge_preserve_exact_weighted_rates_and_actual_receipt_components()
    {
        await using var run = await Run.StartAsync();
        var first = await FirstFxLabelAsync(run, 1m, 1m);
        var second = await FirstFxLabelAsync(run, 2m, 2m);
        var merged = Guid.NewGuid();
        await RepackFirstFxAsync(run, InventoryLabelTransformationKindV1.Merge, [first.Label.Id, second.Label.Id], [(merged, 3m)]);
        var answer = await ReadFirstFxAsync(run, CompanyA, CompanyA, merged);
        Assert.Equal(InventoryFirstReceiptExchangeRateStatusV1.Rated, answer.Status);
        Assert.Null(answer.Rate); Assert.Null(answer.Entry);
        Assert.Equal(new InventoryExactCostRatioV1("5", "3"), answer.RateRatio);
        Assert.Equal(2, answer.Sources.Count);
        Assert.Equal(new InventoryExactCostRatioV1("1", "3"), answer.Sources.Single(x => x.Entry.MovementId == first.MovementId).MaterialShare);
        Assert.Equal(new InventoryExactCostRatioV1("2", "3"), answer.Sources.Single(x => x.Entry.MovementId == second.MovementId).MaterialShare);
        var left = Guid.NewGuid(); var right = Guid.NewGuid();
        await RepackFirstFxAsync(run, InventoryLabelTransformationKindV1.Split, [merged], [(left, 1m), (right, 2m)]);
        Assert.Equal(answer.RateRatio, (await ReadFirstFxAsync(run, CompanyA, CompanyA, left)).RateRatio);
        var diamond = Guid.NewGuid();
        await RepackFirstFxAsync(run, InventoryLabelTransformationKindV1.Merge, [left, right], [(diamond, 3m)]);
        var repeated = await ReadFirstFxAsync(run, CompanyA, CompanyA, diamond);
        Assert.Equal(answer.RateRatio, repeated.RateRatio);
        Assert.Equal(2, repeated.Sources.Count); // Same historical receipt is combined, never counted as a new entry.
        Assert.Equal(answer.Sources.Select(x => (x.Entry.MovementId, x.MaterialShare)).OrderBy(x => x.MovementId),
            repeated.Sources.Select(x => (x.Entry.MovementId, x.MaterialShare)).OrderBy(x => x.MovementId));
        await using var final = run.Context(CompanyA);
        Assert.Equal(4, await final.MovementCostStamps.CountAsync()); // Read and repack do not create new receipt-rate stamps.
        Assert.Equal(3m, await final.Balances.Where(x => x.LabelId == diamond).SumAsync(x => x.QuantityKg));
    }

    [SqlServerFact]
    public async Task First_entry_merge_does_not_substitute_a_readable_input_for_a_missing_rate_or_average_different_currency_pairs()
    {
        await using var run = await Run.StartAsync();
        var readable = await FirstFxLabelAsync(run, 1m, 31m);
        var unavailable = await FirstFxLabelAsync(run, 1m, null);
        var mixed = Guid.NewGuid();
        await RepackFirstFxAsync(run, InventoryLabelTransformationKindV1.Merge, [readable.Label.Id, unavailable.Label.Id], [(mixed, 2m)]);
        var noRate = await ReadFirstFxAsync(run, CompanyA, CompanyA, mixed);
        Assert.Equal(InventoryFirstReceiptExchangeRateStatusV1.Unavailable, noRate.Status);
        Assert.Null(noRate.Rate); Assert.Null(noRate.RateRatio); Assert.Empty(noRate.Sources);
        var usd = await FirstFxLabelAsync(run, 1m, 31m);
        var eur = await FirstFxLabelAsync(run, 1m, 34m, ExchangeRateCurrencyIdentitiesV1.ForIsoCode("EUR"));
        var currencies = Guid.NewGuid();
        await RepackFirstFxAsync(run, InventoryLabelTransformationKindV1.Merge, [usd.Label.Id, eur.Label.Id], [(currencies, 2m)]);
        var mismatch = await ReadFirstFxAsync(run, CompanyA, CompanyA, currencies);
        Assert.Equal(InventoryFirstReceiptExchangeRateReasonsV1.CurrencyPairMismatch, mismatch.UnavailableReason);
        Assert.Null(mismatch.RateRatio);
    }

    [SqlServerFact]
    public async Task First_entry_snapshot_and_exact_issue_FX_are_saved_only_with_the_physical_transaction()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, "SYN-FIRST-FX-ROLLBACK");
        var acceptance = Guid.NewGuid(); var order = Guid.NewGuid();
        var receipt = Movement(CompanyA, StockMovementKind.Receipt,
            new StockSourceReference(InventoryGoodsReceiptContractV1.SourceKind, acceptance),
            In(Position(CompanyA, location, label, BlockState.Free), 1m));
        await using (var owner = run.Store(CompanyA))
        {
            await using var transaction = await owner.DbContext.Database.BeginTransactionAsync();
            await owner.Labels.SaveLabelAsync(label, Metadata(run));
            var snapshot = FirstFxSnapshot(acceptance, order, 31m, ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"));
            owner.Store.UseReceiptCost(receipt.Id, new(CompanyA, acceptance, order, Guid.NewGuid(), snapshot.SourceCurrencyId, 1m, 99m, 1m,
                new(InventoryIssueCostCurrencyStatusV1.Converted, snapshot.SourceCurrencyId, 1m, null, "SENTETIK-exact-issue-FX", null,
                    ExchangeRateRatio: new("1", "3")), "SENTETIK-price-proof", "{}", null, null, FirstEntryRate: snapshot));
            await owner.Store.RecordMovementAsync(receipt, Metadata(run));
            var row = await owner.DbContext.MovementCostStamps.AsNoTracking().SingleAsync(x => x.MovementId == receipt.Id
                && x.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
            Assert.Null(row.EntryExchangeRate);
            Assert.Equal(new InventoryExactCostRatioV1("1", "3"), JsonSerializer.Deserialize<InventoryExactCostRatioV1>(row.EntryExchangeRateRatioJson!, FirstFxJson));
            Assert.NotNull(row.FirstEntryRateJson);
            Assert.Equal(InventoryFirstReceiptExchangeRateStatusV1.Rated,
                (await new InventoryFirstReceiptExchangeRateReadPort(owner.DbContext, ExactLineageCompany(CompanyA)).GetAsync(CompanyA, label.Id)).Status);
            await transaction.RollbackAsync();
        }
        await using var final = run.Context(CompanyA);
        Assert.False(await final.Movements.AnyAsync(x => x.Id == receipt.Id));
        Assert.False(await final.MovementCostStamps.AnyAsync(x => x.MovementId == receipt.Id));
        Assert.False(await final.Labels.AnyAsync(x => x.Id == label.Id));
    }

    private static async Task<(MaterialLabel Label, Guid MovementId, InventoryFirstEntryRateV1 Snapshot)> FirstFxLabelAsync(
        Run run, decimal kg, decimal? rate, Guid? currency = null)
    {
        var label = NewLabel(CompanyA, $"SYN-FIRST-FX-{Guid.NewGuid():N}");
        var acceptance = Guid.NewGuid(); var order = Guid.NewGuid();
        var snapshot = FirstFxSnapshot(acceptance, order, rate, currency ?? ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"));
        var receipt = Movement(CompanyA, StockMovementKind.Receipt,
            new StockSourceReference(InventoryGoodsReceiptContractV1.SourceKind, acceptance),
            In(Position(CompanyA, await run.LocationOfAsync(CompanyA), label, BlockState.Free), kg));
        await using var owner = run.Store(CompanyA);
        await owner.Labels.SaveLabelAsync(label, Metadata(run));
        owner.Store.UseReceiptCost(receipt.Id, new(CompanyA, acceptance, order, Guid.NewGuid(), snapshot.SourceCurrencyId, 1m, 99m, 1m,
            new(InventoryIssueCostCurrencyStatusV1.Converted, snapshot.SourceCurrencyId, 1m, 99m, "SENTETIK-invoice-rate-99", null),
            "SENTETIK-actual-price-proof", "{}", null, null, FirstEntryRate: snapshot));
        await owner.Store.RecordMovementAsync(receipt, Metadata(run));
        return (label, receipt.Id, snapshot);
    }

    private static InventoryFirstEntryRateV1 FirstFxSnapshot(Guid acceptance, Guid order, decimal? rate, Guid currency)
    {
        var day = DateOnly.FromDateTime(At.UtcDateTime);
        var source = rate is null ? InventoryFirstEntryRateSourcesV1.Unavailable : InventoryFirstEntryRateSourcesV1.ApprovedPurchaseOrder;
        var evidence = JsonSerializer.Serialize(new { companyId = CompanyA, acceptanceId = acceptance, purchaseOrderId = order,
            entryDay = day, source = "SENTETIK-owner-first-entry-proof" }, FirstFxJson);
        var answer = new InventoryFirstEntryRateV1(CompanyA, acceptance, order, day, rate is null ? InventoryFirstEntryRateStateV1.Unavailable : InventoryFirstEntryRateStateV1.Rated,
            currency, SettingsCurrencyIdentitiesV1.TurkishLira, rate, rate is { } actual ? InventoryExactCostRatioV1.From(ExactCostRatio.Of(actual)) : null,
            source, string.Empty, evidence, rate is null ? "SENTETIK-order-rate-unreadable" : null, 2);
        return answer with { SourceVersion = InventoryFirstEntryRateAdmissionV1.SourceVersionOf(answer) };
    }

    private static async Task RepackFirstFxAsync(Run run, InventoryLabelTransformationKindV1 kind, Guid[] inputs,
        (Guid Id, decimal Kg)[] outputs)
    {
        await using var context = run.Context(CompanyA);
        var versions = await context.Labels.AsNoTracking().Where(x => inputs.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Version);
        var request = new TransformInventoryLabelsV1(Guid.NewGuid(), kind,
            inputs.Select(id => new InventoryLabelTransformationSourceV1(id, versions[id].ToString(CultureInfo.InvariantCulture))).ToArray(),
            outputs.Select(x => new InventoryLabelTransformationOutputV1(x.Id, $"SYN-FIRST-FX-REPACK-{Guid.NewGuid():N}",
                x.Kg.ToString(CultureInfo.InvariantCulture))).ToArray(), At.AddMinutes(1), "Sentetik kur kaynagi; gercek repack kg izi.");
        await using var owner = run.Store(CompanyA);
        await owner.Store.TransformAsync(request, Metadata(run));
    }

    private static async Task<InventoryFirstReceiptExchangeRateV1> ReadFirstFxAsync(Run run, Guid resolvedCompany, Guid askedCompany, Guid label)
    {
        await using var context = run.Context(resolvedCompany);
        var company = new CompanyContext(); company.Set(resolvedCompany);
        return await new InventoryFirstReceiptExchangeRateReadPort(context, company).GetAsync(askedCompany, label);
    }
}

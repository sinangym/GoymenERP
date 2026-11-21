using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Actual count/ledger/stamp/command transactions; only the receipt's external price proof is synthetic. NOT_RUN.</summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Count_surplus_and_shortage_use_the_recorded_average_and_empty_stock_retains_that_average_without_requiring_USD()
    {
        await using var run = await Run.StartAsync();
        var label = await CountCostReceiptAsync(run, 10m, 12.3456m);
        var surplus = await ApproveCountCostAsync(run, label.Id, "12");
        Assert.Equal("Known", surplus.Count.Decision!.CostStatus);
        await AssertCountCostPoolAsync(run, 12m, 12.3456m, surplus.Count.Decision.AdjustmentMovementId!.Value, 24.6912m);
        var shortage = await ApproveCountCostAsync(run, label.Id, "0");
        Assert.Equal("Known", shortage.Count.Decision!.CostStatus);
        await AssertCountCostPoolAsync(run, 0m, 12.3456m, shortage.Count.Decision.AdjustmentMovementId!.Value, 148.1472m);
        var restored = await ApproveCountCostAsync(run, label.Id, "1.25");
        await AssertCountCostPoolAsync(run, 1.25m, 12.3456m, restored.Count.Decision!.AdjustmentMovementId!.Value, 15.432m);
        await using var db = run.Context(CompanyA);
        Assert.All(await db.MovementCostStamps.Where(row => row.MovementId == restored.Count.Decision.AdjustmentMovementId
            && row.CurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira).ToListAsync(), stamp =>
            Assert.Equal((int)InventoryMovementCostStampStatusV1.Unavailable, stamp.Status));
    }

    [SqlServerFact]
    public async Task Count_unpriced_surplus_records_a_price_free_status_and_replays_the_original_status_after_opening_cost_changes()
    {
        await using var run = await Run.StartAsync();
        var label = await CountCostReceiptAsync(run, 10m, null);
        var request = Count(label.Id, await LabelVersionAsync(run, label.Id), "12");
        var recorder = Metadata(run);
        var decider = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
        var decision = new DecideInventoryStockCountV1(InventoryStockCountDecisionKindV1.Approve, "SENTETIK sayim fazlasi");
        InventoryStockCountResultV1 original;
        await using (var owner = run.Store(CompanyA))
        {
            await owner.Store.RecordStockCountAsync(request, recorder, static _ => Task.CompletedTask);
            original = await owner.Store.DecideStockCountAsync(request.CountId, decision, decider, static _ => Task.CompletedTask);
        }
        Assert.Equal("NotCalculated", original.Count.Decision!.CostStatus);
        Assert.Equal(12m, await LabelKgAsync(run, label.Id));
        await ApproveCountOpeningCostAsync(run, Product, "8.5");
        await using (var owner = run.Store(CompanyA))
        {
            var replay = await owner.Store.DecideStockCountAsync(request.CountId, decision, decider, static _ => Task.CompletedTask);
            var anotherKey = await owner.Store.DecideStockCountAsync(request.CountId, decision,
                decider with { IdempotencyKey = Guid.NewGuid().ToString("N") }, static _ => Task.CompletedTask);
            Assert.Equal(original.Count, replay.Count);
            Assert.Equal(original.Count, anotherKey.Count);
            Assert.Equal("NotCalculated", Assert.Single((await owner.Store.ReadStockCountsAsync(label.Id))!.Items).Decision!.CostStatus);
        }
        var json = JsonSerializer.Serialize(original.Count, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("amount", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("averageCost", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await CountAdjustmentsAsync(run));
    }

    [SqlServerFact]
    public async Task Count_unreadable_TL_shortage_writes_no_effect_and_the_same_key_succeeds_after_actual_opening_cost_approval()
    {
        await using var run = await Run.StartAsync();
        var label = await CountCostReceiptAsync(run, 10m, null);
        var request = Count(label.Id, await LabelVersionAsync(run, label.Id), "8");
        var recorder = Metadata(run);
        var decider = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
        var decision = new DecideInventoryStockCountV1(InventoryStockCountDecisionKindV1.Approve, "SENTETIK sayim eksigi");
        await using (var owner = run.Store(CompanyA)) await owner.Store.RecordStockCountAsync(request, recorder, static _ => Task.CompletedTask);
        var before = await CountCostEffectsAsync(run, label.Id);
        await using (var owner = run.Store(CompanyA))
        {
            var refusal = await Assert.ThrowsAsync<InventoryStockCountException>(() => owner.Store.DecideStockCountAsync(
                request.CountId, decision, decider, static _ => Task.CompletedTask));
            Assert.Equal((503, InventoryStockCountCodesV1.CostNotCalculated), (refusal.Status, refusal.Code));
        }
        Assert.Equal(before, await CountCostEffectsAsync(run, label.Id));
        await ApproveCountOpeningCostAsync(run, Product, "8.5");
        await using (var owner = run.Store(CompanyA))
        {
            var accepted = await owner.Store.DecideStockCountAsync(request.CountId, decision, decider, static _ => Task.CompletedTask);
            Assert.False(accepted.IsReplay);
            Assert.Equal("Known", accepted.Count.Decision!.CostStatus);
            await AssertCountCostPoolAsync(run, 8m, 8.5m, accepted.Count.Decision.AdjustmentMovementId!.Value, 17m);
        }
    }

    [SqlServerFact]
    public async Task Count_cost_and_physical_effects_roll_back_together_when_the_final_authority_fence_is_lost()
    {
        await using var run = await Run.StartAsync();
        var label = await CountCostReceiptAsync(run, 10m, 12m);
        var request = Count(label.Id, await LabelVersionAsync(run, label.Id), "8");
        await using (var owner = run.Store(CompanyA))
            await owner.Store.RecordStockCountAsync(request, Metadata(run), static _ => Task.CompletedTask);
        var before = await CountCostEffectsAsync(run, label.Id);
        var calls = 0;
        await using (var owner = run.Store(CompanyA))
        {
            var failure = await Assert.ThrowsAsync<InventoryStockCountException>(() => owner.Store.DecideStockCountAsync(request.CountId,
                new(InventoryStockCountDecisionKindV1.Approve, "SENTETIK final fence"),
                Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") }, _ =>
                {
                    if (++calls == 6) throw new InventoryStockCountException(403, InventoryStockCountCodesV1.Forbidden);
                    return Task.CompletedTask;
                }));
            Assert.Equal(403, failure.Status);
        }
        Assert.True(calls >= 6);
        Assert.Equal(before, await CountCostEffectsAsync(run, label.Id));
    }

    private static async Task<MaterialLabel> CountCostReceiptAsync(Run run, decimal kg, decimal? tl)
    {
        var label = NewLabel(CompanyA, $"SYN-COUNT-COST-{Guid.NewGuid():N}");
        var location = await run.LocationOfAsync(CompanyA);
        var receipt = Movement(CompanyA, StockMovementKind.Receipt,
            new StockSourceReference(InventoryGoodsReceiptContractV1.SourceKind, Guid.NewGuid()),
            In(Position(CompanyA, location, label, BlockState.Free), kg));
        await using var owner = run.Store(CompanyA);
        await owner.Labels.SaveLabelAsync(label, Metadata(run));
        owner.Store.UseReceiptCost(receipt.Id, new(CompanyA, receipt.Source.Id, Guid.NewGuid(), Guid.NewGuid(), null, null,
            tl, null, new(InventoryIssueCostCurrencyStatusV1.FunctionalCurrency, null, null, null, "SENTETIK-count-cost", null),
            "SENTETIK-receipt-cost", "{}", tl is null ? "SENTETIK-cost-missing" : null, "SENTETIK-USD-missing"));
        await owner.Store.RecordMovementAsync(receipt, Metadata(run));
        return label;
    }

    private static async Task<InventoryStockCountResultV1> ApproveCountCostAsync(Run run, Guid labelId, string counted)
    {
        var request = Count(labelId, await LabelVersionAsync(run, labelId), counted);
        await using var owner = run.Store(CompanyA);
        await owner.Store.RecordStockCountAsync(request, Metadata(run), static _ => Task.CompletedTask);
        return await owner.Store.DecideStockCountAsync(request.CountId,
            new(InventoryStockCountDecisionKindV1.Approve, "SENTETIK actual count cost"),
            Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") }, static _ => Task.CompletedTask);
    }

    private static async Task ApproveCountOpeningCostAsync(Run run, Guid productId, string unitCost)
    {
        var request = new SubmitInventoryOpeningCostV1(Guid.NewGuid(), await run.LocationOfAsync(CompanyA), productId,
            unitCost, "SENTETIK cost prerequisite");
        await using var owner = run.Store(CompanyA);
        await owner.Store.SubmitOpeningCostAsync(request, Metadata(run), static _ => Task.CompletedTask);
        await owner.Store.DecideOpeningCostAsync(request.OpeningCostId,
            new(InventoryOpeningCostDecisionKindV1.Approve, "SENTETIK different checker"),
            Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") }, static _ => Task.CompletedTask);
    }

    private static async Task AssertCountCostPoolAsync(Run run, decimal kg, decimal average, Guid movementId, decimal amount)
    {
        await using var db = run.Context(CompanyA);
        var pool = await db.AverageCostLedger.SingleAsync();
        Assert.Equal((kg, average), (pool.QuantityKg, pool.AverageCostPerKg));
        var stamp = await db.MovementCostStamps.SingleAsync(row => row.MovementId == movementId
            && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.Equal(((int)InventoryMovementCostStampStatusV1.Known, (decimal?)average, (decimal?)amount),
            (stamp.Status, stamp.UnitCostPerKg, stamp.Amount));
    }

    private static async Task<string> CountCostEffectsAsync(Run run, Guid labelId)
    {
        await using var db = run.Context(CompanyA);
        return string.Join("/", await LabelKgAsync(run, labelId), await LabelVersionAsync(run, labelId),
            await db.Movements.CountAsync(), await db.Commands.CountAsync(), await db.StockCountDecisions.CountAsync(),
            await db.MovementCostStamps.CountAsync(), await db.PendingMovementCosts.CountAsync(),
            await db.AverageCostLedger.CountAsync(), await db.ExactCostPools.CountAsync(),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
    }
}

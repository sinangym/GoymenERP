using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Actual SQL movement/average/stamp/history/audit transactions. Receipt price proof alone is explicitly synthetic. NOT_RUN.</summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Forward_cost_and_cursor_Down_guards_refuse_their_own_real_rows_without_removing_them()
    {
        await using var run = await Run.StartAsync();
        var operations = new AddInventoryOutboxCursorAndExactCostRatios().DownOperations;
        var cursorGuard = Assert.IsType<SqlOperation>(operations[0]).Sql;
        var ratioGuard = Assert.IsType<SqlOperation>(operations[1]).Sql;
        await run.ExecuteAsync(cursorGuard); await run.ExecuteAsync(ratioGuard);
        await CostedLabelAsync(run, await run.LocationOfAsync(CompanyA), 1m, 10m, 2m);
        Assert.Equal(51964, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(ratioGuard))).Number);
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString); await connection.OpenAsync();
        await InsertOutboxSourceEventAsync(connection, null, Guid.NewGuid(), CompanyA, InventoryFutureAllocationReleasedOutboxV1.EventType);
        Assert.Equal(51958, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(cursorGuard))).Number);
        await using var context = run.Context(CompanyA);
        Assert.Equal(2, await context.MovementCostStamps.CountAsync());
        Assert.True(await context.OutboxMessages.AnyAsync());
    }

    [SqlServerFact]
    public async Task Recognized_generation_rounds_the_shipped_share_once_and_preserves_the_label_residual_in_stock()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var parent = await CostedLabelAsync(run, location, 3m, 10m, 2m);
        var orderId = Guid.NewGuid();
        var issued = await CommitIssueAsync(run, IssueRequest(orderId, parent.Id, "2", "3"), ManufacturingMetadata(run, "split-issue"));
        var output = OutputRequest(orderId, parent.Id, issued.ParentLabelVersion, location, "3");
        await CommitOutputAsync(run, output, ManufacturingMetadata(run, "split-output"));
        GoymenERP.Modules.Inventory.Domain.Labels.MaterialLabel produced;
        await using (var owner = run.Store(CompanyA)) produced = (await owner.Labels.FindLabelAsync(output.LabelId))!;
        var shipped = Movement(CompanyA, StockMovementKind.Issue,
            new StockSourceReference(InventoryLabelDispositionReader.DefiniteShipmentSourceKind, Guid.NewGuid(), Guid.NewGuid()),
            Out(Position(CompanyA, location, produced, BlockState.Free), 1m));
        await using (var owner = run.Store(CompanyA)) await owner.Store.RecordMovementAsync(shipped, Metadata(run));
        var command = new InventoryManufacturingCostGenerationCommandV1(CompanyA, orderId, Guid.NewGuid(), "0", "8",
            InventoryManufacturingCostGenerationStateV1.Recognized, "SENTETIK-user-approved-consumed-rounding", "SENTETIK-calculator-v1", "{}",
            [new(output.OutputId, output.MovementId, output.LabelId, 3m, 33.3367m, null, 100.01m)],
            "SENTETIK recorded output amount", ManufacturingAt.AddHours(1));
        var result = await StageCostGenerationAsync(run, command, ManufacturingMetadata(run, "split-recognize"), true);
        Assert.Equal((100.01m, 33.34m, 66.67m),
            (result.Outputs[0].OriginalTlAmount, result.Outputs[0].ConsumedTlDifference, result.Outputs[0].RemainingTlValue));
        await using var context = run.Context(CompanyA);
        var pool = await context.AverageCostLedger.SingleAsync();
        Assert.Equal((2m, 33.335m), (pool.QuantityKg, pool.AverageCostPerKg));
        var envelope = await context.OutboxMessages.SingleAsync(row => row.Id == result.CostDifferenceEventId);
        var payload = System.Text.Json.JsonSerializer.Deserialize<InventoryManufacturingCostGenerationResultV1>(envelope.PayloadJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal((envelope.Id, 33.34m), (payload.CostDifferenceEventId, payload.Outputs[0].ConsumedTlDifference));
        Assert.Equal(1, (await context.Movements.SingleAsync(row => row.Id == shipped.Id)).CostStatus);
        Assert.Equal((int)InventoryMovementCostStampStatusV1.Unavailable, (await context.MovementCostStamps.SingleAsync(row => row.MovementId == shipped.Id
            && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira)).Status); // No historical issue backfill.
    }

    [SqlServerFact]
    public async Task A_one_third_receipt_preserves_source_ratio_final_average_and_original_actual_pool_reversal()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, $"SYN-RATIO-{Guid.NewGuid():N}");
        var receipt = Movement(CompanyA, StockMovementKind.Receipt,
            new StockSourceReference(InventoryGoodsReceiptContractV1.SourceKind, Guid.NewGuid()),
            In(Position(CompanyA, location, label, BlockState.Free), 3m));
        await using (var owner = run.Store(CompanyA))
        {
            await owner.Labels.SaveLabelAsync(label, Metadata(run));
            owner.Store.UseReceiptCost(receipt.Id, new(CompanyA, receipt.Source.Id, Guid.NewGuid(), Guid.NewGuid(), null, null, null, null,
                new(InventoryIssueCostCurrencyStatusV1.FunctionalCurrency, null, null, null, "SENTETIK-ratio-price", null),
                "SENTETIK-owner-proof", "{}", null, null, TlUnitCostRatio: new("1", "3"), UsdUnitCostRatio: new("1", "6")));
            await owner.Store.RecordMovementAsync(receipt, Metadata(run));
        }
        await using (var context = run.Context(CompanyA))
        {
            var stamp = await context.MovementCostStamps.SingleAsync(row => row.MovementId == receipt.Id
                && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
            Assert.Equal((int)InventoryMovementCostStampStatusV1.Known, stamp.Status);
            Assert.Null(stamp.UnitCostPerKg); Assert.Equal(1m, stamp.Amount);
            Assert.Contains("\"denominator\":\"3\"", stamp.UnitCostRatioJson!, StringComparison.Ordinal);
            Assert.Equal(0.3333m, (await context.AverageCostLedger.SingleAsync()).AverageCostPerKg);
        }
        var undo = Movement(CompanyA, StockMovementKind.PurchaseAcceptanceReversal,
            new StockSourceReference(InventoryGoodsReceiptReversalCodesV1.SourceKind, receipt.Id, Guid.NewGuid()),
            Out(Position(CompanyA, location, label, BlockState.Free), 3m));
        await using (var owner = run.Store(CompanyA)) await owner.Store.RecordMovementAsync(undo, Metadata(run));
        await using var final = run.Context(CompanyA);
        Assert.Equal((0m, 0.3333m), await final.AverageCostLedger.Select(row => new ValueTuple<decimal?, decimal?>(row.QuantityKg, row.AverageCostPerKg)).SingleAsync());
        var reversed = await final.MovementCostStamps.SingleAsync(row => row.MovementId == undo.Id
            && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.Null(reversed.UnitCostPerKg); Assert.Equal(1m, reversed.Amount);
        using var effect = System.Text.Json.JsonDocument.Parse(reversed.PoolEffectJson!);
        Assert.Equal("-9999", effect.RootElement.GetProperty("recordedValue").GetProperty("numerator").GetString());
        Assert.Equal("10000", effect.RootElement.GetProperty("recordedValue").GetProperty("denominator").GetString());
    }

    [SqlServerFact]
    public async Task Customer_return_uses_its_original_issue_stamp_after_a_later_receipt_changes_the_average()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var first = await CostedLabelAsync(run, location, 100m, 10m, 2m);
        var issue = Movement(CompanyA, StockMovementKind.Issue, Out(Position(CompanyA, location, first, BlockState.Free), 20m));
        await using (var owner = run.Store(CompanyA)) await owner.Store.RecordMovementAsync(issue, Metadata(run));
        await CostedLabelAsync(run, location, 80m, 20m, 4m);
        var returned = Movement(CompanyA, StockMovementKind.Return,
            new StockSourceReference(InventoryCustomerReturnContractV1.SourceKind, issue.Id, Guid.NewGuid()),
            In(Position(CompanyA, location, first, BlockState.Free), 20m));
        await using (var owner = run.Store(CompanyA)) await owner.Store.RecordMovementAsync(returned, Metadata(run));
        await using var context = run.Context(CompanyA);
        var stamp = await context.MovementCostStamps.SingleAsync(row => row.MovementId == returned.Id
            && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.Equal((10m, 200m, issue.Id), (stamp.UnitCostPerKg, stamp.Amount, stamp.SourceIssueMovementId));
        var pool = await context.AverageCostLedger.SingleAsync();
        Assert.Equal((180m, 14.4444m), (pool.QuantityKg, pool.AverageCostPerKg));
    }

    [SqlServerFact]
    public async Task Supplier_return_keeps_the_admitted_invoice_cost_instead_of_the_receipt_price_or_current_average()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var first = await CostedLabelAsync(run, location, 20m, 11m, 2m);
        await CostedLabelAsync(run, location, 80m, 12.25m, 2m); // Actual TL pool100kg @12.
        Guid original;
        await using (var context = run.Context(CompanyA)) original = await context.MovementLines.Where(row => row.LabelId == first.Id).Select(row => row.MovementId).SingleAsync();
        var returned = Movement(CompanyA, StockMovementKind.SupplierReturnExit,
            new StockSourceReference(InventorySupplierReturnExitContractV1.SourceKind, original, Guid.NewGuid()),
            Out(Position(CompanyA, location, first, BlockState.Free), 20m));
        await using (var owner = run.Store(CompanyA))
        {
            owner.Store.UseSupplierReturnCost(returned.Id, 10m); // SENTETIK admitted invoice proof; physical/pool writes are real SQL.
            await owner.Store.RecordMovementAsync(returned, Metadata(run));
        }
        await using var final = run.Context(CompanyA);
        var pool = await final.AverageCostLedger.SingleAsync();
        Assert.Equal((80m, 12.5m), (pool.QuantityKg, pool.AverageCostPerKg));
        Assert.Equal(10m, (await final.MovementCostStamps.SingleAsync(row => row.MovementId == returned.Id
            && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira)).UnitCostPerKg);
    }
    [SqlServerFact]
    public async Task A_three_kg_generation_keeps_the_original_1_01_amount_the_4dp_pool_effect_and_its_exact_reversal()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var parent = await CostedLabelAsync(run, location, 3m, 10m, 2m);
        var orderId = Guid.NewGuid();
        var issued = await CommitIssueAsync(run, IssueRequest(orderId, parent.Id, "2", "3"), ManufacturingMetadata(run, "round-issue"));
        var output = OutputRequest(orderId, parent.Id, issued.ParentLabelVersion, location, "3");
        await CommitOutputAsync(run, output, ManufacturingMetadata(run, "round-output"));
        var command = new InventoryManufacturingCostGenerationCommandV1(CompanyA, orderId, Guid.NewGuid(), "0", "8",
            InventoryManufacturingCostGenerationStateV1.Recognized, "SENTETIK-rounding-1-01", "SENTETIK-calculator-v1", "{}",
            [new(output.OutputId, output.MovementId, output.LabelId, 3m, 0.3367m, null, 1.01m)], "SENTETIK actual recorded amount", ManufacturingAt.AddHours(1));
        var recognized = await StageCostGenerationAsync(run, command, ManufacturingMetadata(run, "round-recognize"), true);
        var line = Assert.Single(recognized.Outputs);
        var effect = Assert.Single(line.PoolEffects!);
        Assert.Equal((1.01m, 1.0101m, 0.0001m, 0.3367m),
            (line.OriginalTlAmount, effect.RecordedPoolValueEffect, effect.AverageRoundingDifference, effect.AfterAverageCostPerKg));
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString); await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var scope = run.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        await using var owner = await InventoryManufacturingCostGenerationParticipant.EnlistAsync(connection, transaction, CompanyA,
            scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        var reversed = await owner.ReverseAsync(new(CompanyA, orderId, command.GenerationId, recognized.GenerationVersion, "8",
            "SENTETIK reopen", ManufacturingAt.AddHours(2)), ManufacturingMetadata(run, "round-reverse"));
        await transaction.CommitAsync();
        Assert.Equal((-1.01m, -1.0101m, -0.0001m), (reversed.Outputs[0].RemainingTlValue,
            reversed.Outputs[0].PoolEffects![0].RecordedPoolValueEffect, reversed.Outputs[0].PoolEffects![0].AverageRoundingDifference));
        await using var context = run.Context(CompanyA);
        Assert.Equal(0m, (await context.AverageCostLedger.SingleAsync(row => row.ProductId == Product)).QuantityKg);
        Assert.Equal(2, await context.ManufacturingCostGenerationHistory.CountAsync());
        Assert.Equal(1, (await context.Movements.SingleAsync(row => row.Id == output.MovementId)).CostStatus);
    }

    [SqlServerFact]
    public async Task Movement_cost_Down_passes_when_empty_and_refuses_after_a_real_stamp_without_removing_it()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddInventoryMovementCostProvenance().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        await CostedLabelAsync(run, await run.LocationOfAsync(CompanyA), 1m, 10m, 2m);
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51959, refused.Number);
        await using var context = run.Context(CompanyA);
        Assert.Equal(2, await context.MovementCostStamps.CountAsync());
    }
    [SqlServerFact]
    public async Task Priced_receipts_and_a_two_line_issue_keep_separate_TL_USD_original_stamps_and_reconcile_the_single_pool()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var first = await CostedLabelAsync(run, location, 100m, 10m, 2m);
        var second = await CostedLabelAsync(run, location, 100m, 20m, 4m);
        var issue = Movement(CompanyA, StockMovementKind.Issue,
            Out(Position(CompanyA, location, first, BlockState.Free), 30m),
            Out(Position(CompanyA, location, second, BlockState.Free), 20m));
        await using (var owner = run.Store(CompanyA)) await owner.Store.RecordMovementAsync(issue, Metadata(run));
        await using var context = run.Context(CompanyA);
        var stamps = await context.MovementCostStamps.Where(row => row.MovementId == issue.Id).OrderBy(row => row.LineNumber).ToListAsync();
        Assert.Equal(4, stamps.Count);
        Assert.All(stamps, stamp => Assert.Equal((int)InventoryMovementCostStampStatusV1.Known, stamp.Status));
        Assert.All(stamps.Where(row => row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira), row => Assert.Equal(15m, row.UnitCostPerKg));
        Assert.All(stamps.Where(row => row.CurrencyId == ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD")), row => Assert.Equal(3m, row.UnitCostPerKg));
        Assert.Equal(150m, (await context.AverageCostLedger.SingleAsync(row => row.LocationId == location && row.ProductId == Product)).QuantityKg);
        Assert.Equal(150m, (await context.CurrencyAverageCostLedger.SingleAsync(row => row.LocationId == location && row.ProductId == Product)).QuantityKg);
        Assert.Equal(150m, await context.Balances.Where(row => row.LocationId == location && row.ProductId == Product).SumAsync(row => row.QuantityKg));
        Assert.Equal(1, (await context.Movements.SingleAsync(row => row.Id == issue.Id)).CostStatus); // Original NotCalculated payload stays immutable.
    }

    [SqlServerFact]
    public async Task Pending_generation_does_not_require_a_cost_and_recognition_rollback_and_replay_keep_immutable_outputs()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var parent = await CostedLabelAsync(run, location, 200m, 10m, 2m);
        var orderId = Guid.NewGuid();
        var issued = await CommitIssueAsync(run, IssueRequest(orderId, parent.Id, "2", "100"), ManufacturingMetadata(run, "cost-issue"));
        var output = OutputRequest(orderId, parent.Id, issued.ParentLabelVersion, location, "100");
        await CommitOutputAsync(run, output, ManufacturingMetadata(run, "cost-output"));
        var generation = new InventoryManufacturingCostGenerationCommandV1(CompanyA, orderId, Guid.NewGuid(), "0", "8",
            InventoryManufacturingCostGenerationStateV1.Pending, "SENTETIK-pending-v1", "SENTETIK-calculator-v1", "{}",
            [new(output.OutputId, output.MovementId, output.LabelId, 100m, null, null)], "SENTETIK pending source", ManufacturingAt.AddHours(1));
        var pending = await StageCostGenerationAsync(run, generation, ManufacturingMetadata(run, "cost-pending"), true);
        Assert.Equal(("1", InventoryManufacturingCostGenerationStateV1.Pending), (pending.GenerationVersion, pending.State));
        Assert.Null(Assert.Single(pending.Outputs).OriginalTlAmount);
        Assert.Empty(Assert.Single(pending.Outputs).Positions);
        var recognized = generation with { ExpectedGenerationVersion = "1", State = InventoryManufacturingCostGenerationStateV1.Recognized,
            InputFingerprint = "SENTETIK-readable-v2", Outputs = [new(output.OutputId, output.MovementId, output.LabelId, 100m, 20m, 4m, 2000m)] };
        var metadata = ManufacturingMetadata(run, "cost-recognize");
        await StageCostGenerationAsync(run, recognized, metadata, false);
        await using (var context = run.Context(CompanyA))
        {
            Assert.Equal(1, (await context.ManufacturingCostGenerations.SingleAsync()).Version);
            Assert.Empty(await context.MovementCostRecognitions.ToListAsync());
            Assert.Equal(100m, (await context.AverageCostLedger.SingleAsync(row => row.ProductId == Product)).QuantityKg);
        }
        var result = await StageCostGenerationAsync(run, recognized, metadata, true);
        Assert.Equal((2000m, 0m, 2000m), (result.Outputs[0].RemainingTlValue, result.Outputs[0].ConsumedTlDifference, result.Outputs[0].OriginalTlAmount));
        var replay = await StageCostGenerationAsync(run, recognized, metadata with { CorrelationId = "SENTETIK-recognition-replay" }, true);
        Assert.True(replay.IsReplay); Assert.Equal(metadata.CorrelationId, replay.OriginalCorrelationId);
        await using var final = run.Context(CompanyA);
        Assert.Equal(2, await final.ManufacturingCostGenerationHistory.CountAsync());
        Assert.Equal(2, await final.MovementCostRecognitions.CountAsync());
        Assert.Equal(100m, (await final.MovementLines.SingleAsync(row => row.MovementId == output.MovementId)).QuantityKg);
        Assert.Equal(1, (await final.Movements.SingleAsync(row => row.Id == output.MovementId)).CostStatus);
    }

    private static async Task<GoymenERP.Modules.Inventory.Domain.Labels.MaterialLabel> CostedLabelAsync(Run run, Guid location,
        decimal quantity, decimal tl, decimal usd)
    {
        var label = NewLabel(CompanyA, $"SYN-COST-{Guid.NewGuid():N}");
        await using var owner = run.Store(CompanyA);
        await owner.Labels.SaveLabelAsync(label, Metadata(run));
        var acceptance = Guid.NewGuid();
        var movement = Movement(CompanyA, StockMovementKind.Receipt, new StockSourceReference(InventoryGoodsReceiptContractV1.SourceKind, acceptance),
            In(Position(CompanyA, location, label, BlockState.Free), quantity));
        owner.Store.UseReceiptCost(movement.Id, new(CompanyA, acceptance, Guid.NewGuid(), Guid.NewGuid(), null, null, tl, usd,
            new(InventoryIssueCostCurrencyStatusV1.FunctionalCurrency, null, null, null, "SENTETIK-entry-v1", null),
            "SENTETIK-cost-v1", "{}", null, null));
        await owner.Store.RecordMovementAsync(movement, Metadata(run));
        return label;
    }

    private static async Task<InventoryManufacturingCostGenerationResultV1> StageCostGenerationAsync(Run run,
        InventoryManufacturingCostGenerationCommandV1 command, InventoryCommandMetadata metadata, bool commit)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString); await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var scope = run.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        await using var owner = await InventoryManufacturingCostGenerationParticipant.EnlistAsync(connection, transaction, CompanyA,
            scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        var result = await owner.StageAsync(command, metadata);
        if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
        return result;
    }
}

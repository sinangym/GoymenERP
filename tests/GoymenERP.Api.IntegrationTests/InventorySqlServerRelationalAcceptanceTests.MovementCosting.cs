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

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Actual SQL movement/average/stamp/history/audit transactions. Receipt price proof alone is explicitly synthetic. NOT_RUN.</summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
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

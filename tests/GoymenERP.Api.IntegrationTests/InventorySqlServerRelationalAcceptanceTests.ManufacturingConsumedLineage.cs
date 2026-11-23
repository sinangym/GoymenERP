using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Real Inventory output/merge/definite-shipment/return/generation and SQL owner history. The existing fixture's
/// Sales demand and Manufacturing calculator amount are explicitly synthetic. No invoice or Accounting attribution
/// is claimed. Intentional legacy/corruption injections and company-switch faults are marked below. NOT_RUN.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Generation_lineage_joins_the_actual_transaction_and_outbox_without_replacing_the_unavailable_original_exit_stamp()
    {
        await using var run = await Run.StartAsync(); var fixture = await ArrangeExactLineageCostingAsync(run);
        var originalExit = await ExactLineageStampAsync(run, fixture.ShipmentMovementId);
        var metadata = ManufacturingMetadata(run, "consumed-lineage-capture");
        await StageCostGenerationAsync(run, fixture.Command, metadata, commit: false);
        await using var context = run.Context(CompanyA);
        var reader = ConsumedLineageReader(context, CompanyA);
        Assert.Equal(InventoryManufacturingConsumedCostLineageOutcomeV1.NotFound,
            (await reader.ReadAsync(CompanyA, fixture.Command.GenerationId, "1")).Outcome);
        var result = await StageCostGenerationAsync(run, fixture.Command, metadata, commit: true);
        var answer = await reader.ReadAsync(CompanyA, result.GenerationId, result.GenerationVersion);
        Assert.Equal(InventoryManufacturingConsumedCostLineageOutcomeV1.Available, answer.Outcome);
        Assert.Equal(result.CostDifferenceEventId, answer.Snapshot!.CostDifferenceEventId);
        var output = Assert.Single(answer.Snapshot.Outputs);
        Assert.Equal((fixture.Output.OutputId, fixture.Output.MovementId, fixture.Output.LabelId),
            (output.OutputId, output.ReceiptMovementId, output.RootLabelId));
        var shipment = Assert.Single(output.Lineage.Shipments);
        Assert.Equal((fixture.ShipmentMovementId, 1, fixture.MixedLabelId, Product, 1m),
            (shipment.MovementId, shipment.MovementLineNumber, shipment.LabelId, shipment.ProductId, shipment.QuantityKg));
        Assert.Equal(new("1", "3"), shipment.RootShare);
        Assert.Equal(new("1", "3"), shipment.NetRootQuantityKg);
        var actual = await context.Movements.AsNoTracking().SingleAsync(row => row.Id == fixture.ShipmentMovementId);
        Assert.Equal((actual.SourceId, actual.SourceLineId!.Value, actual.RecordedAtUtc),
            (shipment.ShipmentId, shipment.LoadingInstructionLineId, shipment.RecordedAtUtc));
        Assert.Null(shipment.CreationSequence); // A later-added rowversion cursor is not an original chronology proof.
        var history = await context.ManufacturingCostGenerationHistory.AsNoTracking().SingleAsync();
        var envelope = await context.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == result.CostDifferenceEventId);
        var wire = JsonSerializer.Deserialize<InventoryManufacturingCostGenerationResultV1>(envelope.PayloadJson, ExactLineageJson)!;
        Assert.Null(Assert.Single(wire.Outputs).ConsumedCostLineage);
        Assert.Equal(InventoryManufacturingConsumedCostLineageEvidenceV1.Sha256(history.ResultJson), wire.ConsumedLineageReference!.HistorySha256);
        Assert.Equal((result.GenerationId, result.GenerationVersion), (wire.ConsumedLineageReference.GenerationId, wire.ConsumedLineageReference.GenerationVersion));
        var replay = await StageCostGenerationAsync(run, fixture.Command,
            metadata with { CorrelationId = "SENTETIK-lineage-replay" }, commit: true);
        Assert.True(replay.IsReplay);
        Assert.Equal(answer.SourceVersion, (await reader.ReadAsync(CompanyA, result.GenerationId, "1")).SourceVersion);
        Assert.Equal(originalExit, await ExactLineageStampAsync(run, fixture.ShipmentMovementId));
        Assert.Equal(1, await context.ManufacturingCostGenerationHistory.CountAsync());
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [SqlServerFact]
    public async Task A_later_actual_customer_return_does_not_backfill_or_rewrite_the_saved_generation_lineage()
    {
        await using var run = await Run.StartAsync(); var fixture = await ArrangeExactLineageCostingAsync(run);
        var result = await StageCostGenerationAsync(run, fixture.Command, ManufacturingMetadata(run, "lineage-before-return"), true);
        await using var context = run.Context(CompanyA);
        var reader = ConsumedLineageReader(context, CompanyA);
        var original = await reader.ReadAsync(CompanyA, result.GenerationId, "1");
        var movement = await context.Movements.AsNoTracking().SingleAsync(row => row.Id == fixture.ShipmentMovementId);
        await CommitReturnAsync(run, new InventoryCustomerReturnRequestV1(CompanyA, Guid.NewGuid(), movement.SourceId, movement.SourceLineId!.Value,
            Guid.NewGuid(), "1", ManufacturingAt.AddMinutes(6)), Metadata(run));
        var live = await InventoryCostLineageReader.ReadAsync(context, CompanyA, Product, fixture.Output.LabelId);
        ExactLineageRatio(live.NetShippedKg, "0", "1");
        var frozen = await reader.ReadAsync(CompanyA, result.GenerationId, "1");
        Assert.Equal(original.SourceVersion, frozen.SourceVersion);
        var shipment = Assert.Single(Assert.Single(frozen.Snapshot!.Outputs).Lineage.Shipments);
        Assert.Equal(new("1", "3"), shipment.NetRootQuantityKg);
        Assert.Empty(shipment.Returns);
    }

    [SqlServerFact]
    public async Task A_return_before_capture_keeps_the_actual_return_identity_and_zero_net_share_without_erasing_the_shipment()
    {
        await using var run = await Run.StartAsync(); var fixture = await ArrangeExactLineageCostingAsync(run);
        await using var context = run.Context(CompanyA);
        var movement = await context.Movements.AsNoTracking().SingleAsync(row => row.Id == fixture.ShipmentMovementId);
        var request = new InventoryCustomerReturnRequestV1(CompanyA, Guid.NewGuid(), movement.SourceId,
            movement.SourceLineId!.Value, Guid.NewGuid(), "1", ManufacturingAt.AddMinutes(6));
        var returned = await CommitReturnAsync(run, request, Metadata(run));
        var result = await StageCostGenerationAsync(run, fixture.Command, ManufacturingMetadata(run, "lineage-after-return"), true);
        var read = await ConsumedLineageReader(context, CompanyA).ReadAsync(CompanyA, result.GenerationId, "1");
        Assert.Equal(InventoryManufacturingConsumedCostLineageOutcomeV1.Available, read.Outcome);
        var shipment = Assert.Single(Assert.Single(read.Snapshot!.Outputs).Lineage.Shipments);
        Assert.Equal(fixture.ShipmentMovementId, shipment.MovementId);
        Assert.Equal(new("0", "1"), shipment.NetRootQuantityKg);
        var actualReturn = Assert.Single(shipment.Returns);
        var returnMovement = await context.Movements.AsNoTracking().SingleAsync(row => row.Id == returned.InventoryMovementId);
        Assert.Equal((returnMovement.SourceLineId!.Value, returnMovement.Id, fixture.ShipmentMovementId, 1m, returnMovement.RecordedAtUtc),
            (actualReturn.ReturnId, actualReturn.MovementId, actualReturn.SourceIssueMovementId, actualReturn.QuantityKg, actualReturn.RecordedAtUtc));
    }

    [SqlServerFact]
    public async Task Corrupt_or_legacy_generation_history_never_uses_the_current_material_graph_as_a_replacement()
    {
        await using var run = await Run.StartAsync(); var fixture = await ArrangeExactLineageCostingAsync(run);
        var result = await StageCostGenerationAsync(run, fixture.Command, ManufacturingMetadata(run, "lineage-corruption-fixture"), true);
        var company = new CompanyContext(); company.Set(CompanyA);
        await using var context = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).AddInterceptors(new NoCurrentLineageFallback()).Options, company);
        var reader = new InventoryManufacturingConsumedCostLineageReadPort(context, company);
        // Isolated corruption fault, not an allowed runtime mutation of immutable history.
        await run.ExecuteAsync("UPDATE inventory.ManufacturingCostGenerationHistory SET ResultJson=JSON_MODIFY(ResultJson,'$.outputs[0].consumedCostLineage.evidenceSha256','changed') WHERE GenerationId=@id",
            ("@id", result.GenerationId));
        var corrupt = await reader.ReadAsync(CompanyA, result.GenerationId, "1");
        Assert.Equal(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable, corrupt.Outcome);
        Assert.Null(corrupt.Snapshot);
        // Explicit legacy JSON fixture: old history has no captured movement attribution.
        await run.ExecuteAsync("UPDATE inventory.ManufacturingCostGenerationHistory SET ResultJson=JSON_MODIFY(ResultJson,'$.outputs[0].consumedCostLineage',NULL) WHERE GenerationId=@id",
            ("@id", result.GenerationId));
        var legacy = await reader.ReadAsync(CompanyA, result.GenerationId, "1");
        Assert.Equal(InventoryManufacturingConsumedCostLineageOutcomeV1.UnreadableLegacy, legacy.Outcome);
        Assert.Null(legacy.Snapshot);
    }

    [SqlServerFact]
    public async Task Saved_generation_lineage_is_company_fenced_and_discards_a_real_SQL_result_after_company_loss()
    {
        await using var run = await Run.StartAsync(); var fixture = await ArrangeExactLineageCostingAsync(run);
        var result = await StageCostGenerationAsync(run, fixture.Command, ManufacturingMetadata(run, "lineage-company-fixture"), true);
        await using (var other = run.Context(CompanyB))
        {
            var foreign = await ConsumedLineageReader(other, CompanyB).ReadAsync(CompanyB, result.GenerationId, "1");
            Assert.Equal(InventoryManufacturingConsumedCostLineageOutcomeV1.NotFound, foreign.Outcome);
            Assert.Null(foreign.Snapshot);
        }
        var company = new ShipmentCostMutableCompany(); company.Set(CompanyA);
        var fault = new ConsumedLineageCompanySwitch(() => company.Set(CompanyB));
        await using var context = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).AddInterceptors(fault).Options, company);
        var answer = await new InventoryManufacturingConsumedCostLineageReadPort(context, company).ReadAsync(CompanyA, result.GenerationId, "1");
        Assert.True(fault.Fired); // Synthetic authority-context fault after the actual SQL reader is returned.
        Assert.Equal(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable, answer.Outcome);
        Assert.Null(answer.Snapshot);
    }

    [SqlServerFact]
    public async Task A_changed_generation_outbox_link_is_unreadable_and_the_existing_outbox_cannot_be_updated_by_its_runtime_context()
    {
        await using var run = await Run.StartAsync(); var fixture = await ArrangeExactLineageCostingAsync(run);
        var result = await StageCostGenerationAsync(run, fixture.Command, ManufacturingMetadata(run, "lineage-event-fixture"), true);
        await using (var writer = run.Context(CompanyA))
        {
            var row = await writer.OutboxMessages.SingleAsync(value => value.Id == result.CostDifferenceEventId);
            row.RecordedAtUtc = row.RecordedAtUtc.AddTicks(1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveChangesAsync());
        }
        // Isolated corrupted-envelope fixture; application writers have no update permission or state exception.
        await run.ExecuteAsync("UPDATE inventory.OutboxMessages SET PayloadJson=JSON_MODIFY(PayloadJson,'$.generationVersion','99') WHERE Id=@id",
            ("@id", result.CostDifferenceEventId!.Value));
        await using var context = run.Context(CompanyA);
        var answer = await ConsumedLineageReader(context, CompanyA).ReadAsync(CompanyA, result.GenerationId, "1");
        Assert.Equal(InventoryManufacturingConsumedCostLineageOutcomeV1.Unreadable, answer.Outcome);
        Assert.Null(answer.Snapshot);
    }

    private static InventoryManufacturingConsumedCostLineageReadPort ConsumedLineageReader(InventoryDbContext context, Guid companyId)
    { var company = new CompanyContext(); company.Set(companyId); return new(context, company); }

    private sealed class NoCurrentLineageFallback : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            Assert.DoesNotContain("[inventory].[MovementLines]", command.CommandText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[inventory].[Labels]", command.CommandText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[inventory].[LabelTransformations]", command.CommandText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[inventory].[Balances]", command.CommandText, StringComparison.OrdinalIgnoreCase);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ConsumedLineageCompanySwitch(Action switchCompany) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.Contains("ManufacturingCostGenerationHistory", StringComparison.Ordinal))
            { Fired = true; switchCompany(); }
            return ValueTask.FromResult(result);
        }
    }
}

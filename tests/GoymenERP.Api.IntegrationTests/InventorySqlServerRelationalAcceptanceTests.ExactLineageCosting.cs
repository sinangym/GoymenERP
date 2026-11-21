using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Real SQL source for exact split/merge quantities, Inventory's actual definite-shipment participant,
/// recognition and borrowed-transaction rollback. Purchasing price proof, the approved Sales demand
/// reference and Manufacturing calculator amount are explicitly synthetic test inputs. NOT_RUN.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private static readonly JsonSerializerOptions ExactLineageJson = new(JsonSerializerDefaults.Web);

    [SqlServerFact]
    public async Task Exact_lineage_recognition_and_reverse_preserve_the_fractional_private_pool_and_original_unavailable_exit()
    {
        await using var run = await Run.StartAsync();
        var fixture = await ArrangeExactLineageCostingAsync(run);
        await AssertExactLineagePoolAsync(run, fixture, "4", "3", 10m, complete: false);
        await AssertExactLineageRootAsync(run, fixture);
        var originalOutputStamp = await ExactLineageStampAsync(run, fixture.Output.MovementId);
        var originalExitStamp = await ExactLineageStampAsync(run, fixture.ShipmentMovementId);
        await using (var context = run.Context(CompanyA))
        {
            var exit = await context.MovementCostStamps.SingleAsync(row => row.MovementId == fixture.ShipmentMovementId
                && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
            Assert.Equal((int)InventoryMovementCostStampStatusV1.Unavailable, exit.Status);
            Assert.Equal("movement_contains_unpriced_lineage", exit.Reason);
            Assert.Null(exit.UnitCostPerKg);
            Assert.Null(exit.UnitCostRatioJson);
            Assert.Null(exit.Amount);
            using var effect = JsonDocument.Parse(exit.PoolEffectJson!);
            Assert.Equal("-20", effect.RootElement.GetProperty("recordedValue").GetProperty("numerator").GetString());
            Assert.Equal("3", effect.RootElement.GetProperty("recordedValue").GetProperty("denominator").GetString());
            Assert.Equal("2", effect.RootElement.GetProperty("pricedQuantityKgRatio").GetProperty("numerator").GetString());
            Assert.Equal("3", effect.RootElement.GetProperty("pricedQuantityKgRatio").GetProperty("denominator").GetString());
        }

        var recognized = await StageCostGenerationAsync(run, fixture.Command,
            ManufacturingMetadata(run, "exact-lineage-recognize"), commit: true);

        var output = Assert.Single(recognized.Outputs);
        Assert.Equal((10m, 3.33m, 6.67m), (output.OriginalTlAmount, output.ConsumedTlDifference, output.RemainingTlValue));
        Assert.Null(output.RemainingQuantityKg);
        Assert.Null(output.ConsumedQuantityKg);
        ExactLineageRatio(output.RemainingQuantityKgRatio, "2", "3");
        ExactLineageRatio(output.ConsumedQuantityKgRatio, "1", "3");
        var position = Assert.Single(output.Positions);
        Assert.True(position.IncludedInMovingAverage);
        Assert.Null(position.QuantityKg);
        ExactLineageRatio(position.QuantityKgRatio, "2", "3");
        Assert.Equal(6.67m, position.TlValue);
        var recordedEffect = Assert.Single(output.PoolEffects!);
        Assert.Null(recordedEffect.RecordedPoolValueEffect);
        ExactLineageRatio(recordedEffect.RecordedPoolValueEffectRatio, "100051", "15000");
        ExactLineageRatio(recordedEffect.AverageRoundingDifferenceRatio, "1", "15000");
        ExactLineageRatio(recordedEffect.QuantityKgRatio, "2", "3");
        ExactLineageRatio(recordedEffect.BeforeQuantityKgRatio, "4", "3");
        ExactLineageRatio(recordedEffect.AfterQuantityKgRatio, "2", "1");
        var immutableEffect = JsonSerializer.Serialize(recordedEffect, ExactLineageJson);
        await AssertExactLineagePoolAsync(run, fixture, "2", "1", 10.0017m, complete: true);
        await using (var context = run.Context(CompanyA))
        {
            var recognition = await context.MovementCostRecognitions.SingleAsync(row => row.GenerationId == recognized.GenerationId
                && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
            Assert.Null(recognition.RemainingQuantityKg);
            Assert.Null(recognition.ConsumedQuantityKg);
            ExactLineageRatioJson(recognition.RemainingQuantityRatioJson, "2", "3");
            ExactLineageRatioJson(recognition.ConsumedQuantityRatioJson, "1", "3");
            var envelope = await context.OutboxMessages.SingleAsync(row => row.Id == recognized.CostDifferenceEventId);
            var payload = JsonSerializer.Deserialize<InventoryManufacturingCostGenerationResultV1>(envelope.PayloadJson, ExactLineageJson)!;
            Assert.Equal((envelope.Id, 3.33m), (payload.CostDifferenceEventId, Assert.Single(payload.Outputs).ConsumedTlDifference));
            Assert.Equal(1, (await context.Movements.SingleAsync(row => row.Id == fixture.ShipmentMovementId)).CostStatus);
        }
        Assert.Equal(originalOutputStamp, await ExactLineageStampAsync(run, fixture.Output.MovementId));
        Assert.Equal(originalExitStamp, await ExactLineageStampAsync(run, fixture.ShipmentMovementId));

        var reversed = await ReverseExactLineageGenerationAsync(run, fixture, recognized);

        Assert.Equal(InventoryManufacturingCostGenerationStateV1.Reversed, reversed.State);
        var reverseOutput = Assert.Single(reversed.Outputs);
        Assert.Equal((-6.67m, -3.33m), (reverseOutput.RemainingTlValue, reverseOutput.ConsumedTlDifference));
        var reverseEffect = Assert.Single(reverseOutput.PoolEffects!);
        Assert.Null(reverseEffect.RecordedPoolValueEffect);
        ExactLineageRatio(reverseEffect.RecordedPoolValueEffectRatio, "-100051", "15000");
        ExactLineageRatio(reverseEffect.AverageRoundingDifferenceRatio, "-1", "15000");
        ExactLineageRatio(reverseEffect.AfterQuantityKgRatio, "4", "3");
        Assert.Equal(10m, reverseEffect.AfterAverageCostPerKg);
        await AssertExactLineagePoolAsync(run, fixture, "4", "3", 10m, complete: false);
        Assert.Equal(originalOutputStamp, await ExactLineageStampAsync(run, fixture.Output.MovementId));
        Assert.Equal(originalExitStamp, await ExactLineageStampAsync(run, fixture.ShipmentMovementId));
        await using var final = run.Context(CompanyA);
        var history = await final.ManufacturingCostGenerationHistory.Where(row => row.GenerationId == recognized.GenerationId)
            .OrderBy(row => row.Version).ToListAsync();
        Assert.Equal(2, history.Count);
        var originalHistory = JsonSerializer.Deserialize<InventoryManufacturingCostGenerationResultV1>(history[0].ResultJson, ExactLineageJson)!;
        Assert.Equal(immutableEffect, JsonSerializer.Serialize(Assert.Single(Assert.Single(originalHistory.Outputs).PoolEffects!), ExactLineageJson));
        Assert.Single(await final.MovementCostRecognitions.Where(row => row.GenerationId == recognized.GenerationId).ToListAsync());
        Assert.Equal(2m, await final.Balances.Where(row => row.LabelId == fixture.MixedLabelId).SumAsync(row => row.QuantityKg));
    }

    [SqlServerFact]
    public async Task Exact_lineage_recognition_flushes_descendant_fences_on_the_host_transaction_and_rollback_removes_every_effect()
    {
        await using var run = await Run.StartAsync();
        var fixture = await ArrangeExactLineageCostingAsync(run);
        var before = await ExactLineageDurableSnapshotAsync(run);
        long rootVersion;
        long mixedVersion;
        long pricedVersion;
        await using (var context = run.Context(CompanyA))
        {
            rootVersion = (await context.Labels.SingleAsync(row => row.Id == fixture.Output.LabelId)).Version;
            mixedVersion = (await context.Labels.SingleAsync(row => row.Id == fixture.MixedLabelId)).Version;
            pricedVersion = (await context.Labels.SingleAsync(row => row.Id == fixture.CostedLabelId)).Version;
        }

        var metadata = ManufacturingMetadata(run, "exact-lineage-rollback");
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            await using var scope = run.Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await using var owner = await InventoryManufacturingCostGenerationParticipant.EnlistAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            var staged = await owner.StageAsync(fixture.Command, metadata);
            var company = ExactLineageCompany(CompanyA);
            await using (var observer = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer(connection, contextOwnsConnection: false).Options, company))
            {
                await observer.Database.UseTransactionAsync(transaction);
                Assert.Same(connection, observer.Database.GetDbConnection());
                Assert.Same(transaction, observer.Database.CurrentTransaction!.GetDbTransaction());
                var pool = await observer.ExactCostPools.AsNoTracking().SingleAsync(row => row.LocationId == fixture.LocationId
                    && row.ProductId == Product && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
                ExactLineageRatioJson(pool.QuantityRatioJson, "2", "1");
                Assert.Equal(10.0017m, pool.AverageCostPerKg);
                Assert.Equal(rootVersion + 1, (await observer.Labels.AsNoTracking().SingleAsync(row => row.Id == fixture.Output.LabelId)).Version);
                Assert.Equal(mixedVersion + 1, (await observer.Labels.AsNoTracking().SingleAsync(row => row.Id == fixture.MixedLabelId)).Version);
                Assert.Equal(pricedVersion, (await observer.Labels.AsNoTracking().SingleAsync(row => row.Id == fixture.CostedLabelId)).Version);
                Assert.Single(await observer.MovementCostRecognitions.AsNoTracking().Where(row => row.GenerationId == staged.GenerationId).ToListAsync());
                Assert.True(await observer.OutboxMessages.AsNoTracking().AnyAsync(row => row.Id == staged.CostDifferenceEventId));
                Assert.Single(await observer.ManufacturingCostGenerationHistory.AsNoTracking().Where(row => row.GenerationId == staged.GenerationId).ToListAsync());
            }
            await using (var audit = new SqlCommand("SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation", connection,
                (SqlTransaction)transaction))
            {
                audit.Parameters.AddWithValue("@correlation", metadata.CorrelationId);
                Assert.True((int)(await audit.ExecuteScalarAsync())! > 0);
            }
            await transaction.RollbackAsync();
        }

        Assert.Equal(before, await ExactLineageDurableSnapshotAsync(run));
        await AssertExactLineagePoolAsync(run, fixture, "4", "3", 10m, complete: false);
        await AssertExactLineageRootAsync(run, fixture);
    }

    [SqlServerFact]
    public async Task Exact_lineage_reader_and_private_pool_ignore_another_company_same_product_merge_component()
    {
        await using var run = await Run.StartAsync();
        var fixture = await ArrangeExactLineageCostingAsync(run);
        var before = await ExactLineageDurableSnapshotAsync(run);
        var foreignLocation = await run.LocationOfAsync(CompanyB);
        var foreignRoot = await ExactLineageForeignCostedLabelAsync(run, Guid.NewGuid(), foreignLocation, 900m, fixture.Output.LabelCode);
        var foreignOther = await ExactLineageForeignCostedLabelAsync(run, Guid.NewGuid(), foreignLocation, 900m);
        var foreignMixedId = Guid.NewGuid();
        string mixedCode;
        await using (var context = run.Context(CompanyA))
            mixedCode = await context.Labels.AsNoTracking().Where(row => row.Id == fixture.MixedLabelId).Select(row => row.Code).SingleAsync();
        await using (var context = run.Context(CompanyB))
        {
            var versions = await context.Labels.AsNoTracking().Where(row => row.Id == foreignRoot.Id || row.Id == foreignOther.Id)
                .ToDictionaryAsync(row => row.Id, row => row.Version);
            var request = new TransformInventoryLabelsV1(Guid.NewGuid(), InventoryLabelTransformationKindV1.Merge,
                [new(foreignRoot.Id, versions[foreignRoot.Id].ToString(CultureInfo.InvariantCulture)),
                    new(foreignOther.Id, versions[foreignOther.Id].ToString(CultureInfo.InvariantCulture))],
                [new(foreignMixedId, mixedCode, "1800")], ExactLineageForeignAt,
                "Sentetik diger sirket lineage bileseni.");
            await using var owner = run.Store(CompanyB);
            await owner.Store.TransformAsync(request, Metadata(run));
        }

        // Label ids are globally unique; only company-scoped codes and the product identity coincide.
        // Company A's entire durable Inventory view is unchanged by the foreign component.
        Assert.Equal(before.InventoryJson, (await ExactLineageDurableSnapshotAsync(run)).InventoryJson);
        await AssertExactLineagePoolAsync(run, fixture, "4", "3", 10m, complete: false);
        await AssertExactLineageRootAsync(run, fixture);
        await using (var context = run.Context(CompanyA))
        {
            var foreign = await InventoryCostLineageReader.ReadAsync(context, CompanyB, Product, foreignRoot.Id);
            Assert.Equal(InventoryCostLineageOutcome.NotFound, foreign.Outcome);
            Assert.Empty(foreign.Labels);
            Assert.Null(foreign.RemainingKg);
        }
        await using (var context = run.Context(CompanyB))
        {
            var foreign = await InventoryCostLineageReader.ReadAsync(context, CompanyB, Product, foreignRoot.Id);
            Assert.Equal(InventoryCostLineageOutcome.Value, foreign.Outcome);
            ExactLineageRatio(foreign.RemainingKg, "900", "1");
            ExactLineageRatio(foreign.Labels.Single(label => label.LabelId == foreignMixedId).RootShare, "1", "2");
            Assert.Equal(1800m, Assert.Single(foreign.Labels.Single(label => label.LabelId == foreignMixedId).Positions).QuantityKg);
        }
    }

    [SqlServerFact]
    public async Task An_actual_customer_return_restores_the_partial_priced_effect_without_fabricating_a_second_pending_material_root()
    {
        await using var run = await Run.StartAsync();
        var fixture = await ArrangeExactLineageCostingAsync(run);
        var originalExit = await ExactLineageStampAsync(run, fixture.ShipmentMovementId);
        string originalPending;
        InventoryCustomerReturnRequestV1 request;
        await using (var context = run.Context(CompanyA))
        {
            var shipment = await context.Movements.AsNoTracking().SingleAsync(row => row.Id == fixture.ShipmentMovementId);
            Assert.NotNull(shipment.SourceLineId);
            request = new(CompanyA, Guid.NewGuid(), shipment.SourceId, shipment.SourceLineId.Value, Guid.NewGuid(), "1",
                ManufacturingAt.AddMinutes(6));
            var pending = await context.PendingMovementCosts.AsNoTracking().Where(row => row.ProductId == Product)
                .OrderBy(row => row.MovementId).ThenBy(row => row.LineNumber).ThenBy(row => row.CurrencyId).ToListAsync();
            Assert.Equal(2, pending.Count); // One original output root per TL/USD, not a descendant root.
            Assert.All(pending, row => Assert.Equal(fixture.Output.LabelId, row.LabelId));
            originalPending = JsonSerializer.Serialize(pending, ExactLineageJson);
        }

        var returned = await CommitReturnAsync(run, request, Metadata(run));

        Assert.Equal((fixture.ShipmentMovementId, fixture.MixedLabelId),
            (returned.OriginalShipmentMovementId, returned.OriginalLabelId));
        await AssertExactLineagePoolAsync(run, fixture, "2", "1", 10m, complete: false);
        Assert.Equal(originalExit, await ExactLineageStampAsync(run, fixture.ShipmentMovementId));
        await using var final = run.Context(CompanyA);
        var lineage = await InventoryCostLineageReader.ReadAsync(final, CompanyA, Product, fixture.Output.LabelId);
        Assert.Equal(InventoryCostLineageOutcome.Value, lineage.Outcome);
        ExactLineageRatio(lineage.RemainingKg, "1", "1");
        ExactLineageRatio(lineage.ShippedKg, "1", "3");
        ExactLineageRatio(lineage.CustomerReturnedKg, "1", "3");
        ExactLineageRatio(lineage.NetShippedKg, "0", "1");
        Assert.Equal(3m, await final.Balances.Where(row => row.LabelId == fixture.MixedLabelId).SumAsync(row => row.QuantityKg));
        var returnStamp = await final.MovementCostStamps.AsNoTracking().SingleAsync(row => row.MovementId == returned.InventoryMovementId
            && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.Equal((int)InventoryMovementCostStampStatusV1.Unavailable, returnStamp.Status);
        Assert.Equal(fixture.ShipmentMovementId, returnStamp.SourceIssueMovementId);
        Assert.True(returnStamp.LedgerVersion is >= 1);
        Assert.Null(returnStamp.UnitCostPerKg);
        Assert.Null(returnStamp.Amount);
        using (var effect = JsonDocument.Parse(returnStamp.PoolEffectJson!))
        {
            Assert.Equal("20", effect.RootElement.GetProperty("recordedValue").GetProperty("numerator").GetString());
            Assert.Equal("3", effect.RootElement.GetProperty("recordedValue").GetProperty("denominator").GetString());
            Assert.Equal("2", effect.RootElement.GetProperty("pricedQuantityKgRatio").GetProperty("numerator").GetString());
            Assert.Equal("3", effect.RootElement.GetProperty("pricedQuantityKgRatio").GetProperty("denominator").GetString());
        }
        var remainingPending = await final.PendingMovementCosts.AsNoTracking().Where(row => row.ProductId == Product)
            .OrderBy(row => row.MovementId).ThenBy(row => row.LineNumber).ThenBy(row => row.CurrencyId).ToListAsync();
        Assert.Equal(originalPending, JsonSerializer.Serialize(remainingPending, ExactLineageJson));
        Assert.False(await final.PendingMovementCosts.AnyAsync(row => row.MovementId == returned.InventoryMovementId));
    }

    [SqlServerFact]
    public async Task A_partial_customer_return_after_recognition_keeps_the_physical_return_but_makes_changed_cost_provenance_unreadable()
    {
        await using var run = await Run.StartAsync();
        var fixture = await ArrangeExactLineageCostingAsync(run);
        var originalExit = await ExactLineageStampAsync(run, fixture.ShipmentMovementId);
        var originalOutput = await ExactLineageStampAsync(run, fixture.Output.MovementId);
        var recognized = await StageCostGenerationAsync(run, fixture.Command,
            ManufacturingMetadata(run, "exact-lineage-before-late-return"), commit: true);
        await AssertExactLineagePoolAsync(run, fixture, "2", "1", 10.0017m, complete: true);
        InventoryCustomerReturnRequestV1 request;
        string originalRecognition;
        await using (var context = run.Context(CompanyA))
        {
            var shipment = await context.Movements.AsNoTracking().SingleAsync(row => row.Id == fixture.ShipmentMovementId);
            Assert.NotNull(shipment.SourceLineId);
            request = new(CompanyA, Guid.NewGuid(), shipment.SourceId, shipment.SourceLineId.Value, Guid.NewGuid(), "1",
                ManufacturingAt.AddHours(2));
            originalRecognition = JsonSerializer.Serialize(await context.MovementCostRecognitions.AsNoTracking().SingleAsync(row =>
                row.GenerationId == recognized.GenerationId && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira), ExactLineageJson);
        }

        var returned = await CommitReturnAsync(run, request, Metadata(run));

        Assert.Equal((fixture.ShipmentMovementId, fixture.MixedLabelId),
            (returned.OriginalShipmentMovementId, returned.OriginalLabelId));
        Assert.Equal(originalExit, await ExactLineageStampAsync(run, fixture.ShipmentMovementId));
        Assert.Equal(originalOutput, await ExactLineageStampAsync(run, fixture.Output.MovementId));
        await using var final = run.Context(CompanyA);
        Assert.Equal(3m, await final.Balances.Where(row => row.LabelId == fixture.MixedLabelId).SumAsync(row => row.QuantityKg));
        Assert.True(await final.Movements.AnyAsync(row => row.Id == returned.InventoryMovementId
            && row.Kind == (int)StockMovementKind.Return && row.SourceKind == InventoryCustomerReturnContractV1.SourceKind));
        var exact = await final.ExactCostPools.AsNoTracking().SingleAsync(row => row.LocationId == fixture.LocationId
            && row.ProductId == Product && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.Equal((int)AverageCostStatus.Unreadable, exact.Status);
        Assert.Equal((int)AverageCostUnreadableReason.Return, exact.UnreadableReason);
        Assert.Null(exact.AverageCostPerKg);
        Assert.Null(exact.QuantityRatioJson);
        var legacy = await final.AverageCostLedger.AsNoTracking().SingleAsync(row => row.LocationId == fixture.LocationId && row.ProductId == Product);
        Assert.Equal((int)AverageCostStatus.Unreadable, legacy.Status);
        Assert.Null(legacy.AverageCostPerKg);
        Assert.Null(legacy.QuantityKg);
        var published = await new InventoryAverageCostReadPort(final, ExactLineageCompany(CompanyA)).ReadAsync(fixture.LocationId, Product);
        Assert.Equal(InventoryAverageCostReadStatusV1.Unreadable, published.Status);
        Assert.Equal(InventoryAverageCostUnreadableCodesV1.Return, published.UnreadableCode);
        Assert.Null(published.AverageCostPerKg);
        var stamp = await final.MovementCostStamps.AsNoTracking().SingleAsync(row => row.MovementId == returned.InventoryMovementId
            && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.Equal((int)InventoryMovementCostStampStatusV1.Unavailable, stamp.Status);
        Assert.Null(stamp.UnitCostPerKg);
        Assert.Null(stamp.Amount);
        var freshPending = await final.PendingMovementCosts.AsNoTracking().SingleAsync(row => row.MovementId == returned.InventoryMovementId
            && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.Equal((fixture.MixedLabelId, 1m), (freshPending.LabelId, freshPending.QuantityKg));
        Assert.False(string.IsNullOrWhiteSpace(freshPending.Reason));
        Assert.Equal(originalRecognition, JsonSerializer.Serialize(await final.MovementCostRecognitions.AsNoTracking().SingleAsync(row =>
            row.GenerationId == recognized.GenerationId && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira), ExactLineageJson));
        var lineage = await InventoryCostLineageReader.ReadAsync(final, CompanyA, Product, fixture.Output.LabelId);
        Assert.Equal(InventoryCostLineageOutcome.Value, lineage.Outcome);
        ExactLineageRatio(lineage.RemainingKg, "1", "1");
        ExactLineageRatio(lineage.NetShippedKg, "0", "1");
    }

    private static async Task<ExactLineageCostingFixture> ArrangeExactLineageCostingAsync(Run run)
    {
        var location = await run.LocationOfAsync(CompanyA);
        var parent = await CostedLabelAsync(run, location, 1m, 10m, 2m);
        var orderId = Guid.NewGuid();
        var issued = await CommitIssueAsync(run, IssueRequest(orderId, parent.Id, "2", "1"),
            ManufacturingMetadata(run, "exact-lineage-parent-issue"));
        // Repack's existing same-lot/material rule is preserved. The real output uses the fixture's
        // existing matching lot; no cross-lot merge or new material-mixing policy is enabled.
        var output = OutputRequest(orderId, parent.Id, issued.ParentLabelVersion, location, "1") with
        { LotId = LotA, HeatNumber = null, CoilNumber = null };
        await CommitOutputAsync(run, output, ManufacturingMetadata(run, "exact-lineage-output"));
        var priced = await CostedLabelAsync(run, location, 2m, 10m, 2m);
        var mixedId = Guid.NewGuid();
        await using (var context = run.Context(CompanyA))
        {
            var versions = await context.Labels.AsNoTracking().Where(row => row.Id == priced.Id || row.Id == output.LabelId)
                .ToDictionaryAsync(row => row.Id, row => row.Version);
            var request = new TransformInventoryLabelsV1(Guid.NewGuid(), InventoryLabelTransformationKindV1.Merge,
                [new(priced.Id, versions[priced.Id].ToString(CultureInfo.InvariantCulture)),
                    new(output.LabelId, versions[output.LabelId].ToString(CultureInfo.InvariantCulture))],
                [new(mixedId, $"SYN-EXACT-MIXED-{Guid.NewGuid():N}", "3")], ManufacturingAt.AddMinutes(4),
                "Sentetik maliyetli ve bekleyen kilogramlarin ayni lotta birlesimi.");
            await using var owner = run.Store(CompanyA);
            await owner.Store.TransformAsync(request, Metadata(run));
        }

        var allocationId = Guid.NewGuid();
        await using (var owner = run.Store(CompanyA))
        {
            var labelVersion = await owner.DbContext.Labels.Where(row => row.Id == mixedId).Select(row => row.Version).SingleAsync();
            await owner.Store.CreateAsync(new(allocationId, Guid.NewGuid(), Guid.NewGuid(), Product, "product-v1", mixedId,
                labelVersion.ToString(CultureInfo.InvariantCulture), "1", "Sentetik onayli Sales talep referansi.", null), Metadata(run));
        }
        InventoryDefiniteShipmentResultV1 shipped;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            long labelVersion;
            await using (var context = run.Context(CompanyA))
                labelVersion = await context.Labels.AsNoTracking().Where(row => row.Id == mixedId).Select(row => row.Version).SingleAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction, CompanyA, Settings(run, CompanyA));
            shipped = await participant.StageAsync(new(CompanyA, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), allocationId, "1",
                mixedId, labelVersion.ToString(CultureInfo.InvariantCulture), Guid.NewGuid(), "1", ManufacturingAt.AddMinutes(5)),
                Metadata(run));
            await transaction.CommitAsync();
        }
        var command = new InventoryManufacturingCostGenerationCommandV1(CompanyA, orderId, Guid.NewGuid(), "0", "8",
            InventoryManufacturingCostGenerationStateV1.Recognized, "SENTETIK-exact-lineage-cost-v1", "SENTETIK-calculator-v1",
            "{\"source\":\"synthetic-calculator-proof\",\"originalTlAmount\":\"10\"}",
            [new(output.OutputId, output.MovementId, output.LabelId, 1m, 10m, null, 10m)],
            "Sentetik uretim maliyet kaynagi; gercek Inventory lineage ve transaction.", ManufacturingAt.AddHours(1));
        return new(orderId, output, priced.Id, mixedId, shipped.MovementId, location, command);
    }

    private static async Task AssertExactLineagePoolAsync(Run run, ExactLineageCostingFixture fixture,
        string numerator, string denominator, decimal average, bool complete)
    {
        await using var context = run.Context(CompanyA);
        var company = ExactLineageCompany(CompanyA);
        var row = await context.ExactCostPools.AsNoTracking().SingleAsync(candidate => candidate.LocationId == fixture.LocationId
            && candidate.ProductId == Product && candidate.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.Equal((int)AverageCostStatus.Known, row.Status);
        ExactLineageRatioJson(row.QuantityRatioJson, numerator, denominator);
        Assert.Equal(average, row.AverageCostPerKg);
        var pool = await InventoryStore.ReadCostPoolAsync(context, company, fixture.LocationId, Product,
            SettingsCurrencyIdentitiesV1.TurkishLira, CancellationToken.None);
        Assert.True(pool.IsConsistent);
        Assert.Equal(complete, pool.IsComplete);
        Assert.NotNull(pool.ExactPosition);
        ExactLineageRatio(pool.CostedKg, numerator, denominator);
        ExactLineageRatio(pool.ExactPosition.QuantityKg, numerator, denominator);
        Assert.Equal(average, pool.ExactPosition.AverageCostPerKg);
        var published = await new InventoryAverageCostReadPort(context, company).ReadAsync(fixture.LocationId, Product);
        Assert.Equal(complete ? InventoryAverageCostReadStatusV1.Value : InventoryAverageCostReadStatusV1.Unreadable, published.Status);
        if (complete) Assert.Equal(average, published.AverageCostPerKg);
        else
        {
            Assert.Null(published.AverageCostPerKg);
            Assert.Equal(InventoryAverageCostUnreadableCodesV1.LedgerStockMismatch, published.UnreadableCode);
        }
    }

    private static async Task AssertExactLineageRootAsync(Run run, ExactLineageCostingFixture fixture)
    {
        await using var context = run.Context(CompanyA);
        var lineage = await InventoryCostLineageReader.ReadAsync(context, CompanyA, Product, fixture.Output.LabelId);
        Assert.Equal(InventoryCostLineageOutcome.Value, lineage.Outcome);
        ExactLineageRatio(lineage.RemainingKg, "2", "3");
        ExactLineageRatio(lineage.ShippedKg, "1", "3");
        ExactLineageRatio(lineage.CustomerReturnedKg, "0", "1");
        ExactLineageRatio(lineage.NetShippedKg, "1", "3");
        Assert.Equal(3, lineage.Labels.Count);
        ExactLineageRatio(lineage.Labels.Single(label => label.LabelId == fixture.MixedLabelId).RootShare, "1", "3");
        ExactLineageRatio(lineage.Labels.Single(label => label.LabelId == fixture.CostedLabelId).RootShare, "0", "1");
        Assert.Equal(2m, Assert.Single(lineage.Labels.Single(label => label.LabelId == fixture.MixedLabelId).Positions).QuantityKg);
    }

    private static async Task<string> ExactLineageStampAsync(Run run, Guid movementId)
    {
        await using var context = run.Context(CompanyA);
        return JsonSerializer.Serialize(await context.MovementCostStamps.AsNoTracking().SingleAsync(row => row.MovementId == movementId
            && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira), ExactLineageJson);
    }

    private static async Task<InventoryManufacturingCostGenerationResultV1> ReverseExactLineageGenerationAsync(Run run,
        ExactLineageCostingFixture fixture, InventoryManufacturingCostGenerationResultV1 recognized)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using var scope = run.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        await using var owner = await InventoryManufacturingCostGenerationParticipant.EnlistAsync(connection, transaction, CompanyA,
            scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        var result = await owner.ReverseAsync(new(CompanyA, fixture.OrderId, recognized.GenerationId, recognized.GenerationVersion, "8",
            "Sentetik exact lineage maliyet ters kaydi.", ManufacturingAt.AddHours(2)), ManufacturingMetadata(run, "exact-lineage-reverse"));
        await transaction.CommitAsync();
        return result;
    }

    // Company B's approved Settings kilogram profile starts on this day; Company A's older fact date is unchanged.
    private static readonly DateTimeOffset ExactLineageForeignAt = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static async Task<MaterialLabel> ExactLineageForeignCostedLabelAsync(Run run, Guid id, Guid location, decimal kg, string? code = null)
    {
        var label = new MaterialLabel(id, CompanyB, code ?? $"SYN-EXACT-FOREIGN-{Guid.NewGuid():N}", Product, LotB, "product-v1");
        await using var owner = run.Store(CompanyB);
        await owner.Labels.SaveLabelAsync(label, Metadata(run));
        var receipt = new StockMovement(Guid.NewGuid(), CompanyB, StockMovementKind.Receipt, ExactLineageForeignAt,
            new StockSourceReference(InventoryGoodsReceiptContractV1.SourceKind, Guid.NewGuid()),
            [In(Position(CompanyB, location, label, BlockState.Free), kg)]);
        owner.Store.UseReceiptCost(receipt.Id, new(CompanyB, receipt.Source.Id, Guid.NewGuid(), Guid.NewGuid(), null, null, 99m, 9m,
            new(InventoryIssueCostCurrencyStatusV1.FunctionalCurrency, null, null, null, "SENTETIK-foreign-entry-v1", null),
            "SENTETIK-foreign-price-proof", "{}", null, null));
        await owner.Store.RecordMovementAsync(receipt, Metadata(run));
        return label;
    }

    private static async Task<ExactLineageDurableSnapshot> ExactLineageDurableSnapshotAsync(Run run)
    {
        await using var context = run.Context(CompanyA);
        var inventory = JsonSerializer.Serialize(new
        {
            Pools = await context.ExactCostPools.AsNoTracking().OrderBy(row => row.LocationId).ThenBy(row => row.CurrencyId).ToListAsync(),
            Ledger = await context.AverageCostLedger.AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            ForeignLedger = await context.CurrencyAverageCostLedger.AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Labels = await context.Labels.AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Balances = await context.Balances.AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Movements = await context.Movements.AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Lines = await context.MovementLines.AsNoTracking().OrderBy(row => row.MovementId).ThenBy(row => row.LineNumber).ToListAsync(),
            Stamps = await context.MovementCostStamps.AsNoTracking().OrderBy(row => row.MovementId).ThenBy(row => row.LineNumber).ThenBy(row => row.CurrencyId).ToListAsync(),
            Pending = await context.PendingMovementCosts.AsNoTracking().OrderBy(row => row.MovementId).ThenBy(row => row.LineNumber).ThenBy(row => row.CurrencyId).ToListAsync(),
            Transformations = await context.LabelTransformations.AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            TransformationInputs = await context.LabelTransformationInputs.AsNoTracking().OrderBy(row => row.TransformationId).ThenBy(row => row.Sequence).ToListAsync(),
            TransformationOutputs = await context.LabelTransformationOutputs.AsNoTracking().OrderBy(row => row.TransformationId).ThenBy(row => row.Sequence).ToListAsync(),
            Generations = await context.ManufacturingCostGenerations.AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            History = await context.ManufacturingCostGenerationHistory.AsNoTracking().OrderBy(row => row.GenerationId).ThenBy(row => row.Version).ToListAsync(),
            Recognitions = await context.MovementCostRecognitions.AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Commands = await context.Commands.AsNoTracking().OrderBy(row => row.CommandName).ThenBy(row => row.IdempotencyKeyHash).ToListAsync(),
            Outbox = await context.OutboxMessages.AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
        }, ExactLineageJson);
        return new(inventory, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @company", ("@company", CompanyA)));
    }

    private static CompanyContext ExactLineageCompany(Guid id)
    {
        var company = new CompanyContext();
        company.Set(id);
        return company;
    }

    private static void ExactLineageRatioJson(string? json, string numerator, string denominator)
    {
        Assert.NotNull(json);
        ExactLineageRatio(JsonSerializer.Deserialize<InventoryExactCostRatioV1>(json, ExactLineageJson), numerator, denominator);
    }

    private static void ExactLineageRatio(InventoryExactCostRatioV1? ratio, string numerator, string denominator)
    {
        Assert.NotNull(ratio);
        Assert.Equal((numerator, denominator), (ratio.Numerator, ratio.Denominator));
        Assert.True(ExactCostRatio.TryParse(ratio.Numerator, ratio.Denominator, out _));
    }

    private static void ExactLineageRatio(ExactCostRatio? ratio, string numerator, string denominator)
    {
        Assert.NotNull(ratio);
        Assert.Equal((numerator, denominator), (ratio.Numerator, ratio.Denominator));
    }

    private sealed record ExactLineageCostingFixture(Guid OrderId, InventoryManufacturingOutputRequestV1 Output,
        Guid CostedLabelId, Guid MixedLabelId, Guid ShipmentMovementId, Guid LocationId, InventoryManufacturingCostGenerationCommandV1 Command);
    private sealed record ExactLineageDurableSnapshot(string InventoryJson, int AuditCount);
}

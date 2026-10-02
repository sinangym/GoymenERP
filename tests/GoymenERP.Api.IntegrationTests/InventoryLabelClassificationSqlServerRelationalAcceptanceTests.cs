using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Labels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Label_classification_is_versioned_replayable_company_scoped_and_has_no_stock_effect()
    {
        const string previousMigration = "20261001160000_AddInventoryLabelProductVersion";
        const string currentMigration = "20261002110000_AddInventoryLabelClassification";
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-LABEL-CLASSIFICATION", 100m);
        var metadata = Metadata(run) with
        {
            IdempotencyKey = $"label-classification-{Guid.NewGuid():N}",
            CorrelationId = $"label-classification-{Guid.NewGuid():N}",
        };
        var request = new ClassifyInventoryLabelV1(
            source.LabelVersion.ToString(), InventoryLabelClassificationV1.SecondQuality);

        InventoryLabelClassificationResultV1 first;
        await using (var scope = run.Store(CompanyA))
        {
            first = await scope.Store.ClassifyAsync(source.LabelId, request, metadata);
            Assert.False(first.IsReplay);
            Assert.Equal((source.LabelId, "SecondQuality", source.LabelVersion + 1),
                (first.Classification.LabelId, first.Classification.Classification,
                    long.Parse(first.Classification.LabelVersion)));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var replay = await scope.Store.ClassifyAsync(source.LabelId, request, metadata with
            {
                CorrelationId = $"label-classification-replay-{Guid.NewGuid():N}",
            });
            Assert.True(replay.IsReplay);
            Assert.Equal(first.Classification, replay.Classification);
            Assert.Equal(metadata.CorrelationId, replay.OriginalCorrelationId);

            var stale = await Assert.ThrowsAsync<InventoryLabelClassificationException>(() =>
                scope.Store.ClassifyAsync(source.LabelId,
                    new(source.LabelVersion.ToString(), InventoryLabelClassificationV1.Scrap),
                    metadata with { IdempotencyKey = $"label-classification-stale-{Guid.NewGuid():N}" }));
            Assert.Equal((409, InventoryLabelClassificationCodesV1.LabelVersionConflict), (stale.Status, stale.Code));
        }

        await using (var foreign = run.Store(CompanyB))
        {
            var hidden = await Assert.ThrowsAsync<InventoryLabelClassificationException>(() =>
                foreign.Store.ClassifyAsync(source.LabelId,
                    new(first.Classification.LabelVersion, InventoryLabelClassificationV1.Standard),
                    metadata with { IdempotencyKey = $"label-classification-foreign-{Guid.NewGuid():N}" }));
            Assert.Equal((404, InventoryLabelClassificationCodesV1.LabelNotFound), (hidden.Status, hidden.Code));
        }

        Assert.Equal(100m, await run.ScalarAsync<decimal>(
            "SELECT SUM(QuantityKg) FROM inventory.Balances WHERE LabelId = @id", ("@id", source.LabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id IN (SELECT MovementId FROM inventory.MovementLines WHERE LabelId = @id)",
            ("@id", source.LabelId)));
        Assert.Equal((int)LabelClassification.SecondQuality, await run.ScalarAsync<int>(
            "SELECT Classification FROM inventory.Labels WHERE Id = @id", ("@id", source.LabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = N'inventory.label-classification.classify.v1' AND TargetId = @id",
            ("@id", source.LabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'inventory.label.classified.v1' AND EntityId = @id",
            ("@id", source.LabelId.ToString("D"))));

        await using (var context = run.Context(CompanyA))
        {
            var refused = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(previousMigration));
            Assert.Equal(51055, refused.Number);
            Assert.Equal(currentMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }
    }
}

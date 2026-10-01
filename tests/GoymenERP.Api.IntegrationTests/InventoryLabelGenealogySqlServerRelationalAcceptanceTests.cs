using GoymenERP.Modules.Inventory.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Split_then_merge_preserves_mass_replays_and_reads_the_many_to_many_genealogy()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-GENEALOGY-SOURCE", 100m);
        var splitA = Guid.NewGuid();
        var splitB = Guid.NewGuid();
        var split = new TransformInventoryLabelsV1(
            Guid.NewGuid(),
            InventoryLabelTransformationKindV1.Split,
            [new(source.LabelId, source.LabelVersion.ToString())],
            [new(splitA, "SYN-GENEALOGY-A", "40"), new(splitB, "SYN-GENEALOGY-B", "60")],
            At,
            "Sentetik fiziksel etiket bolme kabul testi.");
        var splitMetadata = Metadata(run) with
        {
            IdempotencyKey = $"genealogy-split-{Guid.NewGuid():N}",
            CorrelationId = $"genealogy-split-{Guid.NewGuid():N}",
        };

        await using (var scope = run.Store(CompanyA))
        {
            var first = await scope.Store.TransformAsync(split, splitMetadata);
            Assert.False(first.IsReplay);
            Assert.Equal(("Split", CompanyA, split.TransformationId),
                (first.Transformation.Kind, first.Transformation.CompanyId, first.Transformation.TransformationId));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var replay = await scope.Store.TransformAsync(split, splitMetadata with
            {
                CorrelationId = $"genealogy-split-replay-{Guid.NewGuid():N}",
            });
            Assert.True(replay.IsReplay);
            Assert.Equal(splitMetadata.CorrelationId, replay.OriginalCorrelationId);
        }

        var merged = Guid.NewGuid();
        var merge = new TransformInventoryLabelsV1(
            Guid.NewGuid(),
            InventoryLabelTransformationKindV1.Merge,
            [new(splitA, "1"), new(splitB, "1")],
            [new(merged, "SYN-GENEALOGY-MERGED", "100")],
            At.AddMinutes(1),
            "Sentetik fiziksel etiket birlestirme kabul testi.");
        await using (var scope = run.Store(CompanyA))
        {
            var result = await scope.Store.TransformAsync(merge, Metadata(run) with
            {
                IdempotencyKey = $"genealogy-merge-{Guid.NewGuid():N}",
                CorrelationId = $"genealogy-merge-{Guid.NewGuid():N}",
            });
            Assert.False(result.IsReplay);

            var genealogy = (await scope.Store.ReadAsync(merged))!;
            Assert.False(genealogy.WindowFull);
            Assert.Equal((merged, "SYN-GENEALOGY-MERGED", 4),
                (genealogy.LabelId, genealogy.LabelCode, genealogy.Edges.Count));
            Assert.Equal(2, genealogy.Edges.Select(edge => edge.TransformationId).Distinct().Count());
            Assert.Equal(["Merge", "Split"], genealogy.Edges.Select(edge => edge.Kind).Distinct().Order().ToArray());

            // The read is the CONNECTED many-to-many genealogy (DEC-LBL-003), not only the edges that touch the label:
            // the split source and a split child read the very same four edges as the merged label, each under its
            // own identity.
            foreach (var (member, code) in new[] { (source.LabelId, "SYN-GENEALOGY-SOURCE"), (splitA, "SYN-GENEALOGY-A") })
            {
                var view = (await scope.Store.ReadAsync(member))!;
                Assert.Equal((member, code, false), (view.LabelId, view.LabelCode, view.WindowFull));
                Assert.Equal(EdgeKeys(genealogy), EdgeKeys(view));
            }

            var detail = await LabelDetails(scope.DbContext, CompanyA, scope.Store).GetAsync(merged);
            Assert.Equal((merged, "SYN-GENEALOGY-MERGED", 4, false),
                (detail.Genealogy.LabelId, detail.Genealogy.LabelCode,
                    detail.Genealogy.Edges.Count, detail.Genealogy.WindowFull));
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformations"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformationInputs"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformationOutputs"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = N'inventory.label-transformation.record.v1'"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.LabelTransformation'"));
        Assert.Equal(100m, await run.ScalarAsync<decimal>(
            "SELECT SUM(QuantityKg) FROM inventory.Balances WHERE QuantityKg > 0"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Balances WHERE LabelId = @id AND QuantityKg = 100",
            ("@id", merged)));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Balances WHERE LabelId IN (@source, @a, @b) AND QuantityKg = 0",
            ("@source", source.LabelId), ("@a", splitA), ("@b", splitB)));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Id IN (@source, @a, @b, @merged) AND ProductVersion = N'product-v1'",
            ("@source", source.LabelId), ("@a", splitA), ("@b", splitB), ("@merged", merged)));
    }

    [SqlServerFact]
    public async Task Split_and_merge_refuse_missing_or_mixed_product_version_provenance()
    {
        await using var run = await Run.StartAsync();
        var legacy = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-GENEALOGY-LEGACY", 10m);
        await run.ExecuteAsync("UPDATE inventory.Labels SET ProductVersion = NULL WHERE Id = @id",
            ("@id", legacy.LabelId));
        await using (var scope = run.Store(CompanyA))
        {
            var error = await Assert.ThrowsAsync<InventoryLabelGenealogyException>(() => scope.Store.TransformAsync(
                SplitRequest(legacy.LabelId, legacy.LabelVersion, "SYN-GENEALOGY-LEGACY-OUT"), Metadata(run)));
            Assert.Equal((503, InventoryLabelGenealogyCodesV1.CorruptState), (error.Status, error.Code));
        }

        var first = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-GENEALOGY-MIX-A", 5m);
        var second = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-GENEALOGY-MIX-B", 5m);
        await run.ExecuteAsync("UPDATE inventory.Labels SET ProductVersion = N'product-v2' WHERE Id = @id",
            ("@id", second.LabelId));
        var merge = new TransformInventoryLabelsV1(Guid.NewGuid(), InventoryLabelTransformationKindV1.Merge,
            [new(first.LabelId, first.LabelVersion.ToString()), new(second.LabelId, second.LabelVersion.ToString())],
            [new(Guid.NewGuid(), "SYN-GENEALOGY-MIX-OUT", "10")], At, "Sentetik farklı ürün sürümü reddi.");
        await using (var scope = run.Store(CompanyA))
        {
            var error = await Assert.ThrowsAsync<InventoryLabelGenealogyException>(() =>
                scope.Store.TransformAsync(merge, Metadata(run)));
            Assert.Equal((503, InventoryLabelGenealogyCodesV1.CorruptState), (error.Status, error.Code));
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Code LIKE N'SYN-GENEALOGY-%-OUT%'"));
    }

    [SqlServerFact]
    public async Task Stale_foreign_and_allocated_sources_fail_closed_without_a_genealogy_effect()
    {
        await using var run = await Run.StartAsync();
        var own = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-GENEALOGY-OWN", 100m);
        var foreign = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-GENEALOGY-FOREIGN", 100m);
        var before = await GenealogyCountsAsync(run);

        await using (var scope = run.Store(CompanyA))
        {
            var stale = await Assert.ThrowsAsync<InventoryLabelGenealogyException>(() => scope.Store.TransformAsync(
                SplitRequest(own.LabelId, own.LabelVersion + 1, "SYN-GENEALOGY-STALE"), Metadata(run)));
            Assert.Equal((409, InventoryLabelGenealogyCodesV1.LabelVersionConflict), (stale.Status, stale.Code));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var hidden = await Assert.ThrowsAsync<InventoryLabelGenealogyException>(() => scope.Store.TransformAsync(
                SplitRequest(foreign.LabelId, foreign.LabelVersion, "SYN-GENEALOGY-HIDDEN"), Metadata(run)));
            Assert.Equal((404, InventoryLabelGenealogyCodesV1.LabelNotFound), (hidden.Status, hidden.Code));
        }

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(own, "10"), Metadata(run));
        }
        var currentVersion = await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", own.LabelId));
        var afterAllocation = await GenealogyCountsAsync(run);
        await using (var scope = run.Store(CompanyA))
        {
            var allocated = await Assert.ThrowsAsync<InventoryLabelGenealogyException>(() => scope.Store.TransformAsync(
                SplitRequest(own.LabelId, currentVersion, "SYN-GENEALOGY-ALLOCATED"), Metadata(run)));
            Assert.Equal((409, InventoryLabelGenealogyCodesV1.ActiveAllocationConflict),
                (allocated.Status, allocated.Code));
        }

        Assert.Equal(before, afterAllocation);
        Assert.Equal(afterAllocation, await GenealogyCountsAsync(run));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Code LIKE N'SYN-GENEALOGY-%' AND Id NOT IN (@own, @foreign)",
            ("@own", own.LabelId), ("@foreign", foreign.LabelId)));
    }

    [SqlServerFact]
    public async Task Genealogy_migration_rolls_back_when_empty_and_blocks_data_loss_after_a_transformation()
    {
        const string previousMigration = "20260924013338_AddInventoryPhysicalAllocationLifecycle";
        const string currentMigration = "20260924095845_AddInventoryLabelGenealogy";
        await using var run = await Run.StartAsync(migrateInventory: false);
        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'inventory') AND name LIKE N'LabelTransformation%'"));
            await context.GetService<IMigrator>().MigrateAsync(currentMigration);
            Assert.Equal(3, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'inventory') AND name LIKE N'LabelTransformation%'"));
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'inventory') AND name LIKE N'LabelTransformation%'"));
            await context.GetService<IMigrator>().MigrateAsync(currentMigration);
        }

        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-GENEALOGY-DOWN", 10m);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.TransformAsync(
                SplitRequest(source.LabelId, source.LabelVersion, "SYN-GENEALOGY-DOWN-OUT", quantity: "5"),
                Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            var rejected = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(previousMigration));
            Assert.Equal(51053, rejected.Number);
            Assert.Equal(currentMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformations"));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'inventory') AND name LIKE N'LabelTransformation%'"));
    }

    /// <summary>One genealogy's edges as (transformation, source, output), in the reader's order.</summary>
    private static (Guid Transformation, Guid Source, Guid Output)[] EdgeKeys(InventoryLabelGenealogyV1 genealogy) =>
        [.. genealogy.Edges.Select(edge => (edge.TransformationId, edge.SourceLabelId, edge.OutputLabelId))];

    private static TransformInventoryLabelsV1 SplitRequest(
        Guid sourceLabelId,
        long expectedVersion,
        string codePrefix,
        string quantity = "50") =>
        new(
            Guid.NewGuid(),
            InventoryLabelTransformationKindV1.Split,
            [new(sourceLabelId, expectedVersion.ToString())],
            [new(Guid.NewGuid(), $"{codePrefix}-A", quantity), new(Guid.NewGuid(), $"{codePrefix}-B", quantity)],
            At,
            "Sentetik fiziksel etiket bolme kabul testi.");

    private static async Task<(int Transformations, int Inputs, int Outputs, int Commands, int Audits)>
        GenealogyCountsAsync(Run run) =>
        (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformations"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformationInputs"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformationOutputs"),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = N'inventory.label-transformation.record.v1'"),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.LabelTransformation'")
        );
}

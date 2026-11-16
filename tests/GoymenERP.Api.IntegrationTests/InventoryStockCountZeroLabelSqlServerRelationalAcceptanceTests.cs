using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// STK-S22 (SINAN_KARARLARI_2026-11-13_EK1) over real SQL. SAT-D330: a label the system shows at 0 kg is counted
/// against a book of 0 at the one position it was emptied at, and the approved surplus is one CountAdjustment that
/// puts it back in stock. SAT-D329: an over-issue is refused before any write, so the label keeps its kilograms and
/// version and the next movement or count goes through. The zero-book migration rolls back while no such count exists
/// and refuses (THROW 51748) once one does.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Stock_count_of_a_label_shown_at_zero_records_its_surplus_and_the_approval_puts_it_back_in_stock()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-ZERO", 10m);
        await CountAndApproveAsync(run, Count(source.LabelId, source.LabelVersion, "0"));
        Assert.Equal(0m, await LabelKgAsync(run, source.LabelId));
        // The emptied position keeps its zero balance row; nothing is deleted.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Balances WHERE LabelId = @id AND QuantityKg = 0", ("@id", source.LabelId)));

        var zeroVersion = source.LabelVersion + 1;
        var request = Count(source.LabelId, zeroVersion, "7");
        await using (var scope = run.Store(CompanyA))
        {
            var recorded = await scope.Store.RecordStockCountAsync(request, Metadata(run), static _ => Task.CompletedTask);
            Assert.Equal((InventoryStockCountStatusesV1.PendingApproval, "0", "7", "7", zeroVersion.ToString()),
                (recorded.Count.Status, recorded.Count.BookKg, recorded.Count.CountedKg, recorded.Count.DifferenceKg,
                    recorded.Count.LabelVersion));
        }

        // Recording is evidence only: still 0 kg, the same version, one adjustment (the count to zero).
        Assert.Equal(0m, await LabelKgAsync(run, source.LabelId));
        Assert.Equal(zeroVersion, await LabelVersionAsync(run, source.LabelId));
        Assert.Equal(1, await CountAdjustmentsAsync(run));

        Guid movementId;
        await using (var scope = run.Store(CompanyA))
        {
            var approved = await scope.Store.DecideStockCountAsync(request.CountId,
                new(InventoryStockCountDecisionKindV1.Approve, "Sentetik: sıfır görünen rulo sahada bulundu."),
                OtherActor(run), static _ => Task.CompletedTask);
            Assert.Equal(InventoryStockCountStatusesV1.Approved, approved.Count.Status);
            movementId = approved.Count.Decision!.AdjustmentMovementId!.Value;
        }

        Assert.Equal(7m, await LabelKgAsync(run, source.LabelId));
        Assert.Equal(source.LabelVersion + 2, await LabelVersionAsync(run, source.LabelId));
        Assert.Equal(2, await CountAdjustmentsAsync(run));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements m JOIN inventory.MovementLines l ON l.MovementId = m.Id "
            + "WHERE m.Id = @id AND m.Kind = 6 AND m.SourceId = @count AND l.Direction = 1 AND l.QuantityKg = 7 "
            + "AND l.LabelId = @label",
            ("@id", movementId), ("@count", request.CountId), ("@label", source.LabelId)));
        // The surplus lands on the same position row it was emptied at: still one balance row for the label.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Balances WHERE LabelId = @id", ("@id", source.LabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.StockCounts WHERE LabelId = @id AND BookKg = 0", ("@id", source.LabelId)));
    }

    [SqlServerFact]
    public async Task An_over_issue_is_refused_before_any_write_so_the_label_keeps_its_kg_and_version_and_is_counted_next()
    {
        const string code = "SYN-COUNT-OVER-ISSUE";
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, code, 10m);
        var position = Position(CompanyA, await run.LocationOfAsync(CompanyA),
            new MaterialLabel(source.LabelId, CompanyA, code, Product, LotA, "product-v1"), BlockState.Free);
        var movementsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");

        await using (var scope = run.Store(CompanyA))
        {
            var refused = await Assert.ThrowsAsync<InventoryRuleException>(() => scope.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Issue, Out(position, 12m)), Metadata(run)));
            Assert.Equal(InventoryRuleCodes.NegativePhysicalStock, refused.Code);
        }

        // SAT-D329: no movement, no balance change, no version bump; nothing is left locked.
        Assert.Equal(movementsBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(10m, await LabelKgAsync(run, source.LabelId));
        Assert.Equal(source.LabelVersion, await LabelVersionAsync(run, source.LabelId));

        // The next movement goes through, and a count at the version it left is recorded and approved.
        await using (var scope = run.Store(CompanyA))
        {
            Assert.Equal(InventoryWriteOutcome.Recorded, await scope.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Issue, Out(position, 4m)), Metadata(run)));
        }

        Assert.Equal(6m, await LabelKgAsync(run, source.LabelId));
        await CountAndApproveAsync(run, Count(source.LabelId, source.LabelVersion + 1, "5.5"));
        Assert.Equal(5.5m, await LabelKgAsync(run, source.LabelId));
        Assert.Equal(source.LabelVersion + 2, await LabelVersionAsync(run, source.LabelId));
    }

    [SqlServerFact]
    public async Task Zero_book_stock_count_migration_rolls_back_without_one_and_blocks_after_one()
    {
        const string previousMigration = "20261115205912_AddInventorySupplierReturnExit";
        const string currentMigration = "20261116000110_AllowInventoryZeroBookStockCounts";
        const string strictBookCheck =
            "SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'CK_StockCounts_State' "
            + "AND definition LIKE N'%[[]BookKg]>(0)%'";
        await using var run = await Run.StartAsync();
        await using (var context = run.Context(CompanyA))
        {
            // No zero-book count yet: Down restores the strict book check.
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.DoesNotContain(currentMigration, await context.Database.GetAppliedMigrationsAsync());
            Assert.Equal(1, await run.ScalarAsync<int>(strictBookCheck));
            await context.GetService<IMigrator>().MigrateAsync(currentMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(strictBookCheck));
        }

        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-ZERO-DOWN", 10m);
        await CountAndApproveAsync(run, Count(source.LabelId, source.LabelVersion, "0"));
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockCountAsync(Count(source.LabelId, source.LabelVersion + 1, "3"), Metadata(run),
                static _ => Task.CompletedTask);
        }

        await using (var context = run.Context(CompanyA))
        {
            var rejected = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(previousMigration));
            Assert.Equal(51748, rejected.Number);
            Assert.Equal(currentMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal(0, await run.ScalarAsync<int>(strictBookCheck));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockCounts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.StockCounts WHERE BookKg = 0"));
    }

    private static async Task CountAndApproveAsync(Run run, RecordInventoryStockCountV1 request)
    {
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockCountAsync(request, Metadata(run), static _ => Task.CompletedTask);
        }

        await using (var scope = run.Store(CompanyA))
        {
            var approved = await scope.Store.DecideStockCountAsync(request.CountId,
                new(InventoryStockCountDecisionKindV1.Approve, "Sentetik sayım onayı."), OtherActor(run),
                static _ => Task.CompletedTask);
            Assert.Equal(InventoryStockCountStatusesV1.Approved, approved.Count.Status);
        }
    }

    private static InventoryCommandMetadata OtherActor(Run run) => Metadata(run) with
    {
        ActorId = Guid.NewGuid().ToString("D"),
        IdempotencyKey = $"approve-{Guid.NewGuid():N}",
        CorrelationId = $"approve-{Guid.NewGuid():N}",
    };
}

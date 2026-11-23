using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-22b (STK-S5) actual SQL: the dispatch cancellation migration rolls back while nothing it added holds data and its
/// Down refuses (THROW 51823) once a cancelled dispatch exists, leaving the reverse movement and the cancellation row in
/// place. KLN-D64 (2): the cancellation returns the stock to its source position and, like the dispatch, writes nothing
/// to the average-cost ledger. SENTETIK data only.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private const string TransferDispatchCancelReason = "SENTETIK yanlis sevk";

    [SqlServerFact]
    public async Task Transfer_dispatch_cancellation_migration_rolls_back_when_empty_and_refuses_with_51823_after_a_cancelled_dispatch()
    {
        const string previousMigration = "20261115142457_AddInventoryFutureSupplyAllocations";
        const string currentMigration = "20261115151824_AddInventoryTransferDispatchCancellation";
        await using var run = await Run.StartAsync();
        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE object_id = OBJECT_ID(N'inventory.TransferDispatchCancellations')"));
            await context.GetService<IMigrator>().MigrateAsync(currentMigration);
            await context.Database.MigrateAsync();
        }

        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-CANCEL-DOWN", 2m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var plan = TransferPlan(source, sourceLocation, destinationLocation, "2");
        var request = TransferDispatch();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(plan, Metadata(run), Authorized);
            await scope.Store.DispatchAsync(plan.TransferId, request, EffectiveOutgoing(plan, request),
                InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
            var cancelled = await scope.Store.CancelDispatchAsync(plan.TransferId,
                new CancelInventoryTransferDispatchV1("2", At.AddHours(2), TransferDispatchCancelReason),
                null, null, Metadata(run), Authorized);
            Assert.Equal(("3", nameof(InventoryTransferStatus.DispatchCancelled)),
                (cancelled.Transfer.Version, cancelled.Transfer.Status));
        }

        await using (var context = run.Context(CompanyA))
        {
            var appliedBefore = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var proofQueries = new[]
            {
                "SELECT CompanyId, Version FROM inventory.NegativeStockValidationGuards ORDER BY CompanyId FOR JSON PATH",
                "SELECT * FROM inventory.Transfers ORDER BY Id FOR JSON PATH",
                "SELECT * FROM inventory.TransferDispatches ORDER BY TransferId FOR JSON PATH",
                "SELECT * FROM inventory.TransferDispatchCancellations ORDER BY TransferId FOR JSON PATH",
                "SELECT * FROM inventory.Movements ORDER BY Id FOR JSON PATH",
                "SELECT * FROM inventory.Labels ORDER BY Id FOR JSON PATH",
            };
            var proofs = new List<string>();
            foreach (var query in proofQueries) proofs.Add(await run.ScalarAsync<string>(query));
            Assert.True(await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.NegativeStockValidationGuards") > 0);
            var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
                new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryTransferDispatchCancellation().DownOperations[0]).Sql;
            // The empty full roundtrip above is separate; newer physical proof stays intact here.
            var rejected = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(run, guard));
            Assert.Equal(51823, rejected.Number);
            Assert.Equal(appliedBefore, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
            for (var index = 0; index < proofQueries.Length; index++)
                Assert.Equal(proofs[index], await run.ScalarAsync<string>(proofQueries[index]));
        }

        Assert.Equal((1, 1, (int)InventoryTransferStatus.DispatchCancelled), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferDispatchCancellations WHERE TransferId = @id",
                ("@id", plan.TransferId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 11"),
            await run.ScalarAsync<int>("SELECT Status FROM inventory.Transfers WHERE Id = @id", ("@id", plan.TransferId))));
        Assert.Equal(2m, await TransferLabelKgAsync(run, source.LabelId));
    }

    [SqlServerFact]
    public async Task Transfer_dispatch_cancellation_migration_backfills_an_earlier_dispatch_as_an_unreadable_scale_setting()
    {
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryTransferDispatchCancellation();
        var addColumn = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>()
            .Single(operation => operation.Name == "SourceScaleSettingAtDispatch");
        var backfill = Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        // The backfill runs right after the column exists, before any later dispatch can store its own value.
        Assert.Equal(migration.UpOperations.ToList().IndexOf(addColumn) + 1, migration.UpOperations.ToList().IndexOf(backfill));

        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-SCALE-BACKFILL", 2m);
        var plan = TransferPlan(source, await run.LocationOfAsync(CompanyA), await TransferLocationAsync(run, CompanyA, offset: 1), "2");
        var request = TransferDispatch();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(plan, Metadata(run), Authorized);
            await scope.Store.DispatchAsync(plan.TransferId, request, EffectiveOutgoing(plan, request),
                InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
        }

        // A dispatch that existed when the column was added never stored its value: it is a read error, not Off.
        await ExecuteAsync(run, backfill.Sql);
        Assert.Equal((int)InventoryTransferSourceScaleSetting.Unreadable, await run.ScalarAsync<int>(
            "SELECT SourceScaleSettingAtDispatch FROM inventory.TransferDispatches WHERE TransferId = @id", ("@id", plan.TransferId)));
    }

    [SqlServerFact]
    public async Task Transfer_dispatch_cancel_returns_the_stock_to_its_source_without_writing_the_average_cost_ledger()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-CANCEL-COST", 5m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        await using (var context = run.Context(CompanyA))
        {
            await LedgerWriter(context, CompanyA).StageAsync(sourceLocation, source.ProductId,
                CompanyWidePurchase(5m, 0m, 10m));
            await context.SaveChangesAsync();
        }

        var plan = TransferPlan(source, sourceLocation, destinationLocation, "5");
        var request = TransferDispatch();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(plan, Metadata(run), Authorized);
            await scope.Store.DispatchAsync(plan.TransferId, request, EffectiveOutgoing(plan, request),
                InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
        }

        // The dispatch posts nothing to the ledger: the source row is untouched and no row appears elsewhere.
        Assert.Equal(0m, await TransferLabelKgAsync(run, source.LabelId));
        Assert.Equal(1, await LedgerRowCountAsync(run, source.ProductId));
        Assert.Equal((5m, 10m, 1L), await LedgerFiguresAsync(run, CompanyA, sourceLocation, source.ProductId));

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CancelDispatchAsync(plan.TransferId,
                new CancelInventoryTransferDispatchV1("2", At.AddHours(2), TransferDispatchCancelReason),
                null, null, Metadata(run), Authorized);
        }

        // KLN-D64 (2): the stock is back at its source position and the cancellation, like the dispatch, wrote no ledger.
        Assert.Equal((5m, sourceLocation), (await TransferLabelKgAsync(run, source.LabelId),
            await LabelLocationAsync(run, source.LabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 11"));
        Assert.Equal(1, await LedgerRowCountAsync(run, source.ProductId));
        Assert.Equal((5m, 10m, 1L), await LedgerFiguresAsync(run, CompanyA, sourceLocation, source.ProductId));
    }
}

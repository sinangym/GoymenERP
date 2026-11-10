using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D262..D267 (SINAN_KARARLARI_2026-11-10_SATIS_EK52) over real SQL: the average-cost ledger is company-filtered and
/// read only through the versioned port V1 (NoSource without a row, Value / Unreadable from a stored row, another
/// company's row invisible); SQL itself refuses malformed rows, a second row for one position, a location of another
/// company and a stale writer; the migration rolls back while empty and refuses (forward fix) after a row exists.
/// No price input source is connected yet (NEEDS-SINAN), so nothing writes the ledger in this slice. SENTETIK data only.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Average_cost_port_answers_no_source_without_a_row_and_reads_only_its_own_company()
    {
        await using var run = await Run.StartAsync();
        var locationA = await run.LocationOfAsync(CompanyA);
        var locationB = await run.LocationOfAsync(CompanyB);
        var product = Guid.NewGuid();

        Assert.Equal(InventoryAverageCostReadStatusV1.NoSource, (await AverageCostReadAsync(run, CompanyA, locationA, product)).Status);

        await InsertKnownAverageCostAsync(run, CompanyA, locationA, product, 7m, 10.5714m);
        await InsertKnownAverageCostAsync(run, CompanyB, locationB, product, 3m, 99.1234m);
        var unreadableProduct = Guid.NewGuid();
        await run.ExecuteAsync(
            """
            INSERT INTO inventory.AverageCostLedger
                (Id, CompanyId, LocationId, ProductId, Status, UnreadableReason, QuantityKg, AverageCostPerKg, Version, UpdatedAtUtc)
            VALUES (NEWID(), @company, @location, @product, 2, 3, NULL, NULL, 2, SYSUTCDATETIME());
            """, ("@company", CompanyA), ("@location", locationA), ("@product", unreadableProduct));

        var own = await AverageCostReadAsync(run, CompanyA, locationA, product);
        Assert.Equal((InventoryAverageCostReadStatusV1.Value, 10.5714m, null, 1L, CompanyA),
            (own.Status, own.AverageCostPerKg, own.UnreadableCode, own.LedgerVersion, own.CompanyId));

        var unreadable = await AverageCostReadAsync(run, CompanyA, locationA, unreadableProduct);
        Assert.Equal((InventoryAverageCostReadStatusV1.Unreadable, null, "foreign_currency_undecided", 2L),
            (unreadable.Status, unreadable.AverageCostPerKg, unreadable.UnreadableCode, unreadable.LedgerVersion));

        // Company B's figure for the same product is invisible to company A, even naming B's location.
        Assert.Equal(InventoryAverageCostReadStatusV1.NoSource, (await AverageCostReadAsync(run, CompanyA, locationB, product)).Status);
        var other = await AverageCostReadAsync(run, CompanyB, locationB, product);
        Assert.Equal((InventoryAverageCostReadStatusV1.Value, 99.1234m), (other.Status, other.AverageCostPerKg));
    }

    [SqlServerFact]
    public async Task Average_cost_ledger_rows_are_constrained_by_SQL()
    {
        await using var run = await Run.StartAsync();
        var locationA = await run.LocationOfAsync(CompanyA);
        var locationB = await run.LocationOfAsync(CompanyB);
        var product = Guid.NewGuid();
        await InsertKnownAverageCostAsync(run, CompanyA, locationA, product, 1m, 5m);

        // CK_AverageCostLedger_State: known needs both figures non-negative and no reason; unreadable needs a reason 1..8 and no figures.
        foreach (var values in new[]
        {
            "1, NULL, -1, 5, 1",   // negative quantity
            "1, NULL, 1, -5, 1",   // negative average
            "1, NULL, NULL, 5, 1", // known without quantity
            "1, 3, 1, 5, 1",       // known with a reason
            "2, NULL, NULL, NULL, 1", // unreadable without a reason
            "2, 9, NULL, NULL, 1", // unknown reason
            "2, 3, 1, NULL, 1",    // unreadable carrying a quantity
            "2, 3, NULL, 5, 1",    // unreadable carrying an average
            "3, NULL, 1, 5, 1",    // unknown status
            "1, NULL, 1, 5, 0",    // version below 1
        })
        {
            var rejected = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
                $"""
                INSERT INTO inventory.AverageCostLedger
                    (Id, CompanyId, LocationId, ProductId, Status, UnreadableReason, QuantityKg, AverageCostPerKg, Version, UpdatedAtUtc)
                VALUES (NEWID(), @company, @location, NEWID(), {values}, SYSUTCDATETIME());
                """, ("@company", CompanyA), ("@location", locationA)));
            Assert.Equal(547, rejected.Number);
        }

        // A second row for one position is the unique index (2601/2627), not a second figure.
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertKnownAverageCostAsync(run, CompanyA, locationA, product, 2m, 6m));
        Assert.Contains(duplicate.Number, new[] { 2601, 2627 });

        // A location of another company is refused by the composite foreign key.
        var foreign = await Assert.ThrowsAsync<SqlException>(() => InsertKnownAverageCostAsync(run, CompanyA, locationB, Guid.NewGuid(), 1m, 1m));
        Assert.Equal(547, foreign.Number);

        // The nil identity is refused.
        var nil = await Assert.ThrowsAsync<SqlException>(() => InsertKnownAverageCostAsync(run, CompanyA, locationA, Guid.Empty, 1m, 1m));
        Assert.Equal(547, nil.Number);

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.AverageCostLedger"));
    }

    [SqlServerFact]
    public async Task Average_cost_ledger_refuses_a_stale_writer_by_version()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var product = Guid.NewGuid();
        await InsertKnownAverageCostAsync(run, CompanyA, location, product, 4m, 8m);

        await using var first = run.Context(CompanyA);
        await using var second = run.Context(CompanyA);
        var rowOfFirst = await first.AverageCostLedger.SingleAsync(row => row.ProductId == product);
        var rowOfSecond = await second.AverageCostLedger.SingleAsync(row => row.ProductId == product);

        rowOfSecond.AverageCostPerKg = 9m;
        rowOfSecond.Version = 2;
        await second.SaveChangesAsync();

        rowOfFirst.AverageCostPerKg = 7m;
        rowOfFirst.Version = 2;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => first.SaveChangesAsync());

        Assert.Equal(9m, await run.ScalarAsync<decimal>(
            "SELECT AverageCostPerKg FROM inventory.AverageCostLedger WHERE ProductId = @p", ("@p", product)));
    }

    [SqlServerFact]
    public async Task Average_cost_ledger_migration_rolls_back_when_empty_and_blocks_after_a_row()
    {
        const string previousMigration = "20261107081411_AddManufacturingPlanInputAllocations";
        const string currentMigration = "20261110065424_AddInventoryAverageCostLedger";
        await using var run = await Run.StartAsync(migrateInventory: false);
        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'inventory') AND name = N'AverageCostLedger'"));
            await context.GetService<IMigrator>().MigrateAsync(currentMigration);
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'inventory') AND name = N'AverageCostLedger'"));
            // Empty: Down passes the guard and drops the table; Up recreates it.
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'inventory') AND name = N'AverageCostLedger'"));
            await context.Database.MigrateAsync();
        }

        await InsertKnownAverageCostAsync(run, CompanyA, await run.LocationOfAsync(CompanyA), Guid.NewGuid(), 1m, 1m);

        await using (var context = run.Context(CompanyA))
        {
            var rejected = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(previousMigration));
            Assert.Equal(51250, rejected.Number);
            Assert.Equal(context.Database.GetMigrations().Last(),
                (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.AverageCostLedger"));
    }

    private static async Task<InventoryAverageCostReadAnswerV1> AverageCostReadAsync(
        Run run, Guid company, Guid location, Guid product)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(company);
        await using var context = run.Context(company);
        return await new InventoryAverageCostReadPort(context, companyContext).ReadAsync(location, product);
    }

    private static Task InsertKnownAverageCostAsync(
        Run run, Guid company, Guid location, Guid product, decimal quantityKg, decimal averageCostPerKg) =>
        run.ExecuteAsync(
            """
            INSERT INTO inventory.AverageCostLedger
                (Id, CompanyId, LocationId, ProductId, Status, UnreadableReason, QuantityKg, AverageCostPerKg, Version, UpdatedAtUtc)
            VALUES (NEWID(), @company, @location, @product, 1, NULL, @quantity, @average, 1, SYSUTCDATETIME());
            """,
            ("@company", company), ("@location", location), ("@product", product),
            ("@quantity", quantityKg), ("@average", averageCostPerKg));
}

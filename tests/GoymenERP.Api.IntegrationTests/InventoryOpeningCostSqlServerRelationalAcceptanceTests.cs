using GoymenERP.Modules.Inventory.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D304 / SAT-D372 over real SQL: an opening-stock cost is a pending submission that changes no cost ledger until a
/// different actor approves it; the approval opens the position once at the entered cost for the company's own stock
/// on hand; a rejection changes nothing; a tracked position, a position without stock and a zero cost are refused;
/// the migration rolls back while empty and refuses (forward fix) after a row exists. SENTETIK data only.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private static readonly Func<CancellationToken, Task> OpeningCostFence = static _ => Task.CompletedTask;

    [SqlServerFact]
    public async Task Opening_cost_is_pending_until_another_actor_approves_then_opens_the_ledger_once()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-OPENING-COST", 100m);
        var location = await run.LocationOfAsync(CompanyA);
        var request = new SubmitInventoryOpeningCostV1(Guid.NewGuid(), location, source.ProductId, "12.5",
            "SENTETIK acilis sayimi");
        var submitMetadata = Metadata(run);
        var approver = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
        var approve = new DecideInventoryOpeningCostV1(InventoryOpeningCostDecisionKindV1.Approve, "SENTETIK onay");

        await using (var scope = run.Store(CompanyA))
        {
            var submitted = await scope.Store.SubmitOpeningCostAsync(request, submitMetadata, OpeningCostFence);
            Assert.Equal((InventoryOpeningCostStatusesV1.PendingApproval, "12.5", false, CompanyA),
                (submitted.OpeningCost.Status, submitted.OpeningCost.UnitCostPerKg, submitted.IsReplay,
                    submitted.OpeningCost.CompanyId));
            Assert.Null(submitted.OpeningCost.Decision);
        }

        // SAT-D304 (2): a pending submission changes no ledger.
        Assert.Equal(0, await OpeningLedgerRowsAsync(run, location, source.ProductId));

        await using (var scope = run.Store(CompanyA))
        {
            var keyReplay = await scope.Store.SubmitOpeningCostAsync(request, submitMetadata, OpeningCostFence);
            Assert.True(keyReplay.IsReplay);
            var identityReplay = await scope.Store.SubmitOpeningCostAsync(request,
                Metadata(run) with { IdempotencyKey = Guid.NewGuid().ToString("N") }, OpeningCostFence);
            Assert.Equal((true, InventoryOpeningCostStatusesV1.PendingApproval),
                (identityReplay.IsReplay, identityReplay.OpeningCost.Status));

            await AssertOpeningCostRefused(scope.Store.SubmitOpeningCostAsync(request with { UnitCostPerKg = "13" },
                Metadata(run), OpeningCostFence), 409, InventoryOpeningCostCodesV1.Conflict);
            await AssertOpeningCostRefused(scope.Store.SubmitOpeningCostAsync(
                request with { OpeningCostId = Guid.NewGuid(), UnitCostPerKg = "13" }, Metadata(run), OpeningCostFence),
                409, InventoryOpeningCostCodesV1.PendingExists);

            // SAT-D304 (1): the submitter decides neither way.
            await AssertOpeningCostRefused(scope.Store.DecideOpeningCostAsync(request.OpeningCostId, approve,
                Metadata(run), OpeningCostFence), 409, InventoryOpeningCostCodesV1.SelfApproval);
        }

        Assert.Equal(0, await OpeningLedgerRowsAsync(run, location, source.ProductId));

        await using (var scope = run.Store(CompanyA))
        {
            var approved = await scope.Store.DecideOpeningCostAsync(request.OpeningCostId, approve, approver,
                OpeningCostFence);
            Assert.Equal((InventoryOpeningCostStatusesV1.Approved, InventoryOpeningCostStatusesV1.Approved, "100", false),
                (approved.OpeningCost.Status, approved.OpeningCost.Decision!.Decision,
                    approved.OpeningCost.Decision.CostedStockKg, approved.IsReplay));
        }

        Assert.Equal(1, await OpeningLedgerRowsAsync(run, location, source.ProductId));
        Assert.Equal((1, 100m, 12.5m), (
            await run.ScalarAsync<int>(
                "SELECT Status FROM inventory.AverageCostLedger WHERE CompanyId = @c AND LocationId = @l AND ProductId = @p",
                ("@c", CompanyA), ("@l", location), ("@p", source.ProductId)),
            await run.ScalarAsync<decimal>(
                "SELECT QuantityKg FROM inventory.AverageCostLedger WHERE CompanyId = @c AND LocationId = @l AND ProductId = @p",
                ("@c", CompanyA), ("@l", location), ("@p", source.ProductId)),
            await run.ScalarAsync<decimal>(
                "SELECT AverageCostPerKg FROM inventory.AverageCostLedger WHERE CompanyId = @c AND LocationId = @l AND ProductId = @p",
                ("@c", CompanyA), ("@l", location), ("@p", source.ProductId))));

        await using (var scope = run.Store(CompanyA))
        {
            var keyReplay = await scope.Store.DecideOpeningCostAsync(request.OpeningCostId, approve, approver,
                OpeningCostFence);
            Assert.Equal((true, InventoryOpeningCostStatusesV1.Approved), (keyReplay.IsReplay, keyReplay.OpeningCost.Status));
            var decisionReplay = await scope.Store.DecideOpeningCostAsync(request.OpeningCostId, approve,
                approver with { IdempotencyKey = Guid.NewGuid().ToString("N") }, OpeningCostFence);
            Assert.True(decisionReplay.IsReplay);

            await AssertOpeningCostRefused(scope.Store.DecideOpeningCostAsync(request.OpeningCostId,
                new DecideInventoryOpeningCostV1(InventoryOpeningCostDecisionKindV1.Reject, "SENTETIK red"),
                approver with { IdempotencyKey = Guid.NewGuid().ToString("N") }, OpeningCostFence),
                409, InventoryOpeningCostCodesV1.AlreadyDecided);
        }

        Assert.Equal(1, await OpeningLedgerRowsAsync(run, location, source.ProductId));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.OpeningCostDecisions WHERE CompanyId = @c AND OpeningCostId = @id",
            ("@c", CompanyA), ("@id", request.OpeningCostId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.OpeningCost' AND EntityId = @id",
            ("@id", request.OpeningCostId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Opening_cost_rejection_changes_nothing_and_tracked_or_stockless_positions_are_refused()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-OPENING-COST-REJECT", 40m);
        var location = await run.LocationOfAsync(CompanyA);
        var request = new SubmitInventoryOpeningCostV1(Guid.NewGuid(), location, source.ProductId, "7.25",
            "SENTETIK acilis");
        var checker = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.SubmitOpeningCostAsync(request, Metadata(run), OpeningCostFence);
            var rejected = await scope.Store.DecideOpeningCostAsync(request.OpeningCostId,
                new DecideInventoryOpeningCostV1(InventoryOpeningCostDecisionKindV1.Reject, "SENTETIK red"), checker,
                OpeningCostFence);
            Assert.Equal((InventoryOpeningCostStatusesV1.Rejected, null), (rejected.OpeningCost.Status,
                rejected.OpeningCost.Decision!.CostedStockKg));
        }

        Assert.Equal(0, await OpeningLedgerRowsAsync(run, location, source.ProductId));

        await using (var scope = run.Store(CompanyA))
        {
            // A rejected submission no longer blocks the position: a new one may wait for a decision.
            await scope.Store.SubmitOpeningCostAsync(request with { OpeningCostId = Guid.NewGuid() }, Metadata(run),
                OpeningCostFence);

            // No own stock of an unknown product at the location: nothing to cost.
            await AssertOpeningCostRefused(scope.Store.SubmitOpeningCostAsync(
                request with { OpeningCostId = Guid.NewGuid(), ProductId = Guid.NewGuid() }, Metadata(run),
                OpeningCostFence), 409, InventoryOpeningCostCodesV1.NoCostedStock);

            await AssertOpeningCostRefused(scope.Store.SubmitOpeningCostAsync(
                request with { OpeningCostId = Guid.NewGuid(), UnitCostPerKg = "0" }, Metadata(run), OpeningCostFence),
                400, InventoryOpeningCostCodesV1.ValidationFailed);

            // A different decision command needs its own key; the rejected decision's key correctly wins before lookup.
            var missingTargetChecker = checker with { IdempotencyKey = Guid.NewGuid().ToString("N") };
            await AssertOpeningCostRefused(scope.Store.DecideOpeningCostAsync(Guid.NewGuid(),
                new DecideInventoryOpeningCostV1(InventoryOpeningCostDecisionKindV1.Approve, "SENTETIK"), missingTargetChecker,
                OpeningCostFence), 404, InventoryOpeningCostCodesV1.NotFound);
        }

        // A position the ledger already tracks (here an unreadable one) is never overwritten by an opening cost.
        var trackedProduct = Guid.NewGuid();
        await run.ExecuteAsync(
            """
            INSERT INTO inventory.AverageCostLedger
                (Id, CompanyId, LocationId, ProductId, Status, UnreadableReason, QuantityKg, AverageCostPerKg, Version, UpdatedAtUtc)
            VALUES (NEWID(), @company, @location, @product, 2, 3, NULL, NULL, 1, SYSUTCDATETIME());
            """, ("@company", CompanyA), ("@location", location), ("@product", trackedProduct));
        await using (var scope = run.Store(CompanyA))
        {
            await AssertOpeningCostRefused(scope.Store.SubmitOpeningCostAsync(
                request with { OpeningCostId = Guid.NewGuid(), ProductId = trackedProduct }, Metadata(run),
                OpeningCostFence), 409, InventoryOpeningCostCodesV1.LedgerAlreadyTracked);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCosts WHERE CompanyId = @c",
            ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.OpeningCostDecisions WHERE CompanyId = @c AND Decision = 2 AND CostedStockKg IS NULL",
            ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task Opening_cost_migration_rolls_back_when_empty_and_blocks_after_a_row()
    {
        const string previousMigration = "20261115205912_AddInventorySupplierReturnExit";
        const string currentMigration = "20261116064346_AddInventoryOpeningCosts";
        const string tableCount =
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'inventory') AND name IN (N'OpeningCosts', N'OpeningCostDecisions')";
        await using var run = await Run.StartAsync(migrateInventory: false);
        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(tableCount));
            await context.GetService<IMigrator>().MigrateAsync(currentMigration);
            Assert.Equal(2, await run.ScalarAsync<int>(tableCount));
            // Empty: Down passes the guard and drops both tables; Up recreates them.
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(tableCount));
            await context.Database.MigrateAsync();
        }

        await run.ExecuteAsync(
            """
            INSERT INTO inventory.OpeningCosts
                (Id, CompanyId, LocationId, ProductId, UnitCostPerKg, Reason, PayloadHash, RecordedAtUtc, RecordedBy, CorrelationId)
            VALUES (NEWID(), @company, @location, NEWID(), 1.5, N'SENTETIK', REPLICATE(N'0', 64), SYSUTCDATETIME(),
                N'synthetic-actor', N'synthetic-correlation');
            """, ("@company", CompanyA), ("@location", await run.LocationOfAsync(CompanyA)));

        await using (var context = run.Context(CompanyA))
        {
            var rejected = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(previousMigration));
            Assert.Equal(51712, rejected.Number);
            Assert.Equal(currentMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OpeningCosts"));
    }

    private static Task<int> OpeningLedgerRowsAsync(Run run, Guid location, Guid product) =>
        run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.AverageCostLedger WHERE CompanyId = @c AND LocationId = @l AND ProductId = @p",
            ("@c", CompanyA), ("@l", location), ("@p", product));

    private static async Task AssertOpeningCostRefused(Task<InventoryOpeningCostResultV1> task, int status, string code)
    {
        var refused = await Assert.ThrowsAsync<InventoryOpeningCostException>(() => task);
        Assert.Equal((status, code), (refused.Status, refused.Code));
    }
}

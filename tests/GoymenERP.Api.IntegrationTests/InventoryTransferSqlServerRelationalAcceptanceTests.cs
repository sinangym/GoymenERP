using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>INV-R20 actual SQL: two-leg whole-label transfer, line-level partial acceptance and exact replay.</summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Transfer_dispatches_whole_labels_into_transit_then_accepts_a_subset_and_reopens_the_same_record()
    {
        await using var run = await Run.StartAsync();
        var first = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-1", 10m);
        var second = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-2", 5m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var transferId = Guid.NewGuid();
        var firstLine = Guid.NewGuid();
        var secondLine = Guid.NewGuid();
        var plan = new PlanInventoryTransferV1(transferId, sourceLocation, "Company", CompanyA,
            destinationLocation, "Company", CompanyA, At,
            [
                new(firstLine, first.LabelId, first.LabelVersion.ToString(), "12"),
                new(secondLine, second.LabelId, second.LabelVersion.ToString(), "5"),
            ]);
        var planMetadata = Metadata(run) with
        {
            IdempotencyKey = $"transfer-plan-{Guid.NewGuid():N}",
            CorrelationId = $"transfer-plan-{Guid.NewGuid():N}",
        };

        await using (var scope = run.Store(CompanyA))
        {
            var planned = await scope.Store.PlanAsync(plan, planMetadata);
            Assert.Equal((false, "1", "Planned"),
                (planned.IsReplay, planned.Transfer.Version, planned.Transfer.Status));
            var replay = await scope.Store.PlanAsync(plan,
                planMetadata with { IdempotencyKey = Guid.NewGuid().ToString("N") });
            Assert.True(replay.IsReplay);
            Assert.Equal(planMetadata.CorrelationId, replay.OriginalCorrelationId);
        }

        var dispatch = new DispatchInventoryTransferV1("1", At.AddHours(1),
            new("inventory.transfer.outgoing-note", Guid.NewGuid(), null));
        var dispatchMetadata = Metadata(run) with
        {
            IdempotencyKey = $"transfer-dispatch-{Guid.NewGuid():N}",
            CorrelationId = $"transfer-dispatch-{Guid.NewGuid():N}",
        };
        await using (var scope = run.Store(CompanyA))
        {
            var dispatched = await scope.Store.DispatchAsync(transferId, dispatch, dispatchMetadata);
            Assert.Equal(("2", "InTransit"), (dispatched.Transfer.Version, dispatched.Transfer.Status));
            Assert.Equal(("10", "-2"),
                (dispatched.Transfer.Lines[0].ActualShippedKg, dispatched.Transfer.Lines[0].DifferenceKg));
            Assert.Equal(("5", "0"),
                (dispatched.Transfer.Lines[1].ActualShippedKg, dispatched.Transfer.Lines[1].DifferenceKg));
        }

        Assert.Equal(0m, await TransferLabelKgAsync(run, first.LabelId));
        Assert.Equal(0m, await TransferLabelKgAsync(run, second.LabelId));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 8 AND SourceKind = N'inventory.transfer' AND SourceId = @id",
            ("@id", transferId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferDispatchLines"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferBuckets"));

        var firstAccept = new AcceptInventoryTransferLineV1("2", At.AddHours(2),
            new("inventory.transfer.incoming-note", Guid.NewGuid(), firstLine));
        var firstAcceptMetadata = Metadata(run) with
        {
            IdempotencyKey = $"transfer-accept-{Guid.NewGuid():N}",
            CorrelationId = $"transfer-accept-{Guid.NewGuid():N}",
        };
        await using (var scope = run.Store(CompanyA))
        {
            var accepted = await scope.Store.AcceptLineAsync(transferId, firstLine, firstAccept, firstAcceptMetadata);
            Assert.Equal(("3", "PartiallyAccepted"), (accepted.Transfer.Version, accepted.Transfer.Status));
            Assert.Equal("Accepted", accepted.Transfer.Lines.Single(line => line.LineId == firstLine).Status);
            Assert.Equal("InTransit", accepted.Transfer.Lines.Single(line => line.LineId == secondLine).Status);

            var replay = await scope.Store.AcceptLineAsync(transferId, firstLine, firstAccept,
                firstAcceptMetadata with { CorrelationId = $"retry-{Guid.NewGuid():N}" });
            Assert.True(replay.IsReplay);
            Assert.Equal(firstAcceptMetadata.CorrelationId, replay.OriginalCorrelationId);
        }

        Assert.Equal(10m, await TransferLabelKgAsync(run, first.LabelId));
        Assert.Equal(0m, await TransferLabelKgAsync(run, second.LabelId));
        Assert.Equal(destinationLocation, await LabelLocationAsync(run, first.LabelId));

        var secondAccept = new AcceptInventoryTransferLineV1("3", At.AddHours(3),
            new("inventory.transfer.incoming-note", Guid.NewGuid(), secondLine));
        await using (var scope = run.Store(CompanyA))
        {
            var accepted = await scope.Store.AcceptLineAsync(transferId, secondLine, secondAccept, Metadata(run));
            Assert.Equal(("4", "Accepted"), (accepted.Transfer.Version, accepted.Transfer.Status));
            var reopened = await scope.Transfers.ReadAsync(transferId);
            Assert.NotNull(reopened);
            Assert.Equal(("4", "Accepted", 2), (reopened.Version, reopened.Status,
                reopened.Lines.Count(line => line.Status == "Accepted")));
        }

        Assert.Equal(5m, await TransferLabelKgAsync(run, second.LabelId));
        Assert.Equal(destinationLocation, await LabelLocationAsync(run, second.LabelId));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 9 AND SourceKind = N'inventory.transfer' AND SourceId = @id",
            ("@id", transferId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferAcceptances"));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.Transfer' AND EntityId = @id",
            ("@id", transferId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Transfer_refuses_stale_foreign_allocated_or_conflicting_commands_without_a_stock_effect()
    {
        await using var run = await Run.StartAsync();
        var own = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-GUARD", 10m);
        var foreign = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-TRANSFER-FOREIGN", 10m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var transferId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var plan = new PlanInventoryTransferV1(transferId, sourceLocation, "Company", CompanyA,
            destinationLocation, "Company", CompanyA, At,
            [new(lineId, own.LabelId, own.LabelVersion.ToString(), "10")]);

        var foreignLocation = await run.LocationOfAsync(CompanyB);
        var foreignTransferId = Guid.NewGuid();
        await using (var foreignScope = run.Store(CompanyB))
        {
            await foreignScope.Store.PlanAsync(new PlanInventoryTransferV1(
                foreignTransferId,
                foreignLocation,
                "Company",
                CompanyB,
                foreignLocation,
                "Company",
                Guid.NewGuid(),
                At,
                [new(Guid.NewGuid(), foreign.LabelId, foreign.LabelVersion.ToString(), "10")]), Metadata(run));
        }

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(plan, Metadata(run));
            var conflict = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.PlanAsync(
                plan with { DestinationCustodianId = Guid.NewGuid() }, Metadata(run)));
            Assert.Equal((409, InventoryTransferCodesV1.IdempotencyConflict), (conflict.Status, conflict.Code));
            Assert.Null(await scope.Transfers.ReadAsync(foreignTransferId));
        }

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(own, "1"), Metadata(run));
            var allocated = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.DispatchAsync(
                transferId, new("1", At.AddHours(1), new("inventory.transfer.outgoing-note", Guid.NewGuid(), null)),
                Metadata(run)));
            Assert.Equal((409, InventoryTransferCodesV1.ActiveAllocationConflict), (allocated.Status, allocated.Code));
        }

        Assert.Equal(10m, await TransferLabelKgAsync(run, own.LabelId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferDispatches"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Kind IN (8, 9)"));

        var staleId = Guid.NewGuid();
        var stale = new PlanInventoryTransferV1(staleId, sourceLocation, "Company", CompanyA,
            destinationLocation, "Company", CompanyA, At,
            [new(Guid.NewGuid(), own.LabelId, (own.LabelVersion + 1).ToString(), "10")]);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(stale, Metadata(run));
            var refused = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.DispatchAsync(
                staleId, new("1", At.AddHours(1), new("inventory.transfer.outgoing-note", Guid.NewGuid(), null)),
                Metadata(run)));
            Assert.Equal((409, InventoryTransferCodesV1.LabelVersionConflict), (refused.Status, refused.Code));
        }

        Assert.Equal(10m, await TransferLabelKgAsync(run, own.LabelId));
    }

    [SqlServerFact]
    public async Task Transfer_migration_rolls_back_when_empty_and_blocks_after_a_plan()
    {
        const string previousMigration = "20260928161227_AddInventoryStockScraps";
        const string currentMigration = "20260930182024_AddInventoryTransfers";
        await using var run = await Run.StartAsync();
        await using (var context = run.Context(CompanyA))
        {
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'inventory') AND name LIKE N'Transfer%'"));
            await context.GetService<IMigrator>().MigrateAsync(currentMigration);
        }

        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-DOWN", 10m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(new PlanInventoryTransferV1(
                Guid.NewGuid(), sourceLocation, "Company", CompanyA,
                destinationLocation, "Company", CompanyA, At,
                [new(Guid.NewGuid(), source.LabelId, source.LabelVersion.ToString(), "10")]), Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            var rejected = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(previousMigration));
            Assert.Equal(51053, rejected.Number);
            Assert.Equal(currentMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Transfers"));
    }

    private static async Task<decimal> TransferLabelKgAsync(Run run, Guid labelId) => await run.ScalarAsync<decimal>(
        "SELECT COALESCE(SUM(QuantityKg), 0) FROM inventory.Balances WHERE LabelId = @id", ("@id", labelId));

    private static async Task<Guid> LabelLocationAsync(Run run, Guid labelId) => await run.ScalarAsync<Guid>(
        "SELECT TOP (1) LocationId FROM inventory.Balances WHERE LabelId = @id AND QuantityKg > 0", ("@id", labelId));

    private static async Task<Guid> TransferLocationAsync(Run run, Guid companyId, int offset) =>
        await run.ScalarAsync<Guid>(
            "SELECT Id FROM organization.Locations WHERE CompanyId = @company ORDER BY Id OFFSET @offset ROWS FETCH NEXT 1 ROWS ONLY",
            ("@company", companyId), ("@offset", offset));
}

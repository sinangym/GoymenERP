using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Physical_sales_allocation_reduces_free_capacity_without_a_movement_and_release_restores_it()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-CREATE", 100m);
        var movementCount = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");
        var createMetadata = Metadata(run) with
        {
            IdempotencyKey = $"allocate-{Guid.NewGuid():N}",
            CorrelationId = $"allocate-{Guid.NewGuid():N}",
        };
        var create = AllocationRequest(source, "60");

        InventoryPhysicalAllocationResultV1 first;
        await using (var scope = run.Store(CompanyA))
        {
            first = await scope.Store.CreateAsync(create, createMetadata);
        }

        Assert.False(first.IsReplay);
        Assert.Equal(("1", "Active", "60", "60"),
            (first.Allocation.Version, first.Allocation.Status, first.Allocation.AllocatedKg, first.Allocation.RemainingKg));
        Assert.Equal(movementCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(40m, await FreeAfterAllocationsAsync(run, source.LabelId));

        await using (var replayScope = run.Store(CompanyA))
        {
            var replay = await replayScope.Store.CreateAsync(create, createMetadata with
            {
                CorrelationId = $"replay-{Guid.NewGuid():N}",
            });
            Assert.True(replay.IsReplay);
            Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        }

        await using (var conflictScope = run.Store(CompanyA))
        {
            var conflict = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                conflictScope.Store.CreateAsync(create with { QuantityKg = "59" }, createMetadata));
            Assert.Equal((409, InventoryAllocationCodesV1.IdempotencyConflict), (conflict.Status, conflict.Code));
        }

        var releaseMetadata = Metadata(run) with
        {
            IdempotencyKey = $"release-{Guid.NewGuid():N}",
            CorrelationId = $"release-{Guid.NewGuid():N}",
        };
        await using (var scope = run.Store(CompanyA))
        {
            var released = await scope.Store.ReleaseAsync(create.AllocationId,
                new ReleasePhysicalAllocationV1("1", "Sipariş ihtiyacı kaldırıldı."), releaseMetadata);
            Assert.Equal(("2", "Released", "0"),
                (released.Allocation.Version, released.Allocation.Status, released.Allocation.RemainingKg));
        }

        await using (var replayScope = run.Store(CompanyA))
        {
            var replay = await replayScope.Store.ReleaseAsync(create.AllocationId,
                new ReleasePhysicalAllocationV1("1", "Sipariş ihtiyacı kaldırıldı."), releaseMetadata);
            Assert.True(replay.IsReplay);
        }

        Assert.Equal(100m, await FreeAfterAllocationsAsync(run, source.LabelId));
        Assert.Equal(movementCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", create.AllocationId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockAllocation' AND EntityId = @id",
            ("@id", create.AllocationId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Physical_sales_allocation_refuses_over_allocation_stale_and_foreign_sources_without_effect()
    {
        await using var run = await Run.StartAsync();
        var own = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-BOUND", 100m);
        var foreign = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-ALLOC-FOREIGN", 100m);

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(own, "80"), Metadata(run));
        }

        var before = await AllocationEffectCountsAsync(run);
        await using (var scope = run.Store(CompanyA))
        {
            var insufficient = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(own, "20.001") with { AllocationId = Guid.NewGuid() }, Metadata(run)));
            Assert.Equal((409, InventoryAllocationCodesV1.InsufficientFreeStock),
                (insufficient.Status, insufficient.Code));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var stale = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(own, "1") with
                {
                    AllocationId = Guid.NewGuid(), ExpectedLabelVersion = own.LabelVersion.ToString(),
                }, Metadata(run)));
            Assert.Equal((409, InventoryAllocationCodesV1.LabelVersionConflict), (stale.Status, stale.Code));
        }

        var activeAllocationId = await run.ScalarAsync<Guid>(
            "SELECT TOP (1) Id FROM inventory.Allocations WHERE LabelId = @id", ("@id", own.LabelId));
        await using (var scope = run.Store(CompanyA))
        {
            var staleRelease = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.ReleaseAsync(activeAllocationId,
                    new ReleasePhysicalAllocationV1("2", "Stale sürüm reddi."), Metadata(run)));
            Assert.Equal((409, InventoryAllocationCodesV1.AllocationVersionConflict),
                (staleRelease.Status, staleRelease.Code));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var hidden = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(foreign, "1") with { AllocationId = Guid.NewGuid() }, Metadata(run)));
            Assert.Equal((404, InventoryAllocationCodesV1.InvalidReference), (hidden.Status, hidden.Code));
        }

        Assert.Equal(before, await AllocationEffectCountsAsync(run));
        Assert.Equal(20m, await FreeAfterAllocationsAsync(run, own.LabelId));
        Assert.Equal(100m, await FreeAfterAllocationsAsync(run, foreign.LabelId));
    }

    [SqlServerFact]
    public async Task Concurrent_physical_allocations_cannot_both_consume_the_same_free_capacity()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ALLOC-RACE", 100m);
        var barrier = new Barrier(2);

        async Task<object> AllocateAsync()
        {
            await using var scope = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return await scope.Store.CreateAsync(
                    AllocationRequest(source, "60") with { AllocationId = Guid.NewGuid() }, Metadata(run));
            }
            catch (InventoryAllocationException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(AllocateAsync), Task.Run(AllocateAsync));

        Assert.Single(results, result => result is InventoryPhysicalAllocationResultV1);
        Assert.Single(results, result => result is string code && code == InventoryAllocationCodesV1.ConcurrentChange);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE LabelId = @id AND Status = 1", ("@id", source.LabelId)));
        Assert.Equal(40m, await FreeAfterAllocationsAsync(run, source.LabelId));
    }

    [SqlServerFact]
    public async Task Definite_shipment_flushes_one_atomic_effect_replays_and_host_rollback_removes_every_effect()
    {
        await using var run = await Run.StartAsync();
        var state = await SeedAllocationAsync(run, "SYN-LOG5-SUCCESS");
        var correlation = $"log5-{Guid.NewGuid():N}";
        var metadata = Metadata(run) with
        {
            IdempotencyKey = $"log5-key-{Guid.NewGuid():N}",
            CorrelationId = correlation,
        };
        var request = Request(state, quantityKg: "40");

        InventoryDefiniteShipmentResultV1 first;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            first = await participant.StageAsync(request, metadata);
            Assert.False(first.IsReplay);
            Assert.Equal("2", first.AllocationVersion);
            Assert.Equal("40", first.RemainingAllocatedKg);
            Assert.Equal((state.LabelVersion + 1).ToString(), first.LabelVersion);
            await transaction.CommitAsync();
        }

        Assert.Equal(40m, await run.ScalarAsync<decimal>(
            "SELECT RemainingKg FROM inventory.Allocations WHERE Id = @id", ("@id", state.AllocationId)));
        Assert.Equal(2L, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Allocations WHERE Id = @id", ("@id", state.AllocationId)));
        Assert.Equal(60m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id", ("@id", state.LabelId)));
        Assert.Equal(state.LabelVersion + 1, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", state.LabelId)));

        InventoryDefiniteShipmentResultV1 replay;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            replay = await participant.StageAsync(request, metadata);
            await transaction.CommitAsync();
        }

        Assert.True(replay.IsReplay);
        Assert.Equal(first.OutboxMessageId, replay.OutboxMessageId);
        Assert.Equal(first.MovementId, replay.MovementId);
        Assert.Equal(correlation, replay.OriginalCorrelationId);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", request.MovementId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.OutboxMessages WHERE Id = @id", ("@id", first.OutboxMessageId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = @name AND CorrelationId = @c",
            ("@name", InventoryDefiniteShipmentOutboxV1.SourceOperation), ("@c", correlation)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @c", ("@c", correlation)));

        var rollbackState = await SeedAllocationAsync(run, "SYN-LOG5-ROLLBACK");
        var movementCount = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");
        var outboxCount = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages");
        var commandCount = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands");
        var auditCount = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            await participant.StageAsync(Request(rollbackState, "10"), metadata with
            {
                IdempotencyKey = $"rollback-{Guid.NewGuid():N}", CorrelationId = $"rollback-{Guid.NewGuid():N}",
            });
            await transaction.RollbackAsync();
        }

        Assert.Equal(movementCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(outboxCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages"));
        Assert.Equal(commandCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"));
        Assert.Equal(auditCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        Assert.Equal(1L, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Allocations WHERE Id = @id", ("@id", rollbackState.AllocationId)));
    }

    [SqlServerFact]
    public async Task Definite_shipment_hides_foreign_references_and_same_key_payload_conflicts_without_effect()
    {
        await using var run = await Run.StartAsync();
        var state = await SeedAllocationAsync(run, "SYN-LOG5-CONFLICT");
        var metadata = Metadata(run) with
        {
            IdempotencyKey = $"log5-conflict-{Guid.NewGuid():N}",
            CorrelationId = $"log5-conflict-{Guid.NewGuid():N}",
        };
        var foreignState = await SeedForeignAllocationAsync(run, "SYN-LOG5-FOREIGN");
        var request = Request(state, "5");
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            await participant.StageAsync(request, metadata);
            await transaction.CommitAsync();
        }

        var beforeConflict = await SnapshotShipmentEffectsAsync(run, state, foreignState);
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            var conflict = await Assert.ThrowsAsync<InventoryDefiniteShipmentException>(() =>
                participant.StageAsync(request with { QuantityKg = "4" }, metadata));
            Assert.Equal((409, InventoryDefiniteShipmentCodesV1.IdempotencyConflict), (conflict.Status, conflict.Code));
            await transaction.RollbackAsync();
        }
        Assert.Equal(beforeConflict, await SnapshotShipmentEffectsAsync(run, state, foreignState));

        var beforeForeign = await SnapshotShipmentEffectsAsync(run, state, foreignState);
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            var hidden = await Assert.ThrowsAsync<InventoryDefiniteShipmentException>(() => participant.StageAsync(
                request with
                {
                    AllocationId = foreignState.AllocationId,
                    LabelId = foreignState.LabelId,
                    ExpectedLabelVersion = foreignState.LabelVersion.ToString(),
                }, metadata with
                {
                    IdempotencyKey = $"foreign-{Guid.NewGuid():N}",
                }));
            Assert.Equal((404, InventoryDefiniteShipmentCodesV1.InvalidReference), (hidden.Status, hidden.Code));
            await transaction.RollbackAsync();
        }
        Assert.Equal(beforeForeign, await SnapshotShipmentEffectsAsync(run, state, foreignState));
    }

    [SqlServerFact]
    public async Task Physical_allocation_is_company_scoped_constrained_and_optimistically_versioned()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, $"SYN-ALLOC-{Guid.NewGuid():N}");
        var position = Position(CompanyA, location, label, BlockState.Free);

        await using (var store = run.Store(CompanyA))
        {
            await store.Labels.SaveLabelAsync(label, Metadata(run));
            await store.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Receipt, In(position, 100m)), Metadata(run));
        }

        var allocationId = Guid.NewGuid();
        await using (var context = run.Context(CompanyA))
        {
            context.Allocations.Add(new AllocationRow
            {
                Id = allocationId,
                CompanyId = CompanyA,
                OwnerKind = (int)position.Owner.Kind,
                OwnerId = position.Owner.Id,
                CustodianKind = (int)position.Custodian.Kind,
                CustodianId = position.Custodian.Id,
                LocationId = position.LocationId,
                LabelId = position.LabelId,
                Block = (int)position.Block,
                AllocatedKg = 80m,
                RemainingKg = 80m,
                Status = (int)StockAllocationStatus.Active,
                Version = 1,
                CreatedAtUtc = At,
                UpdatedAtUtc = At,
            });
            await context.SaveChangesAsync();
        }

        await using (var otherCompany = run.Context(CompanyB))
        {
            Assert.Empty(await otherCompany.Allocations.AsNoTracking().ToListAsync());
        }

        await using var first = run.Context(CompanyA);
        await using var second = run.Context(CompanyA);
        var firstRow = await first.Allocations.SingleAsync(row => row.Id == allocationId);
        var secondRow = await second.Allocations.SingleAsync(row => row.Id == allocationId);
        firstRow.RemainingKg = 50m;
        firstRow.Version = 2;
        firstRow.UpdatedAtUtc = At.AddMinutes(1);
        await first.SaveChangesAsync();

        secondRow.RemainingKg = 40m;
        secondRow.Version = 2;
        secondRow.UpdatedAtUtc = At.AddMinutes(1);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        Assert.Equal(50m, await run.ScalarAsync<decimal>(
            "SELECT RemainingKg FROM inventory.Allocations WHERE Id = @id", ("@id", allocationId)));
        Assert.Equal(2L, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Allocations WHERE Id = @id", ("@id", allocationId)));

        await using var invalid = run.Context(CompanyA);
        invalid.Allocations.Add(new AllocationRow
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyA,
            OwnerKind = (int)position.Owner.Kind,
            OwnerId = position.Owner.Id,
            CustodianKind = (int)position.Custodian.Kind,
            CustodianId = position.Custodian.Id,
            LocationId = position.LocationId,
            LabelId = position.LabelId,
            Block = (int)BlockState.Blocked,
            AllocatedKg = 1m,
            RemainingKg = 1m,
            Status = (int)StockAllocationStatus.Active,
            Version = 1,
            CreatedAtUtc = At,
            UpdatedAtUtc = At,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations"));
    }

    private static async Task<ShipmentState> SeedAllocationAsync(Run run, string code)
    {
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, code);
        var position = Position(CompanyA, location, label, BlockState.Free);
        var seedMetadata = Metadata(run) with { CorrelationId = $"seed-{Guid.NewGuid():N}" };
        await using (var store = run.Store(CompanyA))
        {
            await store.Labels.SaveLabelAsync(label, seedMetadata);
            await store.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Receipt, In(position, 100m)), seedMetadata);
        }

        var allocationId = Guid.NewGuid();
        await using var context = run.Context(CompanyA);
        context.Allocations.Add(new AllocationRow
        {
            Id = allocationId, CompanyId = CompanyA,
            OwnerKind = (int)position.Owner.Kind, OwnerId = position.Owner.Id,
            CustodianKind = (int)position.Custodian.Kind, CustodianId = position.Custodian.Id,
            LocationId = position.LocationId, LabelId = position.LabelId, Block = (int)position.Block,
            AllocatedKg = 80m, RemainingKg = 80m, Status = (int)StockAllocationStatus.Active, Version = 1,
            CreatedAtUtc = At, UpdatedAtUtc = At,
        });
        await context.SaveChangesAsync();
        var labelVersion = await context.Labels.Where(row => row.Id == label.Id).Select(row => row.Version).SingleAsync();
        return new(allocationId, label.Id, labelVersion);
    }

    private static async Task<PhysicalSource> SeedFreePhysicalSourceAsync(
        Run run,
        Guid company,
        string code,
        decimal quantity)
    {
        var location = await run.LocationOfAsync(company);
        var label = NewLabel(company, code);
        var position = Position(company, location, label, BlockState.Free);
        await using (var scope = run.Store(company))
        {
            await scope.Labels.SaveLabelAsync(label, Metadata(run) with { CorrelationId = $"seed-{Guid.NewGuid():N}" });
            await scope.Movements.RecordMovementAsync(
                Movement(company, StockMovementKind.Receipt, In(position, quantity)),
                Metadata(run) with { CorrelationId = $"seed-{Guid.NewGuid():N}" });
        }

        await using var context = run.Context(company);
        var version = await context.Labels.Where(row => row.Id == label.Id).Select(row => row.Version).SingleAsync();
        return new(label.Id, label.ProductId, version);
    }

    private static CreatePhysicalSalesAllocationV1 AllocationRequest(PhysicalSource source, string quantityKg) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), source.ProductId, "7", source.LabelId,
        source.LabelVersion.ToString(), quantityKg, "Onaylı satış satırı için fiziksel kaynak.", null);

    private static async Task<decimal> FreeAfterAllocationsAsync(Run run, Guid labelId) =>
        await run.ScalarAsync<decimal>(
            "SELECT (SELECT COALESCE(SUM(QuantityKg), 0) FROM inventory.Balances WHERE LabelId = @id AND Block = 1) "
            + "- (SELECT COALESCE(SUM(RemainingKg), 0) FROM inventory.Allocations WHERE LabelId = @id AND Status = 1)",
            ("@id", labelId));

    private static async Task<(int Allocations, int Commands, int Audits)> AllocationEffectCountsAsync(Run run) =>
        (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE CommandName LIKE N'inventory.physical-allocation.%'"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Inventory.StockAllocation'")
        );

    private static async Task<ShipmentState> SeedForeignAllocationAsync(Run run, string code)
    {
        var location = await run.LocationOfAsync(CompanyB);
        var label = NewLabel(CompanyB, code);
        var position = Position(CompanyB, location, label, BlockState.Free);
        await using (var store = run.Store(CompanyB))
        {
            await store.Labels.SaveLabelAsync(label,
                Metadata(run) with { CorrelationId = $"foreign-seed-{Guid.NewGuid():N}" });
        }

        var allocationId = Guid.NewGuid();
        await using var context = run.Context(CompanyB);
        context.Balances.Add(new BalanceRow
        {
            Id = Guid.NewGuid(), CompanyId = CompanyB,
            OwnerKind = (int)position.Owner.Kind, OwnerId = position.Owner.Id,
            CustodianKind = (int)position.Custodian.Kind, CustodianId = position.Custodian.Id,
            LocationId = position.LocationId, LabelId = position.LabelId, ProductId = position.ProductId,
            Block = (int)position.Block, QuantityKg = 100m,
        });
        context.Allocations.Add(new AllocationRow
        {
            Id = allocationId, CompanyId = CompanyB,
            OwnerKind = (int)position.Owner.Kind, OwnerId = position.Owner.Id,
            CustodianKind = (int)position.Custodian.Kind, CustodianId = position.Custodian.Id,
            LocationId = position.LocationId, LabelId = position.LabelId, Block = (int)position.Block,
            AllocatedKg = 80m, RemainingKg = 80m, Status = (int)StockAllocationStatus.Active, Version = 1,
            CreatedAtUtc = At, UpdatedAtUtc = At,
        });
        await context.SaveChangesAsync();
        var labelVersion = await context.Labels.Where(row => row.Id == label.Id).Select(row => row.Version).SingleAsync();
        return new(allocationId, label.Id, labelVersion);
    }

    private static async Task<ShipmentEffectSnapshot> SnapshotShipmentEffectsAsync(
        Run run,
        ShipmentState own,
        ShipmentState foreign) =>
        new(
            await run.ScalarAsync<decimal>(
                "SELECT RemainingKg FROM inventory.Allocations WHERE Id = @id", ("@id", own.AllocationId)),
            await run.ScalarAsync<long>(
                "SELECT Version FROM inventory.Allocations WHERE Id = @id", ("@id", own.AllocationId)),
            await run.ScalarAsync<int>(
                "SELECT Status FROM inventory.Allocations WHERE Id = @id", ("@id", own.AllocationId)),
            await run.ScalarAsync<decimal>(
                "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id", ("@id", own.LabelId)),
            await run.ScalarAsync<long>(
                "SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", own.LabelId)),
            await run.ScalarAsync<decimal>(
                "SELECT RemainingKg FROM inventory.Allocations WHERE Id = @id", ("@id", foreign.AllocationId)),
            await run.ScalarAsync<long>(
                "SELECT Version FROM inventory.Allocations WHERE Id = @id", ("@id", foreign.AllocationId)),
            await run.ScalarAsync<int>(
                "SELECT Status FROM inventory.Allocations WHERE Id = @id", ("@id", foreign.AllocationId)),
            await run.ScalarAsync<decimal>(
                "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id", ("@id", foreign.LabelId)),
            await run.ScalarAsync<long>(
                "SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", foreign.LabelId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.MovementLines"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.SourceGrains"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));

    private static InventoryDefiniteShipmentRequestV1 Request(ShipmentState state, string quantityKg) => new(
        CompanyA, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), state.AllocationId, "1",
        state.LabelId, state.LabelVersion.ToString(), Guid.NewGuid(), quantityKg, At);

    private static ISettingsPrerequisiteProvider Settings(Run run, Guid company)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(company);
        return new ApprovedSettingsPrerequisiteProvider(companyContext, new FixedCurrentUser(run.Actor, company));
    }

    private sealed record ShipmentState(Guid AllocationId, Guid LabelId, long LabelVersion);

    private sealed record PhysicalSource(Guid LabelId, Guid ProductId, long LabelVersion);

    private sealed record ShipmentEffectSnapshot(
        decimal OwnAllocationRemainingKg,
        long OwnAllocationVersion,
        int OwnAllocationStatus,
        decimal OwnBalanceQuantityKg,
        long OwnLabelVersion,
        decimal ForeignAllocationRemainingKg,
        long ForeignAllocationVersion,
        int ForeignAllocationStatus,
        decimal ForeignBalanceQuantityKg,
        long ForeignLabelVersion,
        int MovementCount,
        int MovementLineCount,
        int SourceGrainCount,
        int OutboxCount,
        int CommandCount,
        int AuditCount);
}

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

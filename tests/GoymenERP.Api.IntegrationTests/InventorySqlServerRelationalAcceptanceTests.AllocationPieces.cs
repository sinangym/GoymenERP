using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KLN-D119 / SAT-D434 (AK-4b) on real SQL: a physical sales allocation of a piece-tracking label stores its reserved and
/// remaining pieces, refuses a missing, extra, excessive or out-of-step count with nothing written, frees the pieces on
/// release, re-states them on a change, gives them up with the kilograms on a definite shipment, leaves a kg-only label
/// exactly as before, and the migration's forward-fix Down refuses (52022) while any allocation holds pieces.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_physical_allocation_of_a_piece_tracking_label_stores_and_frees_its_pieces_and_a_kg_only_label_is_unchanged_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var counted = await SeedPieceSourceAsync(run, "SYN-AK4B-PIECES", 100m, 10);
        var plain = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-AK4B-KG-ONLY", 100m);

        // Fail-closed: a piece-tracking label is never reserved without its pieces; a kg-only label never with them.
        await using (var scope = run.Store(CompanyA))
        {
            var missing = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(counted, "40"), AllocationMetadata(run)));
            Assert.Equal((422, InventoryAllocationCodesV1.PieceCountRequired), (missing.Status, missing.Code));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var extra = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(plain, "40") with { PieceCount = 4 }, AllocationMetadata(run)));
            Assert.Equal((422, InventoryAllocationCodesV1.PieceCountNotTracked), (extra.Status, extra.Code));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var tooMany = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(counted, "40") with { PieceCount = 11 }, AllocationMetadata(run)));
            Assert.Equal((409, InventoryAllocationCodesV1.InsufficientFreePieces), (tooMany.Status, tooMany.Code));
        }

        await using (var scope = run.Store(CompanyA))
        {
            // Every free piece with kilograms left over: pieces and kilograms run out together.
            var outOfStep = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(counted, "40") with { PieceCount = 10 }, AllocationMetadata(run)));
            Assert.Equal((422, InventoryAllocationCodesV1.PieceCountInvalid), (outOfStep.Status, outOfStep.Code));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations"));

        var create = AllocationRequest(counted, "40") with { PieceCount = 4 };
        var createMetadata = AllocationMetadata(run);
        InventoryPhysicalAllocationResultV1 created;
        await using (var scope = run.Store(CompanyA))
        {
            created = await scope.Store.CreateAsync(create, createMetadata);
        }

        Assert.Equal(("40", "40", (int?)4, (int?)4), (created.Allocation.AllocatedKg, created.Allocation.RemainingKg,
            created.Allocation.AllocatedPieces, created.Allocation.RemainingPieces));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT RemainingPieces FROM inventory.Allocations WHERE Id = @id", ("@id", create.AllocationId)));

        await using (var scope = run.Store(CompanyA))
        {
            // The piece count is part of the stored request: the same key with another count is an idempotency conflict.
            var conflict = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(create with { PieceCount = 3 }, createMetadata));
            Assert.Equal((409, InventoryAllocationCodesV1.IdempotencyConflict), (conflict.Status, conflict.Code));
        }

        await using (var scope = run.Store(CompanyA))
        {
            Assert.True((await scope.Store.CreateAsync(create, createMetadata)).IsReplay);
        }

        // The label version moved with the create; six pieces and 60 kg stay unreserved, and they run out together.
        var afterCreate = counted with { LabelVersion = counted.LabelVersion + 1 };
        await using (var scope = run.Store(CompanyA))
        {
            var beyond = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.CreateAsync(AllocationRequest(afterCreate, "30") with { PieceCount = 7 }, AllocationMetadata(run)));
            Assert.Equal((409, InventoryAllocationCodesV1.InsufficientFreePieces), (beyond.Status, beyond.Code));
        }

        // A kg-only label behaves exactly as before: no piece columns are written.
        var plainCreate = AllocationRequest(plain, "40");
        await using (var scope = run.Store(CompanyA))
        {
            var kgOnly = await scope.Store.CreateAsync(plainCreate, AllocationMetadata(run));
            Assert.Equal(((int?)null, (int?)null), (kgOnly.Allocation.AllocatedPieces, kgOnly.Allocation.RemainingPieces));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE Id = @id AND AllocatedPieces IS NULL AND RemainingPieces IS NULL",
            ("@id", plainCreate.AllocationId)));

        // Resize in place: the allocation holds 2 pieces / 20 kg afterwards; without the pieces the change is refused.
        var change = new ChangePhysicalSalesAllocationV1(created.Allocation.Version, create.SalesOrderId, create.SalesOrderLineId,
            create.ProductId, create.ProductVersion, create.LabelId, afterCreate.LabelVersion.ToString(), "20",
            "SENTETIK adetli tahsis kucultme.", null);
        await using (var scope = run.Store(CompanyA))
        {
            var missing = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
                scope.Store.ChangeAsync(create.AllocationId, change, AllocationMetadata(run)));
            Assert.Equal((422, InventoryAllocationCodesV1.PieceCountRequired), (missing.Status, missing.Code));
        }

        InventoryPhysicalAllocationChangeResultV1 resized;
        await using (var scope = run.Store(CompanyA))
        {
            resized = await scope.Store.ChangeAsync(create.AllocationId, change with { PieceCount = 2 }, AllocationMetadata(run));
        }

        Assert.Equal(("20", (int?)2, (int?)2), (resized.Change.Allocation.RemainingKg, resized.Change.Allocation.AllocatedPieces,
            resized.Change.Allocation.RemainingPieces));

        // Release frees the pieces: the label's ten pieces are all unreserved again.
        await using (var scope = run.Store(CompanyA))
        {
            var released = await scope.Store.ReleaseAsync(create.AllocationId,
                new ReleasePhysicalAllocationV1(resized.Change.Allocation.Version, "SENTETIK adetli tahsis serbest birakma."),
                AllocationMetadata(run));
            Assert.Equal(("Released", "0", (int?)2, (int?)0), (released.Allocation.Status, released.Allocation.RemainingKg,
                released.Allocation.AllocatedPieces, released.Allocation.RemainingPieces));
        }

        var labelVersion = await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", counted.LabelId));
        await using (var scope = run.Store(CompanyA))
        {
            var whole = await scope.Store.CreateAsync(
                AllocationRequest(counted with { LabelVersion = labelVersion }, "100") with { PieceCount = 10 }, AllocationMetadata(run));
            Assert.Equal(("100", (int?)10), (whole.Allocation.RemainingKg, whole.Allocation.RemainingPieces));
        }

        // The check holds in SQL too: half a piece fact, zero reserved pieces and more held than reserved are refused.
        foreach (var update in new[]
                 {
                     "UPDATE inventory.Allocations SET RemainingPieces = NULL WHERE AllocatedPieces IS NOT NULL",
                     "UPDATE inventory.Allocations SET AllocatedPieces = 0 WHERE AllocatedPieces IS NOT NULL",
                     "UPDATE inventory.Allocations SET RemainingPieces = AllocatedPieces + 1 WHERE AllocatedPieces IS NOT NULL",
                 })
        {
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(update))).Number);
        }
    }

    [SqlServerFact]
    public async Task A_definite_shipment_takes_the_allocations_pieces_with_its_kilograms_and_refuses_a_missing_or_out_of_step_count_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var counted = await SeedPieceSourceAsync(run, "SYN-AK4B-SHIP", 100m, 10);
        var create = AllocationRequest(counted, "80") with { PieceCount = 8 };
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(create, AllocationMetadata(run));
        }

        var labelVersion = await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", counted.LabelId));
        InventoryDefiniteShipmentRequestV1 Ship(string version, string kilograms, int? pieces) => new(
            CompanyA, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), create.AllocationId, version, counted.LabelId,
            labelVersion.ToString(), Guid.NewGuid(), kilograms, At, pieces);

        async Task<InventoryDefiniteShipmentResultV1> StageAsync(InventoryDefiniteShipmentRequestV1 request)
        {
            await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction,
                CompanyA, Settings(run, CompanyA));
            var result = await participant.StageAsync(request, AllocationMetadata(run));
            await transaction.CommitAsync();
            return result;
        }

        // The allocation holds pieces: a shipment that names none, more than it holds, or every piece with kilograms left
        // over is refused and nothing moves.
        var missing = await Assert.ThrowsAsync<InventoryDefiniteShipmentException>(() => StageAsync(Ship("1", "40", null)));
        Assert.Equal((400, InventoryDefiniteShipmentCodesV1.InvalidQuantity), (missing.Status, missing.Code));
        var tooMany = await Assert.ThrowsAsync<InventoryDefiniteShipmentException>(() => StageAsync(Ship("1", "40", 9)));
        Assert.Equal((409, InventoryDefiniteShipmentCodesV1.AllocationInsufficient), (tooMany.Status, tooMany.Code));
        var outOfStep = await Assert.ThrowsAsync<InventoryDefiniteShipmentException>(() => StageAsync(Ship("1", "40", 8)));
        Assert.Equal((400, InventoryDefiniteShipmentCodesV1.InvalidQuantity), (outOfStep.Status, outOfStep.Code));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = @kind", ("@kind", (int)StockMovementKind.Issue)));
        Assert.Equal(8, await run.ScalarAsync<int>(
            "SELECT RemainingPieces FROM inventory.Allocations WHERE Id = @id", ("@id", create.AllocationId)));

        var first = await StageAsync(Ship("1", "30", 3));
        Assert.Equal(("2", "50"), (first.AllocationVersion, first.RemainingAllocatedKg));
        Assert.Equal(5, await run.ScalarAsync<int>(
            "SELECT RemainingPieces FROM inventory.Allocations WHERE Id = @id", ("@id", create.AllocationId)));
        Assert.Equal(7, await run.ScalarAsync<int>(
            "SELECT PieceCount FROM inventory.Balances WHERE LabelId = @id AND QuantityKg = 70", ("@id", counted.LabelId)));

        // The last kilograms take the last pieces and close the allocation; the reserved count is kept.
        labelVersion = await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", counted.LabelId));
        var last = await StageAsync(Ship("2", "50", 5));
        Assert.Equal("0", last.RemainingAllocatedKg);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE Id = @id AND Status = 2 AND RemainingKg = 0 AND RemainingPieces = 0 AND AllocatedPieces = 8",
            ("@id", create.AllocationId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT PieceCount FROM inventory.Balances WHERE LabelId = @id AND QuantityKg = 20", ("@id", counted.LabelId)));
    }

    [SqlServerFact]
    public async Task The_allocation_piece_migrations_Down_passes_with_only_kg_only_allocations_and_refuses_while_an_allocation_holds_pieces()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddInventoryAllocationPieces().DownOperations[0]).Sql;
        var plain = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-AK4B-DOWN-KG", 100m);
        var counted = await SeedPieceSourceAsync(run, "SYN-AK4B-DOWN-PIECES", 100m, 10);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(plain, "40"), AllocationMetadata(run));
        }

        // A kg-only allocation loses nothing: the guard passes.
        await run.ExecuteAsync(guard);

        var create = AllocationRequest(counted, "40") with { PieceCount = 4 };
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(create, AllocationMetadata(run));
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(52022, refused.Number);
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT AllocatedPieces FROM inventory.Allocations WHERE Id = @id", ("@id", create.AllocationId)));
    }

    private static InventoryCommandMetadata AllocationMetadata(Run run) => Metadata(run) with
    {
        IdempotencyKey = $"ak4b-{Guid.NewGuid():N}",
        CorrelationId = $"ak4b-{Guid.NewGuid():N}",
    };

    /// <summary>A company-owned, company-held free label that tracks pieces from its receipt.</summary>
    private static async Task<PhysicalSource> SeedPieceSourceAsync(Run run, string code, decimal kilograms, int pieces)
    {
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, code);
        var position = Position(CompanyA, location, label, BlockState.Free);
        await using (var scope = run.Store(CompanyA))
        {
            var seedMetadata = Metadata(run) with { CorrelationId = $"seed-{Guid.NewGuid():N}" };
            await scope.Lots.SaveLotAsync(new MaterialLot(LotA, CompanyA, Product), [], seedMetadata);
            await scope.Labels.SaveLabelAsync(label, seedMetadata);
            await scope.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Receipt, InPieces(position, kilograms, pieces)), seedMetadata);
        }

        var version = await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", label.Id));
        return new PhysicalSource(label.Id, Product, version);
    }
}

using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KLN-D119 (AK-2) on real SQL: a piece-tracking label stores its pieces on the balance and every movement line, a movement
/// that does not know the count is refused with nothing written, a kg-only label stays piece-less, a split conserves pieces,
/// the checks hold, and the migration's forward-fix Down refuses (52021) while any piece count exists.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Piece_counts_are_stored_with_the_kilograms_refused_when_unknown_and_never_backfilled_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var counted = NewLabel(CompanyA, "SYN-AK2-PIECES");
        var plain = NewLabel(CompanyA, "SYN-AK2-KG-ONLY");
        var countedFree = Position(CompanyA, location, counted, BlockState.Free);
        var plainFree = Position(CompanyA, location, plain, BlockState.Free);
        await using (var a = run.Store(CompanyA))
        {
            await a.Labels.SaveLabelAsync(counted, Metadata(run));
            await a.Labels.SaveLabelAsync(plain, Metadata(run));
        }

        var receipt = Movement(CompanyA, StockMovementKind.Receipt, InPieces(countedFree, 100m, 4));
        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Movements.RecordMovementAsync(receipt, Metadata(run)));
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Issue, OutPieces(countedFree, 25m, 1)), Metadata(run)));
            Assert.Equal(InventoryWriteOutcome.Recorded, await a.Movements.RecordMovementAsync(
                Movement(CompanyA, StockMovementKind.Receipt, In(plainFree, 50m)), Metadata(run)));
            // The same receipt replays: the piece count is part of its content.
            Assert.Equal(InventoryWriteOutcome.Replayed, await a.Movements.RecordMovementAsync(receipt, Metadata(run)));
        }

        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT PieceCount FROM inventory.Balances WHERE LabelId = @id AND QuantityKg = 75", ("@id", counted.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.MovementLines WHERE LabelId = @id AND PieceCount IS NOT NULL", ("@id", counted.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Balances WHERE LabelId = @id AND PieceCount IS NULL", ("@id", plain.Id)));

        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryRuleCodes.PieceCountRequired, (await Assert.ThrowsAsync<InventoryRuleException>(() =>
                a.Movements.RecordMovementAsync(Movement(CompanyA, StockMovementKind.Issue, Out(countedFree, 10m)), Metadata(run)))).Code);
        }

        await using (var a = run.Store(CompanyA))
        {
            Assert.Equal(InventoryRuleCodes.PieceCountNotTracked, (await Assert.ThrowsAsync<InventoryRuleException>(() =>
                a.Movements.RecordMovementAsync(Movement(CompanyA, StockMovementKind.Issue, OutPieces(plainFree, 10m, 1)),
                    Metadata(run)))).Code);
        }

        // Nothing of either refusal was written.
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(75m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id", ("@id", counted.Id)));

        // The checks hold in SQL too: pieces without kilograms and negative pieces are refused.
        var pieceless = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE inventory.Balances SET PieceCount = 0 WHERE LabelId = @id", ("@id", counted.Id)));
        Assert.Equal(547, pieceless.Number);
        var negative = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE inventory.MovementLines SET PieceCount = -1 WHERE LabelId = @id", ("@id", counted.Id)));
        Assert.Equal(547, negative.Number);
    }

    [SqlServerFact]
    public async Task A_split_of_a_piece_tracking_label_stores_output_pieces_that_add_up_and_refuses_unequal_pieces_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var source = NewLabel(CompanyA, "SYN-AK2-SPLIT-SOURCE");
        await using (var a = run.Store(CompanyA))
        {
            await a.Labels.SaveLabelAsync(source, Metadata(run));
            await a.Movements.RecordMovementAsync(Movement(CompanyA, StockMovementKind.Receipt,
                InPieces(Position(CompanyA, location, source, BlockState.Free), 100m, 10)), Metadata(run));
        }

        var version = (await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", source.Id))).ToString();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        TransformInventoryLabelsV1 Split(int firstPieces, int secondPieces) => new(Guid.NewGuid(), InventoryLabelTransformationKindV1.Split,
            [new(source.Id, version)],
            [new(first, "SYN-AK2-SPLIT-A", "30", firstPieces), new(second, "SYN-AK2-SPLIT-B", "70", secondPieces)],
            At, "Sentetik adetli etiket bolme kabul testi.");

        await using (var scope = run.Store(CompanyA))
        {
            var refused = await Assert.ThrowsAsync<InventoryLabelGenealogyException>(() => scope.Store.TransformAsync(Split(3, 6),
                Metadata(run) with { IdempotencyKey = $"ak2-split-refused-{Guid.NewGuid():N}" }));
            Assert.Equal(InventoryLabelGenealogyCodesV1.ValidationFailed, refused.Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.LabelTransformations"));
        await using (var scope = run.Store(CompanyA))
        {
            Assert.False((await scope.Store.TransformAsync(Split(3, 7),
                Metadata(run) with { IdempotencyKey = $"ak2-split-{Guid.NewGuid():N}" })).IsReplay);
        }

        Assert.Equal(3, await run.ScalarAsync<int>("SELECT PieceCount FROM inventory.Balances WHERE LabelId = @id", ("@id", first)));
        Assert.Equal(7, await run.ScalarAsync<int>("SELECT PieceCount FROM inventory.Balances WHERE LabelId = @id", ("@id", second)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT PieceCount FROM inventory.Balances WHERE LabelId = @id AND QuantityKg = 0", ("@id", source.Id)));
        Assert.Equal(10, await run.ScalarAsync<int>("SELECT PieceCount FROM inventory.LabelTransformationInputs WHERE LabelId = @id",
            ("@id", source.Id)));
        Assert.Equal(10, await run.ScalarAsync<int>("SELECT SUM(PieceCount) FROM inventory.LabelTransformationOutputs"));
    }

    [SqlServerFact]
    public async Task The_piece_count_migrations_Down_passes_with_only_kg_only_stock_and_refuses_while_a_piece_count_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddInventoryLabelPieceCounts().DownOperations[0]).Sql;
        var location = await run.LocationOfAsync(CompanyA);
        var plain = NewLabel(CompanyA, "SYN-AK2-DOWN-KG");
        var counted = NewLabel(CompanyA, "SYN-AK2-DOWN-PIECES");
        await using (var a = run.Store(CompanyA))
        {
            await a.Labels.SaveLabelAsync(plain, Metadata(run));
            await a.Labels.SaveLabelAsync(counted, Metadata(run));
            await a.Movements.RecordMovementAsync(Movement(CompanyA, StockMovementKind.Receipt,
                In(Position(CompanyA, location, plain, BlockState.Free), 10m)), Metadata(run));
        }

        await run.ExecuteAsync(guard);

        await using (var a = run.Store(CompanyA))
        {
            await a.Movements.RecordMovementAsync(Movement(CompanyA, StockMovementKind.Receipt,
                InPieces(Position(CompanyA, location, counted, BlockState.Free), 10m, 2)), Metadata(run));
        }

        var refusal = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(52021, refusal.Number);
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT PieceCount FROM inventory.Balances WHERE LabelId = @id", ("@id", counted.Id)));
    }

    private static StockMovementLine InPieces(StockPosition position, decimal kilograms, int pieces) =>
        new(position, StockDirection.In, Kilograms.Positive(kilograms), PieceCount.Of(pieces));

    private static StockMovementLine OutPieces(StockPosition position, decimal kilograms, int pieces) =>
        new(position, StockDirection.Out, Kilograms.Positive(kilograms), PieceCount.Of(pieces));
}

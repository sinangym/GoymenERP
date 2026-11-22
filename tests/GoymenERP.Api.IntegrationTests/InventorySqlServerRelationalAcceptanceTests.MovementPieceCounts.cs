using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KLN-D119 (AK-2b) on real SQL: a scrap of a piece-tracking label stores its book and scrapped pieces and its approved
/// write-off moves them, a scrap without the pieces is refused with nothing written, and the migration's forward-fix Down
/// refuses (52031) while any movement piece count exists.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_piece_tracking_scrap_stores_its_pieces_and_its_write_off_moves_them_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var label = await SeedPieceLabelAsync(run, "SYN-AK2B-SCRAP", 100m, 4);
        var version = await LabelVersionAsync(run, label);
        var request = new RecordInventoryStockScrapV1(Guid.NewGuid(), label, version.ToString(), "25", At.AddHours(1),
            "Sentetik adetli hurda.", ScrapPieceCount: 1);

        await using (var scope = run.Store(CompanyA))
        {
            await AssertScrapRefused(scope.Store.RecordStockScrapAsync(request with { ScrapId = Guid.NewGuid(), ScrapPieceCount = null },
                    Metadata(run) with { IdempotencyKey = $"ak2b-missing-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask),
                400, InventoryStockScrapCodesV1.ValidationFailed);
            var recorded = await scope.Store.RecordStockScrapAsync(request,
                Metadata(run) with { IdempotencyKey = $"ak2b-scrap-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask);
            Assert.Equal((4, 1), (recorded.Scrap.BookPieceCount, recorded.Scrap.ScrapPieceCount));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.StockScraps WHERE LabelId = @id AND BookPieceCount = 4 AND ScrapPieceCount = 1",
            ("@id", label)));
        await using (var scope = run.Store(CompanyA))
        {
            var approver = Metadata(run) with
            {
                ActorId = Guid.NewGuid().ToString("D"),
                IdempotencyKey = $"ak2b-approve-{Guid.NewGuid():N}",
            };
            await scope.Store.DecideStockScrapAsync(request.ScrapId,
                new(InventoryStockScrapDecisionKindV1.Approve, "Onay"), approver, static _ => Task.CompletedTask);
        }

        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT PieceCount FROM inventory.Balances WHERE LabelId = @id AND QuantityKg = 75", ("@id", label)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.MovementLines WHERE LabelId = @id AND Direction = 2 AND PieceCount = 1",
            ("@id", label)));
    }

    [SqlServerFact]
    public async Task The_movement_piece_count_migrations_Down_passes_with_a_kg_only_scrap_and_refuses_while_a_piece_scrap_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddInventoryMovementPieceCounts().DownOperations[0]).Sql;
        var plain = await SeedPieceLabelAsync(run, "SYN-AK2B-DOWN-KG", 10m, pieces: null);
        var counted = await SeedPieceLabelAsync(run, "SYN-AK2B-DOWN-PIECES", 10m, 2);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockScrapAsync(new RecordInventoryStockScrapV1(Guid.NewGuid(), plain,
                    (await LabelVersionAsync(run, plain)).ToString(), "1", At.AddHours(1), "Sentetik hurda."),
                Metadata(run) with { IdempotencyKey = $"ak2b-down-kg-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask);
        }

        await run.ExecuteAsync(guard);

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockScrapAsync(new RecordInventoryStockScrapV1(Guid.NewGuid(), counted,
                    (await LabelVersionAsync(run, counted)).ToString(), "1", At.AddHours(1), "Sentetik hurda.", ScrapPieceCount: 0),
                Metadata(run) with { IdempotencyKey = $"ak2b-down-pieces-{Guid.NewGuid():N}" }, static _ => Task.CompletedTask);
        }

        var refusal = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(52031, refusal.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.StockScraps WHERE LabelId = @id AND BookPieceCount = 2", ("@id", counted)));
    }

    private static async Task<Guid> SeedPieceLabelAsync(Run run, string code, decimal kilograms, int? pieces)
    {
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, code);
        var position = Position(CompanyA, location, label, BlockState.Free);
        await using var scope = run.Store(CompanyA);
        await scope.Labels.SaveLabelAsync(label, Metadata(run));
        await scope.Movements.RecordMovementAsync(Movement(CompanyA, StockMovementKind.Receipt,
            pieces is { } count ? InPieces(position, kilograms, count) : In(position, kilograms)), Metadata(run));
        return label.Id;
    }
}

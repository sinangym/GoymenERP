using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    // Actual native Inventory allocation, shipment and return participants plus the Logistics runtime mapping.
    // The return's Logistics delivery/weighing identifiers remain controlled boundary inputs, not full delivery E2E.
    [SqlServerFact]
    public async Task Customer_return_piece_mapping_moves_measured_kilograms_and_actual_pieces_once_on_the_original_shipment_trace()
    {
        await using var run = await Run.StartAsync();
        var counted = await SeedPieceSourceAsync(run, "SYN-RETURN-PIECE-WEIGHING", 100m, 10);
        var allocation = AllocationRequest(counted, "40") with { PieceCount = 4 };
        await using (var scope = run.Store(CompanyA)) await scope.Store.CreateAsync(allocation, AllocationMetadata(run));
        var labelVersion = await run.ScalarAsync<long>("SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", counted.LabelId));
        var shipment = new InventoryDefiniteShipmentRequestV1(CompanyA, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            allocation.AllocationId, "1", counted.LabelId, labelVersion.ToString(), Guid.NewGuid(), "40", At, 4);
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync(); await using var transaction = await connection.BeginTransactionAsync();
            await using var participant = await InventoryDefiniteShipmentParticipant.EnlistAsync(connection, transaction, CompanyA, Settings(run, CompanyA));
            await participant.StageAsync(shipment, AllocationMetadata(run)); await transaction.CommitAsync();
        }
        await using var frame = ReturnStockFrame(run);
        var request = new LogisticsCustomerReturnStockRequestV1(CompanyA, Guid.NewGuid(), shipment.LoadingInstructionId,
            shipment.LoadingInstructionLineId, Guid.NewGuid(), shipment.ShipmentId, allocation.SalesOrderId, allocation.SalesOrderLineId,
            Guid.NewGuid(), 1m, Guid.NewGuid(), At, QuantityKg: 13.125m, PieceCount: 1);
        var envelope = new LogisticsWriteEnvelope(frame.User.Actor, "synthetic.inventory.test-write", [], "SYN-piece-return", null,
            "piece-return-original-key", "Test");
        var first = await frame.Runner.ExecuteApprovalAsync(CompanyA, (stock, token) => stock.StageAsync(request, envelope, token),
            (stock, token) => stock.RequireStillAsync(token));
        Assert.Equal(13.125m, first.Quantity); Assert.Equal(1, first.PieceCount);
        Assert.Equal(shipment.MovementId, first.OriginalShipmentMovementId); Assert.Equal(counted.LabelId, first.OriginalLabelId);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.MovementLines WHERE MovementId = @id AND QuantityKg = 13.125 AND PieceCount = 1",
            ("@id", request.InventoryMovementId)));
        var effects = await ReturnEffectsAsync(run, counted.LabelId);
        Assert.Equal((1, 1, 1, 1), (effects.ReturnMovements, effects.Commands, effects.OutboxMessages, effects.AuditEvents));
        var replay = await frame.Runner.ExecuteApprovalAsync(CompanyA, (stock, token) => stock.StageAsync(request, envelope, token),
            (stock, token) => stock.RequireStillAsync(token));
        Assert.True(replay.IsReplay); Assert.Equal(first, replay with { IsReplay = false });
        Assert.Equal(effects, await ReturnEffectsAsync(run, counted.LabelId));
        Assert.Equal(409, (await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => frame.Runner.ExecuteApprovalAsync(CompanyA,
            (stock, token) => stock.StageAsync(request with { PieceCount = 2 }, envelope, token), (stock, token) => stock.RequireStillAsync(token)))).Status);
        Assert.Equal(effects, await ReturnEffectsAsync(run, counted.LabelId));
    }
}

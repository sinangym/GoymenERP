using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AK-7 (URT-D112) at the store on a REAL SQL Server: a counted output keeps its piece count with the theoretical kg per
/// piece and its source, a correction takes pieces back in the same row as its kg and is replayed by id, the restored
/// order is net of both, and the migration's Down passes while only uncounted outputs and reversals exist and refuses
/// (52061) once a counted output is stored, keeping every row.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    private const string SyntheticPieceSource = "SENTETIK;product:1;formula:v1;unit-profile:-/1";

    [SqlServerFact]
    public async Task The_output_pieces_migrations_Down_passes_with_uncounted_outputs_and_refuses_while_a_counted_output_exists()
    {
        await using var run = await Run.StartAsync();
        var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);
        var uncounted = Guid.NewGuid();
        var counted = Guid.NewGuid();
        var reversalId = Guid.NewGuid();
        await using (var store = run.Store(CompanyA))
        {
            await store.Orders.IssueInputAsync(orderId, 1, Guid.NewGuid(), RollA, Kg(10_000m), At, Metadata(run));
            await store.Corrections.DeclareOutputAsync(orderId, 2, uncounted, RollA, OutputClass.GoodProduct, Kg(1_000m),
                At.AddHours(1), PlanOutputLineReference.ClassLine, Metadata(run));
            await store.Corrections.ReverseOutputAsync(orderId, 3, Guid.NewGuid(), uncounted, Kg(10m), "tartım düzeltmesi",
                At.AddHours(2), Metadata(run));
        }

        var guard = Assert.IsType<SqlOperation>(new AddManufacturingOutputPieces().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + "; SELECT 1");

        var pieces = new OutputPieces(new PieceCount(48), new OutputTheoreticalKilogramsPerPiece(12.345678m,
            SyntheticPieceSource));
        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(ManufacturingWriteOutcome.Recorded, await store.Corrections.DeclareOutputAsync(orderId, 4, counted,
                RollA, OutputClass.GoodProduct, Kg(600m), At.AddHours(3), PlanOutputLineReference.ClassLine, pieces,
                Metadata(run)));
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(52061, refused.Number);

        await using (var store = run.Store(CompanyA))
        {
            var missing = await Assert.ThrowsAsync<ManufacturingRuleException>(() => store.Corrections.ReverseOutputAsync(
                orderId, 5, Guid.NewGuid(), counted, Kg(50m), "sayım düzeltmesi", At.AddHours(4), Metadata(run)));
            Assert.Equal(ManufacturingRuleCodes.OutputPiecesRequired, missing.Code);
            Assert.Equal(ManufacturingWriteOutcome.Recorded, await store.Corrections.ReverseOutputAsync(orderId, 5,
                reversalId, counted, Kg(50m), new PieceCount(4), "sayım düzeltmesi", At.AddHours(4), Metadata(run)));
        }

        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(ManufacturingWriteOutcome.Replayed, await store.Corrections.ReverseOutputAsync(orderId, 5,
                reversalId, counted, Kg(50m), new PieceCount(4), "sayım düzeltmesi", At.AddHours(4), Metadata(run)));
            var stored = (await store.Orders.FindOrderAsync(orderId))!;
            Assert.Equal(6L, stored.Version);
            Assert.Equal(pieces, stored.Outputs.Single(output => output.OutputId == counted).Pieces);
            Assert.Null(stored.Outputs.Single(output => output.OutputId == uncounted).Pieces);
            Assert.Equal(new PieceCount(44), stored.NetPiecesOf(counted));
            Assert.Equal(550m, stored.NetMassOf(counted).Value);
        }

        var stillRefused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(52061, stillRefused.Number);
        Assert.Equal(48, await run.ScalarAsync<int>(
            "SELECT OutputPieceCount FROM manufacturing.Facts WHERE Id = @o", ("@o", counted)));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT PieceCount FROM manufacturing.OutputReversals WHERE Id = @r", ("@r", reversalId)));
        // The check holds the all-or-none columns against a direct write: a count without its theoretical figure is refused.
        await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE manufacturing.Facts SET OutputTheoreticalKgPerPiece = NULL WHERE Id = @o; SELECT 1", ("@o", counted)));
    }
}

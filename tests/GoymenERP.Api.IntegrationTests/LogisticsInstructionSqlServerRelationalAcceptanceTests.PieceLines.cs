using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Quantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals>;
using QuantityUnit = GoymenERP.BuildingBlocks.Quantities.QuantityUnit;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AK-4 (KLN-D119) on real SQL: a piece line's load / shipment physical kg and the dispatch note line's copy are stored, rebuilt
/// exactly, held by their checks, and protected by the forward-fix Down (52052). SENTETIK.
/// </summary>
public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    private static readonly Guid PieceUnit = Guid.Parse("5e0cbe45-61cc-541a-834a-366355de36e5");

    [SqlServerFact]
    public async Task AK4_piece_line_kilograms_are_stored_rebuilt_and_the_migrations_Down_refuses_while_they_exist()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var guard = Assert.IsType<SqlOperation>(new AddLogisticsPieceLineKilograms().DownOperations[0]).Sql;
        // Only kg-line shares in the store: the guard passes.
        await ShippedInstructionAsync(run, owners, "kg-");
        await run.ExecuteAsync(guard);

        Guid id;
        Guid line;
        var shipment = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        await using (var a = run.Store(CompanyA))
        {
            var stored = (await a.Store.StoreInstructionAsync(run.Envelope("piece-create"), Hash("piece-create"), Day, () =>
            {
                var instruction = LoadingInstruction.Create(Guid.NewGuid(), owners.Company, owners.Party, owners.Account, T0);
                instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(owners.Order, Guid.NewGuid(), 0), Guid.NewGuid(),
                    PieceUnit, null, Quantity.Of(100m, QuantityUnit.Piece), T0);
                return instruction;
            })).Instruction.Instruction;
            id = stored.Id;
            line = stored.Lines[0].Id;
            await a.Store.MutateInstructionAsync(run.Envelope("piece-issue"), Hash("piece-issue"), LogisticsCommands.IssueV1, id,
                (instruction, now) => instruction.Issue(2, now), Issued);
            await a.Store.MutateInstructionAsync(run.Envelope("piece-load"), Hash("piece-load"), LogisticsCommands.RecordLoadV1, id,
                (instruction, now) => instruction.RecordLoad(3, Guid.NewGuid(), line, Quantity.Of(40m, QuantityUnit.Piece), MeasurementKind.Measured,
                    now, Kg(495m)), Loaded);
            await a.Store.MutateInstructionAsync(run.Envelope("piece-ship"), Hash("piece-ship"), LogisticsCommands.RecordShipmentV1, id,
                (instruction, now) => instruction.MarkShipped(4, shipment, line, Quantity.Of(40m, QuantityUnit.Piece), now, Kg(495m)), Shipped);
            await a.Store.MutateInstructionAsync(run.Envelope("piece-note"), Hash("piece-note"), LogisticsCommands.CreateDispatchNoteV1, id,
                (instruction, now) => instruction.CreateDispatchNoteDraft(5, noteId, [shipment], run.Actor, now), Drafted);
        }

        Assert.Equal(495m, await run.ScalarAsync<decimal>("SELECT QuantityKg FROM logistics.LoadingShares WHERE ShareId = @id", ("@id", shipment)));
        Assert.Equal(495m, await run.ScalarAsync<decimal>("SELECT QuantityKg FROM logistics.DispatchNoteLines WHERE DispatchNoteId = @id", ("@id", noteId)));
        Assert.Equal(40m, await run.ScalarAsync<decimal>("SELECT Quantity FROM logistics.DispatchNoteLines WHERE DispatchNoteId = @id", ("@id", noteId)));

        // The rebuild replays the kg through the domain.
        await using (var a = run.Store(CompanyA))
        {
            var rebuilt = (await a.Store.FindInstructionAsync(id))!.Instruction;
            Assert.Equal(495m, rebuilt.PhysicalKilograms(line, LoadingShareKind.Shipped).Value);
            Assert.Equal(495m, Assert.Single(Assert.Single(rebuilt.DispatchNotes).Lines).Kilograms!.Value.Value);
        }

        // A non-positive kg is refused by the share's check, whatever writes it.
        var check = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE logistics.DispatchNoteLines SET QuantityKg = 0 WHERE DispatchNoteId = @id", ("@id", noteId)));
        Assert.Equal(547, check.Number);

        // Forward fix: the Down guard refuses while a piece-line kg exists and leaves it in place.
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(52052, refused.Number);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE QuantityKg IS NOT NULL"));
    }
}

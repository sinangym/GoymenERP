using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-020 (K3) on real SQL Server: a loading instruction is cancelled with a reason, never deleted — the row keeps its lines and
/// shares, the cancel columns and the status check hold, the rebuild reaches the Cancelled state, a replay of the same key writes
/// nothing, and the Sales loading read excludes a cancelled instruction's quantities (the passive allocation release) while still
/// counting its lines; the migration's Down refuses with 51265 while a cancelled instruction exists. SENTETIK.
/// </summary>
public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    private static readonly LogisticsMutationAudit InstructionCancelled =
        new("logistics.loading-instruction.cancelled.v1", "CancelInstruction", "SENTETIK yükleme talimatı iptal edildi");

    [SqlServerFact]
    public async Task A_cancelled_instruction_keeps_its_rows_and_cancel_columns_rebuilds_cancelled_and_replays_without_a_second_write()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        Guid id;
        await using (var a = run.Store(CompanyA))
        {
            var stored = (await a.Store.StoreInstructionAsync(run.Envelope("k-cancel-create"), Hash("cancel-create"), Day, () => Instruction(owners))).Instruction;
            id = stored.Instruction.Id;
            var lineId = stored.Instruction.Lines[0].Id;
            await a.Store.MutateInstructionAsync(run.Envelope("k-cancel-issue"), Hash("cancel-issue"), LogisticsCommands.IssueV1, id,
                (instruction, now) => instruction.Issue(2, now), Issued);
            await a.Store.MutateInstructionAsync(run.Envelope("k-cancel-load"), Hash("cancel-load"), LogisticsCommands.RecordLoadV1, id,
                (instruction, now) => instruction.RecordLoad(3, Guid.NewGuid(), lineId, Kg(2m), MeasurementKind.Measured, now), Loaded);
            var cancelled = await a.Store.MutateInstructionAsync(run.Envelope("k-cancel"), Hash("cancel-4"), LogisticsCommands.CancelInstructionV1, id,
                (instruction, now) => instruction.CancelInstruction(4, run.Actor, "SENTETIK neden", now), InstructionCancelled);
            Assert.Equal((LoadingInstructionStatus.Cancelled, 5L), (cancelled!.Instruction.Instruction.Status, cancelled.Instruction.Instruction.Version));
        }

        Assert.Equal(3, await run.ScalarAsync<int>("SELECT [Status] FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
        Assert.Equal(run.Actor, await run.ScalarAsync<string>("SELECT CancelledBy FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
        Assert.Equal("SENTETIK neden", await run.ScalarAsync<string>("SELECT CancelReason FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions WHERE Id = @id AND CancelledAtUtc IS NOT NULL", ("@id", id)));
        // Nothing is deleted: the line and the load share stay.
        Assert.Equal((1, 1), (await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructionLines WHERE InstructionId = @id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE InstructionId = @id", ("@id", id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'logistics.loading-instruction.cancelled.v1' "
            + "AND AfterJson LIKE N'%cancelReason%'", ("@id", id.ToString("D"))));

        await using (var a = run.Store(CompanyA))
        {
            var rebuilt = (await a.Store.FindInstructionAsync(id))!.Instruction;
            Assert.Equal((LoadingInstructionStatus.Cancelled, 5L, run.Actor, "SENTETIK neden"), (rebuilt.Status, rebuilt.Version, rebuilt.CancelledBy, rebuilt.CancelReason));

            // The same key and payload replays the stored answer; no second write.
            var replay = await a.Store.MutateInstructionAsync(run.Envelope("k-cancel"), Hash("cancel-4"), LogisticsCommands.CancelInstructionV1, id,
                (_, _) => throw new InvalidOperationException("a replay never mutates"), InstructionCancelled);
            Assert.Equal(LogisticsWriteOutcome.Replayed, replay!.Outcome);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'logistics.loading-instruction.cancelled.v1'", ("@id", id.ToString("D"))));

        // Company B sees nothing of it and cannot cancel it.
        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindInstructionAsync(id));
        }
    }

    [SqlServerFact]
    public async Task The_sales_loading_read_leaves_a_cancelled_instructions_quantities_out_but_still_counts_its_line()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var orderLineId = Guid.NewGuid();
        var kept = InstructionForOrderLine(owners, orderLineId, 3m);
        var dropped = InstructionForOrderLine(owners, orderLineId, 5m);
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.StoreInstructionAsync(run.Envelope("k-read-keep"), Hash("read-keep"), Day, () => kept);
            await a.Store.StoreInstructionAsync(run.Envelope("k-read-drop"), Hash("read-drop"), Day, () => dropped);
            await a.Store.MutateInstructionAsync(run.Envelope("k-read-drop-cancel"), Hash("read-drop-cancel"), LogisticsCommands.CancelInstructionV1, dropped.Id,
                (instruction, now) => instruction.CancelInstruction(2, run.Actor, "SENTETIK neden", now), InstructionCancelled);
        }

        await using var read = run.LogisticsContext(CompanyA);
        var line = Assert.Single((await read.Port.ReadOrderLoadingAsync(owners.Order)).Lines);
        Assert.Equal((orderLineId, 2, 3m, 0m), (line.OrderLineId, line.InstructionLineCount, line.Instructed, line.Loaded));
    }

    [SqlServerFact]
    public async Task The_instruction_cancel_migrations_Down_passes_with_no_cancelled_instruction_and_refuses_while_one_exists_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var guard = Assert.IsType<SqlOperation>(new AddLoadingInstructionCancel().DownOperations[0]).Sql;
        var instruction = Instruction(owners);
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.StoreInstructionAsync(run.Envelope("k-down-create"), Hash("down-create"), Day, () => instruction);
        }

        // A draft is harmless: the guard passes.
        await run.ExecuteAsync(guard);

        await using (var a = run.Store(CompanyA))
        {
            await a.Store.MutateInstructionAsync(run.Envelope("k-down-cancel"), Hash("down-cancel"), LogisticsCommands.CancelInstructionV1, instruction.Id,
                (item, now) => item.CancelInstruction(2, run.Actor, "SENTETIK neden", now), InstructionCancelled);
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51265, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions WHERE Id = @id AND [Status] = 3", ("@id", instruction.Id)));
    }
}

using System.Net;
using GoymenERP.Api.Endpoints;
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
/// nothing, and a cancelled instruction's lines are left out of the Sales loading read (count and quantities — the passive
/// release), of the aggregate instructed and loaded limits of its order line and of the operations progress read; a cancel racing
/// a load, a shipment or another cancel records exactly one of them; the cancel route answers only with a fresh grant and only for
/// the context company; the migration's Down refuses with 51265 while a cancelled instruction exists. SENTETIK.
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
    public async Task Cancelling_a_weighed_instruction_keeps_its_weighing_row_unchanged_and_the_rebuilt_instruction_still_carries_it()
    {
        // LOJ-D7 / the spec's acceptance line: the weighing row count and content do not change with the cancel.
        await using var run = await Run.StartAsync();
        var (id, _) = await IssuedForCancelRaceAsync(run, "weighed", loaded: 2m);
        var weighingId = Guid.NewGuid();
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.MutateInstructionAsync(run.Envelope("k-weighed-weigh"), Hash("weighed-weigh"), LogisticsCommands.RecordWeighingV1, id,
                (instruction, now) => instruction.RecordWeighing(4, weighingId, WeighingDirection.TareFirst, Kg(15_000m), Kg(15_002.5m), run.Actor, now), Weighed);
        }

        const string WeighingRow = "SELECT CONCAT(CONVERT(nvarchar(36), WeighingId), N'|', CONVERT(nvarchar(40), NetKg), N'|', CONVERT(nvarchar(40), DifferenceKg), N'|', RecordedBy, N'|', "
            + "CONVERT(nvarchar(40), RecordedAtUtc, 127)) FROM logistics.VehicleWeighings WHERE InstructionId = @id";
        var before = await run.ScalarAsync<string>(WeighingRow, ("@id", id));

        await using (var a = run.Store(CompanyA))
        {
            var cancelled = await a.Store.MutateInstructionAsync(run.Envelope("k-weighed-cancel"), Hash("weighed-cancel"), LogisticsCommands.CancelInstructionV1, id,
                (instruction, now) => instruction.CancelInstruction(5, run.Actor, "SENTETIK neden", now), InstructionCancelled);
            Assert.Equal((LoadingInstructionStatus.Cancelled, 6L, weighingId), (cancelled!.Instruction.Instruction.Status, cancelled.Instruction.Instruction.Version,
                cancelled.Instruction.Instruction.Weighing!.WeighingId));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.VehicleWeighings WHERE InstructionId = @id", ("@id", id)));
        Assert.Equal(before, await run.ScalarAsync<string>(WeighingRow, ("@id", id)));
        await using (var a = run.Store(CompanyA))
        {
            var rebuilt = (await a.Store.FindInstructionAsync(id))!.Instruction;
            Assert.Equal((LoadingInstructionStatus.Cancelled, weighingId, 2.5m, 2m, 0.5m), (rebuilt.Status, rebuilt.Weighing!.WeighingId, rebuilt.Weighing.Net.Value,
                rebuilt.Weighing.LoadedAtWeighing.Value, rebuilt.Weighing.DifferenceKg));

            // A cancelled instruction takes no second weighing either.
            var refusal = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(
                run.Envelope("k-weighed-again"), Hash("weighed-again"), LogisticsCommands.RecordWeighingV1, id,
                (instruction, now) => instruction.RecordWeighing(6, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(15_000m), Kg(15_002.5m), run.Actor, now), Weighed));
            Assert.Contains(refusal.Code, new[] { LogisticsRuleCodes.InstructionCancelled, LogisticsRuleCodes.InstructionNotIssued });
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.VehicleWeighings WHERE InstructionId = @id", ("@id", id)));
    }

    [SqlServerFact]
    public async Task The_sales_loading_read_leaves_a_cancelled_instructions_line_out_of_the_count_and_the_quantities()
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

        // Only the active instruction's line is counted: a positive count always carries a positive instructed total (the Sales
        // consumer refuses any other answer as malformed).
        await using var read = run.LogisticsContext(CompanyA);
        var line = Assert.Single((await read.Port.ReadOrderLoadingAsync(owners.Order)).Lines);
        Assert.Equal((orderLineId, 1, 3m, 0m), (line.OrderLineId, line.InstructionLineCount, line.Instructed, line.Loaded));
        var progress = Assert.Single(await read.Progress.ReadAsync([orderLineId]));
        Assert.Equal((orderLineId, 3m, 0m, 0m, 0m), (progress.OrderLineId, progress.Instructed, progress.Loaded, progress.Shipped, progress.Delivered));
    }

    [SqlServerFact]
    public async Task An_order_line_whose_only_instruction_is_cancelled_is_absent_from_the_sales_loading_read_and_the_progress_read()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var orderLineId = Guid.NewGuid();
        var only = InstructionForOrderLine(owners, orderLineId, 5m);
        var lineId = Assert.Single(only.Lines).Id;
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.StoreInstructionAsync(run.Envelope("k-only-create"), Hash("only-create"), Day, () => only);
            await a.Store.MutateInstructionAsync(run.Envelope("k-only-issue"), Hash("only-issue"), LogisticsCommands.IssueV1, only.Id,
                (instruction, now) => instruction.Issue(2, now), Issued);
            await a.Store.MutateInstructionAsync(run.Envelope("k-only-load"), Hash("only-load"), LogisticsCommands.RecordLoadV1, only.Id,
                (instruction, now) => instruction.RecordLoad(3, Guid.NewGuid(), lineId, Kg(2m), MeasurementKind.Measured, now), Loaded);
        }

        await using (var before = run.LogisticsContext(CompanyA))
        {
            var line = Assert.Single((await before.Port.ReadOrderLoadingAsync(owners.Order)).Lines);
            Assert.Equal((1, 5m, 2m), (line.InstructionLineCount, line.Instructed, line.Loaded));
            var progress = Assert.Single(await before.Progress.ReadAsync([orderLineId]));
            Assert.Equal((5m, 2m), (progress.Instructed, progress.Loaded));
        }

        await using (var a = run.Store(CompanyA))
        {
            await a.Store.MutateInstructionAsync(run.Envelope("k-only-cancel"), Hash("only-cancel"), LogisticsCommands.CancelInstructionV1, only.Id,
                (instruction, now) => instruction.CancelInstruction(4, run.Actor, "SENTETIK neden", now), InstructionCancelled);
        }

        // The row stays (nothing is deleted); the owner's answers hold no line for it — an absent line is the valid zero, never a
        // positive count with a zero instructed total.
        Assert.Equal((5m, 2m), (
            await run.ScalarAsync<decimal>("SELECT Instructed FROM logistics.LoadingInstructionLines WHERE LineId = @id", ("@id", lineId)),
            await run.ScalarAsync<decimal>("SELECT Loaded FROM logistics.LoadingInstructionLines WHERE LineId = @id", ("@id", lineId))));
        await using var after = run.LogisticsContext(CompanyA);
        var answer = await after.Port.ReadOrderLoadingAsync(owners.Order);
        Assert.Equal((CompanyA, owners.Order), (answer.CompanyId, answer.OrderId));
        Assert.Empty(answer.Lines);
        Assert.Empty((await after.Port.ReadOrderCancellationImpactAsync(owners.Order)).Lines);
        Assert.Empty(await after.Progress.ReadAsync([orderLineId]));
    }

    [SqlServerFact]
    public async Task A_cancelled_instruction_releases_its_order_lines_instructed_quantity_for_a_new_instruction_under_the_aggregate_limit()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var orderLineId = Guid.NewGuid();
        var first = InstructionForOrderLine(owners, orderLineId, 5m);
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.StoreInstructionAsync(run.Envelope("k-release-first"), Hash("release-first"), Day,
                () => first, aggregateLimits: [AggregateLimit(Assert.Single(first.Lines).Id, owners, orderLineId, 5m, 7, 3)]);
        }

        // While the first instruction is active the whole order line quantity is taken.
        var early = InstructionForOrderLine(owners, orderLineId, 5m);
        await using (var a = run.Store(CompanyA))
        {
            var refusal = await Assert.ThrowsAsync<LogisticsStoreException>(() =>
                a.Store.StoreInstructionAsync(run.Envelope("k-release-early"), Hash("release-early"), Day,
                    () => early, aggregateLimits: [AggregateLimit(Assert.Single(early.Lines).Id, owners, orderLineId, 5m, 7, 3)]));
            Assert.Equal(LogisticsStoreCodes.AggregateLimitExceeded, refusal.Code);
        }

        await using (var a = run.Store(CompanyA))
        {
            await a.Store.MutateInstructionAsync(run.Envelope("k-release-cancel"), Hash("release-cancel"), LogisticsCommands.CancelInstructionV1, first.Id,
                (instruction, now) => instruction.CancelInstruction(2, run.Actor, "SENTETIK neden", now), InstructionCancelled);
        }

        // The cancel released it: a new instruction for the full quantity is stored, and the limit still holds for the next one.
        var second = InstructionForOrderLine(owners, orderLineId, 5m);
        var over = InstructionForOrderLine(owners, orderLineId, 0.001m);
        await using (var a = run.Store(CompanyA))
        {
            var stored = await a.Store.StoreInstructionAsync(run.Envelope("k-release-second"), Hash("release-second"), Day,
                () => second, aggregateLimits: [AggregateLimit(Assert.Single(second.Lines).Id, owners, orderLineId, 5m, 7, 3)]);
            Assert.Equal((LogisticsWriteOutcome.Recorded, second.Id), (stored.Outcome, stored.Instruction.Instruction.Id));
            var refusal = await Assert.ThrowsAsync<LogisticsStoreException>(() =>
                a.Store.StoreInstructionAsync(run.Envelope("k-release-over"), Hash("release-over"), Day,
                    () => over, aggregateLimits: [AggregateLimit(Assert.Single(over.Lines).Id, owners, orderLineId, 5m, 7, 3)]));
            Assert.Equal(LogisticsStoreCodes.AggregateLimitExceeded, refusal.Code);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions"));
        Assert.Equal(5m, await run.ScalarAsync<decimal>(
            "SELECT SUM(l.Instructed) FROM logistics.LoadingInstructionLines l JOIN logistics.LoadingInstructions i ON i.Id = l.InstructionId "
            + "WHERE l.CompanyId = @company AND l.SourceOrderLineId = @line AND i.[Status] <> 3", ("@company", CompanyA), ("@line", orderLineId)));
        await using var read = run.LogisticsContext(CompanyA);
        var line = Assert.Single((await read.Port.ReadOrderLoadingAsync(owners.Order)).Lines);
        Assert.Equal((orderLineId, 1, 5m), (line.OrderLineId, line.InstructionLineCount, line.Instructed));
    }

    [SqlServerFact]
    public async Task Loads_on_a_cancelled_instruction_do_not_count_against_the_aggregate_load_limit_of_another_instruction_of_the_order_line()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var orderLineId = Guid.NewGuid();
        var first = InstructionForOrderLine(owners, orderLineId, 2.5m);
        var second = InstructionForOrderLine(owners, orderLineId, 2.5m);
        var firstLine = Assert.Single(first.Lines);
        var secondLine = Assert.Single(second.Lines);
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.StoreInstructionAsync(run.Envelope("k-load-release-create-1"), Hash("load-release-create-1"), Day,
                () => first, aggregateLimits: [AggregateLimit(firstLine.Id, owners, orderLineId, 5m, 7, 3)]);
            await a.Store.MutateInstructionAsync(run.Envelope("k-load-release-issue-1"), Hash("load-release-issue-1"),
                LogisticsCommands.IssueV1, first.Id, (instruction, now) => instruction.Issue(2, now), Issued);
            await a.Store.StoreInstructionAsync(run.Envelope("k-load-release-create-2"), Hash("load-release-create-2"), Day,
                () => second, aggregateLimits: [AggregateLimit(secondLine.Id, owners, orderLineId, 5m, 7, 3)]);
            await a.Store.MutateInstructionAsync(run.Envelope("k-load-release-issue-2"), Hash("load-release-issue-2"),
                LogisticsCommands.IssueV1, second.Id, (instruction, now) => instruction.Issue(2, now), Issued);
        }

        await using (var a = run.Store(CompanyA))
        {
            var aggregateStore = Assert.IsAssignableFrom<ILogisticsAggregateLoadStoreV1>(a.Store);
            await aggregateStore.MutateInstructionWithAggregateLoadLimitAsync(
                run.Envelope("k-load-release-1"), Hash("load-release-1"), LogisticsCommands.RecordLoadV1, first.Id,
                (instruction, now) => instruction.RecordLoad(3, Guid.NewGuid(), firstLine.Id, Kg(1.5m), MeasurementKind.Measured, now),
                Loaded, AggregateLoadLimit(firstLine.Id, owners, orderLineId, 2m, 1.5m));

            // The first instruction's 1.5 kg is counted while it is active: 0.501 kg more is over the order line's 2 kg.
            var refused = await Assert.ThrowsAsync<LogisticsStoreException>(() =>
                aggregateStore.MutateInstructionWithAggregateLoadLimitAsync(
                    run.Envelope("k-load-release-early"), Hash("load-release-early"), LogisticsCommands.RecordLoadV1, second.Id,
                    (instruction, now) => instruction.RecordLoad(3, Guid.NewGuid(), secondLine.Id, Kg(0.501m), MeasurementKind.Measured, now),
                    Loaded, AggregateLoadLimit(secondLine.Id, owners, orderLineId, 2m, 0.501m)));
            Assert.Equal(LogisticsStoreCodes.AggregateLimitExceeded, refused.Code);

            await a.Store.MutateInstructionAsync(run.Envelope("k-load-release-cancel"), Hash("load-release-cancel"), LogisticsCommands.CancelInstructionV1, first.Id,
                (instruction, now) => instruction.CancelInstruction(4, run.Actor, "SENTETIK neden", now), InstructionCancelled);
        }

        await using (var a = run.Store(CompanyA))
        {
            var aggregateStore = Assert.IsAssignableFrom<ILogisticsAggregateLoadStoreV1>(a.Store);
            // The cancelled instruction's load is released: the other instruction loads the whole 2 kg, and not a gram more.
            var loaded = await aggregateStore.MutateInstructionWithAggregateLoadLimitAsync(
                run.Envelope("k-load-release-2"), Hash("load-release-2"), LogisticsCommands.RecordLoadV1, second.Id,
                (instruction, now) => instruction.RecordLoad(3, Guid.NewGuid(), secondLine.Id, Kg(2m), MeasurementKind.Measured, now),
                Loaded, AggregateLoadLimit(secondLine.Id, owners, orderLineId, 2m, 2m));
            Assert.Equal((4L, 2m), (loaded!.Instruction.Instruction.Version, loaded.Instruction.Instruction.Lines[0].LoadedQuantity.Value));
            var over = await Assert.ThrowsAsync<LogisticsStoreException>(() =>
                aggregateStore.MutateInstructionWithAggregateLoadLimitAsync(
                    run.Envelope("k-load-release-over"), Hash("load-release-over"), LogisticsCommands.RecordLoadV1, second.Id,
                    (instruction, now) => instruction.RecordLoad(4, Guid.NewGuid(), secondLine.Id, Kg(0.001m), MeasurementKind.Measured, now),
                    Loaded, AggregateLoadLimit(secondLine.Id, owners, orderLineId, 2m, 0.001m)));
            Assert.Equal(LogisticsStoreCodes.AggregateLimitExceeded, over.Code);
        }

        // The cancelled instruction keeps its own loaded total and share (nothing deleted).
        Assert.Equal((1.5m, 2m), (
            await run.ScalarAsync<decimal>("SELECT Loaded FROM logistics.LoadingInstructionLines WHERE LineId = @id", ("@id", firstLine.Id)),
            await run.ScalarAsync<decimal>("SELECT Loaded FROM logistics.LoadingInstructionLines WHERE LineId = @id", ("@id", secondLine.Id))));
    }

    [SqlServerFact]
    public async Task A_cancel_racing_a_load_records_exactly_one_of_them_and_the_loser_then_meets_the_winners_state()
    {
        await using var run = await Run.StartAsync();
        var (id, lineId) = await IssuedForCancelRaceAsync(run, "load-race", loaded: null);
        var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => RaceAsync(run, barrier, "k-load-race-cancel", LogisticsCommands.CancelInstructionV1, id,
                (instruction, now) => instruction.CancelInstruction(3, run.Actor, "SENTETIK neden", now), InstructionCancelled)),
            Task.Run(() => RaceAsync(run, barrier, "k-load-race-load", LogisticsCommands.RecordLoadV1, id,
                (instruction, now) => instruction.RecordLoad(3, Guid.NewGuid(), lineId, Kg(2m), MeasurementKind.Measured, now), Loaded)));

        Assert.Single(results, result => result is LogisticsWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == LogisticsStoreCodes.ConcurrentChange);
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
        var cancelWon = results[0] is LogisticsWriteOutcome.Recorded;
        await using var a = run.Store(CompanyA);
        if (cancelWon)
        {
            // The cancel stands alone: no load share, no loaded quantity; the load now meets a cancelled instruction.
            Assert.Equal((3, 0, 0m), await CancelRaceStateAsync(run, id, lineId));
            var refusal = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(
                run.Envelope("k-load-race-load-retry"), Hash("load-race-load-retry"), LogisticsCommands.RecordLoadV1, id,
                (instruction, now) => instruction.RecordLoad(4, Guid.NewGuid(), lineId, Kg(2m), MeasurementKind.Measured, now), Loaded));
            Assert.Contains(refusal.Code, new[] { LogisticsRuleCodes.InstructionCancelled, LogisticsRuleCodes.InstructionNotIssued });
            Assert.Equal((3, 0, 0m), await CancelRaceStateAsync(run, id, lineId));
        }
        else
        {
            // The load stands; loaded-but-not-shipped is still cancellable on the fresh version (F-020 m.1).
            Assert.Equal((2, 1, 2m), await CancelRaceStateAsync(run, id, lineId));
            var cancelled = await a.Store.MutateInstructionAsync(run.Envelope("k-load-race-cancel-retry"), Hash("load-race-cancel-retry"),
                LogisticsCommands.CancelInstructionV1, id,
                (instruction, now) => instruction.CancelInstruction(4, run.Actor, "SENTETIK neden", now), InstructionCancelled);
            Assert.Equal((LogisticsWriteOutcome.Recorded, LoadingInstructionStatus.Cancelled), (cancelled!.Outcome, cancelled.Instruction.Instruction.Status));
            Assert.Equal((3, 1, 2m), await CancelRaceStateAsync(run, id, lineId));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'logistics.loading-instruction.cancelled.v1'", ("@id", id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task A_cancel_racing_a_shipment_records_exactly_one_of_them_and_never_leaves_a_cancelled_instruction_with_a_shipment()
    {
        await using var run = await Run.StartAsync();
        var (id, lineId) = await IssuedForCancelRaceAsync(run, "ship-race", loaded: 5m);
        var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => RaceAsync(run, barrier, "k-ship-race-cancel", LogisticsCommands.CancelInstructionV1, id,
                (instruction, now) => instruction.CancelInstruction(4, run.Actor, "SENTETIK neden", now), InstructionCancelled)),
            Task.Run(() => RaceAsync(run, barrier, "k-ship-race-ship", LogisticsCommands.RecordShipmentV1, id,
                (instruction, now) => instruction.MarkShipped(4, Guid.NewGuid(), lineId, Kg(3m), now), Shipped)));

        Assert.Single(results, result => result is LogisticsWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == LogisticsStoreCodes.ConcurrentChange);
        Assert.Equal(5L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
        var cancelWon = results[0] is LogisticsWriteOutcome.Recorded;
        await using var a = run.Store(CompanyA);
        if (cancelWon)
        {
            var refusal = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(
                run.Envelope("k-ship-race-ship-retry"), Hash("ship-race-ship-retry"), LogisticsCommands.RecordShipmentV1, id,
                (instruction, now) => instruction.MarkShipped(5, Guid.NewGuid(), lineId, Kg(3m), now), Shipped));
            Assert.Contains(refusal.Code, new[] { LogisticsRuleCodes.InstructionCancelled, LogisticsRuleCodes.InstructionNotIssued });
            Assert.Equal((3, 0m), (
                await run.ScalarAsync<int>("SELECT [Status] FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
                await run.ScalarAsync<decimal>("SELECT Shipped FROM logistics.LoadingInstructionLines WHERE LineId = @id", ("@id", lineId))));
        }
        else
        {
            // The shipment stands: the cancel is refused for good on the fresh version (F-020 m.2), the instruction stays Issued.
            var refusal = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(
                run.Envelope("k-ship-race-cancel-retry"), Hash("ship-race-cancel-retry"), LogisticsCommands.CancelInstructionV1, id,
                (instruction, now) => instruction.CancelInstruction(5, run.Actor, "SENTETIK neden", now), InstructionCancelled));
            Assert.Equal(LogisticsRuleCodes.InstructionShipped, refusal.Code);
            Assert.Equal((2, 3m), (
                await run.ScalarAsync<int>("SELECT [Status] FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
                await run.ScalarAsync<decimal>("SELECT Shipped FROM logistics.LoadingInstructionLines WHERE LineId = @id", ("@id", lineId))));
        }

        // Either way: no cancelled instruction carries a shipped quantity.
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM logistics.LoadingInstructionLines l JOIN logistics.LoadingInstructions i ON i.Id = l.InstructionId "
            + "WHERE i.[Status] = 3 AND l.Shipped > 0"));
    }

    [SqlServerFact]
    public async Task Two_cancels_racing_under_different_keys_record_one_cancel_one_audit_and_the_loser_meets_a_cancelled_instruction()
    {
        await using var run = await Run.StartAsync();
        var (id, lineId) = await IssuedForCancelRaceAsync(run, "twin-race", loaded: null);
        var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => RaceAsync(run, barrier, "k-twin-race-1", LogisticsCommands.CancelInstructionV1, id,
                (instruction, now) => instruction.CancelInstruction(3, run.Actor, "SENTETIK neden 1", now), InstructionCancelled)),
            Task.Run(() => RaceAsync(run, barrier, "k-twin-race-2", LogisticsCommands.CancelInstructionV1, id,
                (instruction, now) => instruction.CancelInstruction(3, run.Actor, "SENTETIK neden 2", now), InstructionCancelled)));

        Assert.Single(results, result => result is LogisticsWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == LogisticsStoreCodes.ConcurrentChange);
        Assert.Equal((3, 0, 0m), await CancelRaceStateAsync(run, id, lineId));
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
        var winner = results[0] is LogisticsWriteOutcome.Recorded ? "SENTETIK neden 1" : "SENTETIK neden 2";
        Assert.Equal(winner, await run.ScalarAsync<string>("SELECT CancelReason FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));

        await using (var a = run.Store(CompanyA))
        {
            // The loser on the fresh version meets a cancelled instruction; on its stale version, a version conflict. Nothing moves.
            var refusal = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(
                run.Envelope("k-twin-race-retry"), Hash("twin-race-retry"), LogisticsCommands.CancelInstructionV1, id,
                (instruction, now) => instruction.CancelInstruction(4, run.Actor, "SENTETIK neden 3", now), InstructionCancelled));
            Assert.Equal(LogisticsRuleCodes.InstructionCancelled, refusal.Code);
            await Assert.ThrowsAsync<LogisticsVersionConflictException>(() => a.Store.MutateInstructionAsync(
                run.Envelope("k-twin-race-stale"), Hash("twin-race-stale"), LogisticsCommands.CancelInstructionV1, id,
                (instruction, now) => instruction.CancelInstruction(3, run.Actor, "SENTETIK neden 3", now), InstructionCancelled));
        }

        Assert.Equal(winner, await run.ScalarAsync<string>("SELECT CancelReason FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'logistics.loading-instruction.cancelled.v1'", ("@id", id.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE CommandName = @command", ("@command", LogisticsCommands.CancelInstructionV1)));
    }

    [SqlServerFact]
    public async Task The_cancel_route_answers_only_with_a_fresh_cancel_grant_and_only_for_the_context_company()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        Guid id;
        await using (var a = run.Store(CompanyA))
        {
            id = (await a.Store.StoreInstructionAsync(run.Envelope("k-route-create"), Hash("route-create"), Day, () => Instruction(owners))).Instruction.Instruction.Id;
        }

        const string Body = "{\"expectedVersion\":\"2\",\"reason\":\"SENTETIK neden\"}";

        // No cancel grant (the read and create grants are not it): the fence refuses before the store.
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.InstructionsCreate);
        using (var ungranted = run.Factory.CreateClientFor(reader, CompanyA))
        {
            ungranted.DefaultRequestHeaders.Remove("Idempotency-Key");
            ungranted.DefaultRequestHeaders.Remove("X-Correlation-Id");
            Assert.Equal(HttpStatusCode.Forbidden, (await ungranted.SendAsync(Command($"{id:D}/cancel", "SENTETIK-cancel-ungranted", Body))).StatusCode);
        }

        // Another company's holder of the same grant reaches nothing: 404, never a 403 or a cancel.
        var cancellerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.InstructionsCancel);
        using (var clientOfB = run.Factory.CreateClientFor(cancellerOfB, CompanyB))
        {
            clientOfB.DefaultRequestHeaders.Remove("Idempotency-Key");
            clientOfB.DefaultRequestHeaders.Remove("X-Correlation-Id");
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.SendAsync(Command($"{id:D}/cancel", "SENTETIK-cancel-b", Body))).StatusCode);
        }

        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.MutateInstructionAsync(run.Envelope("k-route-b-cancel"), Hash("route-b-cancel"), LogisticsCommands.CancelInstructionV1, id,
                (instruction, now) => instruction.CancelInstruction(2, run.Actor, "SENTETIK neden", now), InstructionCancelled));
        }

        Assert.Equal((1, 2L), (
            await run.ScalarAsync<int>("SELECT [Status] FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
            await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id))));

        var canceller = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.InstructionsCancel);
        using (var granted = run.Factory.CreateClientFor(canceller, CompanyA))
        {
            granted.DefaultRequestHeaders.Remove("Idempotency-Key");
            granted.DefaultRequestHeaders.Remove("X-Correlation-Id");
            // A stale version is the key's recorded terminal 409, replayed on the same key; the instruction stays as it is.
            using var stale = await granted.SendAsync(Command($"{id:D}/cancel", "SENTETIK-cancel-stale", "{\"expectedVersion\":\"1\",\"reason\":\"SENTETIK neden\"}"));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Contains($"\"code\":\"{LogisticsProblemCodesV1.VersionConflict}\"", await stale.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            using var staleReplay = await granted.SendAsync(Command($"{id:D}/cancel", "SENTETIK-cancel-stale", "{\"expectedVersion\":\"1\",\"reason\":\"SENTETIK neden\"}"));
            Assert.Equal(HttpStatusCode.Conflict, staleReplay.StatusCode);
            Assert.Equal("true", Assert.Single(staleReplay.Headers.GetValues("Idempotency-Replayed")));

            // A missing reason is a 400 before any write.
            using var loose = await granted.SendAsync(Command($"{id:D}/cancel", "SENTETIK-cancel-loose", "{\"expectedVersion\":\"2\"}"));
            Assert.Equal(HttpStatusCode.BadRequest, loose.StatusCode);
            Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));

            // F-020: the active instruction's detail carries the three cancellation members, each null.
            using var active = await granted.GetAsync($"{LogisticsEndpoints.InstructionsPath}/{id:D}");
            Assert.Equal(HttpStatusCode.OK, active.StatusCode);
            var activeInstruction = System.Text.Json.JsonDocument.Parse(await active.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetProperty("instruction");
            Assert.All(new[] { "cancelledBy", "cancelledAtUtc", "cancelReason" },
                name => Assert.Equal(System.Text.Json.JsonValueKind.Null, activeInstruction.GetProperty(name).ValueKind));

            using var cancelled = await granted.SendAsync(Command($"{id:D}/cancel", "SENTETIK-cancel", Body));
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            Assert.False(cancelled.Headers.Contains("Idempotency-Replayed"));
            var cancelledText = await cancelled.Content.ReadAsStringAsync();
            Assert.Contains("\"status\":\"Cancelled\"", cancelledText, StringComparison.Ordinal);
            // F-020 (LOJ-D6..D8): the answer carries the recorded facts as stored — the canceller's user id, the UTC time and the reason.
            var cancelledInstruction = System.Text.Json.JsonDocument.Parse(cancelledText).RootElement.GetProperty("detail").GetProperty("instruction");
            var recorded = (
                await run.ScalarAsync<string>("SELECT CancelledBy FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
                await run.ScalarAsync<DateTimeOffset>("SELECT CancelledAtUtc FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
                "SENTETIK neden");
            Assert.Equal(recorded, (cancelledInstruction.GetProperty("cancelledBy").GetString(), cancelledInstruction.GetProperty("cancelledAtUtc").GetDateTimeOffset(),
                cancelledInstruction.GetProperty("cancelReason").GetString()));
            Assert.Equal(TimeSpan.Zero, cancelledInstruction.GetProperty("cancelledAtUtc").GetDateTimeOffset().Offset);
            using var replay = await granted.SendAsync(Command($"{id:D}/cancel", "SENTETIK-cancel", Body));
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));

            // The Cancelled page answers the same facts for the instruction (the company filter is the store's, unchanged).
            using var cancelledPage = await granted.GetAsync($"{LogisticsEndpoints.InstructionsPath}?status=Cancelled");
            Assert.Equal(HttpStatusCode.OK, cancelledPage.StatusCode);
            var listed = Assert.Single(System.Text.Json.JsonDocument.Parse(await cancelledPage.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("id").GetGuid() == id);
            Assert.Equal(recorded, (listed.GetProperty("cancelledBy").GetString(), listed.GetProperty("cancelledAtUtc").GetDateTimeOffset(),
                listed.GetProperty("cancelReason").GetString()));

            // A second cancel on the fresh version is a state 409; the cancelled instruction takes no further command.
            using var again = await granted.SendAsync(Command($"{id:D}/cancel", "SENTETIK-cancel-again", "{\"expectedVersion\":\"3\",\"reason\":\"SENTETIK neden\"}"));
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Contains($"\"code\":\"{LogisticsProblemCodesV1.InstructionState}\"", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Equal((3, 3L), (
            await run.ScalarAsync<int>("SELECT [Status] FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
            await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'logistics.loading-instruction.cancelled.v1'", ("@id", id.ToString("D"))));
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

    /// <summary>An issued one-line instruction (6 kg, version 3), optionally with one load (version 4), for a cancel race.</summary>
    private static async Task<(Guid Id, Guid LineId)> IssuedForCancelRaceAsync(Run run, string prefix, decimal? loaded)
    {
        var owners = Owners.For(CompanyA);
        await using var a = run.Store(CompanyA);
        var stored = (await a.Store.StoreInstructionAsync(run.Envelope($"k-{prefix}-create"), Hash($"{prefix}-create"), Day, () => Instruction(owners))).Instruction;
        var id = stored.Instruction.Id;
        var lineId = stored.Instruction.Lines[0].Id;
        await a.Store.MutateInstructionAsync(run.Envelope($"k-{prefix}-issue"), Hash($"{prefix}-issue"), LogisticsCommands.IssueV1, id,
            (instruction, now) => instruction.Issue(2, now), Issued);
        if (loaded is { } quantity)
        {
            await a.Store.MutateInstructionAsync(run.Envelope($"k-{prefix}-load"), Hash($"{prefix}-load"), LogisticsCommands.RecordLoadV1, id,
                (instruction, now) => instruction.RecordLoad(3, Guid.NewGuid(), lineId, Kg(quantity), MeasurementKind.Measured, now), Loaded);
        }

        return (id, lineId);
    }

    /// <summary>One racing write that meets the other at the save: its outcome, or the store's refusal code.</summary>
    private static async Task<object> RaceAsync(Run run, Barrier barrier, string key, string command, Guid id,
        Action<LoadingInstruction, DateTimeOffset> mutate, LogisticsMutationAudit audit)
    {
        await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
        try
        {
            return (await writer.Store.MutateInstructionAsync(run.Envelope(key), Hash(key), command, id, mutate, audit))!.Outcome;
        }
        catch (LogisticsStoreException exception)
        {
            return exception.Code;
        }
        catch (LogisticsVersionConflictException)
        {
            return "version_conflict";
        }
    }

    /// <summary>(instruction status, load share count, the line's loaded total) as SQL Server holds them.</summary>
    private static async Task<(int Status, int Shares, decimal Loaded)> CancelRaceStateAsync(Run run, Guid id, Guid lineId) => (
        await run.ScalarAsync<int>("SELECT [Status] FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE InstructionId = @id", ("@id", id)),
        await run.ScalarAsync<decimal>("SELECT Loaded FROM logistics.LoadingInstructionLines WHERE LineId = @id", ("@id", lineId)));
}

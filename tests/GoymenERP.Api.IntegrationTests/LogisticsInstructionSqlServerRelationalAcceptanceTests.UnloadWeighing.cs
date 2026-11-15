using System.Net;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// LOJ-D17 / LOJ-D16 on real SQL Server: the manual unload weighing (indirme tartısı) of a cancelled instruction is ONE row in ONE
/// save with its attempt and its audit event (the difference warning carried in the audit), the rebuild reads it back as recorded,
/// the same key answers it again without a second row, a second record, a stale version or another company write nothing, and the
/// cancel, the lines, the shares and the order line's allocation binding stay exactly as they were (LOJ-D16: the cancel does not
/// release the allocation, and the unload weighing does not either). The database itself refuses a row whose numbers contradict
/// its direction or its difference, and a second row for the instruction. The route answers only with the weighing grant and only
/// for the context company. The migration's Down refuses with 51266 while any unload weighing exists. No threshold is stored (a
/// Settings threshold is a separate decision); Inventory is never written. SENTETIK.
/// </summary>
public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    private static readonly LogisticsMutationAudit Unloaded =
        new("logistics.loading-instruction.unload-weighing-recorded.v1", "RecordUnloadWeighing", "SENTETIK indirme tartısı kaydedildi",
            "SENTETIK indirme tartısında fark var");

    [SqlServerFact]
    public async Task An_unload_weighing_after_a_cancel_is_stored_once_with_its_audit_rebuilt_as_recorded_and_replayed_without_a_second_row()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var allocationId = Guid.NewGuid();
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), owners.Company, owners.Party, owners.Account, T0);
        instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(owners.Order, Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit,
            allocationId, Kg(6m), T0);
        var id = instruction.Id;
        var lineId = instruction.Lines[0].Id;
        var weighingId = Guid.NewGuid();
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.StoreInstructionAsync(run.Envelope("k-unload-create"), Hash("unload-create"), Day, () => instruction);
            await a.Store.MutateInstructionAsync(run.Envelope("k-unload-issue"), Hash("unload-issue"), LogisticsCommands.IssueV1, id,
                (item, now) => item.Issue(2, now), Issued);
            await a.Store.MutateInstructionAsync(run.Envelope("k-unload-load"), Hash("unload-load"), LogisticsCommands.RecordLoadV1, id,
                (item, now) => item.RecordLoad(3, Guid.NewGuid(), lineId, Kg(4m), MeasurementKind.Measured, now), Loaded);
            var cancelled = await a.Store.MutateInstructionAsync(run.Envelope("k-unload-cancel"), Hash("unload-cancel"), LogisticsCommands.CancelInstructionV1, id,
                (item, now) => item.CancelInstruction(4, run.Actor, "SENTETIK neden", now), InstructionCancelled);
            // LOJ-D17 (3): cancelled with goods loaded and no unload weighing — "indirme kaydı yok"; the cancel is valid without it.
            Assert.Equal((5L, UnloadRecordState.Missing), (cancelled!.Instruction.Instruction.Version, cancelled.Instruction.Instruction.UnloadRecord));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.UnloadWeighings WHERE InstructionId = @id", ("@id", id)));
        const string CancelAndLineFacts = "SELECT CONCAT(i.[Status], N'|', i.CancelledBy, N'|', i.CancelReason, N'|', CONVERT(nvarchar(40), i.CancelledAtUtc, 127), N'|', "
            + "CONVERT(nvarchar(40), l.Loaded), N'|', CONVERT(nvarchar(36), l.AllocationId)) FROM logistics.LoadingInstructions i "
            + "JOIN logistics.LoadingInstructionLines l ON l.CompanyId = i.CompanyId AND l.InstructionId = i.Id WHERE i.Id = @id";
        var before = await run.ScalarAsync<string>(CancelAndLineFacts, ("@id", id));
        Assert.EndsWith(allocationId.ToString("D"), before, StringComparison.OrdinalIgnoreCase);

        await using (var a = run.Store(CompanyA))
        {
            var unloaded = await a.Store.MutateInstructionAsync(run.Envelope("k-unload"), Hash("unload-5"), LogisticsCommands.RecordUnloadWeighingV1, id,
                (item, now) => item.RecordUnloadWeighing(5, weighingId, WeighingDirection.GrossFirst, Kg(15_003.5m), Kg(15_000m), run.Actor, now), Unloaded);
            var weighing = unloaded!.Instruction.Instruction.UnloadWeighing!;
            Assert.Equal((6L, UnloadRecordState.Recorded, 3.5m, 4m, -0.5m, true), (unloaded.Instruction.Instruction.Version,
                unloaded.Instruction.Instruction.UnloadRecord, weighing.Net.Value, weighing.LoadedAtCancel.Value, weighing.DifferenceKg, weighing.DifferenceWarning));
        }

        // One row; one recorded attempt; one audit event on the Web channel carrying the difference; the cancel, the line's loaded
        // total and its allocation binding unchanged (LOJ-D16); no share was added.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.UnloadWeighings WHERE InstructionId = @id", ("@id", id)));
        Assert.Equal(-0.5m, await run.ScalarAsync<decimal>("SELECT DifferenceKg FROM logistics.UnloadWeighings WHERE InstructionId = @id", ("@id", id)));
        Assert.Equal(4m, await run.ScalarAsync<decimal>("SELECT LoadedKgAtCancel FROM logistics.UnloadWeighings WHERE InstructionId = @id", ("@id", id)));
        Assert.Equal(before, await run.ScalarAsync<string>(CancelAndLineFacts, ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE InstructionId = @id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE CommandName = @command", ("@command", LogisticsCommands.RecordUnloadWeighingV1)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'logistics.loading-instruction.unload-weighing-recorded.v1' "
            + "AND SourceChannel = N'Web' AND AfterJson LIKE N'%\"difference\":-0.5%'", ("@id", id.ToString("D"))));

        await using (var a = run.Store(CompanyA))
        {
            var rebuilt = (await a.Store.FindInstructionAsync(id))!.Instruction;
            Assert.Equal((LoadingInstructionStatus.Cancelled, 6L, UnloadRecordState.Recorded, weighingId, -0.5m, run.Actor, allocationId),
                (rebuilt.Status, rebuilt.Version, rebuilt.UnloadRecord, rebuilt.UnloadWeighing!.WeighingId, rebuilt.UnloadWeighing.DifferenceKg,
                    rebuilt.UnloadWeighing.RecordedBy, rebuilt.Lines[0].AllocationId));

            // The same key: the stored answer, never a second row.
            var replay = await a.Store.MutateInstructionAsync(run.Envelope("k-unload", correlation: "SENTETIK-retry"), Hash("unload-5"),
                LogisticsCommands.RecordUnloadWeighingV1, id, (_, _) => throw new InvalidOperationException("a replay never mutates"), Unloaded);
            Assert.Equal((LogisticsWriteOutcome.Replayed, run.Correlation), (replay!.Outcome, replay.OriginalCorrelationId));

            // A second record (another key) and a stale version write nothing.
            var again = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-unload-2"), Hash("unload-6"),
                LogisticsCommands.RecordUnloadWeighingV1, id, (item, now) => item.RecordUnloadWeighing(6, Guid.NewGuid(), WeighingDirection.GrossFirst,
                    Kg(15_004m), Kg(15_000m), run.Actor, now), Unloaded));
            Assert.Equal(LogisticsRuleCodes.UnloadWeighingAlreadyRecorded, again.Code);
            await Assert.ThrowsAsync<LogisticsVersionConflictException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-unload-3"), Hash("unload-stale"),
                LogisticsCommands.RecordUnloadWeighingV1, id, (item, now) => item.RecordUnloadWeighing(5, Guid.NewGuid(), WeighingDirection.GrossFirst,
                    Kg(15_004m), Kg(15_000m), run.Actor, now), Unloaded));
        }

        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindInstructionAsync(id));
            Assert.Null(await b.Store.MutateInstructionAsync(run.Envelope("k-b-unload"), Hash("unload-b"), LogisticsCommands.RecordUnloadWeighingV1, id,
                (item, now) => item.RecordUnloadWeighing(6, Guid.NewGuid(), WeighingDirection.GrossFirst, Kg(2m), Kg(1m), run.Actor, now), Unloaded));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.UnloadWeighings"));
        Assert.Equal(6L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
        Assert.Equal(before, await run.ScalarAsync<string>(CancelAndLineFacts, ("@id", id)));
    }

    [SqlServerFact]
    public async Task An_unload_weighing_is_refused_on_an_active_instruction_and_on_a_cancel_with_nothing_loaded_without_a_row()
    {
        await using var run = await Run.StartAsync();
        var (active, _) = await IssuedForCancelRaceAsync(run, "unload-active", loaded: 2m);
        var (empty, _) = await IssuedForCancelRaceAsync(run, "unload-empty", loaded: null);
        await using (var a = run.Store(CompanyA))
        {
            var notCancelled = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-unload-active"),
                Hash("unload-active"), LogisticsCommands.RecordUnloadWeighingV1, active, (item, now) => item.RecordUnloadWeighing(4, Guid.NewGuid(),
                    WeighingDirection.GrossFirst, Kg(102m), Kg(100m), run.Actor, now), Unloaded));
            Assert.Equal(LogisticsRuleCodes.InstructionNotCancelled, notCancelled.Code);

            var cancelled = await a.Store.MutateInstructionAsync(run.Envelope("k-unload-empty-cancel"), Hash("unload-empty-cancel"), LogisticsCommands.CancelInstructionV1,
                empty, (item, now) => item.CancelInstruction(3, run.Actor, "SENTETIK neden", now), InstructionCancelled);
            Assert.Equal(UnloadRecordState.NotApplicable, cancelled!.Instruction.Instruction.UnloadRecord);
            var nothingLoaded = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-unload-empty"),
                Hash("unload-empty"), LogisticsCommands.RecordUnloadWeighingV1, empty, (item, now) => item.RecordUnloadWeighing(4, Guid.NewGuid(),
                    WeighingDirection.GrossFirst, Kg(102m), Kg(100m), run.Actor, now), Unloaded));
            Assert.Equal(LogisticsRuleCodes.UnloadNothingLoaded, nothingLoaded.Code);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.UnloadWeighings"));
        Assert.Equal((4L, 4L), (
            await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", active)),
            await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", empty))));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT [Status] FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", active)));
    }

    [SqlServerFact]
    public async Task The_database_refuses_an_unload_weighing_row_against_its_direction_or_difference_and_a_second_row_for_the_instruction()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        Guid id;
        await using (var a = run.Store(CompanyA))
        {
            id = (await a.Store.StoreInstructionAsync(run.Envelope("k-unload-ck-create"), Hash("unload-ck-create"), Day, () => Instruction(owners))).Instruction.Instruction.Id;
        }

        const string Insert = "INSERT INTO [logistics].[UnloadWeighings] ([CompanyId], [InstructionId], [WeighingId], [Direction], [FirstWeighingKg], "
            + "[SecondWeighingKg], [NetKg], [LoadedKgAtCancel], [DifferenceKg], [RecordedBy], [RecordedAtUtc]) "
            + "VALUES (@company, @id, NEWID(), @direction, @first, @second, @net, @loaded, @difference, N'SENTETIK', SYSDATETIMEOFFSET())";
        async Task<int> InsertAsync(int direction, decimal first, decimal second, decimal net, decimal loaded, decimal difference)
        {
            try
            {
                await run.ExecuteAsync(Insert, ("@company", CompanyA), ("@id", id), ("@direction", direction), ("@first", first), ("@second", second),
                    ("@net", net), ("@loaded", loaded), ("@difference", difference));
                return 0;
            }
            catch (SqlException exception)
            {
                return exception.Number;
            }
        }

        // LOJ-D17 in the schema: tare first but the second is lighter; gross first but the first is lighter; equal weighings; a net
        // that is not gross − tare; a difference that is not net − loaded; nothing loaded; an unknown direction. Each a CHECK
        // violation (547).
        Assert.Equal(547, await InsertAsync(1, 20m, 10m, 10m, 10m, 0m));
        Assert.Equal(547, await InsertAsync(2, 10m, 20m, 10m, 10m, 0m));
        Assert.Equal(547, await InsertAsync(2, 10m, 10m, 0m, 10m, -10m));
        Assert.Equal(547, await InsertAsync(2, 20m, 10m, 9m, 10m, -1m));
        Assert.Equal(547, await InsertAsync(2, 20m, 10m, 10m, 12m, 2m));
        Assert.Equal(547, await InsertAsync(2, 20m, 10m, 10m, 0m, 10m));
        Assert.Equal(547, await InsertAsync(3, 20m, 10m, 10m, 10m, 0m));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.UnloadWeighings"));

        // A consistent row is accepted once; a second row for the same instruction is the key (2627).
        Assert.Equal(0, await InsertAsync(2, 20m, 10m, 10m, 12m, -2m));
        Assert.Equal(2627, await InsertAsync(1, 10m, 20m, 10m, 10m, 0m));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.UnloadWeighings"));
    }

    [SqlServerFact]
    public async Task The_unload_weighing_migrations_Down_passes_with_no_unload_weighing_and_refuses_with_51266_while_one_exists_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddUnloadWeighings().DownOperations[0]).Sql;
        var (id, _) = await IssuedForCancelRaceAsync(run, "unload-down", loaded: 2m);
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.MutateInstructionAsync(run.Envelope("k-unload-down-cancel"), Hash("unload-down-cancel"), LogisticsCommands.CancelInstructionV1, id,
                (item, now) => item.CancelInstruction(4, run.Actor, "SENTETIK neden", now), InstructionCancelled);
        }

        // A cancelled instruction without its unload weighing ("indirme kaydı yok") is harmless: the guard passes.
        await run.ExecuteAsync(guard);

        await using (var a = run.Store(CompanyA))
        {
            await a.Store.MutateInstructionAsync(run.Envelope("k-unload-down"), Hash("unload-down"), LogisticsCommands.RecordUnloadWeighingV1, id,
                (item, now) => item.RecordUnloadWeighing(5, Guid.NewGuid(), WeighingDirection.GrossFirst, Kg(102m), Kg(100m), run.Actor, now), Unloaded);
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51266, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.UnloadWeighings WHERE InstructionId = @id", ("@id", id)));
    }

    [SqlServerFact]
    public async Task The_unload_weighing_route_answers_only_with_the_weighing_grant_and_only_for_the_context_company()
    {
        await using var run = await Run.StartAsync();
        var (id, _) = await IssuedForCancelRaceAsync(run, "unload-route", loaded: 6m);
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.MutateInstructionAsync(run.Envelope("k-unload-route-cancel"), Hash("unload-route-cancel"), LogisticsCommands.CancelInstructionV1, id,
                (item, now) => item.CancelInstruction(4, run.Actor, "SENTETIK neden", now), InstructionCancelled);
        }

        var body = $$"""{"expectedVersion":"5","weighingId":"{{Guid.NewGuid():D}}","direction":"TareFirst","firstWeighingKg":"14000","secondWeighingKg":"14005.5"}""";

        // Reading the instruction is not the weighing grant: the fence refuses before the store.
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead);
        using (var readOnly = run.Factory.CreateClientFor(reader, CompanyA))
        {
            readOnly.DefaultRequestHeaders.Remove("Idempotency-Key");
            readOnly.DefaultRequestHeaders.Remove("X-Correlation-Id");
            Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.SendAsync(Command($"{id:D}/unload-weighings", "SENTETIK-unload-reader", body))).StatusCode);
        }

        var weigher = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.WeighingsRecord);
        using (var granted = run.Factory.CreateClientFor(weigher, CompanyA))
        {
            granted.DefaultRequestHeaders.Remove("Idempotency-Key");
            granted.DefaultRequestHeaders.Remove("X-Correlation-Id");

            // Before the record the detail carries the "indirme kaydı yok" mark and a null unload weighing.
            using var missing = await granted.GetAsync($"{LogisticsEndpoints.InstructionsPath}/{id:D}");
            var missingText = await missing.Content.ReadAsStringAsync();
            Assert.Contains("\"unloadRecord\":\"Missing\"", missingText, StringComparison.Ordinal);
            Assert.Contains("\"unloadWeighing\":null", missingText, StringComparison.Ordinal);

            using var recorded = await granted.SendAsync(Command($"{id:D}/unload-weighings", "SENTETIK-unload", body));
            Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
            var text = await recorded.Content.ReadAsStringAsync();
            Assert.Contains("\"netKg\":\"5.5\"", text, StringComparison.Ordinal);
            Assert.Contains("\"loadedKgAtCancel\":\"6\"", text, StringComparison.Ordinal);
            Assert.Contains("\"differenceKg\":\"-0.5\"", text, StringComparison.Ordinal);
            Assert.Contains("\"differenceWarning\":true", text, StringComparison.Ordinal);
            Assert.Contains("\"unloadRecord\":\"Recorded\"", text, StringComparison.Ordinal);
            using var replay = await granted.SendAsync(Command($"{id:D}/unload-weighings", "SENTETIK-unload", body));
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            // Reopening the instruction shows the same record.
            using var detail = await granted.GetAsync($"{LogisticsEndpoints.InstructionsPath}/{id:D}");
            Assert.Contains("\"differenceKg\":\"-0.5\"", await detail.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            // A second record under another key is the recorded terminal 409 — never a replacement.
            var secondBody = $$"""{"expectedVersion":"6","weighingId":"{{Guid.NewGuid():D}}","direction":"TareFirst","firstWeighingKg":"14000","secondWeighingKg":"14006"}""";
            using var second = await granted.SendAsync(Command($"{id:D}/unload-weighings", "SENTETIK-unload-2", secondBody));
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Contains($"\"code\":\"{LogisticsProblemCodesV1.UnloadWeighingAlreadyRecorded}\"", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        var weigherOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.WeighingsRecord);
        using (var clientOfB = run.Factory.CreateClientFor(weigherOfB, CompanyB))
        {
            clientOfB.DefaultRequestHeaders.Remove("Idempotency-Key");
            clientOfB.DefaultRequestHeaders.Remove("X-Correlation-Id");
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.SendAsync(Command($"{id:D}/unload-weighings", "SENTETIK-unload-b", body))).StatusCode);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.UnloadWeighings"));
        Assert.Equal(6L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'logistics.loading-instruction.unload-weighing-recorded.v1' "
            + "AND SourceChannel = N'Web' AND PermissionCode = N'logistics.weighings.record'", ("@id", id.ToString("D"))));
    }
}

using System.Net;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AT-053 on real SQL Server: the manual weighbridge record of an issued instruction is ONE row in ONE save with its
/// attempt and its audit event; the rebuild replays it after the shares it followed, so the loaded snapshot and the
/// signed difference stay what they were when the vehicle was weighed; the same key answers it again without a second
/// row; a second record, a stale version or another company write nothing; the database itself refuses a row whose
/// numbers contradict its direction or its difference, and a second row for the instruction. The route answers only
/// with the weighing grant and only for the context company, with the audit on the Web channel. Facts only, no
/// tolerance (OPEN-004); Inventory is never written. SENTETIK.
/// </summary>
public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    private static readonly LogisticsMutationAudit Weighed =
        new LogisticsMutationAudit("logistics.loading-instruction.weighing-recorded.v1", "RecordWeighing", "SENTETIK tartıldı");

    [SqlServerFact]
    public async Task A_weighing_is_stored_once_in_one_save_rebuilt_at_its_share_position_and_replayed_without_a_second_row()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        Guid id;
        Guid first;
        Guid second;
        var weighingId = Guid.NewGuid();
        await using (var a = run.Store(CompanyA))
        {
            var stored = (await a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-1"), Day, () => Instruction(owners, lines: 2))).Instruction.Instruction;
            id = stored.Id;
            first = stored.Lines[0].Id;
            second = stored.Lines[1].Id;
            await a.Store.MutateInstructionAsync(run.Envelope("k-issue"), Hash("issue-3"), LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(3, now), Issued);
            await a.Store.MutateInstructionAsync(run.Envelope("k-load-1"), Hash("load-4"), LogisticsCommands.RecordLoadV1, id,
                (instruction, now) => instruction.RecordLoad(4, Guid.NewGuid(), first, Kg(4.5m), MeasurementKind.Measured, now), Loaded);
            await a.Store.MutateInstructionAsync(run.Envelope("k-load-2"), Hash("load-5"), LogisticsCommands.RecordLoadV1, id,
                (instruction, now) => instruction.RecordLoad(5, Guid.NewGuid(), second, Kg(4.5m), MeasurementKind.AllocatedFromVehicleWeight, now), Loaded);

            var weighed = await a.Store.MutateInstructionAsync(run.Envelope("k-weigh"), Hash("weigh-6"), LogisticsCommands.RecordWeighingV1, id,
                (instruction, now) => instruction.RecordWeighing(6, weighingId, WeighingDirection.TareFirst, Kg(15_000m), Kg(15_008.5m), run.Actor, now), Weighed);
            var weighing = weighed!.Instruction.Instruction.Weighing!;
            Assert.Equal((7L, 8.5m, 9m, -0.5m, 2), (weighed.Instruction.Instruction.Version, weighing.Net.Value, weighing.LoadedAtWeighing.Value,
                weighing.DifferenceKg, weighing.SharesBefore));
        }

        // One row, one recorded attempt, one audit event on the Web channel carrying the facts; the shares are untouched.
        Assert.Equal((1, 2, 2, 5, 5, 7L, 5), await run.CountsAsync(id));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.VehicleWeighings WHERE InstructionId = @id", ("@id", id)));
        Assert.Equal(-0.5m, await run.ScalarAsync<decimal>("SELECT DifferenceKg FROM logistics.VehicleWeighings WHERE InstructionId = @id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'logistics.loading-instruction.weighing-recorded.v1' "
            + "AND SourceChannel = N'Web' AND AfterJson LIKE N'%\"difference\":-0.5%'", ("@id", id.ToString("D"))));

        await using (var a = run.Store(CompanyA))
        {
            // A load after the weighing is still a load; the weighing keeps the kilograms it was compared with.
            await a.Store.MutateInstructionAsync(run.Envelope("k-load-3"), Hash("load-7"), LogisticsCommands.RecordLoadV1, id,
                (instruction, now) => instruction.RecordLoad(7, Guid.NewGuid(), first, Kg(1m), MeasurementKind.Measured, now), Loaded);
            var rebuilt = (await a.Store.FindInstructionAsync(id))!.Instruction;
            Assert.Equal((8L, 3, 10m, 9m, -0.5m, weighingId, run.Actor), (rebuilt.Version, rebuilt.Shares.Count,
                rebuilt.Lines.Sum(line => line.LoadedQuantity.Value), rebuilt.Weighing!.LoadedAtWeighing.Value, rebuilt.Weighing.DifferenceKg,
                rebuilt.Weighing.WeighingId, rebuilt.Weighing.RecordedBy));

            // The same key: the stored answer, never a second row.
            var replay = await a.Store.MutateInstructionAsync(run.Envelope("k-weigh", correlation: "SENTETIK-retry"), Hash("weigh-6"), LogisticsCommands.RecordWeighingV1,
                id, (_, _) => throw new InvalidOperationException("a replay never mutates"), Weighed);
            Assert.Equal((LogisticsWriteOutcome.Replayed, run.Correlation), (replay!.Outcome, replay.OriginalCorrelationId));

            // A second record (another key) and a stale version write nothing.
            var again = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-weigh-2"), Hash("weigh-8"),
                LogisticsCommands.RecordWeighingV1, id, (instruction, now) => instruction.RecordWeighing(8, Guid.NewGuid(), WeighingDirection.TareFirst,
                    Kg(15_000m), Kg(15_010m), run.Actor, now), Weighed));
            Assert.Equal(LogisticsRuleCodes.WeighingAlreadyRecorded, again.Code);
            await Assert.ThrowsAsync<LogisticsVersionConflictException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-weigh-3"), Hash("weigh-7"),
                LogisticsCommands.RecordWeighingV1, id, (instruction, now) => instruction.RecordWeighing(7, Guid.NewGuid(), WeighingDirection.TareFirst,
                    Kg(15_000m), Kg(15_010m), run.Actor, now), Weighed));
        }

        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindInstructionAsync(id));
            Assert.Null(await b.Store.MutateInstructionAsync(run.Envelope("k-b-weigh"), Hash("weigh-b"), LogisticsCommands.RecordWeighingV1, id,
                (instruction, now) => instruction.RecordWeighing(8, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(1m), Kg(2m), run.Actor, now), Weighed));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.VehicleWeighings"));
        Assert.Equal(8L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
    }

    [SqlServerFact]
    public async Task The_database_refuses_a_weighing_row_against_its_direction_or_difference_and_a_second_row_for_the_instruction()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        Guid id;
        await using (var a = run.Store(CompanyA))
        {
            id = (await a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-1"), Day, () => Instruction(owners))).Instruction.Instruction.Id;
        }

        const string Insert = "INSERT INTO [logistics].[VehicleWeighings] ([CompanyId], [InstructionId], [WeighingId], [Direction], [FirstWeighingKg], "
            + "[SecondWeighingKg], [NetKg], [LoadedKgAtWeighing], [DifferenceKg], [SharesBefore], [RecordedBy], [RecordedAtUtc]) "
            + "VALUES (@company, @id, NEWID(), @direction, @first, @second, @net, @loaded, @difference, 0, N'SENTETIK', SYSDATETIMEOFFSET())";
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

        // AT-053 in the schema: tare first but the second is lighter; gross first but the first is lighter; equal weighings;
        // a net that is not gross − tare; a difference that is not net − loaded. Each a CHECK violation (547).
        Assert.Equal(547, await InsertAsync(1, 20m, 10m, 10m, 0m, 10m));
        Assert.Equal(547, await InsertAsync(2, 10m, 20m, 10m, 0m, 10m));
        Assert.Equal(547, await InsertAsync(1, 10m, 10m, 0m, 0m, 0m));
        Assert.Equal(547, await InsertAsync(1, 10m, 20m, 9m, 0m, 9m));
        Assert.Equal(547, await InsertAsync(1, 10m, 20m, 10m, 12m, 2m));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.VehicleWeighings"));

        // A consistent row is accepted once; a second row for the same instruction is the key (2627).
        Assert.Equal(0, await InsertAsync(2, 20m, 10m, 10m, 12m, -2m));
        Assert.Equal(2627, await InsertAsync(1, 10m, 20m, 10m, 10m, 0m));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.VehicleWeighings"));
    }

    [SqlServerFact]
    public async Task The_weighing_migration_rolls_back_an_empty_table_and_refuses_with_51056_while_any_weighing_exists()
    {
        await using var run = await Run.StartAsync();
        var companyContext = new CompanyContext();
        companyContext.Set(CompanyA);
        var options = new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
            .Options;
        const string TableExists = "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'VehicleWeighings' AND SCHEMA_NAME([schema_id]) = N'logistics'";

        // Empty: the rollback runs, the table is dropped, and the forward migration re-creates it.
        await using (var context = new LogisticsDbContext(options, companyContext))
        {
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.EndsWith("_AddVehicleWeighings", applied[^1], StringComparison.Ordinal);
            await context.GetService<IMigrator>().MigrateAsync(applied[^2]);
            Assert.Equal(0, await run.ScalarAsync<int>(TableExists));
            Assert.Equal(applied[..^1], await context.Database.GetAppliedMigrationsAsync());
            await context.Database.MigrateAsync();
            Assert.Equal(1, await run.ScalarAsync<int>(TableExists));
        }

        // One recorded weighing: the rollback is refused BEFORE the drop, with Logistics's module error; nothing changes.
        var owners = Owners.For(CompanyA);
        Guid id;
        await using (var a = run.Store(CompanyA))
        {
            var stored = (await a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-1"), Day, () => Instruction(owners))).Instruction.Instruction;
            id = stored.Id;
            await a.Store.MutateInstructionAsync(run.Envelope("k-issue"), Hash("issue-2"), LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(2, now), Issued);
            await a.Store.MutateInstructionAsync(run.Envelope("k-weigh"), Hash("weigh-3"), LogisticsCommands.RecordWeighingV1, id,
                (instruction, now) => instruction.RecordWeighing(3, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(100m), Kg(106m), run.Actor, now), Weighed);
        }

        await using (var context = new LogisticsDbContext(options, companyContext))
        {
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[^2]));
            Assert.Equal(51056, error.Number);
            Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        }

        Assert.Equal(1, await run.ScalarAsync<int>(TableExists));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.VehicleWeighings WHERE InstructionId = @id", ("@id", id)));
    }

    [SqlServerFact]
    public async Task The_weighing_route_answers_only_with_the_weighing_grant_and_only_for_the_context_company()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        Guid id;
        Guid lineId;
        await using (var a = run.Store(CompanyA))
        {
            var stored = (await a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-1"), Day, () => Instruction(owners))).Instruction.Instruction;
            id = stored.Id;
            lineId = stored.Lines[0].Id;
            await a.Store.MutateInstructionAsync(run.Envelope("k-issue"), Hash("issue-2"), LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(2, now), Issued);
            await a.Store.MutateInstructionAsync(run.Envelope("k-load"), Hash("load-3"), LogisticsCommands.RecordLoadV1, id,
                (instruction, now) => instruction.RecordLoad(3, Guid.NewGuid(), lineId, Kg(6m), MeasurementKind.Measured, now), Loaded);
        }

        var body = $$"""{"expectedVersion":"4","weighingId":"{{Guid.NewGuid():D}}","direction":"TareFirst","firstWeighingKg":"14000","secondWeighingKg":"14005.5"}""";

        // Reading the instruction is not the weighing grant: the fence refuses before the store.
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead);
        using (var readOnly = run.Factory.CreateClientFor(reader, CompanyA))
        {
            readOnly.DefaultRequestHeaders.Remove("Idempotency-Key");
            readOnly.DefaultRequestHeaders.Remove("X-Correlation-Id");
            Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.SendAsync(Command($"{id:D}/weighings", "SENTETIK-weigh-reader", body))).StatusCode);
        }

        var weigher = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.WeighingsRecord);
        using (var granted = run.Factory.CreateClientFor(weigher, CompanyA))
        {
            granted.DefaultRequestHeaders.Remove("Idempotency-Key");
            granted.DefaultRequestHeaders.Remove("X-Correlation-Id");
            using var recorded = await granted.SendAsync(Command($"{id:D}/weighings", "SENTETIK-weigh", body));
            Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
            var text = await recorded.Content.ReadAsStringAsync();
            Assert.Contains("\"netKg\":\"5.5\"", text, StringComparison.Ordinal);
            Assert.Contains("\"loadedKgAtWeighing\":\"6\"", text, StringComparison.Ordinal);
            Assert.Contains("\"differenceKg\":\"-0.5\"", text, StringComparison.Ordinal);
            using var replay = await granted.SendAsync(Command($"{id:D}/weighings", "SENTETIK-weigh", body));
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            // Reopening the instruction shows the same record.
            using var detail = await granted.GetAsync($"{LogisticsEndpoints.InstructionsPath}/{id:D}");
            Assert.Contains("\"differenceKg\":\"-0.5\"", await detail.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            // A second record under another key is the recorded terminal 409 — never a replacement.
            var secondBody = $$"""{"expectedVersion":"5","weighingId":"{{Guid.NewGuid():D}}","direction":"TareFirst","firstWeighingKg":"14000","secondWeighingKg":"14006"}""";
            using var second = await granted.SendAsync(Command($"{id:D}/weighings", "SENTETIK-weigh-2", secondBody));
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Contains("\"code\":\"logistics_weighing_already_recorded\"", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        var weigherOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.WeighingsRecord);
        using (var clientOfB = run.Factory.CreateClientFor(weigherOfB, CompanyB))
        {
            clientOfB.DefaultRequestHeaders.Remove("Idempotency-Key");
            clientOfB.DefaultRequestHeaders.Remove("X-Correlation-Id");
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.SendAsync(Command($"{id:D}/weighings", "SENTETIK-weigh-b", body))).StatusCode);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.VehicleWeighings"));
        Assert.Equal(5L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'logistics.loading-instruction.weighing-recorded.v1' "
            + "AND SourceChannel = N'Web' AND PermissionCode = N'logistics.weighings.record'", ("@id", id.ToString("D"))));
    }
}

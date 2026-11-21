using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

// SAT-D421/D424/D425: real SQL owner store, source lines, immutable distribution, same-transaction rollback.
// Synthetic command envelope only. Does not substitute for the separate HTTP authority/real document checks.
public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Dispatch_kilograms_are_immutable_replayable_company_isolated_and_retained_in_the_owner_read()
    {
        await using var run = await Run.StartAsync();
        var note = await KilogramNoteAsync(run);
        Guid recordId;
        string serialized;
        await using (var scope = KilogramStore(run, CompanyA))
        {
            var source = (await scope.Store.ReadSourceAsync(note))!;
            var record = KilogramRecord(run, source);
            recordId = record.RecordId;
            var saved = await scope.Store.StoreAsync(run.Envelope("kg-record"), Hash("kg-source"), record, source, _ => Task.CompletedTask);
            Assert.Equal(LogisticsWriteOutcome.Recorded, saved.Outcome);
            Assert.Equal(new[] { 3.001m, 2m }, saved.Record.KilogramAllocation!.Lines.Select(x => x.Kilograms));
            serialized = JsonSerializer.Serialize(saved.Record.KilogramAllocation);
        }
        await using (var scope = KilogramStore(run, CompanyA))
        {
            var replay = await scope.Store.FindReplayAsync(run.Envelope("kg-record"), LogisticsWeighingDifferenceCommandsV1.Record, Hash("kg-source"));
            Assert.Equal(LogisticsWriteOutcome.Replayed, replay!.Outcome);
            Assert.Equal(serialized, JsonSerializer.Serialize(replay.Record.KilogramAllocation));
            var owner = await ((ILogisticsWeighingDifferenceReadPortV1)scope.Store).FindByDispatchNoteAsync(CompanyA, note);
            Assert.Equal(serialized, JsonSerializer.Serialize(owner!.KilogramAllocation));
            var tracked = await scope.Context.WeighingDifferences.SingleAsync(x => x.RecordId == recordId);
            tracked.KilogramAllocationJson = null;
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => scope.Context.SaveChangesAsync());
        }
        await using (var foreign = KilogramStore(run, CompanyB))
        {
            Assert.Null(await foreign.Store.FindByDispatchNoteAsync(note));
            Assert.Null(await foreign.Store.ReadSourceAsync(note));
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.WeighingDifferences WHERE RecordId=@id", ("@id", recordId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.WeighingDifferenceAttempts WHERE RecordId=@id", ("@id", recordId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Logistics.WeighingDifference' AND EntityId=@id", ("@id", recordId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Dispatch_kilogram_proof_revalidation_failure_after_save_rolls_back_record_attempt_and_audit()
    {
        await using var run = await Run.StartAsync();
        var note = await KilogramNoteAsync(run);
        Guid recordId;
        await using (var scope = KilogramStore(run, CompanyA))
        {
            var source = (await scope.Store.ReadSourceAsync(note))!;
            var record = KilogramRecord(run, source);
            recordId = record.RecordId;
            var calls = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.StoreAsync(run.Envelope("kg-proof-loss"), Hash("kg-source"), record, source,
                _ => ++calls == 2 ? throw new InvalidOperationException("SENTETIK proof lost after save") : Task.CompletedTask));
            Assert.Equal(2, calls);
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.WeighingDifferences WHERE RecordId=@id", ("@id", recordId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.WeighingDifferenceAttempts WHERE RecordId=@id", ("@id", recordId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Logistics.WeighingDifference' AND EntityId=@id", ("@id", recordId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Dispatch_kilogram_migration_down_allows_an_empty_store_and_preserves_unrelated_dispatch_sources()
    {
        await using var run = await Run.StartAsync();
        var note = await KilogramNoteAsync(run);
        await using var scope = KilogramStore(run, CompanyA);
        var migrations = scope.Context.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, x => x.EndsWith("_AddDispatchKilogramAllocations", StringComparison.Ordinal));
        Assert.True(index > 0);
        await scope.Context.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
        Assert.DoesNotContain(migrations[index], await scope.Context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNotes WHERE DispatchNoteId=@id", ("@id", note)));
        await scope.Context.GetService<IMigrator>().MigrateAsync();
        Assert.Contains(migrations[index], await scope.Context.Database.GetAppliedMigrationsAsync());
    }

    [SqlServerFact]
    public async Task Dispatch_kilogram_down_guard_51973_precedes_destructive_DDL_when_source_allocations_exist()
    {
        await using var run = await Run.StartAsync();
        var note = await KilogramNoteAsync(run);
        await using var scope = KilogramStore(run, CompanyA);
        var source = (await scope.Store.ReadSourceAsync(note))!;
        await scope.Store.StoreAsync(run.Envelope("kg-retained"), Hash("kg-source"), KilogramRecord(run, source), source, _ => Task.CompletedTask);
        var migrations = scope.Context.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, x => x.EndsWith("_AddDispatchKilogramAllocations", StringComparison.Ordinal));
        Assert.True(index > 0);
        var failure = await Assert.ThrowsAsync<SqlException>(() => scope.Context.GetService<IMigrator>().MigrateAsync(migrations[index - 1]));
        Assert.Equal(51973, failure.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.WeighingDifferences WHERE KilogramAllocationJson IS NOT NULL"));
        Assert.Contains(migrations[index], await scope.Context.Database.GetAppliedMigrationsAsync());
    }

    private static async Task<Guid> KilogramNoteAsync(Run run)
    {
        var (instruction, _, _, shipments) = await ShippedInstructionAsync(run, Owners.For(CompanyA));
        var note = Guid.NewGuid();
        await using var scope = run.Store(CompanyA);
        await scope.Store.MutateInstructionAsync(run.Envelope("kg-note"), Hash("kg-note"), LogisticsCommands.CreateDispatchNoteV1, instruction,
            (item, now) => item.CreateDispatchNoteDraft(9, note, [shipments[0], shipments[2]], run.Actor, now), Drafted);
        return note;
    }

    private static WeighingDifferenceRecord KilogramRecord(Run run, WeighingDifferenceSourceV1 source)
    {
        var product = source.Lines[0].ProductId;
        var evaluation = WeighingDifferenceCalculator.Evaluate(source.Lines.Select(x => new WeighingDifferenceNoteLine(x.LineNo, x.ShipmentId, x.ProductId, Kg(x.Quantity))).ToArray(),
            [new(product, Kg(5.001m), Kg(1m))], WeighingDifferencePolicy.Unreadable);
        var allocation = DispatchKilogramSnapshot.Theoretical(source.Lines.Select(x => new DispatchKilogramSourceLine(x.LineNo, x.ShipmentId, x.ProductId, x.Quantity)).ToArray(), [new(product, 5.001m)]);
        return WeighingDifferenceRecord.Create(CompanyA, Guid.NewGuid(), source.DispatchNoteId, source.InstructionId, evaluation,
            source.EnteredBy.Append(run.Actor), source.EnteredByComplete, run.Actor, DateTimeOffset.UtcNow, allocation);
    }

    private static KilogramStoreScope KilogramStore(Run run, Guid companyId)
    {
        var company = new CompanyContext(); company.Set(companyId);
        var collector = new AuditEventCollector();
        var context = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
        return new(context, new LogisticsWeighingDifferenceStore(context, company, collector, TimeProvider.System));
    }
    private sealed class KilogramStoreScope(LogisticsDbContext context, LogisticsWeighingDifferenceStore store) : IAsyncDisposable
    {
        public LogisticsDbContext Context => context;
        public LogisticsWeighingDifferenceStore Store => store;
        public ValueTask DisposeAsync() => context.DisposeAsync();
    }
}

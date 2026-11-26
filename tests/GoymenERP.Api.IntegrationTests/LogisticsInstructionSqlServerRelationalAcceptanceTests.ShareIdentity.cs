using System.Net;
using System.Reflection;
using Microsoft.Data.SqlClient;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Only_unique_errors_naming_the_exact_company_share_index_become_duplicate_share_refusals()
    {
        await using var run = await Run.StartAsync();
        var instruction = await ShareIdentityInstructionAsync(run, CompanyA, "identity-error-filter");
        foreach (var (number, name, expected) in new[]
        {
            (2601, "UX_LoadingShares_CompanyId_ShareId", typeof(LogisticsRuleException)),
            (2627, "UX_LoadingShares_CompanyId_ShareId", typeof(LogisticsRuleException)),
            (2601, "UX_LoadingAttempts_InstructionId", typeof(LogisticsStoreException)),
            (2627, "UX_LoadingShares_CompanyId_ShareId_other", typeof(LogisticsStoreException)),
            (547, "UX_LoadingShares_CompanyId_ShareId", typeof(DbUpdateException)),
        })
        {
            await using var writer = run.Store(CompanyA, new ThrowShareSaveError(ShareIdentitySqlError(number, name)));
            var error = await Record.ExceptionAsync(() => writer.Store.MutateInstructionAsync(run.Envelope($"error-{number}-{name}"),
                Hash($"error-{number}-{name}"), LogisticsCommands.RecordLoadV1, instruction.Id,
                (value, now) => value.RecordLoad(3, Guid.NewGuid(), instruction.Line, Kg(1m), MeasurementKind.Measured, now), Loaded));
            Assert.NotNull(error);
            Assert.Equal(expected, error.GetType());
            if (error is LogisticsRuleException duplicate) Assert.Equal(LogisticsRuleCodes.DuplicateShareId, duplicate.Code);
            if (error is LogisticsStoreException race) Assert.Equal(LogisticsStoreCodes.ConcurrentChange, race.Code);
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares"));
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id=@id", ("@id", instruction.Id)));
    }

    [SqlServerFact]
    public async Task Share_identity_is_company_wide_survives_cancellation_and_replays_without_a_second_share()
    {
        await using var run = await Run.StartAsync();
        var first = await ShareIdentityInstructionAsync(run, CompanyA, "identity-first");
        var second = await ShareIdentityInstructionAsync(run, CompanyA, "identity-second");
        var otherCompany = await ShareIdentityInstructionAsync(run, CompanyB, "identity-other-company");
        var shareId = Guid.NewGuid();
        await using (var writer = run.Store(CompanyA))
        {
            var loaded = await writer.Store.MutateInstructionAsync(run.Envelope("identity-load"), Hash("identity-load"),
                LogisticsCommands.RecordLoadV1, first.Id,
                (value, now) => value.RecordLoad(3, shareId, first.Line, Kg(1m), MeasurementKind.Measured, now), Loaded);
            Assert.Equal(4L, loaded!.Instruction.Instruction.Version);
            await writer.Store.MutateInstructionAsync(run.Envelope("identity-cancel"), Hash("identity-cancel"),
                LogisticsCommands.CancelInstructionV1, first.Id,
                (value, now) => value.CancelInstruction(4, run.Actor, "SENTETIK LOJ-D21", now), InstructionCancelled);
            var replay = await writer.Store.MutateInstructionAsync(run.Envelope("identity-load"), Hash("identity-load"),
                LogisticsCommands.RecordLoadV1, first.Id, (_, _) => throw new InvalidOperationException("Replay cannot mutate"), Loaded);
            Assert.Equal(LogisticsWriteOutcome.Replayed, replay!.Outcome);
            var duplicate = await Assert.ThrowsAsync<LogisticsRuleException>(() => writer.Store.MutateInstructionAsync(
                run.Envelope("identity-duplicate"), Hash("identity-duplicate"), LogisticsCommands.RecordLoadV1, second.Id,
                (value, now) => value.RecordLoad(3, shareId, second.Line, Kg(1m), MeasurementKind.Measured, now), Loaded));
            Assert.Equal(LogisticsRuleCodes.DuplicateShareId, duplicate.Code);
        }
        await using (var writer = run.Store(CompanyB))
            await writer.Store.MutateInstructionAsync(run.Envelope("identity-other-load"), Hash("identity-other-load"),
                LogisticsCommands.RecordLoadV1, otherCompany.Id,
                (value, now) => value.RecordLoad(3, shareId, otherCompany.Line, Kg(1m), MeasurementKind.Measured, now), Loaded);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE ShareId=@id", ("@id", shareId)));
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id=@id", ("@id", second.Id)));
        Assert.Equal(0m, await run.ScalarAsync<decimal>("SELECT Loaded FROM logistics.LoadingInstructionLines WHERE LineId=@id", ("@id", second.Line)));
    }

    [SqlServerFact]
    public async Task Racing_share_identity_on_different_instructions_commits_one_share_attempt_and_audit()
    {
        await using var run = await Run.StartAsync();
        var first = await ShareIdentityInstructionAsync(run, CompanyA, "identity-race-first");
        var second = await ShareIdentityInstructionAsync(run, CompanyA, "identity-race-second");
        var shareId = Guid.NewGuid();
        using var barrier = new Barrier(2);
        async Task<string> WriteAsync((Guid Id, Guid Line) instruction, string key)
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                await writer.Store.MutateInstructionAsync(run.Envelope(key), Hash(key), LogisticsCommands.RecordLoadV1, instruction.Id,
                    (value, now) => value.RecordLoad(3, shareId, instruction.Line, Kg(1m), MeasurementKind.Measured, now), Loaded);
                return "recorded";
            }
            catch (LogisticsRuleException error) { return error.Code; }
        }
        var results = await Task.WhenAll(Task.Run(() => WriteAsync(first, "identity-race-a")),
            Task.Run(() => WriteAsync(second, "identity-race-b")));
        Assert.Single(results, value => value == "recorded");
        Assert.Single(results, value => value == LogisticsRuleCodes.DuplicateShareId);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE ShareId=@id", ("@id", shareId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE CommandName=@command",
            ("@command", LogisticsCommands.RecordLoadV1)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'logistics.loading-instruction.load-recorded.v1'"));
        Assert.Equal(1m, await run.ScalarAsync<decimal>("SELECT SUM(Loaded) FROM logistics.LoadingInstructionLines"));
        Assert.Equal(7L, await run.ScalarAsync<long>("SELECT SUM(Version) FROM logistics.LoadingInstructions"));
    }

    [SqlServerFact]
    public async Task Racing_shipment_identity_rolls_back_the_loser_and_replays_its_terminal_refusal_without_second_physical_effect()
    {
        await using var run = await Run.StartAsync();
        var first = await SeedHostedShipmentAsync(run, "SYN-LOJD21-FIRST");
        var second = await SeedHostedShipmentAsync(run, "SYN-LOJD21-SECOND");
        var grant = await GrantRealizationAsync(run, $"SYN-LOJD21-{Guid.NewGuid():N}");
        var shareId = Guid.NewGuid();
        using var barrier = new Barrier(2);
        var interceptor = new RacingShipmentShareSave(barrier);
        void Configure(IServiceCollection services) => services.AddDbContext<LogisticsDbContext>((_, options) => options.AddInterceptors(interceptor));
        await using var firstHost = HostedShipmentHost(run, first, new HostedAllocationReads(first), grant.Subject, configure: Configure);
        await using var secondHost = HostedShipmentHost(run, second, new HostedAllocationReads(second), grant.Subject, configure: Configure);
        using var firstClient = HostedClientFor(firstHost, grant.Subject, CompanyA);
        using var secondClient = HostedClientFor(secondHost, grant.Subject, CompanyA);
        var a = CreateHostedShipmentRequest(first, "1", "identity-ship-a", shipmentId: shareId);
        var b = CreateHostedShipmentRequest(second, "1", "identity-ship-b", shipmentId: shareId);
        var beforeA = await HostedEffectsAsync(run, first, a);
        var beforeB = await HostedEffectsAsync(run, second, b);
        var responses = await Task.WhenAll(Task.Run(() => firstClient.SendAsync(a.Message)), Task.Run(() => secondClient.SendAsync(b.Message)));
        using var responseA = responses[0];
        using var responseB = responses[1];
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var loserIsA = responseA.StatusCode == HttpStatusCode.Conflict;
        var loser = loserIsA ? a : b;
        var fixture = loserIsA ? first : second;
        var client = loserIsA ? firstClient : secondClient;
        var before = loserIsA ? beforeA : beforeB;
        Assert.Contains(LogisticsProblemCodesV1.DuplicateShare, await (loserIsA ? responseA : responseB).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var after = await HostedEffectsAsync(run, fixture, loser);
        // The sole command for this shared shipment id belongs to the winner. The losing allocation/label/balance,
        // movement, outbox and audit remain unchanged; Logistics records only its one terminal refusal.
        Assert.Equal(before, after with { ShipmentAttemptCount = before.ShipmentAttemptCount, CommandCount = before.CommandCount });
        Assert.Equal(before.ShipmentAttemptCount + 2, after.ShipmentAttemptCount);
        Assert.Equal(1, after.CommandCount);
        var retry = CreateHostedShipmentRequest(fixture, "1", loserIsA ? "identity-ship-a" : "identity-ship-b", loser.ShipmentId, loser.MovementId);
        using var replay = await client.SendAsync(retry.Message);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(loser.CorrelationId, Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        Assert.Equal(after, await HostedEffectsAsync(run, fixture, loser));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE CompanyId=@company AND ShareId=@share",
            ("@company", CompanyA), ("@share", shareId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id IN (@a,@b)",
            ("@a", a.MovementId), ("@b", b.MovementId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE CorrelationId IN (@a,@b)",
            ("@a", a.CorrelationId), ("@b", b.CorrelationId)));
    }

    private static async Task<(Guid Id, Guid Line)> ShareIdentityInstructionAsync(Run run, Guid company, string key)
    {
        var instruction = Instruction(Owners.For(company));
        await using var writer = run.Store(company);
        await writer.Store.StoreInstructionAsync(run.Envelope($"{key}-create"), Hash($"{key}-create"), Day, () => instruction);
        await writer.Store.MutateInstructionAsync(run.Envelope($"{key}-issue"), Hash($"{key}-issue"), LogisticsCommands.IssueV1,
            instruction.Id, (value, now) => value.Issue(2, now), Issued);
        return (instruction.Id, instruction.Lines[0].Id);
    }

    private sealed class RacingShipmentShareSave(Barrier barrier) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            // Only the first shipment save participates; terminal-refusal saves must never wait for another writer.
            if (eventData.Context!.ChangeTracker.Entries<LoadingShareRow>().Any(entry => entry.State == EntityState.Added
                    && entry.Entity.Kind == (int)LoadingShareKind.Shipped)
                && !barrier.SignalAndWait(TimeSpan.FromSeconds(30), cancellationToken))
                throw new TimeoutException("LOJ-D21 concurrent shipment save barrier timed out");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowShareSaveError(SqlException error) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("SENTETIK SQL classification", error);
    }

    // Same pinned SqlClient construction used by DeadlockVictimException; do not replace actual race tests with this.
    private static SqlException ShareIdentitySqlError(int number, string index)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var error = typeof(SqlError).GetConstructor(hidden,
                [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception)])
            ?.Invoke([number, (byte)1, (byte)16, "SENTETIK", $"SENTETIK constraint/index '{index}'", string.Empty, 0, null])
            ?? throw new InvalidOperationException("SqlError constructor is not available");
        var errors = typeof(SqlErrorCollection).GetConstructor(hidden, Type.EmptyTypes)?.Invoke(null)
            ?? throw new InvalidOperationException("SqlErrorCollection constructor is not available");
        (typeof(SqlErrorCollection).GetMethod("Add", hidden, [typeof(SqlError)])
            ?? throw new InvalidOperationException("SqlErrorCollection.Add is not available")).Invoke(errors, [error]);
        return typeof(SqlException).GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic,
                [typeof(SqlErrorCollection), typeof(string)])?.Invoke(null, [errors, "16.00.0000"]) as SqlException
            ?? throw new InvalidOperationException("SqlException.CreateException is not available");
    }
}

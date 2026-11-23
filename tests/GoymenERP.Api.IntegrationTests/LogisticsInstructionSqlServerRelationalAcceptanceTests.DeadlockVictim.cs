using System.Data;
using System.Data.Common;
using System.Reflection;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// RCSI shard flake on real SQL Server: a read inside a Serializable aggregate section (the instructed or loaded SUM of a Sales
/// order line) can be chosen as the deadlock victim (SQL 1205) before SaveChanges runs. The loser must get the same replay-safe
/// ConcurrentChange refusal as a lost save, the section must roll back with nothing written, and the same context and key must
/// succeed afterwards. The 1205 is injected on the exact read so the evidence is deterministic. SENTETIK.
/// </summary>
public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_deadlock_victim_on_the_aggregate_instructed_sum_read_is_a_concurrent_change_that_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var orderLineId = Guid.NewGuid();
        var instruction = InstructionForOrderLine(owners, orderLineId, 3m);
        var line = Assert.Single(instruction.Lines);
        var victim = new DeadlockVictimOnFirstRead("SUM(", ".[Instructed]");

        await using (var a = run.Store(CompanyA, victim))
        {
            var refusal = await Assert.ThrowsAsync<LogisticsStoreException>(() => a.Store.StoreInstructionAsync(
                run.Envelope("k-deadlock-create"), Hash("deadlock-create"), Day, () => instruction,
                aggregateLimits: [AggregateLimit(line.Id, owners, orderLineId, 5m, 7, 3)]));
            Assert.Equal((IsolationLevel.Serializable, LogisticsStoreCodes.ConcurrentChange, true),
                (victim.ObservedIsolation, refusal.Code, CarriesDeadlockVictim(refusal)));
            Assert.Equal((0, 0, 0, 0), await InstructionWritesAsync(run, instruction.Id));

            var retry = await a.Store.StoreInstructionAsync(run.Envelope("k-deadlock-create"), Hash("deadlock-create"), Day,
                () => instruction, aggregateLimits: [AggregateLimit(line.Id, owners, orderLineId, 5m, 7, 3)]);
            Assert.Equal(LogisticsWriteOutcome.Recorded, retry.Outcome);
        }

        Assert.Equal((1, 1, 1, 1), await InstructionWritesAsync(run, instruction.Id));
    }

    [SqlServerFact]
    public async Task A_deadlock_victim_on_the_aggregate_loaded_sum_read_is_a_concurrent_change_that_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var orderLineId = Guid.NewGuid();
        var instruction = InstructionForOrderLine(owners, orderLineId, 2.5m);
        var line = Assert.Single(instruction.Lines);
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.StoreInstructionAsync(run.Envelope("k-deadlock-load-create"), Hash("deadlock-load-create"), Day,
                () => instruction, aggregateLimits: [AggregateLimit(line.Id, owners, orderLineId, 5m, 7, 3)]);
            await a.Store.MutateInstructionAsync(run.Envelope("k-deadlock-load-issue"), Hash("deadlock-load-issue"),
                LogisticsCommands.IssueV1, instruction.Id, (item, now) => item.Issue(2, now), Issued);
        }

        var victim = new DeadlockVictimOnFirstRead("SUM(", ".[Loaded]");
        await using (var a = run.Store(CompanyA, victim))
        {
            var aggregateStore = Assert.IsAssignableFrom<ILogisticsAggregateLoadStoreV1>(a.Store);
            var shareId = Guid.NewGuid();
            Task<LogisticsStoreResult?> LoadAsync() => aggregateStore.MutateInstructionWithAggregateLoadLimitAsync(
                run.Envelope("k-deadlock-load"), Hash("deadlock-load"), LogisticsCommands.RecordLoadV1, instruction.Id,
                (item, now) => item.RecordLoad(3, shareId, line.Id, Kg(1.5m), MeasurementKind.Measured, now),
                Loaded, AggregateLoadLimit(line.Id, owners, orderLineId, 2.5m, 1.5m));

            var refusal = await Assert.ThrowsAsync<LogisticsStoreException>(() => LoadAsync());
            Assert.Equal((IsolationLevel.Serializable, LogisticsStoreCodes.ConcurrentChange, true),
                (victim.ObservedIsolation, refusal.Code, CarriesDeadlockVictim(refusal)));
            Assert.Equal((1, 1, 2, 2), await InstructionWritesAsync(run, instruction.Id));
            Assert.Equal((3L, 0, 0m), await LoadStateAsync(run, instruction.Id));

            var retry = await LoadAsync();
            Assert.Equal((LogisticsWriteOutcome.Recorded, 4L), (retry!.Outcome, retry.Instruction.Instruction.Version));
        }

        Assert.Equal((4L, 1, 1.5m), await LoadStateAsync(run, instruction.Id));
    }

    /// <summary>(instructions, lines, attempts, audit events) the real SQL holds for one instruction.</summary>
    private static async Task<(int Instructions, int Lines, int Attempts, int AuditEvents)> InstructionWritesAsync(Run run, Guid id) =>
        (await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructionLines WHERE InstructionId = @id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE InstructionId = @id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", ("@id", id.ToString("D"))));

    /// <summary>(version, shares, loaded total) the real SQL holds for one instruction.</summary>
    private static async Task<(long Version, int Shares, decimal Loaded)> LoadStateAsync(Run run, Guid id) =>
        (await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE InstructionId = @id", ("@id", id)),
            await run.ScalarAsync<decimal>(
                "SELECT COALESCE(SUM(Loaded), 0) FROM logistics.LoadingInstructionLines WHERE InstructionId = @id", ("@id", id)));

    private static bool CarriesDeadlockVictim(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The SqlException SQL Server raises for a deadlock victim. SqlClient exposes no public factory, so the test builds the
    /// same exception through its internal constructors and fails loudly if a SqlClient upgrade removes them.
    /// </summary>
    private static SqlException DeadlockVictimException()
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var error = typeof(SqlError).GetConstructor(Hidden,
                [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception)])
            ?.Invoke([1205, (byte)51, (byte)13, "SENTETIK", "SENTETIK deadlock victim", string.Empty, 0, null])
            ?? throw new InvalidOperationException("SqlError constructor is not available.");
        var errors = typeof(SqlErrorCollection).GetConstructor(Hidden, Type.EmptyTypes)?.Invoke(null)
            ?? throw new InvalidOperationException("SqlErrorCollection constructor is not available.");
        (typeof(SqlErrorCollection).GetMethod("Add", Hidden, [typeof(SqlError)])
            ?? throw new InvalidOperationException("SqlErrorCollection.Add is not available.")).Invoke(errors, [error]);
        return typeof(SqlException).GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic,
                [typeof(SqlErrorCollection), typeof(string)])?.Invoke(null, [errors, "16.00.0000"]) as SqlException
            ?? throw new InvalidOperationException("SqlException.CreateException is not available.");
    }

    /// <summary>Raises the deadlock-victim error once, on the first reader command whose text holds every fragment.</summary>
    private sealed class DeadlockVictimOnFirstRead(params string[] fragments) : DbCommandInterceptor
    {
        public IsolationLevel? ObservedIsolation { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (ObservedIsolation is null && fragments.All(fragment => command.CommandText.Contains(fragment, StringComparison.Ordinal)))
            {
                ObservedIsolation = command.Transaction?.IsolationLevel ?? IsolationLevel.Unspecified;
                throw DeadlockVictimException();
            }

            return ValueTask.FromResult(result);
        }
    }
}

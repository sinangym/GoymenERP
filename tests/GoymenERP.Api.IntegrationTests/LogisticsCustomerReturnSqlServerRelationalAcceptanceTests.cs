using System.Data;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Domain.Returns;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Real-SQL source preparation for the Logistics-owned half of customer returns. It deliberately does
/// not fake the Inventory participant or host endpoints; full physical acceptance remains blocked on
/// the owned participant and central Serializable runner.
/// </summary>
public sealed class LogisticsCustomerReturnSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 20, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Same_record_closes_reopens_decides_and_replays_without_a_second_attempt()
    {
        await using var run = await Run.StartAsync();
        var source = await run.CreateDeliveredSourceAsync(100m);
        var requested = Request(source, 40m, "alice");

        await run.SerializableAsync(async store =>
        {
            var first = await store.StoreRequestAsync(run.Envelope("request", "alice", "request-original"), Hash("request"), requested);
            var replay = await store.StoreRequestAsync(run.Envelope("request", "alice", "retry"), Hash("request"), requested);
            Assert.Equal((LogisticsWriteOutcome.Recorded, LogisticsWriteOutcome.Replayed, first.OriginalCorrelationId),
                (first.Outcome, replay.Outcome, replay.OriginalCorrelationId));
        });

        await run.SerializableAsync(async store =>
        {
            var reopened = await store.FindAsync(requested.Id);
            Assert.Equal((CustomerReturnStatus.Pending, 1L, 40m),
                (reopened!.Status, reopened.Version, reopened.Quantity.Value));
            reopened.Approve(1, "bob", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0.AddMinutes(2));
            await store.StoreDecisionAsync(run.Envelope("approve", "bob", "approve-original"), Hash("approve"),
                LogisticsCustomerReturnCommandsV1.Approve, reopened);
        });

        await run.SerializableAsync(async store =>
        {
            var reopened = await store.FindAsync(requested.Id);
            Assert.Equal((CustomerReturnStatus.Approved, 2L, "bob"),
                (reopened!.Status, reopened.Version, reopened.DecidedBy));
            var requestReplay = await store.FindReplayAsync(run.Envelope("request", "alice", "request-after-decision"),
                Hash("request"), LogisticsCustomerReturnCommandsV1.Request);
            var requestSnapshot = Assert.IsType<CustomerReturn>(requestReplay!.Return);
            Assert.Equal((CustomerReturnStatus.Pending, 1L, "request-original"),
                (requestSnapshot.Status, requestSnapshot.Version, requestReplay.OriginalCorrelationId));
            Assert.Null(requestSnapshot.DecidedBy);
            Assert.Null(requestSnapshot.DecidedAtUtc);
            Assert.Null(requestSnapshot.DecisionNote);
            Assert.Null(requestSnapshot.InventoryMovementId);
            Assert.Null(requestSnapshot.OriginalLabelId);
            Assert.Null(requestSnapshot.OriginalShipmentMovementId);
            var replay = await store.FindReplayAsync(run.Envelope("approve", "bob", "later"), Hash("approve"),
                LogisticsCustomerReturnCommandsV1.Approve);
            Assert.Equal(reopened.InventoryMovementId, replay!.Return!.InventoryMovementId);
            Assert.Equal("approve-original", replay.OriginalCorrelationId);
            var wrongActor = await Assert.ThrowsAsync<LogisticsStoreException>(() => store.FindReplayAsync(
                run.Envelope("request", "mallory", "probe"), Hash("request"), LogisticsCustomerReturnCommandsV1.Request));
            Assert.Equal(LogisticsStoreCodes.SameKeyPayloadMismatch, wrongActor.Code);
            var wrongPayload = await Assert.ThrowsAsync<LogisticsStoreException>(() => store.FindReplayAsync(
                run.Envelope("request", "alice", "probe"), Hash("changed"), LogisticsCustomerReturnCommandsV1.Request));
            Assert.Equal(LogisticsStoreCodes.SameKeyPayloadMismatch, wrongPayload.Code);
        });

        Assert.Equal((1, 2), await run.CountsAsync());
    }

    [SqlServerFact]
    public async Task Authorized_terminal_version_refusal_is_immutable_and_never_exposes_itself_to_another_actor_or_payload()
    {
        await using var run = await Run.StartAsync();
        var envelope = run.Envelope("terminal-version", "alice", "terminal-original");
        var refusal = new CustomerReturnTerminalRefusalV1(409, LogisticsCustomerReturnCodesV1.VersionConflict,
            [new("expectedVersion", "version_conflict")], ExpectedVersion: 1, CurrentVersion: 2);

        await run.SerializableAsync(async store =>
        {
            var first = await store.RecordRefusalAsync(envelope, Hash("terminal-version"),
                LogisticsCustomerReturnCommandsV1.Reject, refusal);
            Assert.Equal(LogisticsWriteOutcome.Recorded, first.Outcome);
            Assert.Null(first.Replay.Return);
            var storedRefusal = Assert.IsType<CustomerReturnTerminalRefusalV1>(first.Replay.Refusal);
            Assert.Equal((refusal.Status, refusal.Code, refusal.ExpectedVersion, refusal.CurrentVersion),
                (storedRefusal.Status, storedRefusal.Code, storedRefusal.ExpectedVersion, storedRefusal.CurrentVersion));
            Assert.Equal(refusal.Errors, storedRefusal.Errors);
            Assert.Equal("terminal-original", first.Replay.OriginalCorrelationId);
        });

        await run.SerializableAsync(async store =>
        {
            var replay = await store.FindReplayAsync(envelope with { CorrelationId = "terminal-retry" },
                Hash("terminal-version"), LogisticsCustomerReturnCommandsV1.Reject);
            Assert.NotNull(replay);
            Assert.Null(replay!.Return);
            Assert.Equal((409, LogisticsCustomerReturnCodesV1.VersionConflict, 1L, 2L, "terminal-original"),
                (replay.Refusal!.Status, replay.Refusal.Code, replay.Refusal.ExpectedVersion,
                    replay.Refusal.CurrentVersion, replay.OriginalCorrelationId));
            Assert.Equal(refusal.Errors, replay.Refusal.Errors);

            var wrongActor = await Assert.ThrowsAsync<LogisticsStoreException>(() => store.FindReplayAsync(
                envelope with { ActorId = "mallory" }, Hash("terminal-version"), LogisticsCustomerReturnCommandsV1.Reject));
            Assert.Equal(LogisticsStoreCodes.SameKeyPayloadMismatch, wrongActor.Code);
            var wrongPayload = await Assert.ThrowsAsync<LogisticsStoreException>(() => store.FindReplayAsync(
                envelope, Hash("changed-terminal"), LogisticsCustomerReturnCommandsV1.Reject));
            Assert.Equal(LogisticsStoreCodes.SameKeyPayloadMismatch, wrongPayload.Code);
        });

        Assert.Equal((0, 1), await run.CountsAsync());
    }

    [SqlServerFact]
    public async Task Customer_return_attempt_outcome_constraint_refuses_null_payloads_and_accepts_complete_success_and_refusal()
    {
        await using var run = await Run.StartAsync();
        var source = await run.CreateDeliveredSourceAsync(20m);
        var requested = Request(source, 10m, "alice");

        await run.SerializableAsync(store => store.StoreRequestAsync(
            run.Envelope("constraint-success", "alice", "constraint-success-original"),
            Hash("constraint-success"), requested));
        await run.SerializableAsync(store => store.RecordRefusalAsync(
            run.Envelope("constraint-refusal", "bob", "constraint-refusal-original"),
            Hash("constraint-refusal"), LogisticsCustomerReturnCommandsV1.Reject,
            new CustomerReturnTerminalRefusalV1(
                409,
                LogisticsCustomerReturnCodesV1.VersionConflict,
                [new("expectedVersion", "version_conflict")],
                ExpectedVersion: 1,
                CurrentVersion: 2)));
        Assert.Equal((1, 2), await run.CountsAsync());

        var missingSuccessPayload = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            run.InsertIncompleteAttemptAsync(1, requested.Id, "constraint-missing-success"));
        Assert.Equal(547, missingSuccessPayload.Number);
        Assert.Equal((1, 2), await run.CountsAsync());

        var missingRefusalPayload = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            run.InsertIncompleteAttemptAsync(2, null, "constraint-missing-refusal"));
        Assert.Equal(547, missingRefusalPayload.Number);
        Assert.Equal((1, 2), await run.CountsAsync());
    }

    [SqlServerFact]
    public async Task Customer_return_migration_refuses_down_before_losing_history_and_empty_down_up_succeeds()
    {
        await using var run = await Run.StartAsync();
        var applied = await run.AppliedMigrationsAsync();
        var customerReturns = Array.FindIndex(applied,
            migration => migration.EndsWith("_AddCustomerReturns", StringComparison.Ordinal));
        Assert.True(customerReturns > 0);
        var previous = applied[customerReturns - 1];
        var current = applied[customerReturns];

        await run.MigrateAsync(current);
        await run.MigrateAsync(previous);
        Assert.DoesNotContain(current, await run.AppliedMigrationsAsync());
        await run.MigrateAsync(current);

        var source = await run.CreateDeliveredSourceAsync(20m);
        var requested = Request(source, 10m, "alice");
        await run.SerializableAsync(store => store.StoreRequestAsync(
            run.Envelope("down-refusal", "alice", "down-refusal-original"),
            Hash("down-refusal"), requested));

        var refusal = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => run.MigrateAsync(previous));
        Assert.Equal(51057, refusal.Number);
        Assert.Contains(current, await run.AppliedMigrationsAsync());
        Assert.Equal((1, 1), await run.CountsAsync());
        await run.MigrateAsync();
    }

    [SqlServerFact]
    public async Task Serializable_delivery_range_allows_only_one_of_two_returns_that_together_exceed_delivery()
    {
        await using var run = await Run.StartAsync();
        var source = await run.CreateDeliveredSourceAsync(100m);
        var first = Request(source, 60m, "alice");
        var second = Request(source, 60m, "carol");
        await run.SerializableAsync(store => store.StoreRequestAsync(run.Envelope("r1", "alice"), Hash("r1"), first));
        await run.SerializableAsync(store => store.StoreRequestAsync(run.Envelope("r2", "carol"), Hash("r2"), second));

        async Task<object> ApproveAsync(CustomerReturn item, string actor, string key)
        {
            try
            {
                return await run.SerializableAsync(async store =>
                {
                    var pending = (await store.FindAsync(item.Id))!;
                    pending.Approve(1, actor, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0.AddMinutes(3));
                    return (object)(await store.StoreDecisionAsync(run.Envelope(key, actor), Hash(key),
                        LogisticsCustomerReturnCommandsV1.Approve, pending)).Outcome;
                });
            }
            catch (LogisticsStoreException exception) { return exception.Code; }
        }

        var outcomes = await Task.WhenAll(Task.Run(() => ApproveAsync(first, "bob", "a1")),
            Task.Run(() => ApproveAsync(second, "dave", "a2")));
        Assert.Single(outcomes, value => value is LogisticsWriteOutcome.Recorded);
        Assert.Single(outcomes, value => value is string code
            && code is LogisticsStoreCodes.ConcurrentChange or LogisticsStoreCodes.AggregateLimitExceeded);
        Assert.Equal(60m, await run.ApprovedAsync(source.DeliveryId));
    }

    private static CustomerReturn Request(Source source, decimal quantity, string actor) => CustomerReturn.Request(
        Guid.NewGuid(), Company, source.InstructionId, source.LineId, source.DeliveryId, source.ShipmentId,
        source.OrderId, source.OrderLineId, source.UnitId, Quantity.Positive(quantity, QuantityUnit.Kilogram),
        Quantity.Positive(source.Delivered, QuantityUnit.Kilogram), "returned", actor, T0, T0.AddMinutes(1));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record Source(Guid InstructionId, Guid LineId, Guid DeliveryId, Guid ShipmentId,
        Guid OrderId, Guid OrderLineId, Guid UnitId, decimal Delivered);

    private sealed class Run(SqlServerRelationalApiFactory factory) : IAsyncDisposable
    {
        public static async Task<Run> StartAsync()
        {
            var factory = new SqlServerRelationalApiFactory(
                Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!);
            var run = new Run(factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        public LogisticsWriteEnvelope Envelope(string key, string actor, string correlation = "return-sql") =>
            new(actor, "SENTETIK-return", ["SENTETIK"], correlation, null, key, "Test");

        public async Task<Source> CreateDeliveredSourceAsync(decimal quantity)
        {
            var company = new CompanyContext(); company.Set(Company);
            await using var context = new LogisticsDbContext(Options(company), company);
            var audit = new AuditEventCollector();
            var store = new LogisticsStore(context, company, audit, TimeProvider.System);
            var order = Guid.NewGuid(); var orderLine = Guid.NewGuid(); var lineId = Guid.NewGuid(); var unit = Guid.NewGuid();
            var created = await store.StoreInstructionAsync(Envelope("source-create", "seed"), Hash("source-create"),
                new DateOnly(2026, 10, 3), () =>
                {
                    var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), T0);
                    instruction.AddLine(1, lineId, new SourceOrderLineRef(order, orderLine, 0), Guid.NewGuid(), unit,
                        Guid.NewGuid(), Quantity.Positive(quantity, QuantityUnit.Kilogram), T0);
                    return instruction;
                });
            var id = created.Instruction.Instruction.Id;
            await store.MutateInstructionAsync(Envelope("source-issue", "seed"), Hash("source-issue"), LogisticsCommands.IssueV1,
                id, (item, now) => item.Issue(2, now), new("seed.issue", "Issue", "seed"));
            await store.MutateInstructionAsync(Envelope("source-load", "seed"), Hash("source-load"), LogisticsCommands.RecordLoadV1,
                id, (item, now) => item.RecordLoad(3, Guid.NewGuid(), lineId, Quantity.Positive(quantity, QuantityUnit.Kilogram),
                    MeasurementKind.Measured, now), new("seed.load", "Load", "seed"));
            var shipment = Guid.NewGuid();
            await store.MutateInstructionAsync(Envelope("source-ship", "seed"), Hash("source-ship"), LogisticsCommands.RecordShipmentV1,
                id, (item, now) => item.MarkShipped(4, shipment, lineId, Quantity.Positive(quantity, QuantityUnit.Kilogram), now),
                new("seed.ship", "Ship", "seed"));
            var delivery = Guid.NewGuid();
            await store.MutateInstructionAsync(Envelope("source-deliver", "seed"), Hash("source-deliver"), LogisticsCommands.RecordDeliveryV1,
                id, (item, now) => item.MarkDelivered(5, delivery, lineId, shipment, Quantity.Positive(quantity, QuantityUnit.Kilogram), now),
                new("seed.deliver", "Deliver", "seed"));
            return new(id, lineId, delivery, shipment, order, orderLine, unit, quantity);
        }

        public async Task SerializableAsync(Func<LogisticsCustomerReturnStore, Task> operation) =>
            await SerializableAsync(async store => { await operation(store); return true; });

        public async Task<T> SerializableAsync<T>(Func<LogisticsCustomerReturnStore, Task<T>> operation)
        {
            var company = new CompanyContext(); company.Set(Company);
            await using var context = new LogisticsDbContext(Options(company), company);
            var audit = new AuditEventCollector();
            var store = new LogisticsCustomerReturnStore(context, company, audit, TimeProvider.System);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try { var result = await operation(store); await transaction.CommitAsync(); return result; }
            catch { await transaction.RollbackAsync(); throw; }
        }

        public async Task<(int Returns, int Attempts)> CountsAsync() =>
            (await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.CustomerReturns"),
             await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.CustomerReturnAttempts"));

        public Task<decimal> ApprovedAsync(Guid deliveryId) => ScalarAsync<decimal>(
            "SELECT COALESCE(SUM(Quantity), 0) FROM logistics.CustomerReturns WHERE SourceDeliveryId = @p0 AND Status = 2", deliveryId);

        public async Task InsertIncompleteAttemptAsync(int outcome, Guid? customerReturnId, string key)
        {
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO logistics.CustomerReturnAttempts
                    (CompanyId, CommandName, IdempotencyKeyHash, RequestHash, ActorId, Outcome,
                     CustomerReturnId, ResultJson, RefusalJson, CorrelationId, RecordedAtUtc)
                VALUES
                    (@companyId, @commandName, @idempotencyKeyHash, @requestHash, @actorId, @outcome,
                     @customerReturnId, NULL, NULL, @correlationId, @recordedAtUtc);
                """;
            command.Parameters.AddWithValue("@companyId", Company);
            command.Parameters.AddWithValue("@commandName", LogisticsCustomerReturnCommandsV1.Reject);
            command.Parameters.AddWithValue("@idempotencyKeyHash", Hash(key));
            command.Parameters.AddWithValue("@requestHash", Hash(key + "-request"));
            command.Parameters.AddWithValue("@actorId", "constraint-actor");
            command.Parameters.AddWithValue("@outcome", outcome);
            command.Parameters.AddWithValue("@customerReturnId", customerReturnId is null ? DBNull.Value : customerReturnId.Value);
            command.Parameters.AddWithValue("@correlationId", key + "-correlation");
            command.Parameters.AddWithValue("@recordedAtUtc", T0);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<T> ScalarAsync<T>(string sql, object? parameter = null)
        {
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql;
            if (parameter is not null) command.Parameters.AddWithValue("@p0", parameter);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task<string[]> AppliedMigrationsAsync()
        {
            var company = new CompanyContext();
            company.Set(Company);
            await using var context = new LogisticsDbContext(Options(company), company);
            return [.. await context.Database.GetAppliedMigrationsAsync()];
        }

        public async Task MigrateAsync(string? target = null)
        {
            var company = new CompanyContext();
            company.Set(Company);
            await using var context = new LogisticsDbContext(Options(company), company);
            if (target is null)
            {
                await context.Database.MigrateAsync();
                return;
            }

            await context.GetService<IMigrator>().MigrateAsync(target);
        }

        private DbContextOptions<LogisticsDbContext> Options(CompanyContext company) =>
            new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema)).Options;

        public async ValueTask DisposeAsync() { await factory.DisposeAsync(); }
    }
}

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
            var first = await store.StoreRequestAsync(run.Envelope("request", "alice"), Hash("request"), requested);
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
            await store.StoreDecisionAsync(run.Envelope("approve", "bob"), Hash("approve"),
                LogisticsCustomerReturnCommandsV1.Approve, reopened);
        });

        await run.SerializableAsync(async store =>
        {
            var reopened = await store.FindAsync(requested.Id);
            Assert.Equal((CustomerReturnStatus.Approved, 2L, "bob"),
                (reopened!.Status, reopened.Version, reopened.DecidedBy));
            var replay = await store.FindReplayAsync(run.Envelope("approve", "bob", "later"), Hash("approve"),
                LogisticsCustomerReturnCommandsV1.Approve);
            Assert.Equal(reopened.InventoryMovementId, replay!.Return.InventoryMovementId);
        });

        Assert.Equal((1, 2), await run.CountsAsync());
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
            var company = new CompanyContext(); company.Set(Company);
            await using var context = new LogisticsDbContext(run.Options(company), company);
            await context.Database.MigrateAsync();
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

        private async Task<T> ScalarAsync<T>(string sql, object? parameter = null)
        {
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql;
            if (parameter is not null) command.Parameters.AddWithValue("@p0", parameter);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        private DbContextOptions<LogisticsDbContext> Options(CompanyContext company) =>
            new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema)).Options;

        public async ValueTask DisposeAsync() { await factory.DisposeAsync(); }
    }
}

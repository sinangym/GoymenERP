using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Outbox;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkOutboxDispatchSqlServerRelationalAcceptanceTests
{
    private const string Source = "sales";
    private const string Event = "sales.invoice.finalized.v1";
    private const string PreviousMigration = "20261005190000_InitialWorkDesiredStateStorage";

    [SqlServerFact]
    public async Task Dispatch_delivers_once_per_company_with_cursor_and_inbox_rows_in_real_SQL()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var harness = new SqlHarness(connectionString);
            harness.Add(TestCompanyIds.CompanyA, 1, 2);
            harness.Add(TestCompanyIds.CompanyB, 1);

            var first = await harness.RunAsync();
            var second = await harness.RunAsync();

            Assert.Equal(3, first.Delivered);
            Assert.Equal(2, first.Companies);
            Assert.Equal(0, second.Delivered);
            Assert.Equal(3, harness.Handled.Count);
            Assert.Equal(1, await CountAsync(connectionString, "OutboxSourceCursors", TestCompanyIds.CompanyB));
            Assert.Equal("2", await ScalarAsync(connectionString,
                $"SELECT [Position] FROM [work].[OutboxSourceCursors] WHERE [CompanyId] = '{TestCompanyIds.CompanyA}'"));

            await using var scoped = Db(connectionString, TestCompanyIds.CompanyA);
            Assert.Equal(2, await scoped.OutboxDeliveries.CountAsync());
            Assert.All(await scoped.OutboxDeliveries.ToListAsync(), r => Assert.Equal((int)WorkOutboxDeliveryStatus.Delivered, r.Status));
        });
    }

    [SqlServerFact]
    public async Task Dispatch_state_constraints_and_dedup_key_refuse_bad_rows_in_real_SQL()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var badState = await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString, $"""
                INSERT INTO [work].[OutboxDeliveries]
                ([CompanyId],[SourceName],[ConsumerName],[MessageId],[EventType],[SchemaVersion],[PayloadSha256],[Status],[AttemptCount],[FirstAttemptAtUtc],[LastAttemptAtUtc])
                VALUES ('{TestCompanyIds.CompanyA}','sales','c','{Guid.NewGuid()}','{Event}',1,'{new string('a', 64)}',9,1,SYSUTCDATETIME(),SYSUTCDATETIME())
                """));
            Assert.Equal(547, Sql(badState).Number);

            var message = Guid.NewGuid();
            var insert = $"""
                INSERT INTO [work].[OutboxDeliveries]
                ([CompanyId],[SourceName],[ConsumerName],[MessageId],[EventType],[SchemaVersion],[PayloadSha256],[Status],[AttemptCount],[FirstAttemptAtUtc],[LastAttemptAtUtc],[DeliveredAtUtc])
                VALUES ('{TestCompanyIds.CompanyA}','sales','c','{message}','{Event}',1,'{new string('a', 64)}',1,1,SYSUTCDATETIME(),SYSUTCDATETIME(),SYSUTCDATETIME())
                """;
            await ExecAsync(connectionString, insert);
            var duplicate = await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString, insert));
            Assert.Equal(2627, Sql(duplicate).Number);

            await using var other = Db(connectionString, TestCompanyIds.CompanyB);
            Assert.Equal(0, await other.OutboxDeliveries.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task Down_refuses_while_dispatch_records_exist_and_drops_the_empty_tables()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await ExecAsync(connectionString, $"""
                INSERT INTO [work].[OutboxSourceCursors] ([CompanyId],[SourceName],[Position],[UpdatedAtUtc])
                VALUES ('{TestCompanyIds.CompanyA}','sales','1',SYSUTCDATETIME())
                """);
            await using (var occupied = Db(connectionString, TestCompanyIds.CompanyA))
            {
                var refusal = await Assert.ThrowsAnyAsync<Exception>(() =>
                    occupied.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration));
                Assert.Equal(51234, Sql(refusal).Number);
            }

            await ExecAsync(connectionString, "DELETE FROM [work].[OutboxSourceCursors] WHERE 1 = 1");
            await using (var empty = Db(connectionString, TestCompanyIds.CompanyA))
            {
                await empty.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                Assert.True(string.IsNullOrEmpty(await ScalarAsync(connectionString, "SELECT OBJECT_ID(N'[work].[OutboxDeliveries]')")));
                await empty.Database.MigrateAsync();
            }
        });
    }

    private static SqlException Sql(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException!)
        {
            if (e is SqlException sql)
            {
                return sql;
            }

            if (e.InnerException is null)
            {
                break;
            }
        }

        throw new InvalidOperationException("No SqlException found.", exception);
    }

    private static WorkDbContext Db(string connectionString, Guid companyId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        return new WorkDbContext(
            new DbContextOptionsBuilder<WorkDbContext>()
                .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", WorkDbContext.Schema))
                .Options,
            company);
    }

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountAsync(string connectionString, string table, Guid companyId) =>
        int.Parse((await ScalarAsync(connectionString, $"SELECT COUNT(*) FROM [work].[{table}] WHERE [CompanyId] = '{companyId}'"))!);

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Work_Outbox_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(baseConnection, databaseName: databaseName);
        string connectionString;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            connectionString = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>().Database.GetConnectionString()!;
        }

        try
        {
            await using var migrate = Db(connectionString, TestCompanyIds.CompanyA);
            await migrate.Database.MigrateAsync();
            await test(connectionString);
        }
        finally
        {
            await using var cleanupScope = factory.Services.CreateAsyncScope();
            await cleanupScope.ServiceProvider.GetRequiredService<OrganizationDbContext>().Database.EnsureDeletedAsync();
        }
    }

    private sealed class SqlHarness
    {
        private readonly WorkOutboxDispatcher dispatcher;
        private readonly Dictionary<Guid, List<OutboxMessageEnvelopeV1>> messages = [];
        private Guid active;

        public SqlHarness(string connectionString)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped<ICompanyContext>(_ => new Tracking(new CompanyContext(), id => active = id));
            services.AddDbContext<WorkDbContext>(o => o.UseSqlServer(
                connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", WorkDbContext.Schema)));
            services.AddSingleton<IOutboxSourceV1>(new Src(this));
            services.AddSingleton<IOutboxConsumerV1>(new Consumer(this));
            services.AddSingleton<IOutboxCompanyDirectoryV1>(new Dir());
            var provider = services.BuildServiceProvider();
            dispatcher = new WorkOutboxDispatcher(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new WorkOutboxDispatcherOptions
                {
                    Enabled = true,
                    BatchSize = 10,
                    MaxAttempts = 3,
                    InitialBackoff = TimeSpan.FromSeconds(1),
                    MaxBackoff = TimeSpan.FromSeconds(4),
                    PollInterval = TimeSpan.FromSeconds(1),
                }),
                TimeProvider.System,
                NullLogger<WorkOutboxDispatcher>.Instance);
        }

        public List<OutboxMessageEnvelopeV1> Handled { get; } = [];

        public void Add(Guid company, params int[] positions) =>
            messages[company] = positions.Select(n => new OutboxMessageEnvelopeV1(
                Source, Guid.Parse($"00000000-0000-0000-0000-{n:D12}"), company, Event, 1, "{\"n\":" + n + "}",
                new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero), "corr-" + n, null, n.ToString())).ToList();

        public Task<WorkOutboxDispatchSummary> RunAsync() => dispatcher.RunOnceAsync(CancellationToken.None);

        private sealed class Src(SqlHarness owner) : IOutboxSourceV1
        {
            public string SourceName => Source;

            public Task<IReadOnlyList<OutboxMessageEnvelopeV1>> ReadAfterAsync(string? afterPosition, int maxCount, CancellationToken cancellationToken)
            {
                var after = afterPosition is null ? 0 : int.Parse(afterPosition);
                IReadOnlyList<OutboxMessageEnvelopeV1> page = owner.messages.GetValueOrDefault(owner.active, [])
                    .Where(m => int.Parse(m.Position) > after).Take(maxCount).ToList();
                return Task.FromResult(page);
            }
        }

        private sealed class Consumer(SqlHarness owner) : IOutboxConsumerV1
        {
            public string ConsumerName => "finance";
            public IReadOnlyCollection<OutboxSubscriptionV1> Subscriptions { get; } = [new OutboxSubscriptionV1(Source, Event, 1)];

            public Task HandleAsync(OutboxMessageEnvelopeV1 message, CancellationToken cancellationToken)
            {
                owner.Handled.Add(message);
                return Task.CompletedTask;
            }
        }

        private sealed class Dir : IOutboxCompanyDirectoryV1
        {
            public Task<IReadOnlyList<Guid>> ListCompanyIdsAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<Guid>>([TestCompanyIds.CompanyA, TestCompanyIds.CompanyB]);
        }

        private sealed class Tracking(CompanyContext inner, Action<Guid> onSet) : ICompanyContext
        {
            public bool IsResolved => inner.IsResolved;
            public Guid CompanyId => inner.CompanyId;

            public void Set(Guid companyId)
            {
                inner.Set(companyId);
                onSet(companyId);
            }
        }
    }
}

using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyAuditEventTenantGuardSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task AuditSidecar_IsTenantFilteredAndLateCollectorWritesAreGuarded()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using var healthClient = factory.CreateClient();
            var healthResponse = await healthClient.GetAsync("/health/live");
            healthResponse.EnsureSuccessStatusCode();

            var companyACorrelation = $"cari-audit-a-{Guid.NewGuid():N}";
            var companyBCorrelation = $"cari-audit-b-{Guid.NewGuid():N}";

            await SaveCollectorEventAsync(
                factory,
                TestCompanyIds.CompanyA,
                CreateDraft(TestCompanyIds.CompanyA, companyACorrelation));
            await SaveCollectorEventAsync(
                factory,
                TestCompanyIds.CompanyB,
                CreateDraft(TestCompanyIds.CompanyB, companyBCorrelation));

            await AssertTenantFilteredQueriesAsync(
                factory,
                companyACorrelation,
                companyBCorrelation);
            await AssertUnresolvedQueryFailsClosedAsync(factory);

            var beforeMismatch = await ReadCountsAsync(factory.DatabaseConnectionString);
            var mismatchedCorrelation = $"cari-audit-mismatch-{Guid.NewGuid():N}";
            var mismatchException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SaveCollectorEventAsync(
                    factory,
                    TestCompanyIds.CompanyA,
                    CreateDraft(TestCompanyIds.CompanyB, mismatchedCorrelation)));

            Assert.Equal(
                "Audit kaydı aktif şirket bağlamıyla eşleşmelidir.",
                mismatchException.Message);
            Assert.Equal(
                beforeMismatch,
                await ReadCountsAsync(factory.DatabaseConnectionString));
            Assert.Equal(
                0,
                await CountAuditEventsAsync(
                    factory.DatabaseConnectionString,
                    mismatchedCorrelation));

            var beforeEmptyCompany = await ReadCountsAsync(factory.DatabaseConnectionString);
            var emptyCompanyCorrelation = $"cari-audit-empty-{Guid.NewGuid():N}";
            var emptyCompanyException = await Assert.ThrowsAsync<ArgumentException>(
                () => SaveCollectorEventAsync(
                    factory,
                    TestCompanyIds.CompanyA,
                    CreateDraft(Guid.Empty, emptyCompanyCorrelation)));

            Assert.Contains(
                nameof(AuditEvent.CompanyId),
                emptyCompanyException.ToString(),
                StringComparison.Ordinal);
            Assert.Equal(
                beforeEmptyCompany,
                await ReadCountsAsync(factory.DatabaseConnectionString));
            Assert.Equal(
                0,
                await CountAuditEventsAsync(
                    factory.DatabaseConnectionString,
                    emptyCompanyCorrelation));

            await AssertAppendOnlyGuardAsync(factory, companyACorrelation);
            Assert.Equal(
                2,
                await CountAuditEventsAsync(
                    factory.DatabaseConnectionString,
                    companyACorrelation,
                    companyBCorrelation));
            Assert.Equal(
                2,
                await ScalarAsync<int>(
                    factory.DatabaseConnectionString,
                    "SELECT COUNT(*) FROM [counterparty].[__EFMigrationsHistory];"));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task SaveCollectorEventAsync(
        SqlServerRelationalApiFactory factory,
        Guid activeCompanyId,
        AuditEventDraft draft)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContext>();
        companyContext.Set(activeCompanyId);
        var collector = scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
        collector.Enqueue(draft);
        var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();

        await dbContext.SaveChangesAsync();
    }

    private static async Task AssertTenantFilteredQueriesAsync(
        SqlServerRelationalApiFactory factory,
        string companyACorrelation,
        string companyBCorrelation)
    {
        await using (var companyAScope = factory.Services.CreateAsyncScope())
        {
            var companyContext = companyAScope.ServiceProvider
                .GetRequiredService<ICompanyContext>();
            companyContext.Set(TestCompanyIds.CompanyA);
            var dbContext = companyAScope.ServiceProvider
                .GetRequiredService<CounterpartyDbContext>();
            var visible = await dbContext.Set<AuditEvent>()
                .AsNoTracking()
                .Where(x =>
                    x.CorrelationId == companyACorrelation
                    || x.CorrelationId == companyBCorrelation)
                .ToListAsync();

            var auditEvent = Assert.Single(visible);
            Assert.Equal(TestCompanyIds.CompanyA, auditEvent.CompanyId);
            Assert.Equal(companyACorrelation, auditEvent.CorrelationId);
        }

        await using (var companyBScope = factory.Services.CreateAsyncScope())
        {
            var companyContext = companyBScope.ServiceProvider
                .GetRequiredService<ICompanyContext>();
            companyContext.Set(TestCompanyIds.CompanyB);
            var dbContext = companyBScope.ServiceProvider
                .GetRequiredService<CounterpartyDbContext>();
            var visible = await dbContext.Set<AuditEvent>()
                .AsNoTracking()
                .Where(x =>
                    x.CorrelationId == companyACorrelation
                    || x.CorrelationId == companyBCorrelation)
                .ToListAsync();

            var auditEvent = Assert.Single(visible);
            Assert.Equal(TestCompanyIds.CompanyB, auditEvent.CompanyId);
            Assert.Equal(companyBCorrelation, auditEvent.CorrelationId);
        }
    }

    private static async Task AssertUnresolvedQueryFailsClosedAsync(
        SqlServerRelationalApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.Set<AuditEvent>().AsNoTracking().CountAsync());

        Assert.Equal("Şirket bağlamı henüz belirlenmedi.", exception.Message);
    }

    private static async Task AssertAppendOnlyGuardAsync(
        SqlServerRelationalApiFactory factory,
        string correlationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContext>();
        companyContext.Set(TestCompanyIds.CompanyA);
        var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
        var auditEvent = await dbContext.Set<AuditEvent>()
            .SingleAsync(x => x.CorrelationId == correlationId);
        dbContext.Remove(auditEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal("Audit kayıtları değiştirilemez veya silinemez.", exception.Message);
        Assert.Equal(
            1,
            await CountAuditEventsAsync(factory.DatabaseConnectionString, correlationId));
    }

    private static AuditEventDraft CreateDraft(Guid companyId, string correlationId)
    {
        return new AuditEventDraft(
            companyId,
            "counterparty.audit-tenant-guard.probe.v1",
            "CounterpartyAuditTenantGuardProbe",
            Guid.NewGuid().ToString("N"),
            "Probe",
            null,
            null,
            AuditActorTypes.System,
            "counterparty-audit-tenant-guard-test",
            "counterparty.parties.read",
            ["SYSTEM_ADMINISTRATOR"],
            "{}",
            "{}",
            "Counterparty AuditEvent tenant guard gerçek SQL kabul testi",
            null,
            AuditAiInvolvement.None,
            null,
            SourceChannels.System,
            null,
            null,
            correlationId,
            null,
            $"idem-{correlationId}");
    }

    private static async Task<DatabaseCounts> ReadCountsAsync(string connectionString)
    {
        return new DatabaseCounts(
            await ScalarAsync<int>(
                connectionString,
                "SELECT COUNT(*) FROM [counterparty].[Parties];"),
            await ScalarAsync<int>(
                connectionString,
                "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts];"),
            await ScalarAsync<int>(
                connectionString,
                "SELECT COUNT(*) FROM [audit].[AuditEvents];"),
            await ScalarAsync<int>(
                connectionString,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions];"));
    }

    private static async Task<int> CountAuditEventsAsync(
        string connectionString,
        params string[] correlationIds)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM [audit].[AuditEvents]
            WHERE [CorrelationId] IN (@CorrelationId0, @CorrelationId1);
            """;
        command.Parameters.AddWithValue(
            "@CorrelationId0",
            correlationIds[0]);
        command.Parameters.AddWithValue(
            "@CorrelationId1",
            correlationIds.Length > 1 ? correlationIds[1] : correlationIds[0]);
        return Assert.IsType<int>(await command.ExecuteScalarAsync());
    }

    private static async Task<T> ScalarAsync<T>(
        string connectionString,
        string commandText)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Assert.IsType<T>(await command.ExecuteScalarAsync());
    }

    private static async Task DropDatabaseAsync(
        string baseConnectionString,
        string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed record DatabaseCounts(
        int Parties,
        int CompanyPartyAccounts,
        int AuditEvents,
        int CommandExecutions);
}

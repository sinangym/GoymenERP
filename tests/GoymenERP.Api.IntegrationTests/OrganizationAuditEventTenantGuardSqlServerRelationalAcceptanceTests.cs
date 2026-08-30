using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class OrganizationAuditEventTenantGuardSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task AuditSidecar_IsTenantFilteredAndAllWritePathsFailClosed()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using var healthClient = factory.CreateClient();
            var healthResponse = await healthClient.GetAsync("/health/live");
            healthResponse.EnsureSuccessStatusCode();

            var companyACorrelation = $"organization-audit-a-{Guid.NewGuid():N}";
            var companyBCorrelation = $"organization-audit-b-{Guid.NewGuid():N}";

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
            await AssertDirectAddedEmptyCompanyRejectedAsync(factory);
            await AssertDirectAddedUnresolvedWriteRejectedAsync(factory);
            await AssertDirectAddedMismatchRejectedAsync(factory);
            await AssertModifiedAndDeletedRejectedAsync(factory, companyACorrelation);
            await AssertAtomicRollbackAndSameScopeRetryAsync(factory);

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
                    "SELECT COUNT(*) FROM [organization].[__EFMigrationsHistory];"));
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
        var dbContext = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();

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
                .GetRequiredService<OrganizationDbContext>();
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
                .GetRequiredService<OrganizationDbContext>();
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
        var dbContext = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.Set<AuditEvent>().AsNoTracking().CountAsync());

        Assert.Equal("Şirket bağlamı henüz belirlenmedi.", exception.Message);
    }

    private static async Task AssertDirectAddedEmptyCompanyRejectedAsync(
        SqlServerRelationalApiFactory factory)
    {
        var before = await ReadCountsAsync(factory.DatabaseConnectionString);

        await using var scope = factory.Services.CreateAsyncScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContext>();
        companyContext.Set(TestCompanyIds.CompanyA);
        var dbContext = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var auditEvent = Assert.IsType<AuditEvent>(
            Activator.CreateInstance(typeof(AuditEvent), nonPublic: true));
        dbContext.Set<AuditEvent>().Add(auditEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal("Audit kaydı CompanyId olmadan yazılamaz.", exception.Message);
        Assert.Equal(before, await ReadCountsAsync(factory.DatabaseConnectionString));
    }

    private static async Task AssertDirectAddedUnresolvedWriteRejectedAsync(
        SqlServerRelationalApiFactory factory)
    {
        var correlationId = $"organization-audit-unresolved-write-{Guid.NewGuid():N}";
        var before = await ReadCountsAsync(factory.DatabaseConnectionString);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        dbContext.Set<AuditEvent>().Add(
            CreateAuditEvent(TestCompanyIds.CompanyA, correlationId));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kaydı yazmak için aktif şirket bağlamı zorunludur.",
            exception.Message);
        Assert.Equal(before, await ReadCountsAsync(factory.DatabaseConnectionString));
        Assert.Equal(
            0,
            await CountAuditEventsAsync(factory.DatabaseConnectionString, correlationId));
    }

    private static async Task AssertDirectAddedMismatchRejectedAsync(
        SqlServerRelationalApiFactory factory)
    {
        var correlationId = $"organization-audit-direct-mismatch-{Guid.NewGuid():N}";
        var before = await ReadCountsAsync(factory.DatabaseConnectionString);

        await using var scope = factory.Services.CreateAsyncScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContext>();
        companyContext.Set(TestCompanyIds.CompanyA);
        var dbContext = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        dbContext.Set<AuditEvent>().Add(
            CreateAuditEvent(TestCompanyIds.CompanyB, correlationId));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kaydı aktif şirket bağlamıyla eşleşmelidir.",
            exception.Message);
        Assert.Equal(before, await ReadCountsAsync(factory.DatabaseConnectionString));
        Assert.Equal(
            0,
            await CountAuditEventsAsync(factory.DatabaseConnectionString, correlationId));
    }

    private static async Task AssertModifiedAndDeletedRejectedAsync(
        SqlServerRelationalApiFactory factory,
        string correlationId)
    {
        var before = await ReadCountsAsync(factory.DatabaseConnectionString);

        await using (var modifiedScope = factory.Services.CreateAsyncScope())
        {
            var companyContext = modifiedScope.ServiceProvider
                .GetRequiredService<ICompanyContext>();
            companyContext.Set(TestCompanyIds.CompanyA);
            var dbContext = modifiedScope.ServiceProvider
                .GetRequiredService<OrganizationDbContext>();
            var auditEvent = await dbContext.Set<AuditEvent>()
                .SingleAsync(x => x.CorrelationId == correlationId);
            var reason = dbContext.Entry(auditEvent).Property(nameof(AuditEvent.Reason));
            reason.CurrentValue = "Değiştirilemez audit nedeni";
            reason.IsModified = true;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbContext.SaveChangesAsync());

            Assert.Equal(
                "Audit kayıtları değiştirilemez veya silinemez.",
                exception.Message);
        }

        Assert.Equal(before, await ReadCountsAsync(factory.DatabaseConnectionString));

        await using (var deletedScope = factory.Services.CreateAsyncScope())
        {
            var companyContext = deletedScope.ServiceProvider
                .GetRequiredService<ICompanyContext>();
            companyContext.Set(TestCompanyIds.CompanyA);
            var dbContext = deletedScope.ServiceProvider
                .GetRequiredService<OrganizationDbContext>();
            var auditEvent = await dbContext.Set<AuditEvent>()
                .SingleAsync(x => x.CorrelationId == correlationId);
            dbContext.Remove(auditEvent);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbContext.SaveChangesAsync());

            Assert.Equal(
                "Audit kayıtları değiştirilemez veya silinemez.",
                exception.Message);
        }

        Assert.Equal(before, await ReadCountsAsync(factory.DatabaseConnectionString));
        Assert.Equal(
            1,
            await CountAuditEventsAsync(factory.DatabaseConnectionString, correlationId));
    }

    private static async Task AssertAtomicRollbackAndSameScopeRetryAsync(
        SqlServerRelationalApiFactory factory)
    {
        var invalidCorrelation = $"organization-audit-atomic-invalid-{Guid.NewGuid():N}";
        var validCorrelation = $"organization-audit-atomic-valid-{Guid.NewGuid():N}";
        var before = await ReadCountsAsync(factory.DatabaseConnectionString);
        var siteId = Guid.NewGuid();
        var commandExecutionId = Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContext>();
        companyContext.Set(TestCompanyIds.CompanyA);
        var collector = scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();

        dbContext.Sites.Add(
            new Site(
                siteId,
                TestCompanyIds.CompanyA,
                $"AUD-{Guid.NewGuid():N}",
                "Audit Tenant Guard Atomik Test Tesisi",
                SiteType.Factory));
        dbContext.Set<CommandExecution>().Add(
            CommandExecution.Complete(
                new CommandExecutionData(
                    commandExecutionId,
                    TestCompanyIds.CompanyA,
                    "organization.audit-tenant-guard.atomic.v1",
                    new string('A', 64),
                    new string('B', 64),
                    "organization-audit-tenant-guard-test",
                    "Succeeded",
                    201,
                    "{}",
                    validCorrelation,
                    DateTimeOffset.UtcNow,
                    1)));
        collector.Enqueue(CreateDraft(TestCompanyIds.CompanyB, invalidCorrelation));

        var mismatchException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Audit kaydı aktif şirket bağlamıyla eşleşmelidir.",
            mismatchException.Message);
        Assert.Equal(before, await ReadCountsAsync(factory.DatabaseConnectionString));
        Assert.Equal(
            0,
            await CountAuditEventsAsync(
                factory.DatabaseConnectionString,
                invalidCorrelation,
                validCorrelation));
        Assert.Empty(dbContext.ChangeTracker.Entries<AuditEvent>());
        Assert.Equal(EntityState.Added, dbContext.Entry(dbContext.Sites.Local.Single()).State);
        Assert.Equal(
            EntityState.Added,
            dbContext.Entry(dbContext.Set<CommandExecution>().Local.Single()).State);

        collector.Enqueue(CreateDraft(TestCompanyIds.CompanyA, validCorrelation));
        await dbContext.SaveChangesAsync();

        var afterRetry = await ReadCountsAsync(factory.DatabaseConnectionString);
        Assert.Equal(before.Sites + 1, afterRetry.Sites);
        Assert.Equal(before.AuditEvents + 1, afterRetry.AuditEvents);
        Assert.Equal(before.CommandExecutions + 1, afterRetry.CommandExecutions);
        Assert.Equal(
            0,
            await CountAuditEventsAsync(factory.DatabaseConnectionString, invalidCorrelation));
        Assert.Equal(
            1,
            await CountAuditEventsAsync(factory.DatabaseConnectionString, validCorrelation));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                factory.DatabaseConnectionString,
                "SELECT COUNT(*) FROM [organization].[Sites] WHERE [Id] = @Id;",
                new SqlParameter("@Id", siteId)));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                factory.DatabaseConnectionString,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [Id] = @Id;",
                new SqlParameter("@Id", commandExecutionId)));
    }

    private static AuditEventDraft CreateDraft(Guid companyId, string correlationId)
    {
        return new AuditEventDraft(
            companyId,
            "organization.audit-tenant-guard.probe.v1",
            "OrganizationAuditTenantGuardProbe",
            Guid.NewGuid().ToString("N"),
            "Probe",
            null,
            null,
            AuditActorTypes.System,
            "organization-audit-tenant-guard-test",
            "organization.sites.read",
            ["SYSTEM_ADMINISTRATOR"],
            "{}",
            "{}",
            "Organization AuditEvent tenant guard gerçek SQL kabul testi",
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

    private static AuditEvent CreateAuditEvent(Guid companyId, string correlationId)
    {
        var envelope = new AuditEventEnvelope(
            Guid.NewGuid(),
            companyId,
            DateTimeOffset.UtcNow,
            "organization.audit-tenant-guard.direct.v1",
            "OrganizationAuditTenantGuardProbe",
            Guid.NewGuid().ToString("N"),
            "Probe",
            null,
            null,
            AuditActorTypes.System,
            "organization-audit-tenant-guard-test",
            "organization.sites.read",
            "[\"SYSTEM_ADMINISTRATOR\"]",
            "{}",
            "{}",
            "Organization direct AuditEvent tenant guard gerçek SQL kabul testi",
            null,
            AuditAiInvolvement.None,
            null,
            SourceChannels.System,
            null,
            null,
            correlationId,
            null,
            new string('C', 64),
            AuditOutcomes.Succeeded,
            1);

        return AuditEvent.Create(
            new AuditEventData(envelope, AuditEventIntegrity.Compute(envelope)));
    }

    private static async Task<DatabaseCounts> ReadCountsAsync(string connectionString)
    {
        return new DatabaseCounts(
            await ScalarAsync<int>(
                connectionString,
                "SELECT COUNT(*) FROM [organization].[Sites];"),
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
        var parameterNames = correlationIds
            .Select((_, index) => $"@CorrelationId{index}")
            .ToArray();
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM [audit].[AuditEvents]
            WHERE [CorrelationId] IN ({string.Join(", ", parameterNames)});
            """;
        for (var index = 0; index < correlationIds.Length; index++)
        {
            command.Parameters.AddWithValue(parameterNames[index], correlationIds[index]);
        }

        return Assert.IsType<int>(await command.ExecuteScalarAsync());
    }

    private static async Task<T> ScalarAsync<T>(
        string connectionString,
        string commandText,
        params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
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
        int Sites,
        int AuditEvents,
        int CommandExecutions);
}

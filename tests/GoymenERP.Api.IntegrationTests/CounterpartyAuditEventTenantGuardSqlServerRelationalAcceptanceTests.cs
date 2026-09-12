using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyAuditEventTenantGuardSqlServerRelationalAcceptanceTests
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
            await AssertDirectAddedMismatchRejectedAsync(factory);
            await AssertUnresolvedWriteRejectedAsync(factory);
            await AssertModifiedAndDeletedRejectedAsync(factory, companyACorrelation);
            await AssertAtomicRollbackAndSameScopeRetryAsync(factory);
            await AssertEmptyCompanyRejectedBySharedGuardAsync(factory);

            Assert.Equal(
                2,
                await CountAuditEventsAsync(
                    factory.DatabaseConnectionString,
                    companyACorrelation,
                    companyBCorrelation));
            Assert.Equal(
                4,
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

    private static async Task AssertDirectAddedMismatchRejectedAsync(
        SqlServerRelationalApiFactory factory)
    {
        var correlationId = $"cari-audit-direct-mismatch-{Guid.NewGuid():N}";
        var before = await ReadCountsAsync(factory.DatabaseConnectionString);

        await using var scope = factory.Services.CreateAsyncScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContext>();
        companyContext.Set(TestCompanyIds.CompanyA);
        var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
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

    private static async Task AssertUnresolvedWriteRejectedAsync(
        SqlServerRelationalApiFactory factory)
    {
        var correlationId = $"cari-audit-unresolved-write-{Guid.NewGuid():N}";
        var before = await ReadCountsAsync(factory.DatabaseConnectionString);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
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
                .GetRequiredService<CounterpartyDbContext>();
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
                .GetRequiredService<CounterpartyDbContext>();
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
        var invalidCorrelation = $"cari-audit-atomic-invalid-{Guid.NewGuid():N}";
        var validCorrelation = $"cari-audit-atomic-valid-{Guid.NewGuid():N}";
        var before = await ReadCountsAsync(factory.DatabaseConnectionString);
        var partyId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var commandExecutionId = Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContext>();
        companyContext.Set(TestCompanyIds.CompanyA);
        var collector = scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
        var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();

        dbContext.Parties.Add(
            new Party(
                partyId,
                PartyCode.Parse("CAR-900001"),
                PartyKind.LegalEntity,
                "Audit Tenant Guard Atomik Test Cari"));
        dbContext.CompanyPartyAccounts.Add(
            new CompanyPartyAccount(
                accountId,
                TestCompanyIds.CompanyA,
                partyId,
                PartyRole.Customer));
        dbContext.Set<CommandExecution>().Add(
            CommandExecution.Complete(
                new CommandExecutionData(
                    commandExecutionId,
                    TestCompanyIds.CompanyA,
                    "counterparty.audit-tenant-guard.atomic.v1",
                    new string('A', 64),
                    new string('B', 64),
                    "counterparty-audit-tenant-guard-test",
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
        Assert.Equal(EntityState.Added, dbContext.Entry(dbContext.Parties.Local.Single()).State);
        Assert.Equal(
            EntityState.Added,
            dbContext.Entry(dbContext.CompanyPartyAccounts.Local.Single()).State);
        Assert.Equal(
            EntityState.Added,
            dbContext.Entry(dbContext.Set<CommandExecution>().Local.Single()).State);

        collector.Enqueue(CreateDraft(TestCompanyIds.CompanyA, validCorrelation));
        await dbContext.SaveChangesAsync();

        var afterRetry = await ReadCountsAsync(factory.DatabaseConnectionString);
        Assert.Equal(before.Parties + 1, afterRetry.Parties);
        Assert.Equal(before.CompanyPartyAccounts + 1, afterRetry.CompanyPartyAccounts);
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
                $"SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id] = '{partyId}';"));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                factory.DatabaseConnectionString,
                $"SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [Id] = '{commandExecutionId}';"));
    }

    private static async Task AssertEmptyCompanyRejectedBySharedGuardAsync(
        SqlServerRelationalApiFactory factory)
    {
        var before = await ReadCountsAsync(factory.DatabaseConnectionString);
        var correlationId = $"cari-audit-empty-{Guid.NewGuid():N}";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SaveCollectorEventAsync(
                factory,
                TestCompanyIds.CompanyA,
                CreateDraft(Guid.Empty, correlationId)));

        Assert.Equal(
            "Audit kaydı CompanyId olmadan yazılamaz.",
            exception.Message);
        Assert.Equal(before, await ReadCountsAsync(factory.DatabaseConnectionString));
        Assert.Equal(
            0,
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

    private static AuditEvent CreateAuditEvent(Guid companyId, string correlationId)
    {
        var envelope = new AuditEventEnvelope(
            Guid.NewGuid(),
            companyId,
            DateTimeOffset.UtcNow,
            "counterparty.audit-tenant-guard.direct.v1",
            "CounterpartyAuditTenantGuardProbe",
            Guid.NewGuid().ToString("N"),
            "Probe",
            null,
            null,
            AuditActorTypes.System,
            "counterparty-audit-tenant-guard-test",
            "counterparty.parties.read",
            "[\"SYSTEM_ADMINISTRATOR\"]",
            "{}",
            "{}",
            "Counterparty direct AuditEvent tenant guard gerçek SQL kabul testi",
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

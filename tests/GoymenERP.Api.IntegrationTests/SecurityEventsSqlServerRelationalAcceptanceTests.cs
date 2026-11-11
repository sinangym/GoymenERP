using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Domain.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>AI-2a security event channel on real SQL Server (ADR-SEC-001, #66 6806619303 item 11).</summary>
public sealed class SecurityEventsSqlServerRelationalAcceptanceTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 11, 11, 9, 15, 30, 123, TimeSpan.Zero);

    [SqlServerFact]
    public async Task The_writer_appends_hash_verifiable_rows_and_company_reads_never_see_another_company_or_platform_rows_on_real_sql()
    {
        await WithHostAsync(async (services, connectionString) =>
        {
            var writer = services.GetRequiredService<ISecurityEventWriterV1>();
            var companyA = Denial(TestCompanyIds.CompanyA);
            var companyB = Denial(TestCompanyIds.CompanyB);

            Assert.True((await writer.WriteAsync(companyA, CancellationToken.None)).Recorded);
            Assert.True((await writer.WriteAsync(companyB, CancellationToken.None)).Recorded);
            Assert.True((await writer.WriteAsync(PlatformLoginFailure(), CancellationToken.None)).Recorded);
            Assert.Equal("3", await ScalarAsync(connectionString, "SELECT COUNT(*) FROM [security].[SecurityEvents]"));

            await using (var readA = Db(connectionString, TestCompanyIds.CompanyA))
            {
                var row = Assert.Single(await readA.SecurityEvents.AsNoTracking().ToListAsync());
                Assert.Equal(companyA.EventId, row.Id);
                Assert.Equal(OccurredAt, row.OccurredAtUtc);
                Assert.Equal(OccurredAt.AddYears(2), row.RetainUntilUtc);
                Assert.Equal("SINAN-2026-10-21-No134", row.RetentionPolicyRef);
                Assert.False(row.LegalHold);
                Assert.Equal(1, row.OccurrenceCount);
                Assert.Null(row.AggregationWindowStartUtc);
                Assert.Equal(row.IntegrityHash, SecurityEventIntegrity.Compute(row.ToIntegrityEnvelope()));
            }

            await using (var readB = Db(connectionString, TestCompanyIds.CompanyB))
            {
                var row = Assert.Single(await readB.SecurityEvents.AsNoTracking().ToListAsync());
                Assert.Equal(companyB.EventId, row.Id);
                Assert.Null(await readB.SecurityEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == companyA.EventId));
            }
        });
    }

    [SqlServerFact]
    public async Task Update_and_delete_are_refused_with_51271_and_bad_rows_are_refused_by_check_constraints_on_real_sql()
    {
        await WithHostAsync(async (services, connectionString) =>
        {
            var writer = services.GetRequiredService<ISecurityEventWriterV1>();
            var stored = Denial(TestCompanyIds.CompanyA);
            Assert.True((await writer.WriteAsync(stored, CancellationToken.None)).Recorded);

            var update = await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString,
                $"UPDATE [security].[SecurityEvents] SET [Outcome] = 'Allowed' WHERE [Id] = '{stored.EventId}'"));
            Assert.Equal(51271, Sql(update).Number);
            var delete = await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString,
                $"DELETE FROM [security].[SecurityEvents] WHERE [Id] = '{stored.EventId}'"));
            Assert.Equal(51271, Sql(delete).Number);
            Assert.Equal("1", await ScalarAsync(connectionString, "SELECT COUNT(*) FROM [security].[SecurityEvents]"));

            foreach (var badColumns in new[]
            {
                "1, 1, NULL, NULL",
                "0, 2, NULL, NULL",
                "0, 0, NULL, NULL",
                "0, 2, SYSUTCDATETIME(), NULL",
                "0, 2, DATEADD(minute, 5, SYSUTCDATETIME()), SYSUTCDATETIME()",
            })
            {
                var refused = await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString, $"""
                    INSERT INTO [security].[SecurityEvents]
                    ([Id],[SchemaVersion],[OccurredAtUtc],[ScopeKind],[CompanyId],[ActorType],[ActorId],[EventType],[Phase],[Outcome],[ReasonCode],[Severity],[CorrelationId],[SourceChannel],[RetainUntilUtc],[RetentionPolicyRef],[LegalHold],[OccurrenceCount],[AggregationWindowStartUtc],[AggregationWindowEndUtc],[IntegrityHash])
                    VALUES ('{Guid.NewGuid()}',1,SYSUTCDATETIME(),'Company','{TestCompanyIds.CompanyA}','User','user-1','authz.denied','Single','Denied','permission_missing','Warning','corr-x','Web',DATEADD(year, 2, SYSUTCDATETIME()),'X',{badColumns},'{new string('A', 64)}')
                    """));
                Assert.Equal(547, Sql(refused).Number);
            }
        });
    }

    [SqlServerFact]
    public async Task Down_refuses_with_51270_while_security_events_exist_on_real_sql()
    {
        await WithHostAsync(async (services, connectionString) =>
        {
            var writer = services.GetRequiredService<ISecurityEventWriterV1>();
            Assert.True((await writer.WriteAsync(Denial(TestCompanyIds.CompanyA), CancellationToken.None)).Recorded);

            await using var context = Db(connectionString, TestCompanyIds.CompanyA);
            var refusal = await Assert.ThrowsAnyAsync<Exception>(() => context.GetService<IMigrator>().MigrateAsync("0"));
            Assert.Equal(51270, Sql(refusal).Number);
            Assert.Equal("1", await ScalarAsync(connectionString, "SELECT COUNT(*) FROM [security].[SecurityEvents]"));
        });
    }

    [SqlServerFact]
    public async Task Down_drops_an_empty_security_events_table_and_up_reapplies_it_on_real_sql()
    {
        await WithHostAsync(async (_, connectionString) =>
        {
            await using var context = Db(connectionString, TestCompanyIds.CompanyA);
            await context.GetService<IMigrator>().MigrateAsync("0");
            Assert.True(string.IsNullOrEmpty(await ScalarAsync(connectionString, "SELECT OBJECT_ID(N'[security].[SecurityEvents]')")));

            await context.Database.MigrateAsync();
            Assert.False(string.IsNullOrEmpty(await ScalarAsync(connectionString,
                "SELECT OBJECT_ID(N'[security].[TR_SecurityEvents_RejectUpdateDelete]')")));
        });
    }

    private static SecurityEventV1 Denial(Guid companyId) => new()
    {
        EventId = Guid.NewGuid(),
        OccurredAtUtc = OccurredAt,
        ScopeKind = SecurityEventScopeKindsV1.Company,
        CompanyId = companyId,
        ActorType = SecurityEventActorTypesV1.User,
        ActorId = "7c4f2b1e-3d5a-4e6f-8a9b-00000000051a",
        EventType = SecurityEventTypesV1.AuthorizationDenied,
        Outcome = SecurityEventOutcomesV1.Denied,
        ReasonCode = SecurityEventReasonCodesV1.PermissionMissing,
        Severity = SecurityEventSeveritiesV1.Warning,
        PermissionCode = "purchasing.requests.approve",
        CorrelationId = $"corr-{Guid.NewGuid():N}",
        SourceChannel = SourceChannels.Web,
    };

    private static SecurityEventV1 PlatformLoginFailure() => new()
    {
        EventId = Guid.NewGuid(),
        OccurredAtUtc = OccurredAt,
        ScopeKind = SecurityEventScopeKindsV1.Platform,
        RequestedCompanyId = TestCompanyIds.CompanyA,
        ActorType = SecurityEventActorTypesV1.Anonymous,
        EventType = SecurityEventTypesV1.LoginFailed,
        Outcome = SecurityEventOutcomesV1.Denied,
        ReasonCode = SecurityEventReasonCodesV1.CredentialsInvalid,
        Severity = SecurityEventSeveritiesV1.Warning,
        CorrelationId = $"corr-{Guid.NewGuid():N}",
        SourceChannel = SourceChannels.Web,
    };

    private static SecurityEventsDbContext Db(string connectionString, Guid companyId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        return new SecurityEventsDbContext(
            new DbContextOptionsBuilder<SecurityEventsDbContext>()
                .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable(
                    "__EFMigrationsHistory", SecurityEventModelBuilderExtensions.Schema))
                .Options,
            company);
    }

    private static SqlException Sql(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql)
            {
                return sql;
            }
        }

        throw new InvalidOperationException("No SqlException found.", exception);
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
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task WithHostAsync(Func<IServiceProvider, string, Task> test)
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        await using var factory = new SqlServerRelationalApiFactory(
            baseConnection, databaseName: $"GoymenERP_SecurityEvents_{Guid.NewGuid():N}");
        try
        {
            await test(factory.Services, factory.DatabaseConnectionString);
        }
        finally
        {
            await using var cleanupScope = factory.Services.CreateAsyncScope();
            await cleanupScope.ServiceProvider.GetRequiredService<SecurityEventsDbContext>().Database.EnsureDeletedAsync();
        }
    }
}

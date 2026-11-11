using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.SecurityEvents.Domain.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Reading;
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
            var serverBefore = DateTimeOffset.UtcNow;

            Assert.True((await writer.WriteAsync(companyA, CancellationToken.None)).Recorded);
            Assert.True((await writer.WriteAsync(companyB, CancellationToken.None)).Recorded);
            Assert.True((await writer.WriteAsync(PlatformLoginFailure(), CancellationToken.None)).Recorded);
            var serverAfter = DateTimeOffset.UtcNow;
            Assert.Equal("3", await ScalarAsync(connectionString, "SELECT COUNT(*) FROM [security].[SecurityEvents]"));

            await using (var readA = Db(connectionString, TestCompanyIds.CompanyA))
            {
                var row = Assert.Single(await readA.SecurityEvents.AsNoTracking().ToListAsync());
                Assert.Equal(companyA.EventId, row.Id);
                Assert.Equal(OccurredAt, row.OccurredAtUtc);
                // Retention is measured from the server clock, never from the caller-supplied (back-dated) time.
                Assert.InRange(row.RetainUntilUtc, serverBefore.AddYears(2), serverAfter.AddYears(2));
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
    public async Task Duplicate_event_ids_are_recorded_once_on_retry_and_refused_on_conflicting_content_on_real_sql()
    {
        await WithHostAsync(async (services, connectionString) =>
        {
            var writer = services.GetRequiredService<ISecurityEventWriterV1>();
            var original = Denial(TestCompanyIds.CompanyA);
            Assert.True((await writer.WriteAsync(original, CancellationToken.None)).Recorded);

            var retry = await writer.WriteAsync(original with { }, CancellationToken.None);
            Assert.True(retry.Recorded);
            Assert.Null(retry.FailureKind);

            var conflicting = await writer.WriteAsync(
                original with { PermissionCode = "purchasing.requests.reject" }, CancellationToken.None);
            Assert.False(conflicting.Recorded);
            Assert.Equal(SecurityEventWriteFailureKindsV1.DuplicateEventId, conflicting.FailureKind);

            var otherCompany = await writer.WriteAsync(
                original with { CompanyId = TestCompanyIds.CompanyB }, CancellationToken.None);
            Assert.False(otherCompany.Recorded);
            Assert.Equal(SecurityEventWriteFailureKindsV1.DuplicateEventId, otherCompany.FailureKind);

            var platform = PlatformLoginFailure();
            Assert.True((await writer.WriteAsync(platform, CancellationToken.None)).Recorded);
            var platformRetry = await writer.WriteAsync(platform with { }, CancellationToken.None);
            Assert.False(platformRetry.Recorded);
            Assert.Equal(SecurityEventWriteFailureKindsV1.DuplicateEventId, platformRetry.FailureKind);

            Assert.Equal("2", await ScalarAsync(connectionString, "SELECT COUNT(*) FROM [security].[SecurityEvents]"));
            await using var readA = Db(connectionString, TestCompanyIds.CompanyA);
            var row = Assert.Single(await readA.SecurityEvents.AsNoTracking().ToListAsync());
            Assert.Equal("purchasing.requests.approve", row.PermissionCode);
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
    public async Task Code_list_checks_refuse_unknown_codes_and_their_migration_rolls_back_and_reapplies_on_real_sql()
    {
        await WithHostAsync(async (services, connectionString) =>
        {
            var writer = services.GetRequiredService<ISecurityEventWriterV1>();
            Assert.True((await writer.WriteAsync(Denial(TestCompanyIds.CompanyA), CancellationToken.None)).Recorded);

            await ExecAsync(connectionString, RawInsert(new Dictionary<string, string>()));
            foreach (var bad in new Dictionary<string, string>[]
            {
                new() { ["ActorType"] = "'Robot'" },
                new() { ["ActorType"] = "'Anonymous'" },
                new() { ["ActorId"] = "NULL" },
                new() { ["Severity"] = "'Critical'" },
                new() { ["SourceChannel"] = "'Email'" },
                new() { ["ReasonCode"] = "'made_up'" },
                new() { ["AiRiskClass"] = "'Delete'" },
                new() { ["AiDataClass"] = "'Secret'" },
            })
            {
                var refused = await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString, RawInsert(bad)));
                Assert.Equal(547, Sql(refused).Number);
            }

            Assert.Equal("2", await ScalarAsync(connectionString, "SELECT COUNT(*) FROM [security].[SecurityEvents]"));
            await using var context = Db(connectionString, TestCompanyIds.CompanyA);
            await context.GetService<IMigrator>().MigrateAsync(nameof(InitialSecurityEvents));
            Assert.True(string.IsNullOrEmpty(await ScalarAsync(connectionString,
                "SELECT OBJECT_ID(N'[security].[CK_SecurityEvents_ActorType]')")));
            Assert.Equal("2", await ScalarAsync(connectionString, "SELECT COUNT(*) FROM [security].[SecurityEvents]"));

            await context.Database.MigrateAsync();
            Assert.False(string.IsNullOrEmpty(await ScalarAsync(connectionString,
                "SELECT OBJECT_ID(N'[security].[CK_SecurityEvents_ActorType]')")));
            Assert.Equal("1", await ScalarAsync(connectionString,
                "SELECT COUNT(*) FROM sys.check_constraints WHERE [name] = N'CK_SecurityEvents_SourceChannel' AND [is_not_trusted] = 0"));
        });
    }

    /// <summary>
    /// AI-2b (ADR-SEC-001 section 5): GET /api/v1/security-events on real SQL Server. A test-local role holds the
    /// read permission (no shipped role does). The reader sees only its own company's company-scoped rows (never
    /// Company B or platform rows), and its read is stored as security.events.read before the rows are returned.
    /// </summary>
    [SqlServerFact]
    public async Task The_security_events_read_returns_only_own_company_rows_and_is_recorded_as_a_read_event_on_real_sql()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        await using var factory = new SqlServerRelationalApiFactory(
            baseConnection, databaseName: $"GoymenERP_SecurityEventsRead_{Guid.NewGuid():N}");
        try
        {
            using (var health = await factory.CreateClient().GetAsync("/health/live"))
            {
                health.EnsureSuccessStatusCode();
            }

            var reader = await GrantReaderAsync(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA);
            var writer = factory.Services.GetRequiredService<ISecurityEventWriterV1>();
            var companyA = Denial(TestCompanyIds.CompanyA);
            Assert.True((await writer.WriteAsync(companyA, CancellationToken.None)).Recorded);
            Assert.True((await writer.WriteAsync(Denial(TestCompanyIds.CompanyB), CancellationToken.None)).Recorded);
            Assert.True((await writer.WriteAsync(PlatformLoginFailure(), CancellationToken.None)).Recorded);

            using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);
            var readCorrelation = $"se-read-{Guid.NewGuid():N}";
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            client.DefaultRequestHeaders.Add("X-Correlation-Id", readCorrelation);
            using var response = await client.GetAsync("/api/v1/security-events?pageSize=100");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = Assert.IsType<SecurityEventPageV1>(await response.Content.ReadFromJsonAsync<SecurityEventPageV1>());
            Assert.All(page.Items, item => Assert.Equal(TestCompanyIds.CompanyA, item.CompanyId));
            Assert.Contains(page.Items, item => item.Id == companyA.EventId);
            // The read event was committed before the rows were read, so it is on the page it guards.
            var readEvent = Assert.Single(page.Items, item => item.EventType == SecurityEventTypesV1.SecurityEventsRead);
            Assert.Equal(readCorrelation, readEvent.CorrelationId);
            Assert.Equal(reader.ToString("D"), readEvent.ActorId);
            Assert.Equal(ErpPermissions.PlatformSecurityEventsRead, readEvent.PermissionCode);
            Assert.Equal(2, page.Items.Count);

            Assert.Equal("1", await ScalarAsync(factory.DatabaseConnectionString,
                "SELECT COUNT(*) FROM [security].[SecurityEvents] WHERE [EventType] = N'security.events.read' "
                + $"AND [CompanyId] = '{TestCompanyIds.CompanyA:D}' AND [Outcome] = N'allowed'"));
            Assert.Equal("4", await ScalarAsync(factory.DatabaseConnectionString,
                "SELECT COUNT(*) FROM [security].[SecurityEvents]"));
        }
        finally
        {
            await using var cleanupScope = factory.Services.CreateAsyncScope();
            await cleanupScope.ServiceProvider.GetRequiredService<SecurityEventsDbContext>().Database.EnsureDeletedAsync();
        }
    }

    private static async Task<Guid> GrantReaderAsync(SqlServerRelationalApiFactory factory, string subject, Guid company)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var link = await identity.ExternalIdentities.SingleOrDefaultAsync(
            x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == subject);
        Guid actor;
        if (link is null)
        {
            actor = Guid.NewGuid();
            identity.Users.Add(new UserAccount(actor, "Synthetic security events reader", null));
            identity.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, subject));
        }
        else
        {
            actor = link.UserId;
        }

        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"SE-READ-{roleId:N}", "Synthetic security events reader role", false);
        role.AddPermission(ErpPermissions.PlatformSecurityEventsRead);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, company);
        identity.Roles.Add(role);
        identity.RoleAssignments.Add(assignment);
        await identity.SaveChangesAsync();
        return actor;
    }

    private static string RawInsert(IReadOnlyDictionary<string, string> overrides)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Id"] = $"'{Guid.NewGuid()}'",
            ["SchemaVersion"] = "1",
            ["OccurredAtUtc"] = "SYSUTCDATETIME()",
            ["ScopeKind"] = "'Company'",
            ["CompanyId"] = $"'{TestCompanyIds.CompanyA}'",
            ["ActorType"] = "'User'",
            ["ActorId"] = "'7c4f2b1e-3d5a-4e6f-8a9b-00000000051b'",
            ["EventType"] = "'authz.denied'",
            ["Phase"] = "'Single'",
            ["Outcome"] = "'Denied'",
            ["ReasonCode"] = "'permission_missing'",
            ["Severity"] = "'Warning'",
            ["CorrelationId"] = "'corr-raw'",
            ["SourceChannel"] = "'Web'",
            ["AiRiskClass"] = "NULL",
            ["AiDataClass"] = "NULL",
            ["RetainUntilUtc"] = "DATEADD(year, 2, SYSUTCDATETIME())",
            ["RetentionPolicyRef"] = "'X'",
            ["LegalHold"] = "0",
            ["OccurrenceCount"] = "1",
            ["IntegrityHash"] = $"'{new string('A', 64)}'",
        };
        foreach (var (column, value) in overrides)
        {
            values[column] = value;
        }

        return $"INSERT INTO [security].[SecurityEvents] ({string.Join(",", values.Keys.Select(k => $"[{k}]"))}) "
            + $"VALUES ({string.Join(",", values.Values)})";
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

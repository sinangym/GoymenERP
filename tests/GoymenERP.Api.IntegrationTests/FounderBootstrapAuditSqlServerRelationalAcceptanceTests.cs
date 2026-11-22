using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Infrastructure.Bootstrap;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// K3-B finding 3 on real SQL Server: the founder bootstrap writes one typed company System audit row per bootstrapped
/// company in its own Serializable transaction, and a refused audit rolls the founder, its assignments and the marker
/// back. SENTETIK.
/// </summary>
public sealed class FounderBootstrapAuditSqlServerRelationalAcceptanceTests
{
    private const string Password = "SentetikOnly!Kurulum.6413";

    [SqlServerFact]
    public async Task Founder_bootstrap_writes_one_typed_system_audit_row_per_company_and_a_refused_audit_rolls_everything_back_on_real_sql()
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        await using var factory = new SqlServerRelationalApiFactory(connection);
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);
        using var health = await client.GetAsync("/health/live"); health.EnsureSuccessStatusCode();
        var guard = Assert.IsType<SqlOperation>(new AddFounderBootstrapSystemAudit().DownOperations[0]).Sql;
        var adminRoleCode = "SENTETIK-KURUCU-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        List<Guid> companies;
        await using (var setup = factory.Services.CreateAsyncScope())
        {
            var identity = setup.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.False(await identity.BootstrapStates.AnyAsync());
            var role = new RoleDefinition(Guid.NewGuid(), adminRoleCode, "Sentetik kurucu yönetici", isSystem: true);
            role.AddPermission(ErpPermissions.CounterpartyPartiesTaxIdentityApprove);
            Assert.True(ErpPermissions.IsCritical(ErpPermissions.CounterpartyPartiesTaxIdentityApprove));
            identity.Roles.Add(role);
            await identity.SaveChangesAsync();
            companies = await identity.Companies.AsNoTracking().OrderBy(company => company.Id).Select(company => company.Id).ToListAsync();
        }
        Assert.NotEmpty(companies);

        var refusedUser = "sentetik.kurucu.ret." + Guid.NewGuid().ToString("N")[..6];
        Assert.Equal(FounderBootstrapStatus.AuditRefused, (await RunAsync(factory, adminRoleCode, refusedUser, new RefusingAudit())).Status);
        await using (var check = factory.Services.CreateAsyncScope())
        {
            var identity = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.False(await identity.BootstrapStates.AnyAsync());
            Assert.False(await identity.LocalCredentials.AnyAsync(credential => credential.UserName == refusedUser));
            Assert.False(await identity.RoleAssignments.AnyAsync(assignment => identity.Roles.Any(role => role.Id == assignment.RoleId && role.Code == adminRoleCode)));
        }
        Assert.Equal(0, await ScalarAsync(factory.DatabaseConnectionString, "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [ActorId] = N'identity.bootstrap-founder'"));

        var userName = "sentetik.kurucu." + Guid.NewGuid().ToString("N")[..6];
        var result = await RunAsync(factory, adminRoleCode, userName, audit: null);
        Assert.Equal(FounderBootstrapStatus.Completed, result.Status);
        var founder = Assert.IsType<Guid>(result.FounderUserId);
        Assert.Equal(companies, result.CompanyIds.Order().ToList());

        await using (var read = new SqlConnection(factory.DatabaseConnectionString))
        {
            await read.OpenAsync();
            await using var command = new SqlCommand("""
                SELECT [CompanyId], [EntityId], [PermissionCode], [ActorType], [EventType], [Operation], [EntityVersionBefore], [EntityVersionAfter], [ApprovalContextJson], [AfterJson]
                FROM [audit].[AuditEvents] WHERE [ActorId] = N'identity.bootstrap-founder' ORDER BY [CompanyId]
                """, read);
            await using var reader = await command.ExecuteReaderAsync();
            var seen = new List<Guid>();
            while (await reader.ReadAsync())
            {
                seen.Add(reader.GetGuid(0));
                Assert.Equal(founder.ToString("D"), reader.GetString(1));
                Assert.True(reader.IsDBNull(2));
                Assert.Equal(("System", AuditEvent.FounderBootstrappedEvent, AuditEvent.FounderBootstrapOperation),
                    (reader.GetString(3), reader.GetString(4), reader.GetString(5)));
                Assert.True(reader.IsDBNull(6));
                Assert.Equal(1L, reader.GetInt64(7));
                Assert.Contains("SENTETIK-os", reader.GetString(8), StringComparison.Ordinal);
                Assert.Contains(founder.ToString("D"), reader.GetString(9), StringComparison.Ordinal);
                Assert.DoesNotContain(Password, reader.GetString(9), StringComparison.Ordinal);
            }
            Assert.Equal(companies, seen);
        }

        // The closed CHECK admits only the exact typed variant; the migration Down refuses while history exists.
        foreach (var change in new[] { "[ActorType] = N'User'", "[EntityVersionBefore] = 0", "[ApprovalContextJson] = N'{}'", "[Operation] = N'Other'" })
        {
            var invalidInsert = await Assert.ThrowsAsync<SqlException>(() => AuditSqlConstraintProbe.InsertInvalidCloneAsync(
                factory.DatabaseConnectionString, "[ActorId] = N'identity.bootstrap-founder'", change));
            Assert.Equal(547, invalidInsert.Number);
        }
        var downRefused = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory.DatabaseConnectionString, guard));
        Assert.Equal(52081, downRefused.Number);
        Assert.Equal(companies.Count, await ScalarAsync(factory.DatabaseConnectionString, "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [ActorId] = N'identity.bootstrap-founder'"));
    }

    private static async Task<FounderBootstrapResult> RunAsync(SqlServerRelationalApiFactory factory, string adminRoleCode, string userName,
        IFounderBootstrapAuditParticipant? audit)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<ICorrelationContext>().Set("SENTETIK-bootstrap-" + Guid.NewGuid().ToString("N"), null, "System", null, null);
        var service = new FounderBootstrapService(
            provider.GetRequiredService<IdentityDbContext>(),
            provider.GetRequiredService<ILocalPasswordHasher>(),
            provider.GetRequiredService<ISecurityEventWriterV1>(),
            provider.GetRequiredService<ICorrelationContext>(),
            TimeProvider.System,
            Options.Create(SyntheticLocalAccountOptions()),
            Options.Create(new IdentityBootstrapOptions { AdminRoleCode = adminRoleCode }),
            audit ?? provider.GetRequiredService<IFounderBootstrapAuditParticipant>());
        return await service.RunAsync(new Console(userName), CancellationToken.None);
    }

    private static LocalAccountOptions SyntheticLocalAccountOptions() => new()
    {
        Lockout = new LocalAccountLockoutSettings { MaxFailedAttempts = 5, Duration = TimeSpan.FromMinutes(15) },
        Session = new LocalAccountSessionSettings { IdleTimeout = TimeSpan.FromHours(1), AbsoluteLifetime = TimeSpan.FromHours(2) },
        PasswordHashing = new LocalAccountPasswordHashingSettings
        {
            Algorithm = LocalAccountPasswordHashingSettings.Pbkdf2Algorithm,
            Pbkdf2Iterations = LocalAccountOptions.MinimumPbkdf2Iterations,
        },
        RateLimit = new LocalAccountRateLimitSettings
        {
            Login = new LocalAccountLoginRateLimitSettings
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                PartitionBy = LocalAccountLoginRateLimitPartition.IpAddressAndUserName,
            },
        },
        PasswordPolicy = new LocalAccountPasswordPolicySettings
        {
            MinimumLength = 8,
            MaximumLength = 128,
            RequireCharacterClasses = false,
            BlocklistEnabled = true,
        },
    };

    private static async Task<int> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString); await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString); await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection); await command.ExecuteNonQueryAsync();
    }

    private sealed class RefusingAudit : IFounderBootstrapAuditParticipant
    {
        public Task AppendAsync(IReadOnlyList<FounderBootstrapCompanyAuditV1> records, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("SENTETIK audit owner refusal.");
    }

    private sealed class Console(string userName) : IFounderBootstrapConsole
    {
        public bool IsInteractive => true;
        public string? ReadUserName() => userName;
        public string? ReadDisplayName() => "Sentetik Kurucu";
        public string? ReadNewPassword() => Password;
        public string? ReadNewPasswordConfirmation() => Password;
        public GoymenERP.Modules.Identity.Application.Contracts.FounderRecoveryOperatorDeclaration? ReadOperatorDeclaration() => new("SENTETIK-os", "SENTETIK-machine");
    }
}

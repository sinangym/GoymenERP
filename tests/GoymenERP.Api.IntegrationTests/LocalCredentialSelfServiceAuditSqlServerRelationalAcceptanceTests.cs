using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class LocalCredentialSelfServiceAuditSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Self_service_audit_joins_identity_rollback_and_commit_and_retains_company_isolation_on_real_sql()
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        await using var factory = new SqlServerRelationalApiFactory(connection);
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);
        using var health = await client.GetAsync("/health/live"); health.EnsureSuccessStatusCode();
        var guard = Assert.IsType<SqlOperation>(new AddLocalCredentialSelfServiceAudit().DownOperations[0]).Sql;
        await ExecuteAsync(factory.DatabaseConnectionString, guard);
        var user = Guid.NewGuid();
        var record = new LocalCredentialSelfServiceAuditV1(TestCompanyIds.CompanyA, user, user,
            LocalCredentialSelfServiceOperationV1.EnableTwoFactor, 0, 1, "TOTP", DateTimeOffset.UtcNow,
            "SENTETIK kişisel güvenlik", "SENTETIK-correlation", null, "SENTETIK-command");
        foreach (var commit in new[] { false, true })
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await using var transaction = await identity.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await scope.ServiceProvider.GetRequiredService<ILocalCredentialSelfServiceAuditWriterV1>().AppendAsync(transaction.GetDbTransaction(), record);
            if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
            Assert.Equal(commit ? 1 : 0, await CountAsync(factory, TestCompanyIds.CompanyA, user));
        }
        Assert.Equal(0, await CountAsync(factory, TestCompanyIds.CompanyB, user));
        Assert.Equal(51970, (await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory.DatabaseConnectionString, guard))).Number);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var owner = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            var audit = Assert.Single(await owner.AuditEvents.AsNoTracking().Where(row => row.ActorId == user.ToString("D")).ToArrayAsync());
            Assert.Null(audit.PermissionCode); Assert.Equal(audit.ActorId, audit.EntityId);
            Assert.Equal(audit.IntegrityHash, AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope()));
        }
        foreach (var change in new[] { "[ActorType] = N'System'", "[EntityId] = N'00000000-0000-0000-0000-000000000001'",
            "[ApprovalContextJson] = N'{}'", "[Operation] = N'ResetPassword'", "[EntityVersionAfter] = 5", "[PermissionCode] = N''" })
        {
            Assert.Equal(51000, (await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory.DatabaseConnectionString,
                "UPDATE [audit].[AuditEvents] SET " + change + " WHERE [EntityType] = N'Identity.LocalCredential' AND [PermissionCode] IS NULL"))).Number);
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => AuditSqlConstraintProbe.InsertInvalidCloneAsync(
                factory.DatabaseConnectionString, "[EntityType] = N'Identity.LocalCredential' AND [PermissionCode] IS NULL", change))).Number);
        }
        Assert.Equal(1, await CountAsync(factory, TestCompanyIds.CompanyA, user));
    }

    [SqlServerFact]
    public async Task Self_service_audit_refuses_a_nonserializable_transaction_and_a_different_target_on_real_sql()
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        await using var factory = new SqlServerRelationalApiFactory(connection);
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);
        using var health = await client.GetAsync("/health/live"); health.EnsureSuccessStatusCode();
        var user = Guid.NewGuid();
        var record = new LocalCredentialSelfServiceAuditV1(TestCompanyIds.CompanyA, user, user,
            LocalCredentialSelfServiceOperationV1.DisableTwoFactor, 1, 2, "TOTP", DateTimeOffset.UtcNow,
            "SENTETIK kişisel güvenlik", "SENTETIK-correlation", null, "SENTETIK-key");
        foreach (var isolation in new[] { IsolationLevel.ReadCommitted, IsolationLevel.Serializable })
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var owner = scope.ServiceProvider.GetRequiredService<ILocalCredentialSelfServiceAuditWriterV1>();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await using var transaction = await identity.Database.BeginTransactionAsync(isolation);
            if (isolation == IsolationLevel.ReadCommitted)
                await Assert.ThrowsAsync<InvalidOperationException>(() => owner.AppendAsync(transaction.GetDbTransaction(), record));
            else await Assert.ThrowsAsync<ArgumentException>(() => owner.AppendAsync(transaction.GetDbTransaction(), record with { TargetUserId = Guid.NewGuid() }));
            await transaction.RollbackAsync();
        }
        Assert.Equal(0, await CountAsync(factory, TestCompanyIds.CompanyA, user));
    }
    private static async Task<int> CountAsync(SqlServerRelationalApiFactory factory, Guid company, Guid actor)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        return await scope.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents.AsNoTracking().CountAsync(row => row.ActorId == actor.ToString("D"));
    }
    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString); await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection); await command.ExecuteNonQueryAsync();
    }
}

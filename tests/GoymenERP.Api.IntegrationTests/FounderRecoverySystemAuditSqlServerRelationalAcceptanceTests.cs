using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class FounderRecoverySystemAuditSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Typed_founder_audit_joins_identity_rollback_and_persists_company_scoped_canonical_operator_evidence_on_real_sql()
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        await using var factory = new SqlServerRelationalApiFactory(connection);
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);
        using var health = await client.GetAsync("/health/live"); health.EnsureSuccessStatusCode();
        var guard = Assert.IsType<SqlOperation>(new AddTypedFounderRecoverySystemAudit().DownOperations[0]).Sql;
        await ExecuteAsync(factory.DatabaseConnectionString, guard);
        var founder = Guid.NewGuid();
        var records = new[] { TestCompanyIds.CompanyA, TestCompanyIds.CompanyB }.Select(company => new FounderRecoveryCompanyAuditV1(
            company, founder, FounderRecoverySystemAuditOperationV1.Reactivate, new("SENTETIK-os", "SENTETIK-machine"),
            [TestCompanyIds.CompanyA], [Guid.NewGuid()], 1, 2, new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero),
            "SENTETIK-recovery-correlation", null, "SENTETIK-recovery-run")).ToArray();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await using var transaction = await identity.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await scope.ServiceProvider.GetRequiredService<IFounderRecoverySystemAuditV1>().AppendAsync(transaction.GetDbTransaction(), records);
            await transaction.RollbackAsync();
        }
        Assert.Equal(0, await CountAsync(factory.DatabaseConnectionString));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await using var transaction = await identity.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await scope.ServiceProvider.GetRequiredService<IFounderRecoverySystemAuditV1>().AppendAsync(transaction.GetDbTransaction(), records);
            await transaction.CommitAsync();
        }
        var downRefused = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory.DatabaseConnectionString, guard));
        Assert.Equal(51952, downRefused.Number);
        foreach (var company in new[] { TestCompanyIds.CompanyA, TestCompanyIds.CompanyB })
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
            var owner = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            var audit = Assert.Single(await owner.AuditEvents.AsNoTracking().Where(row => row.ActorId == AuditEvent.FounderRecoveryActor).ToArrayAsync());
            Assert.Equal(company, audit.CompanyId); Assert.Null(audit.PermissionCode);
            Assert.Contains("SENTETIK-os", audit.ApprovalContextJson!);
            Assert.Equal(audit.IntegrityHash, AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope()));
        }
        foreach (var change in new[] { "[ActorType] = N'User'", "[ActorType] = N'Ai'", "[ApprovalContextJson] = N'{}'", "[PermissionCode] = N''", "[Operation] = N'Other'" })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory.DatabaseConnectionString,
                "UPDATE [audit].[AuditEvents] SET " + change + " WHERE [ActorId] = N'identity.recover-founder'"));
            Assert.Equal(547, refused.Number);
        }
        Assert.Equal(2, await CountAsync(factory.DatabaseConnectionString));
    }
    [SqlServerFact]
    public async Task A_system_audit_writer_requires_the_real_open_serializable_owner_transaction_on_real_sql()
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        await using var factory = new SqlServerRelationalApiFactory(connection);
        using var client = factory.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);
        using var health = await client.GetAsync("/health/live"); health.EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await using var transaction = await identity.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var record = new FounderRecoveryCompanyAuditV1(TestCompanyIds.CompanyA, Guid.NewGuid(), FounderRecoverySystemAuditOperationV1.ResetPassword,
            new("SENTETIK-os", "SENTETIK-machine"), [], [], 1, 1, DateTimeOffset.UtcNow, "SENTETIK-correlation", null, "SENTETIK-key");
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IFounderRecoverySystemAuditV1>().AppendAsync(transaction.GetDbTransaction(), [record]));
        await transaction.RollbackAsync();
        Assert.Equal(0, await CountAsync(factory.DatabaseConnectionString));
    }
    private static async Task<int> CountAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString); await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [ActorId] = N'identity.recover-founder'", connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }
    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString); await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection); await command.ExecuteNonQueryAsync();
    }
}

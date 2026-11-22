using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

// WP-A4 real-SQL evidence for the AI knowledge approvals (schema aiknowledge). The user, authority, package catalog, audit
// collector and correlation are SENTETIK test doubles: this is storage, constraint, atomicity, race and forward-fix evidence.
public sealed class AiKnowledgeSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Approver = Guid.Parse("b4000000-0000-4000-8000-0000000000a4");
    private static readonly string PackageSha = new('a', 64);

    [SqlServerFact]
    public async Task The_initial_migrations_Down_allows_an_empty_store_and_refuses_with_52290_while_an_approval_exists()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var guard = Assert.IsType<SqlOperation>(new InitialAiKnowledgeApprovals().DownOperations[0]).Sql;
            await ExecAsync(connectionString, guard);
            await Service(connectionString).ApproveAsync(new("decision-index", PackageSha), new("down-1", "corr-down", null));
            var refusal = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, guard));
            Assert.Equal(52290, refusal.Number);
            Assert.Equal(1, await CountAsync(connectionString, "PackageApprovals", TestCompanyIds.CompanyA));
            Assert.Equal(1, await CountAsync(connectionString, "CommandAttempts", TestCompanyIds.CompanyA));
        });
    }

    [SqlServerFact]
    public async Task An_approval_and_its_attempt_are_one_save_and_racing_approvals_of_one_package_leave_one_active_approval()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var sameKey = await Task.WhenAll(
                Service(connectionString).ApproveAsync(new("decision-index", PackageSha), new("race-1", "corr-a", null)),
                Service(connectionString).ApproveAsync(new("decision-index", PackageSha), new("race-1", "corr-b", null)));
            Assert.Single(sameKey.Select(answer => answer.Approval.Id).Distinct());
            Assert.Equal(1, await CountAsync(connectionString, "PackageApprovals", TestCompanyIds.CompanyA));
            Assert.Equal(1, await CountAsync(connectionString, "CommandAttempts", TestCompanyIds.CompanyA));

            var otherKeys = await Task.WhenAll(
                Capture(Service(connectionString).ApproveAsync(new("release-knowledge", PackageSha), new("race-2", "corr-c", null))),
                Capture(Service(connectionString).ApproveAsync(new("release-knowledge", PackageSha), new("race-3", "corr-d", null))));
            Assert.Single(otherKeys, outcome => outcome.Response is not null);
            Assert.Single(otherKeys, outcome => outcome.Error is { Status: 409 } error
                && (error.Code == AiKnowledgeProblemCodesV1.AlreadyApproved || error.Code == AiKnowledgeProblemCodesV1.ConcurrentChange));
            Assert.Equal(1, int.Parse((await ScalarAsync(connectionString,
                $"SELECT COUNT(*) FROM [aiknowledge].[PackageApprovals] WHERE [CompanyId] = '{TestCompanyIds.CompanyA}' AND [SourceKind] = 2 AND [Status] = 1"))!));
            Assert.Equal(0, await CountAsync(connectionString, "PackageApprovals", TestCompanyIds.CompanyB));
            await using var other = Db(connectionString, TestCompanyIds.CompanyB);
            Assert.Empty(await other.Approvals.ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task The_checks_refuse_a_second_active_approval_a_bad_digest_and_a_withdrawal_without_a_reason_in_real_SQL()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var company = TestCompanyIds.CompanyA;
            await ExecAsync(connectionString, Insert(company, Guid.NewGuid(), 1, PackageSha, 1, 1, "NULL", "NULL", "NULL"));
            var duplicate = Sql(await Assert.ThrowsAnyAsync<Exception>(() =>
                ExecAsync(connectionString, Insert(company, Guid.NewGuid(), 1, PackageSha, 1, 1, "NULL", "NULL", "NULL"))));
            Assert.Contains(duplicate.Number, new[] { 2601, 2627 });
            var badDigest = Sql(await Assert.ThrowsAnyAsync<Exception>(() =>
                ExecAsync(connectionString, Insert(company, Guid.NewGuid(), 1, new string('A', 64), 1, 1, "NULL", "NULL", "NULL"))));
            Assert.Equal(547, badDigest.Number);
            var noReason = Sql(await Assert.ThrowsAnyAsync<Exception>(() =>
                ExecAsync(connectionString, Insert(company, Guid.NewGuid(), 2, PackageSha, 2, 2, $"'{Approver}'", "SYSUTCDATETIME()", "NULL"))));
            Assert.Equal(547, noReason.Number);
            var unknownKind = Sql(await Assert.ThrowsAnyAsync<Exception>(() =>
                ExecAsync(connectionString, Insert(company, Guid.NewGuid(), 3, PackageSha, 1, 1, "NULL", "NULL", "NULL"))));
            Assert.Equal(547, unknownKind.Number);
            // A withdrawn approval does not block a new active one of the same package.
            await ExecAsync(connectionString, Insert(company, Guid.NewGuid(), 2, PackageSha, 2, 2, $"'{Approver}'", "SYSUTCDATETIME()", "N'SENTETIK'"));
            await ExecAsync(connectionString, Insert(company, Guid.NewGuid(), 2, PackageSha, 1, 1, "NULL", "NULL", "NULL"));
            Assert.Equal(3, await CountAsync(connectionString, "PackageApprovals", company));
        });
    }

    private static string Insert(Guid company, Guid id, int kind, string sha, int status, long version, string withdrawnBy,
        string withdrawnAt, string reason) => $"""
        INSERT INTO [aiknowledge].[PackageApprovals] ([CompanyId],[Id],[SourceKind],[PackageSha256],[Status],[Version],[ApprovedByUserId],
            [ApprovedAtUtc],[WithdrawnByUserId],[WithdrawnAtUtc],[WithdrawReason],[CorrelationId],[CausationId])
        VALUES ('{company}','{id}',{kind},'{sha}',{status},{version},'{Approver}',DATEADD(minute,-1,SYSUTCDATETIME()),{withdrawnBy},{withdrawnAt},{reason},'c',NULL);
        """;

    private static async Task<(AiKnowledgeApprovalResponseV1? Response, AiKnowledgeException? Error)> Capture(
        Task<AiKnowledgeApprovalResponseV1> call)
    {
        try { return (await call, null); }
        catch (AiKnowledgeException error) { return (null, error); }
    }

    private static AiKnowledgeApprovalServiceV1 Service(string connectionString)
    {
        var company = new CompanyContext();
        company.Set(TestCompanyIds.CompanyA);
        return new AiKnowledgeApprovalServiceV1(Db(connectionString, TestCompanyIds.CompanyA), company, new SqlUser(), new SqlAuthority(),
            new SqlCatalog(), new SqlAudit(), new SqlCorrelation(), TimeProvider.System);
    }

    private static AiKnowledgeDbContext Db(string connectionString, Guid companyId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        return new AiKnowledgeDbContext(new DbContextOptionsBuilder<AiKnowledgeDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AiKnowledgeDbContext.Schema)).Options, company);
    }

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_AiKnowledge_{Guid.NewGuid():N}";
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

    private static SqlException Sql(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException!)
        {
            if (e is SqlException sql) return sql;
            if (e.InnerException is null) break;
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
        return Convert.ToString(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountAsync(string connectionString, string table, Guid companyId) =>
        int.Parse((await ScalarAsync(connectionString, $"SELECT COUNT(*) FROM [aiknowledge].[{table}] WHERE [CompanyId] = '{companyId}'"))!);

    private sealed class SqlUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => Approver.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([TestCompanyIds.CompanyA]);
        public bool CanAccessCompany(Guid companyId) => companyId == TestCompanyIds.CompanyA;
    }

    private sealed class SqlAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == ErpPermissions.AiKnowledgeApprove && companyId == TestCompanyIds.CompanyA
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK_ROLE"]), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class SqlCatalog : IAiKnowledgePackageCatalogV1
    {
        public AiKnowledgePackageInfoV1? Find(AiKnowledgeSourceKindV1 kind) => new(kind, PackageSha, new string('1', 64), 1, 0, 0);
    }

    private sealed class SqlAudit : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent) { }
    }

    private sealed class SqlCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "corr-sql";
        public string? CausationId => null;
        public string SourceChannel => "api";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
    }
}

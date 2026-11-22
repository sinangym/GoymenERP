using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.ApprovalRules;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>WP1 versioned approval rule table on real SQL Server. SENTETIK data; authority and sources are doubles.</summary>
public sealed partial class WorkOutboxDispatchSqlServerRelationalAcceptanceTests
{
    private static readonly Guid RuleManager = Guid.Parse("b2000000-0000-4000-8000-000000000001");
    private const string RuleKind = "finance.manual-bank-payment.v1";

    [SqlServerFact]
    public async Task Approval_rule_Down_allows_an_empty_store_refuses_with_52100_while_a_version_exists_and_a_same_version_race_keeps_one()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var guard = Assert.IsType<SqlOperation>(new AddWorkApprovalRuleVersions().DownOperations[0]).Sql;
            await ExecAsync(connectionString, guard);

            // Two changes from the same version: the unique (company, kind, version) index lets exactly one land.
            var first = RuleService(connectionString).RecordAsync(RuleKind, new("0", "Hours", null, "SENTETIK saat"), new("rule-a", "corr-a", null));
            var second = RuleService(connectionString).RecordAsync(RuleKind, new("0", "Hours", ErpPermissions.SettingsWorkApprovalDurationManage,
                "SENTETIK saat ve yükseltme"), new("rule-b", "corr-b", null));
            var outcomes = await Task.WhenAll(Settle(first), Settle(second));
            Assert.Single(outcomes, outcome => outcome is null);
            Assert.Single(outcomes, outcome => outcome is (409, WorkApprovalRuleProblemCodesV1.ConcurrentChange)
                or (409, WorkApprovalRuleProblemCodesV1.VersionConflict));
            Assert.Equal(1, await CountAsync(connectionString, "ApprovalRuleVersions", TestCompanyIds.CompanyA));

            // The check refuses an unknown class and version 0 (the default is never stored).
            var badClass = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, $"""
                INSERT INTO [work].[ApprovalRuleVersions] ([CompanyId],[Id],[Kind],[Version],[DurationClass],[ApproverPermission],[EscalationPermission],
                    [RecordedByUserId],[RecordedAtUtc],[Reason],[CorrelationId],[CausationId])
                VALUES ('{TestCompanyIds.CompanyA}', NEWID(), '{RuleKind}', 9, 3, 'finance.manual-bank-payment.approve', NULL,
                    '{RuleManager}', SYSDATETIMEOFFSET(), N'SENTETIK', N'corr-raw', NULL)
                """));
            Assert.Equal(547, badClass.Number);
            var zeroVersion = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, $"""
                INSERT INTO [work].[ApprovalRuleVersions] ([CompanyId],[Id],[Kind],[Version],[DurationClass],[ApproverPermission],[EscalationPermission],
                    [RecordedByUserId],[RecordedAtUtc],[Reason],[CorrelationId],[CausationId])
                VALUES ('{TestCompanyIds.CompanyA}', NEWID(), '{RuleKind}', 0, 1, 'finance.manual-bank-payment.approve', NULL,
                    '{RuleManager}', SYSDATETIMEOFFSET(), N'SENTETIK', N'corr-raw', NULL)
                """));
            Assert.Equal(547, zeroVersion.Number);

            // CompanyId: the other company still reads the decided default; this company reads its version 1.
            var sources = new IWorkInboxSourceV1[] { new RuleSource() };
            await using var own = Db(connectionString, TestCompanyIds.CompanyA);
            await using var other = Db(connectionString, TestCompanyIds.CompanyB);
            var ownRule = (await new WorkApprovalRuleReaderV1(own).ResolveAsync(TestCompanyIds.CompanyA, sources, default))![RuleKind];
            var otherRule = (await new WorkApprovalRuleReaderV1(other).ResolveAsync(TestCompanyIds.CompanyB, sources, default))![RuleKind];
            Assert.Equal((WorkApprovalDurationClassV1.Hours, 1L, false), (ownRule.DurationClass, ownRule.Version, ownRule.IsDefault));
            Assert.Equal((WorkApprovalDurationClassV1.BusinessDays, 0L, true), (otherRule.DurationClass, otherRule.Version, otherRule.IsDefault));

            var refusal = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, guard));
            Assert.Equal(52100, refusal.Number);
            Assert.Equal(1, await CountAsync(connectionString, "ApprovalRuleVersions", TestCompanyIds.CompanyA));
        });
    }

    private static async Task<(int Status, string Code)?> Settle(Task<WorkApprovalRuleCommandResultV1> command)
    {
        try
        {
            await command;
            return null;
        }
        catch (WorkApprovalRuleException exception)
        {
            return (exception.Status, exception.Code);
        }
    }

    private static WorkApprovalRuleServiceV1 RuleService(string connectionString)
    {
        var company = new CompanyContext();
        company.Set(TestCompanyIds.CompanyA);
        var database = new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", WorkDbContext.Schema)).Options, company);
        return new WorkApprovalRuleServiceV1(database, company, new RuleUser(), new RuleAuthority(), new RuleAudit(), new RuleCorrelation(),
            TimeProvider.System, [new RuleSource()]);
    }

    private sealed class RuleSource : IWorkInboxSourceV1
    {
        public string Module => "finance";
        public string Kind => RuleKind;
        public string ApproverPermission => "finance.manual-bank-payment.approve";

        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount,
            CancellationToken cancellationToken) => throw new InvalidOperationException("The rule table never reads records.");
    }

    private sealed class RuleUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => RuleManager.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([TestCompanyIds.CompanyA]);
        public bool CanAccessCompany(Guid companyId) => companyId == TestCompanyIds.CompanyA;
    }

    private sealed class RuleAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == ErpPermissions.SettingsWorkApprovalDurationManage && companyId == TestCompanyIds.CompanyA
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK_ROLE"]), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class RuleAudit : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent) { }
    }

    private sealed class RuleCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "corr-rule-sql";
        public string? CausationId => null;
        public string SourceChannel => "api";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
    }
}

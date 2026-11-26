using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.ApprovalFollowUp;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Actual SQL/Audit owner and shared database fixture; source, users and authority are explicitly synthetic.</summary>
public sealed partial class WorkOutboxDispatchSqlServerRelationalAcceptanceTests
{
    private static readonly Guid DelegationSender = Guid.Parse("d3110000-0000-4000-8000-000000000001");
    private static readonly Guid DelegationReceiver = Guid.Parse("d3110000-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset DelegationAt = new(2026, 11, 26, 18, 0, 0, TimeSpan.Zero);
    private const string DelegationKind = "sales.order.v1";
    private static CreateWorkDelegationV1 DelegationInput() => new(DelegationKind, DelegationReceiver,
        DelegationAt.AddDays(1), "SENTETIK absence");

    [SqlServerFact]
    public async Task Delegation_persona_facts_use_the_actual_Identity_SQL_query_and_keep_the_managers_scope_separate()
    {
        await WithDatabaseAsync(async (connection, _) =>
        {
            var company = new CompanyContext(); company.Set(TestCompanyIds.CompanyA);
            await using var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connection).Options, company);
            var person = Guid.NewGuid(); var site = Guid.NewGuid();
            db.Users.Add(new UserAccount(person, "SENTETIK delegation persona", null));
            void Grant(string code, string permission, AccessScopeType type, Guid scope)
            {
                var role = new RoleDefinition(Guid.NewGuid(), code, "SENTETIK delegation fixture", false); role.AddPermission(permission);
                var assignment = new RoleAssignment(Guid.NewGuid(), person, role.Id, TestCompanyIds.CompanyA,
                    DelegationAt.AddDays(-1), null, null);
                assignment.AddScope(type, scope);
                db.Roles.Add(role); db.RoleAssignments.Add(assignment);
            }
            // Only disposable fixture data; no production role, seed or real person is changed.
            Grant(ErpPersonaRoleCodes.GeneralManager, ErpPermissions.OrganizationContextRead, AccessScopeType.Company, TestCompanyIds.CompanyA);
            Grant(ErpPersonaRoleCodes.SystemAdministrator, ErpPermissions.IdentityAccessApprove, AccessScopeType.Company, TestCompanyIds.CompanyA);
            Grant(ErpPersonaRoleCodes.OperationsManager, ErpPermissions.IdentityAccessApprove, AccessScopeType.Site, site);
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var port = new PermissionHolderListPortV1(db, company, new DelegationClock());
            var answer = await port.ListAsync(new(TestCompanyIds.CompanyA, ErpPermissions.IdentityAccessApprove));
            Assert.Equal(UserDirectoryOutcomeV1.Found, answer.Outcome);
            var holder = Assert.Single(answer.Holders, item => item.UserId == person);
            Assert.True(holder.IsGeneralManager);
            Assert.True(holder.IsCompanyWide);
            Assert.False(holder.ModuleManagerScope!.IsCompanyWide);
            Assert.Equal(new[] { site }, holder.ModuleManagerScope.SiteIds);
            Assert.Equal(UserDirectoryOutcomeV1.Denied,
                (await port.ListAsync(new(TestCompanyIds.CompanyB, ErpPermissions.IdentityAccessApprove))).Outcome);
        });
    }

    [SqlServerFact]
    public async Task Racing_overlapping_delegations_leave_one_record_one_attempt_and_one_owned_audit_then_restart_replays_it()
    {
        await WithDatabaseAsync(async (connection, factory) =>
        {
            await using var left = DelegationScope(factory, connection);
            await using var right = DelegationScope(factory, connection);
            async Task<WorkDelegationCommandResultV1?> Try(WorkDelegationServiceV1 service, string key)
            {
                try { return await service.CreateAsync(DelegationInput(), new(key, key, null)); }
                catch (WorkDelegationException error)
                {
                    Assert.Equal(409, error.Status);
                    Assert.Contains(error.Code, new[] { WorkDelegationCodesV1.Chain, WorkDelegationCodesV1.Concurrent });
                    return null;
                }
            }
            var answers = await Task.WhenAll(Try(left.Service, "delegate-left"), Try(right.Service, "delegate-right"));
            var winner = Assert.Single(answers, answer => answer is not null)!;
            Assert.Equal(1, await CountAsync(connection, "ApprovalDelegations", TestCompanyIds.CompanyA));
            Assert.Equal("1", await ScalarAsync(connection, "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = 'Work.ApprovalDelegation'"));
            // The losing overlap may keep a terminal refusal, but exactly one successful receipt exists.
            Assert.Equal("1", await ScalarAsync(connection, "SELECT COUNT(*) FROM [work].[CollaborationAttempts] WHERE [Command] = 'work.delegation.create.v1' AND [Status] = 201"));
            await using var restarted = DelegationScope(factory, connection);
            var replay = await restarted.Service.CreateAsync(DelegationInput(), new(winner.OriginalCorrelationId, "retry", null));
            Assert.True(replay.Replayed);
            Assert.Equal(winner.Delegation.Id, replay.Delegation.Id);
            Assert.Equal(winner.OriginalCorrelationId, replay.OriginalCorrelationId);
            await using var other = Db(connection, TestCompanyIds.CompanyB);
            Assert.Empty(await other.Delegations.ToArrayAsync());
        });
    }

    [SqlServerFact]
    public async Task Late_delegation_authority_loss_rolls_back_the_record_receipt_and_the_real_Audit_owner_batch()
    {
        await WithDatabaseAsync(async (connection, factory) =>
        {
            // First authorization succeeds, pre-save revalidation succeeds, post-save revalidation loses authority.
            await using var scope = DelegationScope(factory, connection, revokeAtCall: 3);
            Assert.Equal(403, (await Assert.ThrowsAsync<WorkDelegationException>(() =>
                scope.Service.CreateAsync(DelegationInput(), new("late", "late", null)))).Status);
            Assert.Equal(0, await CountAsync(connection, "ApprovalDelegations", TestCompanyIds.CompanyA));
            Assert.Equal(0, await CountAsync(connection, "CollaborationAttempts", TestCompanyIds.CompanyA));
            Assert.Equal("0", await ScalarAsync(connection, "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = 'Work.ApprovalDelegation'"));
            Assert.False(scope.Audit.HasPendingDrafts);
        });
    }

    [SqlServerFact]
    public async Task Delegation_SQL_checks_refuse_self_and_invalid_periods_and_Down_refuses_52120_without_losing_history()
    {
        await WithDatabaseAsync(async (connection, factory) =>
        {
            var guard = Assert.IsType<SqlOperation>(new AddWorkApprovalDelegations().DownOperations[0]).Sql;
            await ExecAsync(connection, guard);
            await using var scope = DelegationScope(factory, connection);
            var created = await scope.Service.CreateAsync(DelegationInput(), new("one", "one", null));
            foreach (var change in new[] { "[DelegateUserId] = [DelegatorUserId]", "[EndsAtUtc] = [StartsAtUtc]",
                         "[Version] = 2, [RevokedAtUtc] = SYSUTCDATETIME(), [RevokedByUserId] = [RecordedByUserId], [RevocationReason] = NULL" })
            {
                var failure = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connection,
                    $"UPDATE [work].[ApprovalDelegations] SET {change} WHERE [Id] = '{created.Delegation.Id}'"));
                Assert.Equal(547, failure.Number);
            }
            var revoked = await scope.Service.RevokeAsync(created.Delegation.Id, new("1", "SENTETIK return"), new("revoke", "revoke", null));
            Assert.Equal("2", revoked.Delegation.Version);
            Assert.Equal(52120, (await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connection, guard))).Number);
            Assert.Equal(1, await CountAsync(connection, "ApprovalDelegations", TestCompanyIds.CompanyA));
            Assert.Equal("2", await ScalarAsync(connection, "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = 'Work.ApprovalDelegation'"));
        });
    }

    private static DelegationSqlScope DelegationScope(SqlServerRelationalApiFactory factory, string connection, int revokeAtCall = int.MaxValue)
    {
        var scope = factory.Services.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(TestCompanyIds.CompanyA);
        var database = new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>().UseSqlServer(connection,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", WorkDbContext.Schema)).Options, company);
        var audit = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var service = new WorkDelegationServiceV1(database, company, new DelegationUser(), new DelegationAuthority(revokeAtCall),
            new DelegationPeople(), [new DelegationSource()], scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), audit,
            new RuleCorrelation(), Options.Create(new WorkDelegationOptionsV1 { Enabled = true }), new DelegationClock());
        return new(scope, database, audit, service);
    }
    private sealed class DelegationSqlScope(AsyncServiceScope scope, WorkDbContext database, IAuditTransactionWriterV1 audit,
        WorkDelegationServiceV1 service) : IAsyncDisposable
    {
        public WorkDelegationServiceV1 Service => service;
        public IAuditTransactionWriterV1 Audit => audit;
        public async ValueTask DisposeAsync() { await database.DisposeAsync(); await scope.DisposeAsync(); }
    }
    private sealed class DelegationSource : IWorkInboxSourceV1
    {
        public string Module => "sales"; public string Kind => DelegationKind;
        public string ApproverPermission => ErpPermissions.SalesOrdersApprove;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Configuration reads no owner record.");
    }
    private sealed class DelegationPeople : IWorkPermissionHolderDirectoryV1
    {
        public Task<WorkPermissionHoldersV1> ListHoldersAsync(Guid companyId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkPermissionHoldersV1(companyId == TestCompanyIds.CompanyA && permission == ErpPermissions.SalesOrdersApprove,
                new[] { DelegationSender, DelegationReceiver }.Select(id => new WorkPermissionHolderV1(id, true,
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())).ToArray()));
    }
    private sealed class DelegationAuthority(int revokeAtCall) : IErpAuthorityRevalidationService
    {
        private int calls;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(++calls < revokeAtCall && companyId == TestCompanyIds.CompanyA && permission == ErpPermissions.SalesOrdersApprove
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK_ROLE"]), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }
    private sealed class DelegationUser : ICurrentUser
    {
        public bool IsAuthenticated => true; public string? UserId => DelegationSender.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([TestCompanyIds.CompanyA]);
        public bool CanAccessCompany(Guid companyId) => companyId == TestCompanyIds.CompanyA;
    }
    private sealed class DelegationClock : TimeProvider { public override DateTimeOffset GetUtcNow() => DelegationAt; }
}

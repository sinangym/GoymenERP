using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.AssistantMemory;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

// WP-C6 (AI-D30) real-SQL evidence for the assistant memory tables. Directory and authority are SENTETIK test doubles: this
// is storage, constraint, isolation and forward-fix evidence, not the production owner composition.
public sealed partial class WorkOutboxDispatchSqlServerRelationalAcceptanceTests
{
    private static readonly Guid MemoryOwner = Guid.Parse("b2000000-0000-4000-8000-0000000000c1");
    private static readonly Guid MemoryColleague = Guid.Parse("b2000000-0000-4000-8000-0000000000c2");
    private static readonly Guid MemoryOverseer = Guid.Parse("b2000000-0000-4000-8000-0000000000c3");

    [SqlServerFact]
    public async Task AssistantMemory_Down_allows_an_empty_store_and_refuses_with_52210_while_an_entry_exists()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var guard = Assert.IsType<SqlOperation>(new AddWorkAssistantMemory().DownOperations[0]).Sql;
            await ExecAsync(connectionString, guard);
            await MemoryService(connectionString, MemoryOwner, ErpPermissions.AiPanelUse)
                .CreateEntryAsync(new("preference", "SENTETIK tercih"), new("memory-down-1", "corr-memory-down", null));
            var refusal = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, guard));
            Assert.Equal(52210, refusal.Number);
            Assert.Equal(1, await CountAsync(connectionString, "AssistantMemoryEntries", TestCompanyIds.CompanyA));
            Assert.Equal(1, await CountAsync(connectionString, "CollaborationAttempts", TestCompanyIds.CompanyA));
        });
    }

    [SqlServerFact]
    public async Task AssistantMemory_entries_stay_with_their_owner_and_company_and_each_overseer_view_is_one_saved_row_in_real_SQL()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var created = await MemoryService(connectionString, MemoryOwner, ErpPermissions.AiPanelUse)
                .CreateEntryAsync(new("note", "SENTETIK hatirlatma"), new("memory-own-1", "corr-memory-own", null));

            Assert.Empty((await MemoryService(connectionString, MemoryColleague).ListMyEntriesAsync(true)).Items);
            Assert.Empty((await MemoryService(connectionString, MemoryColleague).ReadForCurrentActorAsync()).Items);
            Assert.Equal(404, (await Assert.ThrowsAsync<WorkCollaborationException>(() => MemoryService(connectionString, MemoryColleague)
                .DeleteEntryAsync(created.Entry.Id, new("1"), new("memory-own-2", "corr-memory-own-2", null)))).Status);
            await using (var other = Db(connectionString, TestCompanyIds.CompanyB))
            {
                Assert.Empty(await other.AssistantMemoryEntries.ToListAsync());
            }

            var view = await MemoryService(connectionString, MemoryOverseer, WorkPermissionManifestV1.AssistantMemoryOversee)
                .ViewUserMemoryAsync(MemoryOwner);
            Assert.Equal(new[] { created.Entry.Id }, view.Items.Select(item => item.Id));
            Assert.Equal(1, await CountAsync(connectionString, "AssistantMemoryOversightViews", TestCompanyIds.CompanyA));
            Assert.Equal(0, await CountAsync(connectionString, "InboxNotifications", TestCompanyIds.CompanyA));

            // A deletion recorded by anyone but the owner, and a closure without a reason, are refused by the table itself.
            var forged = Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString,
                $"UPDATE [work].[AssistantMemoryEntries] SET [State] = 2, [Version] = 2, [EndedAtUtc] = SYSUTCDATETIME(), "
                + $"[EndedByUserId] = '{MemoryColleague}' WHERE [Id] = '{created.Entry.Id}';")));
            Assert.Equal(547, forged.Number);
            var reasonless = Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString,
                $"UPDATE [work].[AssistantMemoryEntries] SET [State] = 3, [Version] = 2, [EndedAtUtc] = SYSUTCDATETIME(), "
                + $"[EndedByUserId] = '{MemoryOverseer}' WHERE [Id] = '{created.Entry.Id}';")));
            Assert.Equal(547, reasonless.Number);
        });
    }

    private static WorkAssistantMemoryServiceV1 MemoryService(string connectionString, Guid actor, string? permission = null)
    {
        var company = new CompanyContext();
        company.Set(TestCompanyIds.CompanyA);
        var database = new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", WorkDbContext.Schema)).Options, company);
        return new WorkAssistantMemoryServiceV1(database, company, new MemoryUser(actor), new MemoryAuthority(permission), new MemoryPeople(),
            new CollaborationAudit(), new CollaborationCorrelation(), Options.Create(new WorkAssistantMemoryOptions
            {
                PilotEnabled = true, RetentionDays = 30, MaxEntriesPerUser = 5, InformationNoticeVersion = "SENTETIK-NOTICE-1",
            }), TimeProvider.System);
    }

    private sealed class MemoryUser(Guid actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([TestCompanyIds.CompanyA]);
        public bool CanAccessCompany(Guid companyId) => companyId == TestCompanyIds.CompanyA;
    }

    private sealed class MemoryAuthority(string? granted) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == granted && companyId == TestCompanyIds.CompanyA
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK_ROLE"]), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class MemoryPeople : IWorkPersonDirectoryV1
    {
        public Task<WorkPeopleAnswerV1> ResolveAsync(Guid companyId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkPeopleAnswerV1(WorkPersonOutcomeV1.Found, [], false));

        public Task<WorkPeopleAnswerV1> SearchAsync(Guid companyId, string? query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkPeopleAnswerV1(WorkPersonOutcomeV1.Found, [], false));

        public Task<WorkPersonOutcomeV1> IsActiveMemberAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(companyId == TestCompanyIds.CompanyA && (userId == MemoryOwner || userId == MemoryColleague || userId == MemoryOverseer)
                ? WorkPersonOutcomeV1.Found : WorkPersonOutcomeV1.Denied);
    }
}

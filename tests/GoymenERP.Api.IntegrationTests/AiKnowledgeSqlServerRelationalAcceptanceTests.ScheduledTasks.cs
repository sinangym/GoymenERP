using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using GoymenERP.Modules.AiKnowledge.Infrastructure.ScheduledTasks;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

// Source only. Requires root-generated ScheduledTasks migration before actual SQL acceptance.
// Real Ai context and real scoped Audit collector/writer; authority/Settings/morning ports are SENTETIK.
public sealed partial class AiKnowledgeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Scheduled_task_native_command_and_run_receipts_survive_restart_without_a_second_read_or_audit()
    {
        await WithDatabaseAsync(async connection =>
        {
            await using var provider = ScheduledServices(connection);
            var id = Guid.NewGuid(); var ports = new ScheduledPorts();
            await using (var owner = new ScheduledScope(provider, connection, ports))
            {
                Assert.Equal(201, (await owner.Service.CreateAsync(id, ScheduledDefinition, ScheduledMeta("create"))).Status);
                var first = await owner.Service.RunCurrentAsync(id, new(1, ScheduledAt, "settings-1"), ScheduledMeta("run"));
                Assert.Equal("read", first.Run!.Outcome); Assert.Equal(1, ports.Reads);
                Assert.Equal(2, await owner.Database.ScheduledTaskCommands.CountAsync());
                Assert.Single(await owner.Database.ScheduledTaskRuns.ToArrayAsync());
            }
            await using (var restarted = new ScheduledScope(provider, connection, ports))
            {
                var replay = await restarted.Service.RunCurrentAsync(id, new(1, ScheduledAt, "settings-1"), ScheduledMeta("run") with { CorrelationId = "new-correlation" });
                Assert.True(replay.IsReplay); Assert.Equal("corr-run", replay.OriginalCorrelationId); Assert.Equal(1, ports.Reads);
                Assert.Equal(2, await restarted.Database.ScheduledTaskCommands.CountAsync());
                Assert.Equal(2, await restarted.Database.ScheduledTaskEvents.CountAsync());
                Assert.Single(await restarted.Database.ScheduledTaskRuns.ToArrayAsync());
            }
            Assert.Equal("2", await ScalarAsync(connection, "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Ai.ScheduledTask'"));
        });
    }

    [SqlServerFact]
    public async Task Scheduled_task_actual_scoped_Audit_records_every_manager_view_and_close_without_owner_notification()
    {
        await WithDatabaseAsync(async connection =>
        {
            await using var provider = ScheduledServices(connection); var id = Guid.NewGuid(); var ports = new ScheduledPorts();
            await using var owner = new ScheduledScope(provider, connection, ports);
            await owner.Service.CreateAsync(id, ScheduledDefinition, ScheduledMeta("create"));
            var notificationsBefore = await ScalarAsync(connection, "SELECT COUNT(*) FROM work.InboxNotifications");
            owner.User.Actor = "SENTETIK-manager";
            await owner.Service.ViewForManagerAsync(id, "SENTETIK-owner", ScheduledMeta("view"));
            await owner.Service.ViewForManagerAsync(id, "SENTETIK-owner", ScheduledMeta("view"));
            var closed = await owner.Service.CloseForManagerAsync(id, "SENTETIK-owner", 1, ScheduledMeta("close"));
            Assert.Equal(AiScheduledTaskStateV1.Closed, closed.Task!.State);
            Assert.Equal(4, await owner.Database.ScheduledTaskEvents.CountAsync());
            Assert.Equal("2", await ScalarAsync(connection, "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'ai.scheduled-task.viewed.v1'"));
            Assert.Equal("1", await ScalarAsync(connection, "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'ai.scheduled-task.closed.v1'"));
            Assert.Equal(notificationsBefore, await ScalarAsync(connection, "SELECT COUNT(*) FROM work.InboxNotifications"));
            // No Work notification/outbox port is present in this service composition; the native API only writes content-free ai/Audit evidence.
        });
    }

    [SqlServerFact]
    public async Task Scheduled_task_actual_owner_and_Audit_saves_roll_back_when_the_next_authority_read_is_denied()
    {
        foreach (var phase in new[] { "task-save", "audit-save" })
        {
            await WithDatabaseAsync(async connection =>
            {
                var observed = new ScheduledSavePhase(phase);
                await using var provider = ScheduledServices(connection, observed);
                var ports = new ScheduledPorts { AfterAuthority = () => { if (observed.Fired) return false; return true; } };
                await using var owner = new ScheduledScope(provider, connection, ports, observed);
                var id = Guid.NewGuid();
                Assert.Equal(403, (await Assert.ThrowsAsync<AiScheduledTaskException>(() => owner.Service.CreateAsync(id, ScheduledDefinition, ScheduledMeta("create")))).Status);
                Assert.True(observed.Fired); Assert.Empty(owner.Database.ChangeTracker.Entries()); Assert.False(owner.Writer.HasPendingDrafts);
                await using var fresh = Db(connection, TestCompanyIds.CompanyA);
                Assert.Empty(await fresh.ScheduledTasks.ToArrayAsync()); Assert.Empty(await fresh.ScheduledTaskCommands.ToArrayAsync());
                Assert.Empty(await fresh.ScheduledTaskEvents.ToArrayAsync());
                Assert.Equal("0", await ScalarAsync(connection, "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Ai.ScheduledTask'"));
                observed.Enabled = false; ports.AfterAuthority = null;
                var retry = await owner.Service.CreateAsync(id, ScheduledDefinition, ScheduledMeta("create"));
                Assert.Equal(201, retry.Status); Assert.False(retry.IsReplay);
                Assert.Single(await owner.Database.ScheduledTasks.ToArrayAsync()); Assert.Single(await owner.Database.ScheduledTaskCommands.ToArrayAsync());
                Assert.Equal("1", await ScalarAsync(connection, "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Ai.ScheduledTask'"));
            });
        }
    }

    [SqlServerFact]
    public async Task Scheduled_task_preserves_actual_caller_pending_Audit_dirty_tracker_and_borrowed_transaction()
    {
        await WithDatabaseAsync(async connection =>
        {
            await using var provider = ScheduledServices(connection); await using var owner = new ScheduledScope(provider, connection, new());
            var id = Guid.NewGuid();
            owner.Collector.Enqueue(new AuditEventDraft(TestCompanyIds.CompanyA, "ai.synthetic.caller.v1", "SENTETIK", id.ToString("D"), "Test", null, null,
                "user", owner.User.UserId, "SENTETIK", [], "{}", "{}", "SENTETIK", null, "none", null, "api", null, null, "caller-correlation", null, "caller-key"));
            Assert.True(owner.Writer.HasPendingDrafts);
            Assert.Equal(503, (await Assert.ThrowsAsync<AiScheduledTaskException>(() => owner.Service.CreateAsync(id, ScheduledDefinition, ScheduledMeta("create")))).Status);
            Assert.True(owner.Writer.HasPendingDrafts); owner.Writer.DiscardPending(); // Caller explicitly ends its own draft.
            var dirty = new AiCommandRecordRow { CompanyId = TestCompanyIds.CompanyA, PreviewId = Guid.NewGuid() };
            owner.Database.CommandRecords.Add(dirty);
            Assert.Equal(503, (await Assert.ThrowsAsync<AiScheduledTaskException>(() => owner.Service.CreateAsync(id, ScheduledDefinition, ScheduledMeta("create")))).Status);
            Assert.Equal(EntityState.Added, owner.Database.Entry(dirty).State); owner.Database.Entry(dirty).State = EntityState.Detached;
            await using (var borrowed = await owner.Database.Database.BeginTransactionAsync())
            {
                Assert.Equal(503, (await Assert.ThrowsAsync<AiScheduledTaskException>(() => owner.Service.CreateAsync(id, ScheduledDefinition, ScheduledMeta("create")))).Status);
                Assert.Same(borrowed, owner.Database.Database.CurrentTransaction); await borrowed.RollbackAsync();
            }
            Assert.Empty(await owner.Database.ScheduledTasks.ToArrayAsync()); Assert.Empty(await owner.Database.ScheduledTaskCommands.ToArrayAsync());
        });
    }

    private static readonly DateTimeOffset ScheduledAt = new(2026, 11, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly AiScheduledTaskDefinitionV1 ScheduledDefinition = new(AiScheduledTaskPurposesV1.MorningSummary, AiScheduledTaskEffectV1.Read, "settings-1");
    private static AiScheduledTaskMetadataV1 ScheduledMeta(string key) => new(key, "corr-" + key);
    private static ServiceProvider ScheduledServices(string connection, ScheduledSavePhase? observer = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICompanyContext>(_ => new CompanyContext());
        services.AddAuditModule(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = connection,
            // Reuse the actual AddAuditModule synthetic fixture inputs from AccountingNormalApprovalCompositionTests.
            ["Audit:RefusedAttempts:Retention:Period"] = "P2Y",
            ["Audit:RefusedAttempts:Retention:PolicyRef"] = "policy-ref-1",
            ["Audit:RefusedAttempts:Retention:AutoDisposalEnabled"] = "false",
            ["Audit:RefusedAttempts:StandaloneWrite:Timeout"] = "00:00:02",
        }).Build());
        if (observer is not null) services.AddDbContext<AuditDbContext>(options => options.AddInterceptors(observer));
        return services.BuildServiceProvider();
    }
    private sealed class ScheduledScope : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        internal readonly AiKnowledgeDbContext Database; internal readonly ScheduledUser User = new();
        internal readonly IAuditEventCollector Collector; internal readonly IAuditTransactionWriterV1 Writer;
        internal readonly AiScheduledTaskServiceV1 Service;
        internal ScheduledScope(ServiceProvider provider, string connection, ScheduledPorts ports, IInterceptor? observer = null)
        {
            scope = provider.CreateAsyncScope(); var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(TestCompanyIds.CompanyA);
            Collector = scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(); Writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<AiKnowledgeDbContext>().UseSqlServer(connection,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AiKnowledgeDbContext.Schema));
            if (observer is not null) options.AddInterceptors(observer);
            Database = new(options.Options, company);
            Service = new(company, User, TimeProvider.System, new AiScheduledTaskPersistenceV1(Database, company, Collector, Writer), ports, ports, ports);
        }
        public async ValueTask DisposeAsync() { await Database.DisposeAsync(); await scope.DisposeAsync(); }
    }
    private sealed class ScheduledUser : ICurrentUser
    { internal string Actor = "SENTETIK-owner"; public bool IsAuthenticated => true; public string UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { TestCompanyIds.CompanyA };
      public bool CanAccessCompany(Guid company) => AllowedCompanyIds.Contains(company); }
    private sealed class ScheduledPorts : IAiScheduledTaskAuthorityV1, IAiScheduledTaskSettingsV1, IAiScheduledTaskMorningReadV1
    {
        internal int Reads; internal Func<bool>? AfterAuthority;
        public Task<AiScheduledTaskAuthorityV1> CheckAsync(Guid company, string actor, string owner, AiScheduledTaskActionV1 action, CancellationToken token = default) =>
            Task.FromResult(new AiScheduledTaskAuthorityV1(AfterAuthority?.Invoke() != false ? AiScheduledTaskAuthorityOutcomeV1.Allowed : AiScheduledTaskAuthorityOutcomeV1.Denied, "SENTETIK"));
        public Task<AiScheduledTaskSettingsV1?> ReadAsync(Guid company, string owner, CancellationToken token = default) =>
            Task.FromResult<AiScheduledTaskSettingsV1?>(new(company, "settings-1", ScheduledAt, 3, true));
        public Task<AiScheduledTaskMorningReadV1?> ReadCurrentAsync(CancellationToken token = default)
        { Reads++; return Task.FromResult<AiScheduledTaskMorningReadV1?>(new(TestCompanyIds.CompanyA, "settings-1", ScheduledAt, Guid.NewGuid(), AiScheduledTaskMorningReadOutcomeV1.Available)); }
    }
    private sealed class ScheduledSavePhase(string phase) : SaveChangesInterceptor
    {
        internal bool Enabled = true; internal bool Fired;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        {
            if (Enabled && ((phase == "task-save" && data.Context is AiKnowledgeDbContext)
                || (phase == "audit-save" && data.Context is AuditDbContext))) Fired = true;
            return ValueTask.FromResult(result);
        }
    }
}

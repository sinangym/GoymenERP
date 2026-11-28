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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
            Assert.Equal("2", await ScalarAsync(connection, "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Ai.ScheduledTask' AND SourceChannel=N'Web'"));
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
                AuditActorTypes.User, owner.User.UserId, "SENTETIK", [], "{}", "{}", "SENTETIK", null, AuditAiInvolvement.None, null, GoymenERP.BuildingBlocks.Observability.SourceChannels.Web, null, null, "caller-correlation", null, "caller-key"));
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

    [SqlServerFact]
    public async Task Scheduled_task_rejects_caller_tracked_stale_state_and_then_reads_actual_pause_or_close_version()
    {
        foreach (var close in new[] { false, true })
        {
            await WithDatabaseAsync(async connection =>
            {
                await using var provider = ScheduledServices(connection); var ports = new ScheduledPorts(); var id = Guid.NewGuid();
                await using var staleOwner = new ScheduledScope(provider, connection, ports);
                await staleOwner.Service.CreateAsync(id, ScheduledDefinition, ScheduledMeta("create"));
                var stale = await staleOwner.Database.ScheduledTasks.SingleAsync(x => x.TaskId == id);
                Assert.Equal(1, stale.Version); Assert.Equal(EntityState.Unchanged, staleOwner.Database.Entry(stale).State);
                await using (var other = new ScheduledScope(provider, connection, ports))
                {
                    var changed = close
                        ? await other.Service.CloseAsync(id, 1, ScheduledMeta("close"))
                        : await other.Service.PauseAsync(id, 1, ScheduledMeta("pause"));
                    Assert.Equal(2, changed.Task!.Version);
                }
                Assert.Equal(503, (await Assert.ThrowsAsync<AiScheduledTaskException>(() => staleOwner.Service.RunCurrentAsync(id,
                    new(1, ScheduledAt, "settings-1"), ScheduledMeta("run")))).Status);
                Assert.Equal(1, stale.Version); Assert.Equal(EntityState.Unchanged, staleOwner.Database.Entry(stale).State);
                Assert.Equal(0, ports.Reads); Assert.Empty(await staleOwner.Database.ScheduledTaskRuns.ToArrayAsync());
                staleOwner.Database.Entry(stale).State = EntityState.Detached; // Only the caller releases its stale tracking.
                Assert.Equal(409, (await Assert.ThrowsAsync<AiScheduledTaskException>(() => staleOwner.Service.RunCurrentAsync(id,
                    new(1, ScheduledAt, "settings-1"), ScheduledMeta("run")))).Status);
                var current = Assert.IsType<AiScheduledTaskViewV1>(await staleOwner.Service.ReadOwnAsync(id));
                Assert.Equal(2, current.Version); Assert.Equal(close ? AiScheduledTaskStateV1.Closed : AiScheduledTaskStateV1.Paused, current.State);
                Assert.Equal(0, ports.Reads); Assert.Equal(2, await staleOwner.Database.ScheduledTaskCommands.CountAsync());
            });
        }
    }

    [SqlServerFact]
    public async Task Scheduled_task_foreign_callback_entries_and_Audit_remain_caller_owned_before_and_after_native_save()
    {
        foreach (var afterSave in new[] { false, true })
        foreach (var foreignAudit in new[] { false, true })
        {
            await WithDatabaseAsync(async connection =>
            {
                var phase = new ScheduledSavePhase("task-save"); await using var provider = ScheduledServices(connection);
                var ports = new ScheduledPorts(); await using var owner = new ScheduledScope(provider, connection, ports, phase);
                var id = Guid.NewGuid(); var fired = false;
                var canary = new AiCommandRecordRow { CompanyId = TestCompanyIds.CompanyA, PreviewId = Guid.NewGuid(), ActorId = "SENTETIK-caller",
                    State = 1, Version = 1, BindingSha256 = new string('a', 64), RecordJson = "{}" };
                ports.AfterAuthority = () =>
                {
                    if (!fired && owner.Database.Database.CurrentTransaction is not null && phase.Fired == afterSave)
                    {
                        fired = true;
                        if (foreignAudit) owner.Collector.Enqueue(ScheduledCallerDraft(id, owner.User.UserId));
                        else owner.Database.CommandRecords.Add(canary);
                    }
                    return true;
                };
                Assert.Equal(503, (await Assert.ThrowsAsync<AiScheduledTaskException>(() => owner.Service.CreateAsync(id, ScheduledDefinition, ScheduledMeta("create")))).Status);
                Assert.True(fired); Assert.Equal(afterSave, phase.Fired);
                Assert.Empty(await owner.Database.ScheduledTasks.ToArrayAsync()); Assert.Empty(await owner.Database.ScheduledTaskCommands.ToArrayAsync());
                Assert.Empty(await owner.Database.ScheduledTaskEvents.ToArrayAsync());
                Assert.Equal("0", await ScalarAsync(connection, "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Ai.ScheduledTask'"));
                if (foreignAudit)
                {
                    Assert.True(owner.Writer.HasPendingDrafts);
                    // The caller can still persist its exact draft after the scheduled command's proven abort.
                    await using var caller = await owner.Database.Database.BeginTransactionAsync();
                    await owner.Writer.WritePendingAsync(caller.GetDbTransaction()); await caller.CommitAsync();
                    Assert.Equal("1", await ScalarAsync(connection, "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'ai.synthetic.caller.v1'"));
                }
                else
                {
                    Assert.Equal(EntityState.Added, owner.Database.Entry(canary).State);
                    Assert.Equal("SENTETIK-caller", canary.ActorId); Assert.Equal("{}", canary.RecordJson);
                    Assert.Equal("0", await ScalarAsync(connection, "SELECT COUNT(*) FROM ai.CommandRecords"));
                    owner.Database.Entry(canary).State = EntityState.Detached;
                }
            });
        }
    }

    [SqlServerFact]
    public async Task Scheduled_task_Down_allows_empty_tables_and_refuses_52354_with_each_retained_artifact_family()
    {
        await WithDatabaseAsync(async connection =>
        {
            await using var db = Db(connection, TestCompanyIds.CompanyA);
            var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            var scheduled = Assert.Single(migrations, id => id.EndsWith("_AddAiScheduledTasks", StringComparison.Ordinal));
            var index = Array.IndexOf(migrations, scheduled); Assert.True(index > 0);
            var previous = migrations[index - 1]; var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(previous);
            Assert.Equal("0", await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id "
                + "WHERE s.name=N'ai' AND t.name IN (N'ScheduledTasks',N'ScheduledTaskCommands',N'ScheduledTaskRuns',N'ScheduledTaskEvents')"));
            Assert.Equal("1", await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id "
                + "WHERE s.name=N'ai' AND t.name=N'CommandRecords'"));
            await migrator.MigrateAsync();
            Assert.Equal("4", await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id "
                + "WHERE s.name=N'ai' AND t.name IN (N'ScheduledTasks',N'ScheduledTaskCommands',N'ScheduledTaskRuns',N'ScheduledTaskEvents')"));
        });
        foreach (var family in new[] { "task", "command", "run", "event" })
        {
            await WithDatabaseAsync(async connection =>
            {
                await using var db = Db(connection, TestCompanyIds.CompanyA);
                var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
                var scheduled = Assert.Single(migrations, id => id.EndsWith("_AddAiScheduledTasks", StringComparison.Ordinal));
                var index = Array.IndexOf(migrations, scheduled); Assert.True(index > 0); var previous = migrations[index - 1];
                var id = Guid.NewGuid();
                // Schema-preservation seed only, not a fabricated native business approval/receipt.
                // A run has a real FK to its task, so that family retains its required task parent too.
                if (family is "task" or "run") db.ScheduledTasks.Add(new()
                {
                    CompanyId = TestCompanyIds.CompanyA, TaskId = id, OwnerActorId = "SENTETIK-owner",
                    Purpose = AiScheduledTaskPurposesV1.MorningSummary, Effect = 1, State = 1, Version = 1,
                    ScheduleVersion = "settings-1", CreatedAtUtc = ScheduledAt, UpdatedAtUtc = ScheduledAt,
                });
                if (family == "command") db.ScheduledTaskCommands.Add(new()
                {
                    CompanyId = TestCompanyIds.CompanyA, ActorId = "SENTETIK-owner", KeyHash = new string('a', 64), TaskId = id,
                    Command = "create", PayloadHash = new string('b', 64), ResponseJson = "{}", CorrelationId = "SENTETIK-retained",
                });
                if (family == "run") db.ScheduledTaskRuns.Add(new() { CompanyId = TestCompanyIds.CompanyA, TaskId = id,
                    SlotUtc = ScheduledAt, ReceiptJson = "{}" });
                if (family == "event") db.ScheduledTaskEvents.Add(new() { CompanyId = TestCompanyIds.CompanyA, EventId = Guid.NewGuid(),
                    TaskId = id, ActorId = "SENTETIK-owner", Kind = "created", CorrelationId = "SENTETIK-retained", RecordedAtUtc = ScheduledAt });
                await db.SaveChangesAsync(); db.ChangeTracker.Clear();
                var before = (await db.ScheduledTasks.CountAsync(), await db.ScheduledTaskCommands.CountAsync(),
                    await db.ScheduledTaskRuns.CountAsync(), await db.ScheduledTaskEvents.CountAsync());
                Assert.Equal(family is "task" or "run" ? 1 : 0, before.Item1);
                Assert.Equal(family == "command" ? 1 : 0, before.Item2);
                Assert.Equal(family == "run" ? 1 : 0, before.Item3);
                Assert.Equal(family == "event" ? 1 : 0, before.Item4);
                var error = Sql(await Assert.ThrowsAnyAsync<Exception>(() => db.GetService<IMigrator>().MigrateAsync(previous)));
                Assert.Equal(52354, error.Number);
                Assert.Equal(before, (await db.ScheduledTasks.CountAsync(), await db.ScheduledTaskCommands.CountAsync(),
                    await db.ScheduledTaskRuns.CountAsync(), await db.ScheduledTaskEvents.CountAsync()));
                Assert.Contains(scheduled, await db.Database.GetAppliedMigrationsAsync());
            });
        }
    }

    private static AuditEventDraft ScheduledCallerDraft(Guid id, string actor) => new(TestCompanyIds.CompanyA,
        "ai.synthetic.caller.v1", "SENTETIK", id.ToString("D"), "Test", null, null, AuditActorTypes.User, actor, "SENTETIK", [], "{}", "{}",
        "SENTETIK", null, AuditAiInvolvement.None, null, GoymenERP.BuildingBlocks.Observability.SourceChannels.Web, null, null,
        "caller-correlation", null, "caller-key");

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

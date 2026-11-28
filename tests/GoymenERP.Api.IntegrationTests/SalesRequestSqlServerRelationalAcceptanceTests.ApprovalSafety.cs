using System.Data;
using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Infrastructure;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesRequestSqlServerRelationalAcceptanceTests
{
    [Theory]
    [InlineData("caller-audit")]
    [InlineData("caller-dirty")]
    [InlineData("caller-audit-second-await")]
    [InlineData("caller-dirty-second-await")]
    [InlineData("missing-writer")]
    public async Task Real_Sales_module_request_aliases_preserve_caller_work_and_refuse_without_SQL(string fault)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = "Server=unused.invalid;Database=unused;Connect Timeout=1;",
            ["Audit:RefusedAttempts:Retention:Period"] = "P2Y",
            ["Audit:RefusedAttempts:Retention:PolicyRef"] = "policy-ref-1",
            ["Audit:RefusedAttempts:Retention:AutoDisposalEnabled"] = "false",
            ["Audit:RefusedAttempts:StandaloneWrite:Timeout"] = "00:00:02",
        }).Build();
        var services = new ServiceCollection(); services.AddLogging(); var opens = new RequestNeverOpen();
        services.AddScoped<ICompanyContext>(_ => { var company = new CompanyContext(); company.Set(CompanyA); return company; });
        services.AddScoped<ICurrentUser>(_ => new FixedUser("SENTETIK-composition", CompanyA));
        services.AddScoped(_ => new RequestSafetyAuthority());
        services.AddScoped<IErpAuthorityRevalidationService>(p => p.GetRequiredService<RequestSafetyAuthority>());
        services.AddScoped<IActiveRoleCodeReader>(_ => new NoRoles());
        services.AddScoped<ICorrelationContext>(_ => new FixedCorrelation("SENTETIK-composition"));
        services.AddScoped<ISalesPartyAccountPortV1>(_ => new SyntheticOwners());
        services.AddScoped<ISettingsPrerequisiteProvider>(_ => new SyntheticOwners());
        services.AddScoped<IProductSalesEligibilityV1>(_ => new SyntheticOwners());
        services.AddAuditModule(config); services.AddSalesModule(config, (_, options) => options.AddInterceptors(opens));
        if (fault == "missing-writer") services.RemoveAll<IAuditTransactionWriterV1>();
        using var provider = services.BuildServiceProvider(validateScopes: true); using var scope = provider.CreateScope();
        var p = scope.ServiceProvider; var core = p.GetRequiredService<SalesDraftService>();
        Assert.Same(core, p.GetRequiredService<ISalesRequestService>()); Assert.Same(core, p.GetRequiredService<ISalesDraftService>());
        var store = p.GetRequiredService<SalesStore>(); Assert.Same(store, p.GetRequiredService<ISalesRequestStore>());
        var db = p.GetRequiredService<SalesDbContext>(); Assert.Same(db, p.GetRequiredService<SalesDbContext>());
        var collector = p.GetRequiredService<AuditEventCollector>(); Assert.Same(collector, p.GetRequiredService<IAuditEventCollector>());
        var writer = p.GetService<IAuditTransactionWriterV1>(); var authority = p.GetRequiredService<RequestSafetyAuthority>();
        var callerPhase = fault.EndsWith("second-await", StringComparison.Ordinal) ? 2 : 1;
        var caller = new DocumentCounterRow { CompanyId = CompanyA, Series = "SR", Next = 1, Version = 1, UpdatedAtUtc = DateTimeOffset.UtcNow };
        authority.AfterAwait = () =>
        {
            if (authority.Reads != callerPhase) return;
            if (fault.StartsWith("caller-audit", StringComparison.Ordinal)) collector.Enqueue(CallerRequestAudit());
            if (fault.StartsWith("caller-dirty", StringComparison.Ordinal)) db.DocumentCounters.Add(caller);
        };
        var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => core.ApproveRequestAsync(Guid.NewGuid(), new("1"),
            new("SENTETIK-composition-key", "SENTETIK-composition", null, DateTimeOffset.UtcNow)));
        Assert.Equal((503, "service_unavailable"), (failure.Status, failure.Code)); Assert.Equal(callerPhase, authority.Reads);
        Assert.Null(db.Database.CurrentTransaction); Assert.Equal(0, opens.Opens);
        if (fault.StartsWith("caller-dirty", StringComparison.Ordinal)) Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity);
        else Assert.Empty(db.ChangeTracker.Entries());
        if (fault.StartsWith("caller-audit", StringComparison.Ordinal)) { Assert.True(writer!.HasPendingDrafts); writer.DiscardPending(); }
        else if (writer is not null) Assert.False(writer.HasPendingDrafts);
        if (fault == "missing-writer") Assert.Null(writer);
        // No replacement service/store factory, synthetic transaction owner or native SQL was used.
    }

    [SqlServerFact]
    public async Task Owned_request_decisions_keep_self_approval_native_current_row_replay_and_one_actual_audit()
    {
        await using var run = await Run.StartAsync(); var id = await SeedRequest(run);
        await using (var safe = ApprovalScope(run))
        {
            var approved = await safe.Service.ApproveRequestAsync(id, new("1"), run.Metadata("safe-approve", "SENTETIK-original-approve"));
            Assert.Equal(("Approved", "2", run.Actor), (approved.Detail.Request.State, approved.Detail.Request.Version, approved.Detail.Decision!.DecidedBy));
            var rejected = await safe.Service.RejectRequestAsync(id, new("2", "  SENTETIK ret  "), run.Metadata("safe-reject"));
            Assert.Equal(("Rejected", "3", "  SENTETIK ret  "), (rejected.Detail.Request.State, rejected.Detail.Request.Version, rejected.Detail.Decision!.RejectionReason));
            var replay = await safe.Service.ApproveRequestAsync(id, new("1"), run.Metadata("safe-approve", "SENTETIK-later"));
            Assert.Equal((true, "Rejected", "3", "SENTETIK-original-approve"),
                (replay.IsReplay, replay.Detail.Request.State, replay.Detail.Request.Version, replay.OriginalCorrelationId));
            Assert.DoesNotContain(safe.Db.ChangeTracker.Entries(), x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
            Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
            var json = JsonSerializer.Serialize(replay, SalesDraftJson.Options);
            Assert.DoesNotContain("receipt", json, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(2, await DecisionCount(run, id)); Assert.Equal(2, await DecisionAuditCount(run, id));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.RequestAttempts WHERE RequestId=@id AND ResultJson IS NOT NULL", ("@id", id)));
    }

    [SqlServerFact]
    public async Task Owned_request_late_audit_save_and_precommit_revocations_roll_back_then_same_key_decides_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false }) foreach (var phase in new[] { "after-audit", "post-save", "precommit" })
        {
            var id = await SeedRequest(run); var key = $"safe-{phase}-{id:N}"; var save = new RequestSaveProbe();
            await using (var safe = ApprovalScope(run, save))
            {
                var observedPending = false; var savedFences = 0;
                safe.Authority.AfterAwait = () =>
                {
                    if (safe.Writer.HasPendingDrafts) observedPending = true;
                    // The first drained draft with a still-dirty native header is AFTER actual Audit.WritePendingAsync.
                    if (phase == "after-audit" && observedPending && !safe.Writer.HasPendingDrafts && !save.Saved) safe.Authority.Granted = false;
                    // Post-Save 1; attempt before/after 2/3; Audit before/after 4/5; post-execute 6; actual precommit 7.
                    if (save.Saved && ++savedFences == (phase == "precommit" ? 7 : 1) && phase != "after-audit") safe.Authority.Granted = false;
                };
                var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => Decide(safe, run, id, key, approve));
                Assert.Equal(403, failure.Status); Assert.False(safe.Writer.HasPendingDrafts);
                Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.Null(safe.Db.Database.CurrentTransaction);
                if (phase == "after-audit") { Assert.True(observedPending); Assert.False(save.Saved); }
                else Assert.True(save.Saved);
            }
            Assert.Equal(0, await DecisionCount(run, id)); Assert.Equal(0, await DecisionAuditCount(run, id));
            Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM sales.Requests WHERE Id=@id", ("@id", id)));
            await using (var retry = ApprovalScope(run))
            {
                var result = await Decide(retry, run, id, key, approve); Assert.False(result.IsReplay);
                var replay = await Decide(retry, run, id, key, approve); Assert.True(replay.IsReplay);
                Assert.Equal(result.OriginalCorrelationId, replay.OriginalCorrelationId);
            }
            Assert.Equal(1, await DecisionCount(run, id)); Assert.Equal(1, await DecisionAuditCount(run, id));
        }
    }

    [SqlServerFact]
    public async Task Owned_request_commit_release_denial_suppresses_response_without_undoing_the_native_decision()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await SeedRequest(run); var key = $"safe-release-{id:N}"; var commit = new RequestCommitProbe();
            await using (var safe = ApprovalScope(run, commit))
            {
                commit.AfterCommit = () => safe.Authority.Granted = false;
                var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => Decide(safe, run, id, key, approve));
                Assert.Equal(403, failure.Status); Assert.Equal(1, commit.Commits);
                Assert.DoesNotContain(safe.Db.ChangeTracker.Entries(), x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
                Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
            }
            Assert.Equal(1, await DecisionCount(run, id)); Assert.Equal(1, await DecisionAuditCount(run, id));
            await using var retry = ApprovalScope(run); var replay = await Decide(retry, run, id, key, approve);
            Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-safe-original", replay.OriginalCorrelationId);
            Assert.Equal(1, await DecisionCount(run, id)); Assert.Equal(1, await DecisionAuditCount(run, id));
        }
    }

    [SqlServerFact]
    public async Task Owned_request_SavedChanges_domain_failure_cannot_become_a_durable_validation_refusal()
    {
        await using var run = await Run.StartAsync(); var id = await SeedRequest(run); var key = $"safe-save-{id:N}";
        var save = new RequestSaveProbe { AfterSaved = () => throw new SalesRuleException(SalesRuleCodes.StateConflict, "SENTETIK SavedChanges fault") };
        await using (var safe = ApprovalScope(run, save))
        {
            var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => Decide(safe, run, id, key, true));
            Assert.Equal(503, failure.Status); Assert.True(save.Saved); Assert.Equal(1, save.Saves);
            Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
        }
        Assert.Equal(0, await DecisionCount(run, id)); Assert.Equal(0, await DecisionAuditCount(run, id));
        await using var retry = ApprovalScope(run); Assert.False((await Decide(retry, run, id, key, true)).IsReplay);
        Assert.Equal(1, await DecisionCount(run, id)); Assert.Equal(1, await DecisionAuditCount(run, id));
    }

    [SqlServerFact]
    public async Task Owned_request_final_save_races_do_not_retry_mutation_or_record_a_false_terminal_refusal()
    {
        await using var run = await Run.StartAsync();
        foreach (var number in new[] { 0, 2601, 2627 })
        {
            var id = await SeedRequest(run); var key = $"safe-race-{id:N}";
            Exception error = number == 0 ? new DbUpdateConcurrencyException("SENTETIK final-save race")
                : new DbUpdateException("SENTETIK final-save unique", SqlConcurrencyWrapperPolicyTests.SqlFailure(number));
            var save = new RequestSaveProbe { BeforeSave = () => throw error };
            await using (var safe = ApprovalScope(run, save))
            {
                var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => Decide(safe, run, id, key, true));
                Assert.Equal((409, SalesDraftProblemCodesV1.ConcurrentChange), (failure.Status, failure.Code));
                Assert.Equal(1, save.Saves); Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
            }
            Assert.Equal(0, await DecisionCount(run, id)); Assert.Equal(0, await DecisionAuditCount(run, id));
        }
    }

    [SqlServerFact]
    public async Task Owned_request_injected_provider_deadlock_or_lock_timeout_reads_only_the_original_key_once_after_cleanup()
    {
        await using var run = await Run.StartAsync();
        foreach (var number in new[] { 1205, 1222 }) foreach (var wrapped in new[] { false, true })
        {
            var id = await SeedRequest(run); var key = $"safe-provider-{id:N}";
            var reads = new RequestFirstReadProbe();
            reads.BeforeFirstRead = async () =>
            {
                await using var winner = ApprovalScope(run);
                await Decide(winner, run, id, key, true);
                var error = SqlConcurrencyWrapperPolicyTests.SqlFailure(number);
                if (wrapped) throw new DbUpdateException("SENTETIK injected provider race", error);
                throw error;
            };
            await using var safe = ApprovalScope(run, reads);
            var replay = await Decide(safe, run, id, key, true);
            Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-safe-original", replay.OriginalCorrelationId);
            Assert.Equal(2, reads.KeyReads); Assert.Null(safe.Db.Database.CurrentTransaction); Assert.False(safe.Writer.HasPendingDrafts);
            Assert.Equal(1, await DecisionCount(run, id)); Assert.Equal(1, await DecisionAuditCount(run, id));
        }
        // Real transactions and rollback/replay are exercised. The provider errors are controlled injections, not a two-session deadlock reproduction.
    }

    [SqlServerFact]
    public async Task Owned_request_native_terminal_conflict_is_committed_once_and_replayed_without_a_second_decision()
    {
        await using var run = await Run.StartAsync(); var id = await SeedRequest(run); var key = $"safe-stale-{id:N}";
        await using var safe = ApprovalScope(run);
        var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveRequestAsync(id, new("9"), run.Metadata(key)));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, false), (failure.Status, failure.Code, failure.IsReplay));
        var replay = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveRequestAsync(id, new("9"), run.Metadata(key)));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, true, run.Correlation), (replay.Status, replay.Code, replay.IsReplay, replay.OriginalCorrelationId));
        Assert.Equal(1, await DecisionCount(run, id)); Assert.Equal(0, await DecisionAuditCount(run, id));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM sales.Requests WHERE Id=@id", ("@id", id)));
    }

    [SqlServerFact]
    public async Task Owned_request_entry_preserves_borrowed_ambient_dirty_and_pending_caller_state()
    {
        await using var run = await Run.StartAsync(); var id = await SeedRequest(run);
        foreach (var fault in new[] { "borrowed", "ambient", "dirty", "pending" })
        {
            await using var safe = ApprovalScope(run);
            IDbContextTransaction? borrowed = null; System.Transactions.TransactionScope? ambient = null;
            var caller = new DocumentCounterRow { CompanyId = CompanyA, Series = "SENTETIK", Next = 1, Version = 1, UpdatedAtUtc = DateTimeOffset.UtcNow };
            if (fault == "borrowed") borrowed = await safe.Db.Database.BeginTransactionAsync();
            if (fault == "ambient") ambient = new(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
            if (fault == "dirty") safe.Db.DocumentCounters.Add(caller);
            if (fault == "pending") safe.Collector.Enqueue(CallerRequestAudit());
            try
            {
                var capturedAmbient = System.Transactions.Transaction.Current;
                var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => Decide(safe, run, id, $"entry-{fault}", true));
                Assert.Equal(503, failure.Status); Assert.Same(borrowed, safe.Db.Database.CurrentTransaction);
                Assert.Same(capturedAmbient, System.Transactions.Transaction.Current);
                if (fault == "dirty") Assert.Same(caller, Assert.Single(safe.Db.ChangeTracker.Entries()).Entity);
                else Assert.Empty(safe.Db.ChangeTracker.Entries());
                Assert.Equal(fault == "pending", safe.Writer.HasPendingDrafts);
            }
            finally
            {
                // Only the test caller tears down its borrowed work after preservation was checked.
                ambient?.Dispose();
                if (borrowed is not null) { await borrowed.RollbackAsync(); await borrowed.DisposeAsync(); }
                safe.Db.ChangeTracker.Clear(); safe.Writer.DiscardPending();
            }
        }
        Assert.Equal(0, await DecisionCount(run, id)); Assert.Equal(0, await DecisionAuditCount(run, id));
    }

    [SqlServerFact]
    public async Task Owned_request_final_fence_honors_normal_completion_with_cancelled_token_and_discards_owned_late_audit()
    {
        await using var run = await Run.StartAsync();
        foreach (var fault in new[] { "cancel", "late-audit" })
        {
            var id = await SeedRequest(run); var save = new RequestSaveProbe(); using var cancelled = new CancellationTokenSource();
            await using var safe = ApprovalScope(run, save);
            if (fault == "cancel") safe.Authority.AfterAwait = () => { if (save.Saved) cancelled.Cancel(); };
            else save.AfterSaved = () => safe.Collector.Enqueue(CallerRequestAudit());
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                safe.Service.ApproveRequestAsync(id, new("1"), run.Metadata($"cancel-{id:N}"), cancelled.Token));
            else
            {
                var unavailable = await Assert.ThrowsAsync<SalesDraftServiceException>(() => Decide(safe, run, id, $"audit-{id:N}", true));
                Assert.Equal(503, unavailable.Status);
            }
            Assert.True(save.Saved); Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
            Assert.Null(safe.Db.Database.CurrentTransaction); Assert.Equal(0, await DecisionCount(run, id)); Assert.Equal(0, await DecisionAuditCount(run, id));
        }
    }

    [SqlServerFact]
    public async Task Owned_request_rollback_failure_remains_visible_and_still_clears_tracker_and_owned_audit()
    {
        await using var run = await Run.StartAsync(); var id = await SeedRequest(run);
        var save = new RequestSaveProbe { BeforeSave = () => throw new InvalidOperationException("SENTETIK Save fault") };
        var rollback = new RequestRollbackFault();
        await using (var safe = ApprovalScope(run, save, rollback))
        {
            var failure = await Assert.ThrowsAsync<AggregateException>(() => Decide(safe, run, id, "safe-cleanup", true));
            Assert.Collection(failure.InnerExceptions,
                error => Assert.Equal("SENTETIK Save fault", error.InnerException?.Message),
                error => Assert.Equal("SENTETIK rollback fault", error.Message));
            Assert.Equal(1, save.Saves); Assert.Equal(1, rollback.Rollbacks);
            Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
        }
        Assert.Equal(0, await DecisionCount(run, id)); Assert.Equal(0, await DecisionAuditCount(run, id));
    }

    [SqlServerFact]
    public async Task Owned_request_known_1205_with_completed_provider_transaction_skips_only_the_redundant_rollback()
    {
        await using var run = await Run.StartAsync(); var id = await SeedRequest(run); var reads = new RequestFirstReadProbe(); var completed = false;
        await using var safe = ApprovalScope(run, reads);
        reads.BeforeFirstRead = async () =>
        {
            var transaction = safe.Db.Database.CurrentTransaction!.GetDbTransaction();
            await transaction.RollbackAsync();
            completed = transaction.Connection is null; Assert.True(completed);
            throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
        };
        var unavailable = await Assert.ThrowsAsync<SalesDraftServiceException>(() => Decide(safe, run, id, "safe-completed-deadlock", true));
        Assert.Equal(503, unavailable.Status); Assert.True(completed); Assert.Equal(2, reads.KeyReads);
        Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
        Assert.Equal(0, await DecisionCount(run, id)); Assert.Equal(0, await DecisionAuditCount(run, id));
        // Actual provider transaction completion + controlled 1205 classification, not server deadlock reproduction.
    }

    [SqlServerFact]
    public async Task Owned_request_postsave_actor_or_membership_change_prevents_release_and_rolls_back()
    {
        await using var run = await Run.StartAsync();
        foreach (var change in new[] { "actor", "membership" })
        {
            var id = await SeedRequest(run); var key = $"safe-identity-{id:N}"; var save = new RequestSaveProbe();
            await using (var safe = ApprovalScope(run, save))
            {
                safe.Authority.AfterAwait = () =>
                {
                    if (!save.Saved) return;
                    if (change == "actor") safe.User.ActorId = "SENTETIK-other-actor";
                    else safe.User.HasMembership = false;
                };
                var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => Decide(safe, run, id, key, true));
                Assert.Equal(403, failure.Status); Assert.True(save.Saved); Assert.Empty(safe.Db.ChangeTracker.Entries());
                Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
            }
            Assert.Equal(0, await DecisionCount(run, id)); Assert.Equal(0, await DecisionAuditCount(run, id));
            await using var retry = ApprovalScope(run); Assert.False((await Decide(retry, run, id, key, true)).IsReplay);
            Assert.Equal(1, await DecisionCount(run, id)); Assert.Equal(1, await DecisionAuditCount(run, id));
        }
    }

    private static Task<SalesRequestResponse> Decide(RequestApprovalScope safe, Run run, Guid id, string key, bool approve) => approve
        ? safe.Service.ApproveRequestAsync(id, new("1"), run.Metadata(key, "SENTETIK-safe-original"))
        : safe.Service.RejectRequestAsync(id, new("1", "SENTETIK ret"), run.Metadata(key, "SENTETIK-safe-original"));
    private static async Task<Guid> SeedRequest(Run run)
    {
        await using var scope = run.Store(CompanyA);
        return (await run.Service(scope, run.Actor).CreateRequestAsync(Create(Guid.NewGuid(), Guid.NewGuid(), 1), run.Metadata($"seed-{Guid.NewGuid():N}"))).Detail.Request.Id;
    }
    private static Task<int> DecisionCount(Run run, Guid id) => run.ScalarAsync<int>(
        "SELECT COUNT(*) FROM sales.RequestAttempts WHERE RequestId=@id AND CommandName<>N'sales.request.create.v1'", ("@id", id));
    private static Task<int> DecisionAuditCount(Run run, Guid id) => run.ScalarAsync<int>(
        "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Sales.Request' AND EntityId=@id AND EventType IN (N'sales.request.approved.v1',N'sales.request.rejected.v1')", ("@id", id.ToString("D")));

    private static RequestApprovalScope ApprovalScope(Run run, params IInterceptor[] interceptors)
    {
        var scope = run.Factory.Services.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = scope.ServiceProvider.GetRequiredService<AuditEventCollector>();
        Assert.Same(collector, scope.ServiceProvider.GetRequiredService<IAuditEventCollector>());
        var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var builder = new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
        builder.AddInterceptors(interceptors);
        foreach (var probe in interceptors.OfType<RequestCleanupReleaseProbe>()) probe.Configure(builder);
        var db = new SalesDbContext(builder.Options, company); var store = new SalesStore(db, company, collector, TimeProvider.System, officialCancellationAuditWriter: writer);
        var authority = new RequestSafetyAuthority(); var owners = new SyntheticOwners();
        var user = new RequestSafetyUser(run.Actor);
        var service = new SalesDraftService(company, user, authority, new NoRoles(), new FixedCorrelation(run.Correlation),
            owners, owners, owners, store, new SalesDraftServiceBindings(), TimeProvider.System, requests: store).WithRequestApprovalSafety();
        return new(scope, db, service, authority, writer, collector, user);
    }
    private sealed class RequestApprovalScope(AsyncServiceScope scope, SalesDbContext db, SalesDraftService service,
        RequestSafetyAuthority authority, IAuditTransactionWriterV1 writer, AuditEventCollector collector, RequestSafetyUser user) : IAsyncDisposable
    {
        internal SalesDbContext Db => db; internal SalesDraftService Service => service;
        internal RequestSafetyAuthority Authority => authority; internal IAuditTransactionWriterV1 Writer => writer;
        internal AuditEventCollector Collector => collector;
        internal RequestSafetyUser User => user;
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await scope.DisposeAsync(); }
    }
    private sealed class RequestSafetyAuthority : IErpAuthorityRevalidationService
    {
        internal bool Granted = true; internal int Reads; internal Action? AfterAwait;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(SalesPermissionManifestV1.RequestsApprove, permission); Assert.Equal(CompanyA, companyId);
            Reads++; await Task.Yield(); AfterAwait?.Invoke();
            return new(Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }
    }
    private sealed class RequestSafetyUser(string actor) : ICurrentUser
    {
        internal string ActorId = actor; internal bool HasMembership = true;
        public bool IsAuthenticated => true; public string UserId => ActorId;
        public IReadOnlySet<Guid> AllowedCompanyIds => HasMembership ? new HashSet<Guid>([CompanyA]) : new HashSet<Guid>();
        public bool CanAccessCompany(Guid companyId) => HasMembership && companyId == CompanyA;
    }
    private sealed class RequestSaveProbe : SaveChangesInterceptor
    {
        internal int Saves; internal bool Saved; internal Action? BeforeSave; internal Action? AfterSaved;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Saves++; BeforeSave?.Invoke(); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { Saved = true; AfterSaved?.Invoke(); return ValueTask.FromResult(result); }
    }
    private sealed class RequestCommitProbe : DbTransactionInterceptor
    {
        internal int Commits; internal Action? AfterCommit;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; }
    }
    private sealed class RequestFirstReadProbe : DbCommandInterceptor
    {
        internal int KeyReads; internal Func<Task>? BeforeFirstRead;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("RequestAttempts", StringComparison.Ordinal) && ++KeyReads == 1 && BeforeFirstRead is not null) await BeforeFirstRead();
            return result;
        }
    }
    private sealed class RequestNeverOpen : DbConnectionInterceptor
    {
        internal int Opens;
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { Opens++; throw new InvalidOperationException("Clean-entry refusal must not open SQL."); }
    }
    private sealed class RequestRollbackFault : DbTransactionInterceptor
    {
        internal int Rollbacks;
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { Rollbacks++; throw new InvalidOperationException("SENTETIK rollback fault"); }
    }
    private static AuditEventDraft CallerRequestAudit() => new(CompanyA, "SENTETIK.caller.v1", "Sales.Request", "SENTETIK", "Caller", null, null,
        AuditActorTypes.User, "SENTETIK-composition", SalesPermissionManifestV1.RequestsApprove, [], "{}", "{}", "SENTETIK caller", null,
        AuditAiInvolvement.None, null, "test", null, null, "SENTETIK-composition", null, "SENTETIK-caller-key");
}

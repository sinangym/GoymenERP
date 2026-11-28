using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesPriceFloorApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Owned_floor_approval_preserves_reasoned_maker_check_original_result_and_commercial_invalidation()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeFloor(run);
        var request = SafeFloorRequest(draft); var key = "SENTETIK-floor-original";
        await using (var maker = SafeFloorScope(run, actor: Requester))
        {
            var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => maker.Service.ApprovePriceFloorExceptionsAsync(draft.Id, request, SafeFloorMetadata("maker")));
            Assert.Equal(422, denied.Status); Assert.Equal(SalesRuleCodes.PriceFloorExceptionSameActor, Assert.Single(denied.Errors).Code);
            Assert.Empty(maker.Db.ChangeTracker.Entries()); Assert.False(maker.Writer.HasPendingDrafts);
        }
        Assert.Equal(0, await FloorDecisionCount(run, draft.Id));
        SalesOrderDraftResponse original;
        await using (var safe = SafeFloorScope(run))
        {
            original = await safe.Service.ApprovePriceFloorExceptionsAsync(draft.Id, request, SafeFloorMetadata(key));
            Assert.False(original.IsReplay); Assert.Equal((draft.Version + 1).ToString(CultureInfo.InvariantCulture), original.Detail.Draft.Version);
            Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
        }
        // The real native commercial revision invalidates the current approval; the old key still returns its stored answer.
        await using (var raw = run.Store(Company))
        {
            var current = await raw.Store.FindDraftAsync(draft.Id); Assert.NotNull(current);
            var line = Assert.Single(current.Lines);
            await raw.Store.ReviseDraftCommercialAsync(Envelope("SENTETIK-reprice", Requester), Hash("SENTETIK-reprice"), SalesDraftCommands.ReviseCommercialV1,
                draft.Id, (actual, now) => actual.ReviseCommercial(actual.Version,
                    [new LineCommercialInput(line.Id, line.Commercial with { UnitPrice = "91" })], Requester, now,
                    SalesDiscountLimit.Defined(10m, Guid.Parse("4d15c0a1-0000-4000-8000-0000000000c1"), 1)), Priced, _ => Task.CompletedTask);
        }
        await using (var replayScope = SafeFloorScope(run))
        {
            var replay = await replayScope.Service.ApprovePriceFloorExceptionsAsync(draft.Id, request, SafeFloorMetadata(key, "SENTETIK-later"));
            Assert.True(replay.IsReplay); Assert.Equal(original.Detail.Draft.Version, replay.Detail.Draft.Version);
            Assert.Equal(JsonSerializer.Serialize(original.Detail, SalesDraftJson.Options), JsonSerializer.Serialize(replay.Detail, SalesDraftJson.Options));
            Assert.Equal(original.OriginalCorrelationId, replay.OriginalCorrelationId);
            var current = await replayScope.Db.Drafts.AsNoTracking().SingleAsync(x => x.Id == draft.Id);
            Assert.Equal(draft.Version + 2, current.Version);
        }
        Assert.Equal(1, await FloorDecisionCount(run, draft.Id)); Assert.Equal(1, await FloorAuditCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Owned_floor_actual_audit_save_and_precommit_revocations_roll_back_then_same_key_approves_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var phase in new[] { "after-audit", "post-save", "precommit" })
        {
            var draft = await SeedSafeFloor(run); var key = "SENTETIK-floor-" + phase + draft.Id.ToString("N"); var probe = new FloorSaveProbe();
            await using (var safe = SafeFloorScope(run, probe))
            {
                var pendingSeen = false; var savedFences = 0; var fired = false;
                safe.Authority.AfterRead = permission =>
                {
                    if (safe.Writer.HasPendingDrafts) pendingSeen = true;
                    if (permission == SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove)
                    {
                        // Actual Audit flush precedes Sales Save. Post-Save fence 1, completed native answer fence 2,
                        // actual precommit fence 3. The separate price-disclosure measurement is not an approval fence.
                        var hit = phase == "after-audit" ? pendingSeen && !safe.Writer.HasPendingDrafts && !probe.Saved
                            : probe.Saved && ++savedFences == (phase == "precommit" ? 3 : 1);
                        if (hit) { fired = true; safe.Authority.Granted = false; }
                    }
                    return Task.CompletedTask;
                };
                var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata(key)));
                Assert.Equal(403, denied.Status); Assert.True(fired); Assert.Equal(phase != "after-audit", probe.Saved);
                Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
            }
            Assert.Equal(0, await FloorDecisionCount(run, draft.Id)); Assert.Equal(0, await FloorAuditCount(run, draft.Id));
            Assert.Equal(draft.Version, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
            await using var retry = SafeFloorScope(run);
            Assert.False((await retry.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata(key))).IsReplay);
            Assert.True((await retry.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata(key))).IsReplay);
            Assert.Equal(1, await FloorDecisionCount(run, draft.Id)); Assert.Equal(1, await FloorAuditCount(run, draft.Id));
        }
    }

    [SqlServerFact]
    public async Task Owned_floor_release_denial_keeps_durable_original_answer_and_does_not_repeat_effects()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeFloor(run); var commit = new FloorCommitProbe();
        await using (var safe = SafeFloorScope(run, commit))
        {
            commit.AfterCommit = () => safe.Authority.Granted = false;
            var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata("release")));
            Assert.Equal(403, denied.Status); Assert.Equal(1, commit.Commits); Assert.Empty(safe.Db.ChangeTracker.Entries());
            Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
        }
        Assert.Equal(1, await FloorDecisionCount(run, draft.Id)); Assert.Equal(1, await FloorAuditCount(run, draft.Id));
        await using var retry = SafeFloorScope(run);
        var replay = await retry.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata("release", "SENTETIK-after-release"));
        Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-floor-original", replay.OriginalCorrelationId);
        Assert.Equal(1, await FloorDecisionCount(run, draft.Id)); Assert.Equal(1, await FloorAuditCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Owned_floor_preserves_initial_caller_work_even_if_first_authority_await_cleans_it()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeFloor(run);
        foreach (var fault in new[] { "dirty", "audit", "borrowed", "dirty-clean", "audit-clean", "borrowed-clean", "late-dirty", "late-audit" })
        {
            await using var safe = SafeFloorScope(run); var caller = new DocumentCounterRow { CompanyId = Company, Series = "SENTETIK-PF", Next = 1, Version = 1, UpdatedAtUtc = T0 };
            await using var borrowed = fault.StartsWith("borrowed", StringComparison.Ordinal) ? await safe.Db.Database.BeginTransactionAsync() : null;
            if (fault.StartsWith("dirty", StringComparison.Ordinal)) safe.Db.DocumentCounters.Add(caller);
            if (fault.StartsWith("audit", StringComparison.Ordinal)) safe.Collector.Enqueue(FloorCallerAudit());
            var cleaned = false; var lateInjected = false;
            safe.Authority.AfterRead = async _ =>
            {
                if (fault.StartsWith("late-", StringComparison.Ordinal) && safe.Authority.Reads == 2)
                {
                    if (fault == "late-dirty") safe.Db.DocumentCounters.Add(caller);
                    else safe.Collector.Enqueue(FloorCallerAudit());
                    lateInjected = true;
                }
                if (!fault.EndsWith("-clean", StringComparison.Ordinal) || cleaned) return;
                if (fault.StartsWith("dirty", StringComparison.Ordinal)) safe.Db.ChangeTracker.Clear();
                if (fault.StartsWith("audit", StringComparison.Ordinal)) safe.Writer.DiscardPending();
                if (borrowed is not null) { await borrowed.RollbackAsync(); await borrowed.DisposeAsync(); }
                cleaned = true;
            };
            var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata(fault)));
            Assert.Equal(503, failure.Status); Assert.Equal(fault.EndsWith("-clean", StringComparison.Ordinal), cleaned);
            Assert.Equal(fault == "audit-clean" ? 1 : 0, safe.Writer.Discards);
            Assert.Equal(fault.StartsWith("late-", StringComparison.Ordinal), lateInjected);
            if (fault is "dirty" or "late-dirty") Assert.Same(caller, Assert.Single(safe.Db.ChangeTracker.Entries()).Entity);
            if (fault is "audit" or "late-audit") Assert.True(safe.Writer.HasPendingDrafts);
            if (fault == "borrowed") Assert.Same(borrowed, safe.Db.Database.CurrentTransaction);
            // Only the caller releases its own retained work after the preservation assertions.
            if (borrowed is not null && !cleaned) await borrowed.RollbackAsync();
            safe.Db.ChangeTracker.Clear(); safe.Writer.DiscardPending();
        }
        Assert.Equal(0, await FloorDecisionCount(run, draft.Id)); Assert.Equal(0, await FloorAuditCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Owned_floor_restore_read_authority_loss_stops_before_the_next_child_query()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeFloor(run); var reads = new FloorReadProbe();
        await using var safe = SafeFloorScope(run, reads);
        safe.Authority.AfterRead = _ => { if (reads.MarginReads > 0) safe.Authority.Granted = false; return Task.CompletedTask; };
        var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata("read-stop")));
        Assert.Equal(403, denied.Status); Assert.Equal(1, reads.MarginReads); Assert.Equal(0, reads.KilogramReads);
        Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
        Assert.Equal(0, await FloorDecisionCount(run, draft.Id)); Assert.Equal(0, await FloorAuditCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Owned_floor_saved_domain_fault_rolls_back_without_a_false_terminal_receipt_then_key_retries_once()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeFloor(run);
        var probe = new FloorSaveProbe { AfterSaved = () => throw new SalesRuleException(SalesRuleCodes.StateConflict, "SENTETIK accepted-save domain fault") };
        await using (var safe = SafeFloorScope(run, probe))
        {
            var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata("saved-fault")));
            Assert.Equal(503, failure.Status); Assert.True(probe.Saved); Assert.Equal(1, probe.Saves);
            Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
        }
        Assert.Equal(0, await FloorDecisionCount(run, draft.Id)); Assert.Equal(0, await FloorAuditCount(run, draft.Id));
        await using var retry = SafeFloorScope(run);
        Assert.False((await retry.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata("saved-fault"))).IsReplay);
        Assert.Equal(1, await FloorDecisionCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Controlled_floor_provider_races_abort_once_then_read_only_the_original_key_or_refuse_without_losing_receipts()
    {
        await using var run = await Run.StartAsync();
        foreach (var (number, providerCompleted) in new[] { (1205, false), (1205, true), (1222, false), (2601, false), (2627, false) })
        foreach (var winner in new[] { false, true })
        {
            var draft = await SeedSafeFloor(run); var key = "SENTETIK-race-" + draft.Id.ToString("N");
            var probe = new FloorSaveProbe { BeforeSave = () => throw new DbUpdateException("SENTETIK controlled provider failure", SqlConcurrencyWrapperPolicyTests.SqlFailure(number)) };
            await using (var safe = SafeFloorScope(run, probe))
            {
                if (providerCompleted) probe.BeforeSave = () =>
                {
                    // A real completed provider transaction plus controlled 1205, not a real SQL deadlock claim.
                    var providerTransaction = safe.Db.Database.CurrentTransaction!.GetDbTransaction();
                    providerTransaction.Rollback(); Assert.Null(providerTransaction.Connection);
                    throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                };
                var suppliedWinner = false;
                safe.Authority.AfterRead = async _ =>
                {
                    if (safe.Writer.Discards == 0 || suppliedWinner || !winner) return;
                    suppliedWinner = true;
                    // Separate real command admitted only after the losing owned scope has rolled back and disposed.
                    await using var winning = SafeFloorScope(run);
                    var result = await winning.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata(key, "SENTETIK-original-winner"));
                    Assert.False(result.IsReplay);
                };
                if (winner)
                {
                    var replay = await safe.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata(key));
                    Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-original-winner", replay.OriginalCorrelationId);
                }
                else
                {
                    var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata(key)));
                    Assert.Equal(number is 2601 or 2627 ? 409 : 503, failure.Status);
                }
                Assert.Equal(1, probe.Saves); Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
                Assert.Null(safe.Db.Database.CurrentTransaction);
            }
            Assert.Equal(winner ? 1 : 0, await FloorDecisionCount(run, draft.Id)); Assert.Equal(winner ? 1 : 0, await FloorAuditCount(run, draft.Id));
        }
        // Controlled failures plus real rollback/original-key reads; not evidence of a real simultaneous SQL deadlock.
    }

    [SqlServerFact]
    public async Task Owned_floor_rollback_failure_never_returns_a_readonly_winner_or_a_native_validation_answer()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeFloor(run); var rollback = new FloorRollbackFault();
        await using var safe = SafeFloorScope(run, rollback);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => safe.Service.ApprovePriceFloorExceptionsAsync(draft.Id,
            SafeFloorRequest(draft) with { Lines = [new(Assert.Single(draft.Lines).Id, "")] }, SafeFloorMetadata("rollback-fault")));
        Assert.Contains(failure.InnerExceptions, x => x is InvalidOperationException); Assert.Equal(1, rollback.Rollbacks);
    }

    [SqlServerFact]
    public async Task Owned_floor_terminal_version_refusal_is_atomic_original_and_does_not_change_the_commercial_snapshot()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeFloor(run);
        var stale = SafeFloorRequest(draft) with { ExpectedVersion = "1" };
        await using (var safe = SafeFloorScope(run))
        {
            var first = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApprovePriceFloorExceptionsAsync(draft.Id, stale, SafeFloorMetadata("terminal")));
            Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (first.Status, first.Code)); Assert.False(first.IsReplay);
            var replay = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApprovePriceFloorExceptionsAsync(draft.Id, stale, SafeFloorMetadata("terminal", "SENTETIK-later")));
            Assert.Equal((first.Status, first.Code, true), (replay.Status, replay.Code, replay.IsReplay));
            Assert.Equal(JsonSerializer.Serialize(first.Errors), JsonSerializer.Serialize(replay.Errors));
            Assert.Equal("SENTETIK-floor-original", replay.OriginalCorrelationId);
            Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
        }
        Assert.Equal(draft.Version, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
        Assert.Equal(1, await FloorDecisionCount(run, draft.Id)); Assert.Equal(0, await FloorAuditCount(run, draft.Id));
        await using var valid = SafeFloorScope(run);
        Assert.False((await valid.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata("after-terminal"))).IsReplay);
        Assert.Equal(2, await FloorDecisionCount(run, draft.Id)); Assert.Equal(1, await FloorAuditCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Owned_floor_post_save_actor_company_membership_and_completed_cancellation_losses_abort_every_effect()
    {
        await using var run = await Run.StartAsync();
        foreach (var fault in new[] { "actor", "company", "membership", "cancel" })
        {
            var draft = await SeedSafeFloor(run); var save = new FloorSaveProbe(); using var cancelled = new CancellationTokenSource();
            await using var safe = SafeFloorScope(run, save); var fired = false;
            safe.Authority.AfterRead = _ =>
            {
                if (!save.Saved || fired) return Task.CompletedTask;
                fired = true;
                if (fault == "actor") safe.User.Actor = "SENTETIK-other-actor";
                if (fault == "company") safe.Company.Set(TestCompanyIds.CompanyB);
                if (fault == "membership") safe.User.Membership = false;
                if (fault == "cancel") cancelled.Cancel();
                return Task.CompletedTask;
            };
            if (fault == "cancel")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => safe.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata("context-" + fault), cancelled.Token));
            else
            {
                var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApprovePriceFloorExceptionsAsync(draft.Id, SafeFloorRequest(draft), SafeFloorMetadata("context-" + fault)));
                Assert.Equal(403, denied.Status);
            }
            Assert.True(fired); Assert.True(save.Saved); Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
            Assert.Equal(0, await FloorDecisionCount(run, draft.Id)); Assert.Equal(0, await FloorAuditCount(run, draft.Id));
            Assert.Equal(draft.Version, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
        }
    }

    private static async Task<SalesOrderDraft> SeedSafeFloor(Run run)
    {
        var key = Guid.NewGuid().ToString("N"); await using var scope = run.Store(Company);
        var draft = (await scope.Store.StoreDraftAsync(Envelope(key, Requester), Hash(key), number => Draft(number))).Draft;
        var line = Assert.Single(draft.Lines);
        var reference = new PriceReferenceSnapshot(PriceList, "SENTETIK fiyat listesi", 3, Day.AddDays(-1), Day.AddDays(30), "TRY", Kg, "100", "95", "ExcludingVat", "0", null);
        var priced = await scope.Store.ReviseDraftCommercialAsync(Envelope(key + "price", Requester), Hash(key + "price"), SalesDraftCommands.ReviseCommercialV1, draft.Id,
            (actual, now) => actual.ReviseCommercial(actual.Version, [new LineCommercialInput(line.Id, new CommercialDraftFields("90", Kg, null, reference))], Requester, now,
                SalesDiscountLimit.Defined(10m, Guid.Parse("4d15c0a1-0000-4000-8000-0000000000c1"), 1)), Priced, _ => Task.CompletedTask);
        return priced!.Draft;
    }
    private static SalesDraftPriceFloorExceptionApprovalRequest SafeFloorRequest(SalesOrderDraft draft) => new(draft.Version.ToString(CultureInfo.InvariantCulture),
        draft.Lines.Where(x => SalesOrderDraft.IsBelowPriceFloor(x.Commercial)).Select(x => new SalesDraftPriceFloorExceptionApprovalLineV1(x.Id, "  SENTETIK gerekçe  ")).ToArray());
    private static SalesCommandMetadata SafeFloorMetadata(string key, string correlation = "SENTETIK-floor-original") => new(key, correlation, null, T0);
    private static Task<int> FloorDecisionCount(Run run, Guid id) => run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id AND CommandName=@cmd", ("@id", id), ("@cmd", SalesDraftCommands.ApprovePriceFloorExceptionsV1));
    private static Task<int> FloorAuditCount(Run run, Guid id) => run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'sales.order-draft.price-floor-exceptions-approved.v1'", ("@id", id.ToString("D")));

    private static FloorScope SafeFloorScope(Run run, IInterceptor? interceptor = null, string actor = Approver)
    {
        using var template = run.Context(Company);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = template.Database.GetConnectionString(), ["Audit:RefusedAttempts:Retention:Period"] = "P2Y",
            ["Audit:RefusedAttempts:Retention:PolicyRef"] = "policy-ref-1", ["Audit:RefusedAttempts:Retention:AutoDisposalEnabled"] = "false",
            ["Audit:RefusedAttempts:StandaloneWrite:Timeout"] = "00:00:02",
        }).Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddScoped<ICompanyContext>(_ => new FloorCompany()); services.AddAuditModule(config);
        var provider = services.BuildServiceProvider(validateScopes: true); var scope = provider.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(Company);
        var collector = scope.ServiceProvider.GetRequiredService<AuditEventCollector>(); Assert.Same(collector, scope.ServiceProvider.GetRequiredService<IAuditEventCollector>());
        var writer = new FloorWriter(scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        var builder = new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(template.Database.GetConnectionString(), sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        var db = new SalesDbContext(builder.Options, company); var store = new SalesStore(db, company, collector, TimeProvider.System, officialCancellationAuditWriter: writer);
        var authority = new FloorAuthority(); var user = new FloorUser(actor);
        // Approval consumes only the already-recorded native commercial facts; no new Cari/Product/Settings lookup is allowed.
        var service = new SalesDraftService(company, user, authority, new FloorRoles(), new FloorCorrelation(), null!, null!, null, store, new SalesDraftServiceBindings(), TimeProvider.System)
            .WithPriceFloorApprovalSafety();
        return new(provider, scope, db, service, authority, writer, collector, company, user);
    }
    private sealed class FloorScope(ServiceProvider provider, AsyncServiceScope scope, SalesDbContext db, SalesDraftService service,
        FloorAuthority authority, FloorWriter writer, AuditEventCollector collector, ICompanyContext company, FloorUser user) : IAsyncDisposable
    {
        internal ICompanyContext Company => company; internal FloorUser User => user;
        internal SalesDbContext Db => db; internal SalesDraftService Service => service; internal FloorAuthority Authority => authority;
        internal FloorWriter Writer => writer; internal AuditEventCollector Collector => collector;
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await scope.DisposeAsync(); await provider.DisposeAsync(); }
    }
    private sealed class FloorAuthority : IErpAuthorityRevalidationService
    {
        internal bool Granted = true; internal int Reads; internal Func<string, Task>? AfterRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Company, companyId); Reads++; await Task.Yield(); if (AfterRead is not null) await AfterRead(permission);
            return new(Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }
    }
    // Controlled context drift only; production CompanyContext remains immutable.
    private sealed class FloorCompany : ICompanyContext
    {
        public bool IsResolved { get; private set; }
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId) { CompanyId = companyId; IsResolved = true; }
    }
    private sealed class FloorUser(string actor) : ICurrentUser
    {
        internal string Actor = actor; internal bool Membership = true;
        public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => Membership ? new HashSet<Guid> { Company } : new HashSet<Guid>();
        public bool CanAccessCompany(Guid companyId) => Membership && companyId == Company;
    }
    private sealed class FloorRoles : IActiveRoleCodeReader
    { public Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>()); }
    private sealed class FloorCorrelation : ICorrelationContext
    {
        public bool IsResolved => true; public string CorrelationId => "SENTETIK-floor-original"; public string? CausationId => null;
        public string SourceChannel => "test"; public string? ClientIpAddress => null; public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) => throw new NotSupportedException();
    }
    private sealed class FloorWriter(IAuditTransactionWriterV1 inner) : IAuditTransactionWriterV1
    {
        internal int Discards;
        public bool HasPendingDrafts => inner.HasPendingDrafts;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default) => inner.WritePendingAsync(transaction, cancellationToken);
        public void DiscardPending() { Discards++; inner.DiscardPending(); }
    }
    private sealed class FloorSaveProbe : SaveChangesInterceptor
    {
        internal int Saves; internal bool Saved; internal Action? BeforeSave; internal Action? AfterSaved;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Saves++; BeforeSave?.Invoke(); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { Saved = true; AfterSaved?.Invoke(); return ValueTask.FromResult(result); }
    }
    private sealed class FloorCommitProbe : DbTransactionInterceptor
    {
        internal int Commits; internal Action? AfterCommit;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; }
    }
    private sealed class FloorReadProbe : DbCommandInterceptor
    {
        internal int MarginReads; internal int KilogramReads;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DraftMarginCosts", StringComparison.Ordinal)) MarginReads++;
            if (command.CommandText.Contains("DraftLineKilogramsPerUnit", StringComparison.Ordinal)) KilogramReads++;
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FloorRollbackFault : DbTransactionInterceptor
    {
        internal int Rollbacks;
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        { Rollbacks++; throw new InvalidOperationException("SENTETIK rollback failure"); }
    }
    private static AuditEventDraft FloorCallerAudit() => new(Company, "SENTETIK.caller.v1", "Sales.OrderDraft", "SENTETIK", "Caller", null, null,
        AuditActorTypes.User, Approver, SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove, [], "{}", "{}", "SENTETIK caller", null,
        AuditAiInvolvement.None, null, "test", null, null, "SENTETIK-floor-original", null, "SENTETIK-caller-key");
}

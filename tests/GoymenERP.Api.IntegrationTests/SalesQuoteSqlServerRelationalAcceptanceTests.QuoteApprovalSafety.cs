using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Sales.Infrastructure;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesQuoteSqlServerRelationalAcceptanceTests
{
    [Theory]
    [InlineData(true,1,false)]
    [InlineData(false,1,false)]
    [InlineData(true,2,false)]
    [InlineData(false,2,false)]
    [InlineData(true,1,true)]
    public async Task Actual_Sales_quote_aliases_preserve_caller_audit_and_fail_closed_without_opening_SQL(bool approve,int phase,bool missingWriter)
    {
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["DB_CONNECTION_STRING"]="Server=unused.invalid;Database=unused;Connect Timeout=1;",
            ["Audit:RefusedAttempts:Retention:Period"]="P2Y",["Audit:RefusedAttempts:Retention:PolicyRef"]="policy-ref-1",
            ["Audit:RefusedAttempts:Retention:AutoDisposalEnabled"]="false",["Audit:RefusedAttempts:StandaloneWrite:Timeout"]="00:00:02"}).Build();
        var services=new ServiceCollection();services.AddLogging();var opens=new QuoteNeverOpen();
        services.AddScoped<ICompanyContext>(_=>{var context=new CompanyContext();context.Set(CompanyA);return context;});
        services.AddScoped<ICurrentUser>(_=>new QuoteSafetyUser("SENTETIK-composition"));services.AddScoped<QuoteSafetyAuthority>();
        services.AddScoped<IErpAuthorityRevalidationService>(p=>p.GetRequiredService<QuoteSafetyAuthority>());
        services.AddScoped<IActiveRoleCodeReader>(_=>new NoRoles());services.AddScoped<ICorrelationContext>(_=>new FixedCorrelation("SENTETIK-composition"));
        services.AddScoped<ISalesPartyAccountPortV1>(_=>new SyntheticOwners());services.AddScoped<ISettingsPrerequisiteProvider>(_=>new SyntheticOwners());services.AddScoped<IProductSalesEligibilityV1>(_=>new SyntheticOwners());
        services.AddAuditModule(configuration);services.AddSalesModule(configuration,(_,options)=>options.AddInterceptors(opens));
        if(missingWriter)services.RemoveAll<IAuditTransactionWriterV1>();
        using var provider=services.BuildServiceProvider(validateScopes:true);using var scope=provider.CreateScope();var p=scope.ServiceProvider;
        var core=p.GetRequiredService<SalesDraftService>();Assert.Same(core,p.GetRequiredService<ISalesQuoteService>());Assert.Same(core,p.GetRequiredService<ISalesApprovalRequestService>());Assert.Same(core,p.GetRequiredService<ISalesDraftService>());
        var store=p.GetRequiredService<SalesStore>();Assert.Same(store,p.GetRequiredService<ISalesQuoteStore>());
        var db=p.GetRequiredService<SalesDbContext>();Assert.Same(db,p.GetRequiredService<SalesDbContext>());
        var collector=p.GetRequiredService<AuditEventCollector>();Assert.Same(collector,p.GetRequiredService<IAuditEventCollector>());
        var authority=p.GetRequiredService<QuoteSafetyAuthority>();authority.AfterAwait=()=>{if(authority.Reads==phase&&!missingWriter)collector.Enqueue(QuoteCallerAudit());};
        var refusal=await Assert.ThrowsAsync<SalesDraftServiceException>(async()=>{
            if(approve)await core.ApproveExpiredQuoteConversionAsync(Guid.NewGuid(),new("1"),new("SENTETIK-key","SENTETIK-composition",null,DateTimeOffset.UtcNow));
            else await core.RejectQuoteConversionRequestAsync(Guid.NewGuid(),1,new("1","SENTETIK ret"),new("SENTETIK-key","SENTETIK-composition",null,DateTimeOffset.UtcNow));});
        Assert.Equal((503,"service_unavailable"),(refusal.Status,refusal.Code));Assert.Equal(0,opens.Opens);Assert.Empty(db.ChangeTracker.Entries());Assert.Null(db.Database.CurrentTransaction);
        var writer=p.GetService<IAuditTransactionWriterV1>();
        if(missingWriter)Assert.Null(writer);else {Assert.Equal(phase,authority.Reads);Assert.True(writer!.HasPendingDrafts);writer.DiscardPending();}
        // No factory override: the actual module must activate the same guarded core, with its actual scoped writer.
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_decisions_keep_self_approval_versions_raw_notes_and_current_row_replay()
    {
        await using var run = await Run.StartAsync(); var q = await SeedSafeQuote(run, false);
        await using (var safe = QuoteSafetyScope(run))
        {
            var body = new SalesQuoteExpiryApprovalRequest("1", "  SENTETIK note  ");
            var first = await safe.Service.ApproveExpiredQuoteConversionAsync(q.Id, body, run.Metadata("owned-quote", "SENTETIK-original"));
            Assert.Equal(("2", "Open", run.Actor, "  SENTETIK note  "),
                (first.Detail.Quote.Version, first.Detail.Quote.State, Assert.Single(first.Detail.ExpiryApprovals).ApprovedBy, first.Detail.ExpiryApprovals[0].Note));
            var replay = await safe.Service.ApproveExpiredQuoteConversionAsync(q.Id, body, run.Metadata("owned-quote", "SENTETIK-later"));
            Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-original", replay.OriginalCorrelationId); Assert.Equal(first.Detail.Quote, replay.Detail.Quote);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(replay, SalesDraftJson.Options));
            Assert.Equal(3, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("receipt", out _));
            AssertQuoteClean(safe);
        }
        Assert.Equal((1, 1), (await QuoteDecisionCount(run, q.Id, true), await QuoteAuditCount(run, q.Id, true)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.QuoteAttempts WHERE QuoteId=@id AND ResultJson IS NOT NULL", ("@id", q.Id)));
        var r = await SeedSafeQuote(run, true);
        await using var rejected = QuoteSafetyScope(run);
        var answer = await rejected.Service.RejectQuoteConversionRequestAsync(r.Id, r.RequestNo, new("1", "  SENTETIK ret  "), run.Metadata("owned-reject"));
        Assert.Equal(("Rejected", "2", run.Actor, "  SENTETIK ret  "), (answer.Request.Status, answer.Request.Version, answer.Request.DecidedBy, answer.Request.DecisionReason));
        var again = await rejected.Service.RejectQuoteConversionRequestAsync(r.Id, r.RequestNo, new("1", "  SENTETIK ret  "), run.Metadata("owned-reject"));
        Assert.True(again.IsReplay); Assert.Equal(answer.CorrelationId, again.CorrelationId); AssertQuoteClean(rejected);
        Assert.Equal((1, 1), (await QuoteDecisionCount(run, r.Id, false), await QuoteAuditCount(run, r.Id, false)));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM sales.Quotes WHERE Id=@id", ("@id", r.Id))); // Rejection advances the REQUEST, never the quote.
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_native_terminal_version_refusals_commit_once_without_decision_audit()
    {
        await using var run=await Run.StartAsync();
        foreach(var approve in new[]{true,false})
        {
            var q=await SeedSafeQuote(run,!approve);var key=$"quote-terminal-{q.Id:N}";await using var safe=QuoteSafetyScope(run);
            async Task Execute(){if(approve)await safe.Service.ApproveExpiredQuoteConversionAsync(q.Id,new("9","SENTETIK note"),run.Metadata(key));else await safe.Service.RejectQuoteConversionRequestAsync(q.Id,q.RequestNo,new("9","SENTETIK ret"),run.Metadata(key));}
            var first=await Assert.ThrowsAsync<SalesDraftServiceException>(Execute);Assert.Equal((409,SalesDraftProblemCodesV1.VersionConflict,false),(first.Status,first.Code,first.IsReplay));
            var replay=await Assert.ThrowsAsync<SalesDraftServiceException>(Execute);Assert.Equal((409,SalesDraftProblemCodesV1.VersionConflict,true,run.Correlation),(replay.Status,replay.Code,replay.IsReplay,replay.OriginalCorrelationId));
            Assert.Equal((1,0),(await QuoteDecisionCount(run,q.Id,approve),await QuoteAuditCount(run,q.Id,approve)));AssertQuoteClean(safe);
            Assert.Equal(1L,await run.ScalarAsync<long>("SELECT Version FROM sales.Quotes WHERE Id=@id",("@id",q.Id)));
        }
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_after_audit_postsave_and_precommit_revocations_roll_back_then_same_key_decides_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false }) foreach (var phase in new[] { "after-audit", "post-save", "precommit" })
        {
            var q = await SeedSafeQuote(run, !approve); var key = $"quote-late-{q.Id:N}"; var save = new QuoteSaveProbe();
            await using (var safe = QuoteSafetyScope(run, save))
            {
                var pending = false; var savedFences = 0;
                safe.Authority.AfterAwait = () =>
                {
                    if (safe.Writer.HasPendingDrafts) pending = true;
                    if (phase == "after-audit" && pending && !safe.Writer.HasPendingDrafts && !save.Saved) safe.Authority.Granted = false;
                    // Count only expiry-approval authority: approval post-Save1/post-execute2/precommit3.
                    // Rejection post-Save1, current quote+four retained children pre/post2..11, Guarded release12, post-execute13/precommit14.
                    if (save.Saved && ++savedFences == (phase == "precommit" ? approve ? 3 : 14 : 1) && phase != "after-audit") safe.Authority.Granted = false;
                };
                var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => QuoteDecide(safe, run, q, key, approve));
                Assert.Equal(403, refusal.Status); AssertQuoteAborted(safe);
                if (phase == "after-audit") { Assert.True(pending); Assert.False(save.Saved); } else Assert.True(save.Saved);
            }
            Assert.Equal((0, 0), (await QuoteDecisionCount(run, q.Id, approve), await QuoteAuditCount(run, q.Id, approve)));
            await using var retry = QuoteSafetyScope(run); await QuoteDecide(retry, run, q, key, approve);
            Assert.Equal((1, 1), (await QuoteDecisionCount(run, q.Id, approve), await QuoteAuditCount(run, q.Id, approve)));
        }
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_postcommit_denial_suppresses_response_but_preserves_original_key_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var q = await SeedSafeQuote(run, !approve); var key = $"quote-release-{q.Id:N}"; var commit = new QuoteCommitProbe();
            await using (var safe = QuoteSafetyScope(run, commit))
            {
                commit.AfterCommit = () => safe.Authority.Granted = false;
                var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => QuoteDecide(safe, run, q, key, approve));
                Assert.Equal(403, denied.Status); Assert.Equal(1, commit.Commits); AssertQuoteAborted(safe);
            }
            Assert.Equal((1, 1), (await QuoteDecisionCount(run, q.Id, approve), await QuoteAuditCount(run, q.Id, approve)));
            await using var retry = QuoteSafetyScope(run);
            if (approve) { var replay = await retry.Service.ApproveExpiredQuoteConversionAsync(q.Id, new("1", "SENTETIK note"), run.Metadata(key)); Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-quote-original", replay.OriginalCorrelationId); }
            else { var replay = await retry.Service.RejectQuoteConversionRequestAsync(q.Id, q.RequestNo, new("1", "SENTETIK ret"), run.Metadata(key)); Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-quote-original", replay.CorrelationId); }
            Assert.Equal(1, await QuoteDecisionCount(run, q.Id, approve)); AssertQuoteClean(retry);
        }
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_final_save_failure_cannot_be_mapped_to_a_committed_native_refusal()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false }) foreach (var after in new[] { true, false })
        {
            var q = await SeedSafeQuote(run, !approve); var key = $"quote-save-fault-{q.Id:N}"; var save = new QuoteSaveProbe();
            Action failure = () => throw new SalesRuleException(SalesRuleCodes.StateConflict, "SENTETIK intercepted persistence failure");
            if (after) save.AfterSaved = failure; else save.BeforeSave = failure;
            await using (var safe = QuoteSafetyScope(run, save))
            {
                var answer = await Assert.ThrowsAsync<SalesDraftServiceException>(() => QuoteDecide(safe, run, q, key, approve));
                Assert.Equal(503, answer.Status); Assert.Equal(1, save.Saves); AssertQuoteAborted(safe);
            }
            Assert.Equal((0, 0), (await QuoteDecisionCount(run, q.Id, approve), await QuoteAuditCount(run, q.Id, approve)));
            await using var retry = QuoteSafetyScope(run); await QuoteDecide(retry, run, q, key, approve);
            Assert.Equal((1, 1), (await QuoteDecisionCount(run, q.Id, approve), await QuoteAuditCount(run, q.Id, approve)));
        }
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_provider_races_allow_only_one_clean_readonly_original_key_pass()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false }) foreach (var number in new[] { 1205, 1222 }) foreach (var wrapped in new[] { false, true })
        {
            var q = await SeedSafeQuote(run, !approve); var key = $"quote-race-{q.Id:N}"; var reads = new QuoteKeyReadProbe(); var save = new QuoteSaveProbe();
            reads.BeforeFirst = async () =>
            {
                await using var winner = QuoteSafetyScope(run); await QuoteDecide(winner, run, q, key, approve);
                var error = SqlConcurrencyWrapperPolicyTests.SqlFailure(number);
                if (wrapped) throw new DbUpdateException("SENTETIK injected provider fault", error); throw error;
            };
            await using var safe = QuoteSafetyScope(run, reads, save); var replay = await QuoteDecide(safe, run, q, key, approve);
            Assert.Equal((true,"SENTETIK-quote-original"),replay); Assert.Equal(2, reads.Reads); Assert.Equal(0, save.Saves); AssertQuoteAborted(safe);
            Assert.Equal((1, 1), (await QuoteDecisionCount(run, q.Id, approve), await QuoteAuditCount(run, q.Id, approve)));
        }
        // Real SQL transactions and another native writer; 1205/1222 are controlled injections, not deadlock reproduction.
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_completed_provider_1205_and_absent_winner_do_not_retry_mutation()
    {
        await using var run = await Run.StartAsync(); var q = await SeedSafeQuote(run, false); var reads = new QuoteKeyReadProbe(); var save = new QuoteSaveProbe();
        await using var safe = QuoteSafetyScope(run, reads, save); var completed = false;
        reads.BeforeFirst = async () =>
        {
            var transaction = safe.Db.Database.CurrentTransaction!.GetDbTransaction(); await transaction.RollbackAsync();
            completed = transaction.Connection is null; Assert.True(completed); throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
        };
        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => QuoteDecide(safe, run, q, "quote-completed", true));
        Assert.Equal(503, refusal.Status); Assert.True(completed); Assert.Equal(2, reads.Reads); Assert.Equal(0, save.Saves); AssertQuoteAborted(safe);
        Assert.Equal((0, 0), (await QuoteDecisionCount(run, q.Id, true), await QuoteAuditCount(run, q.Id, true)));
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_final_save_concurrency_without_winner_preserves_native_conflict_and_key_availability()
    {
        await using var run=await Run.StartAsync();
        foreach(var approve in new[]{true,false})foreach(var fault in new[]{0,2601,2627})
        {
            var q=await SeedSafeQuote(run,!approve);var key=$"quote-final-race-{q.Id:N}";var reads=new QuoteKeyReadProbe();
            var save=new QuoteSaveProbe {BeforeSave=()=>{if(fault==0)throw new DbUpdateConcurrencyException("SENTETIK final Save concurrency");throw new DbUpdateException("SENTETIK final Save duplicate",SqlConcurrencyWrapperPolicyTests.SqlFailure(fault));}};
            await using(var safe=QuoteSafetyScope(run,reads,save))
            {
                var refusal=await Assert.ThrowsAsync<SalesDraftServiceException>(()=>QuoteDecide(safe,run,q,key,approve));
                Assert.Equal((409,SalesDraftProblemCodesV1.ConcurrentChange),(refusal.Status,refusal.Code));Assert.Equal(1,save.Saves);Assert.Equal(3,reads.Reads);AssertQuoteAborted(safe);
            }
            Assert.Equal((0,0),(await QuoteDecisionCount(run,q.Id,approve),await QuoteAuditCount(run,q.Id,approve)));
            await using var retry=QuoteSafetyScope(run);var actual=await QuoteDecide(retry,run,q,key,approve);Assert.False(actual.Replay);
            Assert.Equal((1,1),(await QuoteDecisionCount(run,q.Id,approve),await QuoteAuditCount(run,q.Id,approve)));
        }
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_entry_preserves_caller_dirty_pending_borrowed_and_ambient_state()
    {
        await using var run = await Run.StartAsync(); var q = await SeedSafeQuote(run, false);
        foreach (var fault in new[] { "dirty", "pending", "borrowed", "ambient", "after-first-await" })
        {
            var save = new QuoteSaveProbe(); await using var safe = QuoteSafetyScope(run, save);
            var caller = new DocumentCounterRow {CompanyId=CompanyA,Series="SQ",Next=1,Version=1,UpdatedAtUtc=run.Clock.GetUtcNow()};
            if (fault == "dirty") safe.Db.DocumentCounters.Add(caller);
            if (fault == "pending") safe.Collector.Enqueue(QuoteCallerAudit());
            if (fault == "after-first-await") safe.Authority.AfterAwait = () => safe.Db.DocumentCounters.Add(caller);
            await using var borrowed = fault == "borrowed" ? await safe.Db.Database.BeginTransactionAsync() : null;
            using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => QuoteDecide(safe, run, q, $"quote-entry-{fault}", true));
            Assert.Equal(503, refusal.Status); Assert.Equal(0, save.Saves);
            if (fault is "dirty" or "after-first-await") Assert.Same(caller, Assert.Single(safe.Db.ChangeTracker.Entries()).Entity); else Assert.Empty(safe.Db.ChangeTracker.Entries());
            Assert.Equal(fault == "pending", safe.Writer.HasPendingDrafts);
            if (borrowed is not null) Assert.Same(borrowed, safe.Db.Database.CurrentTransaction); else Assert.Null(safe.Db.Database.CurrentTransaction);
            // Caller residue is cleared by the fixture only, never by an unowned guard.
            safe.Writer.DiscardPending(); safe.Db.ChangeTracker.Clear();
        }
        Assert.Equal(0, await QuoteDecisionCount(run, q.Id, true));
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_final_cancel_or_late_audit_rolls_back_and_discards_only_owned_drafts()
    {
        await using var run = await Run.StartAsync();
        foreach (var fault in new[] { "cancel", "audit" })
        {
            var q = await SeedSafeQuote(run, false); var save = new QuoteSaveProbe(); using var cancellation = new CancellationTokenSource();
            await using var safe = QuoteSafetyScope(run, save);
            if (fault == "cancel") safe.Authority.AfterAwait = () => { if (save.Saved) cancellation.Cancel(); };
            else save.AfterSaved = () => safe.Collector.Enqueue(QuoteCallerAudit());
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => safe.Service.ApproveExpiredQuoteConversionAsync(q.Id, new("1", "SENTETIK note"), run.Metadata("quote-cancel"), cancellation.Token));
            else { var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => QuoteDecide(safe, run, q, "quote-late-audit", true)); Assert.Equal(503, refusal.Status); }
            Assert.True(save.Saved); AssertQuoteAborted(safe); Assert.Equal((0, 0), (await QuoteDecisionCount(run, q.Id, true), await QuoteAuditCount(run, q.Id, true)));
        }
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_postsave_actor_or_membership_change_rolls_back_before_release()
    {
        await using var run=await Run.StartAsync();
        foreach(var approve in new[]{true,false})foreach(var actor in new[]{true,false})
        {
            var q=await SeedSafeQuote(run,!approve);var key=$"quote-identity-{q.Id:N}";var save=new QuoteSaveProbe();
            await using(var safe=QuoteSafetyScope(run,save))
            {
                safe.Authority.AfterAwait=()=>{if(save.Saved){if(actor)safe.User.Actor="SENTETIK-other-actor";else safe.User.Member=false;}};
                var refused=await Assert.ThrowsAsync<SalesDraftServiceException>(()=>QuoteDecide(safe,run,q,key,approve));Assert.Equal(403,refused.Status);Assert.True(save.Saved);AssertQuoteAborted(safe);
            }
            Assert.Equal((0,0),(await QuoteDecisionCount(run,q.Id,approve),await QuoteAuditCount(run,q.Id,approve)));
            await using var retry=QuoteSafetyScope(run);await QuoteDecide(retry,run,q,key,approve);Assert.Equal((1,1),(await QuoteDecisionCount(run,q.Id,approve),await QuoteAuditCount(run,q.Id,approve)));
        }
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_cleanup_failure_is_visible_and_never_recovers_a_winner()
    {
        await using var run = await Run.StartAsync(); var q = await SeedSafeQuote(run, false); var save = new QuoteSaveProbe {BeforeSave=()=>throw new InvalidOperationException("SENTETIK save fault")};
        var cleanup = new QuoteRollbackProbe {Fail=true}; var reads = new QuoteKeyReadProbe();
        await using (var safe = QuoteSafetyScope(run, save, cleanup, reads))
        {
            var failure = await Assert.ThrowsAsync<AggregateException>(() => QuoteDecide(safe, run, q, "quote-cleanup", true));
            Assert.Collection(failure.InnerExceptions, error=>Assert.Equal("SENTETIK save fault",error.InnerException?.Message), error=>Assert.Equal("SENTETIK rollback fault",error.Message));
            Assert.Equal(1, save.Saves); Assert.Equal(1, cleanup.Rollbacks); Assert.Equal(2,reads.Reads); AssertQuoteAborted(safe);
        }
        Assert.Equal((0, 0), (await QuoteDecisionCount(run, q.Id, true), await QuoteAuditCount(run, q.Id, true)));
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_dispose_fault_keeps_unknown_outcome_distinct_from_a_rolled_back_attempt()
    {
        await using var run=await Run.StartAsync();
        foreach(var committed in new[]{false,true})
        {
            var q=await SeedSafeQuote(run,false);var key=$"quote-dispose-{q.Id:N}";var dispose=new QuoteDisposeFault();var save=new QuoteSaveProbe();
            if(!committed)save.BeforeSave=()=>throw new InvalidOperationException("SENTETIK save before disposal");
            await using(var safe=QuoteSafetyScope(run,dispose,save))
            {
                var error=await Assert.ThrowsAsync<AggregateException>(()=>QuoteDecide(safe,run,q,key,true));Assert.True(dispose.Failed);
                Assert.Contains("SENTETIK quote dispose fault",error.ToString(),StringComparison.Ordinal);Assert.Equal(1,save.Saves);AssertQuoteAborted(safe);
            }
            Assert.Equal((committed?1:0,committed?1:0),(await QuoteDecisionCount(run,q.Id,true),await QuoteAuditCount(run,q.Id,true)));
            await using var retry=QuoteSafetyScope(run);var result=await QuoteDecide(retry,run,q,key,true);Assert.Equal(committed,result.Replay);
            Assert.Equal((1,1),(await QuoteDecisionCount(run,q.Id,true),await QuoteAuditCount(run,q.Id,true)));
        }
    }

    [SqlServerFact]
    public async Task Owned_expired_quote_nonrecorded_rejection_freshly_releases_after_abort_and_keeps_native_422()
    {
        await using var run = await Run.StartAsync();
        foreach(var phase in new[]{"rollback","dispose"})foreach (var fault in new[] { "unchanged", "permission", "membership", "cancel" })
        {
            var q = await SeedSafeQuote(run, true); var probe = new QuoteCleanupReleaseProbe(phase); using var cancellation = new CancellationTokenSource();
            await using var safe = QuoteSafetyScope(run, probe);
            probe.AfterCleanup=()=>{if(fault=="permission")safe.Authority.Granted=false;if(fault=="membership")safe.User.Member=false;if(fault=="cancel")cancellation.Cancel();};
            Task Reject() => safe.Service.RejectQuoteConversionRequestAsync(q.Id,q.RequestNo,new("1",null),run.Metadata("quote-invalid-reason"),cancellation.Token);
            if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(Reject);
            else {var refusal=await Assert.ThrowsAsync<SalesDraftServiceException>(Reject);Assert.Equal(fault=="unchanged"?422:403,refusal.Status);}
            Assert.Equal(1,probe.Observed);AssertQuoteAborted(safe);Assert.Equal(0,await QuoteDecisionCount(run,q.Id,false));Assert.Equal(0,await QuoteAuditCount(run,q.Id,false));
        }
    }

    private static async Task<(Guid Id,int RequestNo)> SeedSafeQuote(Run run,bool request)
    {
        run.Clock=IstanbulMorning(Day);Guid id;
        await using(var scope=run.Store(CompanyA))id=(await run.Service(scope,run.Actor).CreateQuoteAsync(CreateQuote(Guid.NewGuid(),Guid.NewGuid(),[],"12.50"),run.Metadata("safe-create-"+Guid.NewGuid().ToString("N")))).Detail.Quote.Id;
        run.Clock=IstanbulMorning(Day.AddDays(16));
        if(!request)return(id,0);
        await using var pending=run.Store(CompanyA);var answer=await run.Service(pending,run.Actor).RequestQuoteConversionAsync(id,new("1","SENTETIK request"),run.Metadata("safe-request-"+id.ToString("N")));
        return(id,answer.Request.RequestNo);
    }
    private static async Task<(bool Replay,string Correlation)> QuoteDecide(QuoteSafetyScopeState safe,Run run,(Guid Id,int RequestNo) q,string key,bool approve)
    {
        if(approve){var answer=await safe.Service.ApproveExpiredQuoteConversionAsync(q.Id,new("1","SENTETIK note"),run.Metadata(key,"SENTETIK-quote-original"));return(answer.IsReplay,answer.OriginalCorrelationId);}
        var rejection=await safe.Service.RejectQuoteConversionRequestAsync(q.Id,q.RequestNo,new("1","SENTETIK ret"),run.Metadata(key,"SENTETIK-quote-original"));return(rejection.IsReplay,rejection.CorrelationId);
    }
    private static Task<int> QuoteDecisionCount(Run run,Guid id,bool approve)=>run.ScalarAsync<int>(approve
        ? "SELECT COUNT(*) FROM sales.QuoteAttempts WHERE QuoteId=@id AND CommandName=N'sales.quote.approve-expired-conversion.v1'"
        : "SELECT COUNT(*) FROM sales.ApprovalRequestAttempts WHERE SubjectId=@id AND CommandName=N'sales.approval-request.reject-quote-conversion.v1'",("@id",id));
    private static Task<int> QuoteAuditCount(Run run,Guid id,bool approve)=>run.ScalarAsync<int>(approve
        ? "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Sales.Quote' AND EntityId=@id AND EventType=N'sales.quote.expired-conversion-approved.v1'"
        : "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Sales.ApprovalRequest' AND EventType=N'sales.quote.expired-conversion-request-rejected.v1' AND EntityId IN (SELECT CONVERT(nvarchar(36),Id) FROM sales.ApprovalRequests WHERE SubjectId=@subject)",
        approve?("@id",(object)id.ToString("D")):("@subject",(object)id));
    private static void AssertQuoteClean(QuoteSafetyScopeState safe)
    {Assert.DoesNotContain(safe.Db.ChangeTracker.Entries(),e=>e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);Assert.False(safe.Writer.HasPendingDrafts);Assert.Null(safe.Db.Database.CurrentTransaction);}
    private static void AssertQuoteAborted(QuoteSafetyScopeState safe){AssertQuoteClean(safe);Assert.Empty(safe.Db.ChangeTracker.Entries());}

    private static QuoteSafetyScopeState QuoteSafetyScope(Run run,params IInterceptor[] interceptors)
    {
        var scope=run.Factory.Services.CreateAsyncScope();var company=scope.ServiceProvider.GetRequiredService<ICompanyContext>();company.Set(CompanyA);
        var collector=scope.ServiceProvider.GetRequiredService<AuditEventCollector>();Assert.Same(collector,scope.ServiceProvider.GetRequiredService<IAuditEventCollector>());
        var writer=scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var options=new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,sql=>sql.MigrationsHistoryTable("__EFMigrationsHistory",SalesDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector,company)).AddInterceptors(interceptors);
        foreach(var probe in interceptors.OfType<QuoteCleanupReleaseProbe>())probe.Configure(options);
        foreach(var probe in interceptors.OfType<QuoteDisposeFault>())probe.Configure(options);
        var db=new SalesDbContext(options.Options,company);var store=new SalesStore(db,company,collector,run.Clock,officialCancellationAuditWriter:writer);
        var user=new QuoteSafetyUser(run.Actor);var authority=new QuoteSafetyAuthority();var owners=new SyntheticOwners();var links=new NoOwnerLinks();
        var service=new SalesDraftService(company,user,authority,new NoRoles(),new FixedCorrelation(run.Correlation),owners,owners,owners,store,new SalesDraftServiceBindings(),run.Clock,
            allocations:links,loadings:links,requests:store,quotes:store,quotePolicies:new SyntheticQuotePolicies(),allocationsV2:links).WithQuoteApprovalSafety();
        return new(scope,db,service,authority,writer,collector,user);
    }
    private sealed class QuoteSafetyScopeState(AsyncServiceScope scope,SalesDbContext db,SalesDraftService service,QuoteSafetyAuthority authority,IAuditTransactionWriterV1 writer,AuditEventCollector collector,QuoteSafetyUser user):IAsyncDisposable
    {
        internal SalesDbContext Db=>db;internal SalesDraftService Service=>service;internal QuoteSafetyAuthority Authority=>authority;internal IAuditTransactionWriterV1 Writer=>writer;internal AuditEventCollector Collector=>collector;internal QuoteSafetyUser User=>user;
        public async ValueTask DisposeAsync(){await db.DisposeAsync();await scope.DisposeAsync();}
    }
    private sealed class QuoteSafetyAuthority:IErpAuthorityRevalidationService
    {
        internal bool Granted=true;internal int Reads;internal Action? AfterAwait;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission,Guid companyId,CancellationToken cancellationToken=default)
        {Assert.Equal(CompanyA,companyId);await Task.Yield();if(permission==SalesPermissionManifestV1.QuotesExpiredConversionApprove){Reads++;AfterAwait?.Invoke();}return new(permission!=SalesPermissionManifestV1.QuotesExpiredConversionApprove||Granted,true,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>());}
    }
    private sealed class QuoteSafetyUser(string actor):ICurrentUser
    {
        internal string Actor=actor;internal bool Member=true;public bool IsAuthenticated=>true;public string UserId=>Actor;public IReadOnlySet<Guid> AllowedCompanyIds=>Member?new HashSet<Guid>([CompanyA]):new HashSet<Guid>();public bool CanAccessCompany(Guid id)=>Member&&id==CompanyA;
    }
    private sealed class QuoteSaveProbe:SaveChangesInterceptor
    {
        internal bool Saved;internal int Saves;internal Action? BeforeSave;internal Action? AfterSaved;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken cancellationToken=default){Saves++;BeforeSave?.Invoke();return ValueTask.FromResult(result);}
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,int result,CancellationToken cancellationToken=default){Saved=true;AfterSaved?.Invoke();return ValueTask.FromResult(result);}
    }
    private sealed class QuoteCommitProbe:DbTransactionInterceptor
    {internal int Commits;internal Action? AfterCommit;public override Task TransactionCommittedAsync(DbTransaction transaction,TransactionEndEventData data,CancellationToken cancellationToken=default){Commits++;AfterCommit?.Invoke();return Task.CompletedTask;}}
    private sealed class QuoteKeyReadProbe:DbCommandInterceptor
    {
        internal int Reads;internal Func<Task>? BeforeFirst;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default)
        {if((command.CommandText.Contains("QuoteAttempts",StringComparison.Ordinal)||command.CommandText.Contains("ApprovalRequestAttempts",StringComparison.Ordinal))&&++Reads==1&&BeforeFirst is not null)await BeforeFirst();return result;}
    }
    private sealed class QuoteNeverOpen:DbConnectionInterceptor
    {
        internal int Opens;
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,ConnectionEventData data,InterceptionResult result,CancellationToken cancellationToken=default)
        {Opens++;throw new InvalidOperationException("Quote entry guard must refuse before SQL.");}
    }
    private sealed class QuoteDisposeFault:DbTransactionInterceptor
    {
        internal bool Failed;
        internal void Configure(DbContextOptionsBuilder<SalesDbContext> options)=>options.LogTo((id,_)=>id==RelationalEventId.TransactionDisposed,_=>{if(!Failed){Failed=true;throw new InvalidOperationException("SENTETIK quote dispose fault");}});
    }
    private sealed class QuoteCleanupReleaseProbe(string phase):DbTransactionInterceptor
    {
        internal int Observed;internal Action? AfterCleanup;
        internal void Configure(DbContextOptionsBuilder<SalesDbContext> options)=>options.LogTo((id,_)=>id==RelationalEventId.TransactionDisposed,_=>{if(phase=="dispose")Changed();});
        public override Task TransactionRolledBackAsync(DbTransaction transaction,TransactionEndEventData data,CancellationToken cancellationToken=default){if(phase=="rollback")Changed();return Task.CompletedTask;}
        private void Changed(){if(Observed++==0)AfterCleanup?.Invoke();}
    }
    private sealed class QuoteRollbackProbe:DbTransactionInterceptor
    {
        internal bool Fail;internal int Rollbacks;internal Action? AfterRollback;
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction,TransactionEventData data,InterceptionResult result,CancellationToken cancellationToken=default){Rollbacks++;if(Fail)throw new InvalidOperationException("SENTETIK rollback fault");return ValueTask.FromResult(result);}
        public override Task TransactionRolledBackAsync(DbTransaction transaction,TransactionEndEventData data,CancellationToken cancellationToken=default){AfterRollback?.Invoke();return Task.CompletedTask;}
    }
    private static AuditEventDraft QuoteCallerAudit()=>new(CompanyA,"SENTETIK.quote.caller.v1","Sales.Quote","SENTETIK","Caller",null,null,AuditActorTypes.User,"SENTETIK",SalesPermissionManifestV1.QuotesExpiredConversionApprove,[],"{}","{}","SENTETIK caller",null,AuditAiInvolvement.None,null,"test",null,null,"SENTETIK",null,"SENTETIK-caller");
}

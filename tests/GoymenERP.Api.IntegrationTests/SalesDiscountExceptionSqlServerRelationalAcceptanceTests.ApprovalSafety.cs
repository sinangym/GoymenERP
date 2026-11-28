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

public sealed partial class SalesDiscountExceptionSqlServerRelationalAcceptanceTests
{
    private const string DiscountApprover = "SENTETIK-discount-checker";

    [SqlServerFact]
    public async Task Owned_discount_preserves_listed_maker_exclusion_unlisted_self_exception_raw_hash_and_original_result()
    {
        await using var run = await Run.StartAsync(); var listed = await SeedSafeDiscount(run, listed: true);
        await using (var maker = SafeDiscountScope(run, actor: run.Actor))
        {
            var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => maker.Service.ApproveDiscountExceptionsAsync(listed.Id, SafeDiscountRequest(listed), SafeDiscountMetadata("maker")));
            Assert.Equal(422, denied.Status); Assert.Equal(SalesRuleCodes.DiscountExceptionSameActor, Assert.Single(denied.Errors).Code);
            Assert.Empty(maker.Db.ChangeTracker.Entries()); Assert.False(maker.Writer.HasPendingDrafts);
        }
        Assert.Equal(0, await DiscountDecisionCount(run, listed.Id));
        var unlisted = await SeedSafeDiscount(run); var key = "SENTETIK-discount-original";
        await using var self = SafeDiscountScope(run, actor: run.Actor);
        var body = SafeDiscountRequest(unlisted);
        var first = await self.Service.ApproveDiscountExceptionsAsync(unlisted.Id, body, SafeDiscountMetadata(key));
        Assert.False(first.IsReplay); Assert.Equal((unlisted.Version + 1).ToString(CultureInfo.InvariantCulture), first.Detail.Draft.Version);
        // A later genuine commercial revision invalidates today's discount check, never the original keyed result.
        await using (var raw = run.SalesStore(CompanyA))
        {
            var current = await raw.Store.FindDraftAsync(unlisted.Id); Assert.NotNull(current); var line = Assert.Single(current.Lines);
            await raw.Store.ReviseDraftCommercialAsync(run.SalesEnvelope("SENTETIK-later-price"), Hash("SENTETIK-later-price"), SalesDraftCommands.ReviseCommercialV1,
                current.Id, (actual, now) => actual.ReviseCommercial(actual.Version,
                    [new LineCommercialInput(line.Id, new CommercialDraftFields("91", KgUnit, "5"))], run.Actor, now, SalesDiscountLimit.Undefined),
                new SalesDraftStateAudit("sales.order-draft.commercial-revised.v1", "ReviseCommercial", "SENTETIK sonraki fiyat"), _ => Task.CompletedTask);
        }
        Assert.Equal(unlisted.Version + 2, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", unlisted.Id)));
        var replay = await self.Service.ApproveDiscountExceptionsAsync(unlisted.Id, body, SafeDiscountMetadata(key, "SENTETIK-later"));
        Assert.True(replay.IsReplay); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(JsonSerializer.Serialize(first.Detail, SalesDraftJson.Options), JsonSerializer.Serialize(replay.Detail, SalesDraftJson.Options));
        var changed = body with { Lines = body.Lines.Select(x => x with { Reason = x.Reason.Trim() }).ToArray() };
        var conflict = await Assert.ThrowsAsync<SalesDraftServiceException>(() => self.Service.ApproveDiscountExceptionsAsync(unlisted.Id, changed, SafeDiscountMetadata(key)));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), (conflict.Status, conflict.Code));
        Assert.Equal(1, await DiscountDecisionCount(run, unlisted.Id)); Assert.Equal(1, await DiscountAuditCount(run, unlisted.Id));
        Assert.Equal(0, await DiscountDecisionCount(run, listed.Id));
        Assert.DoesNotContain(self.Db.ChangeTracker.Entries(), x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        Assert.False(self.Writer.HasPendingDrafts); Assert.Null(self.Db.Database.CurrentTransaction);
    }

    [SqlServerFact]
    public async Task Owned_discount_actual_audit_save_and_precommit_revocations_roll_back_then_same_key_approves_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var phase in new[] { "after-audit", "post-save", "precommit" })
        {
            var draft = await SeedSafeDiscount(run); var key = "SENTETIK-discount-" + phase + draft.Id.ToString("N"); var probe = new DiscountSaveProbe();
            await using (var safe = SafeDiscountScope(run, probe))
            {
                var pendingSeen = false; var savedFences = 0; var fired = false;
                safe.Authority.AfterRead = permission =>
                {
                    if (safe.Writer.HasPendingDrafts) pendingSeen = true;
                    if (permission == SalesPermissionManifestV1.OrdersDiscountExceptionApprove)
                    {
                        // Actual Audit flush precedes Sales Save. Post-Save fence 1, completed native answer 2,
                        // actual precommit 3. Native price disclosure is not an approval fence; no receipt port is added.
                        var hit = phase == "after-audit" ? pendingSeen && !safe.Writer.HasPendingDrafts && !probe.Saved
                            : probe.Saved && ++savedFences == (phase == "precommit" ? 3 : 1);
                        if (hit) { fired = true; safe.Authority.Granted = false; }
                    }
                    return Task.CompletedTask;
                };
                var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata(key)));
                Assert.Equal(403, denied.Status); Assert.True(fired); Assert.Equal(phase != "after-audit", probe.Saved);
                Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
            }
            Assert.Equal(0, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(0, await DiscountAuditCount(run, draft.Id));
            Assert.Equal(draft.Version, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
            await using var retry = SafeDiscountScope(run);
            Assert.False((await retry.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata(key))).IsReplay);
            Assert.True((await retry.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata(key))).IsReplay);
            Assert.Equal(1, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(1, await DiscountAuditCount(run, draft.Id));
        }
    }

    [SqlServerFact]
    public async Task Owned_discount_release_denial_keeps_durable_original_answer_and_does_not_repeat_effects()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeDiscount(run); var commit = new DiscountCommitProbe();
        await using (var safe = SafeDiscountScope(run, commit))
        {
            commit.AfterCommit = () => safe.Authority.Granted = false;
            var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata("release")));
            Assert.Equal(403, denied.Status); Assert.Equal(1, commit.Commits); Assert.Empty(safe.Db.ChangeTracker.Entries());
            Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
        }
        Assert.Equal(1, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(1, await DiscountAuditCount(run, draft.Id));
        await using var retry = SafeDiscountScope(run);
        var replay = await retry.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata("release", "SENTETIK-after-release"));
        Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-discount-original", replay.OriginalCorrelationId);
        Assert.Equal(1, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(1, await DiscountAuditCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Owned_discount_preserves_initial_caller_work_even_if_first_authority_await_cleans_it()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeDiscount(run);
        foreach (var fault in new[] { "dirty", "audit", "borrowed", "dirty-clean", "audit-clean", "borrowed-clean", "late-dirty", "late-audit", "ambient" })
        {
            using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            await using var safe = SafeDiscountScope(run); var caller = new DocumentCounterRow { CompanyId = CompanyA, Series = "SENTETIK-DS", Next = 1, Version = 1, UpdatedAtUtc = T0 };
            await using var borrowed = fault.StartsWith("borrowed", StringComparison.Ordinal) ? await safe.Db.Database.BeginTransactionAsync() : null;
            if (fault.StartsWith("dirty", StringComparison.Ordinal)) safe.Db.DocumentCounters.Add(caller);
            if (fault.StartsWith("audit", StringComparison.Ordinal)) safe.Collector.Enqueue(DiscountCallerAudit());
            var cleaned = false; var lateInjected = false;
            safe.Authority.AfterRead = async _ =>
            {
                if (fault.StartsWith("late-", StringComparison.Ordinal) && safe.Authority.Reads == 2)
                {
                    if (fault == "late-dirty") safe.Db.DocumentCounters.Add(caller);
                    else safe.Collector.Enqueue(DiscountCallerAudit());
                    lateInjected = true;
                }
                if (!fault.EndsWith("-clean", StringComparison.Ordinal) || cleaned) return;
                if (fault.StartsWith("dirty", StringComparison.Ordinal)) safe.Db.ChangeTracker.Clear();
                if (fault.StartsWith("audit", StringComparison.Ordinal)) safe.Writer.DiscardPending();
                if (borrowed is not null) { await borrowed.RollbackAsync(); await borrowed.DisposeAsync(); }
                cleaned = true;
            };
            var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata(fault)));
            Assert.Equal(503, failure.Status); Assert.Equal(fault.EndsWith("-clean", StringComparison.Ordinal), cleaned);
            Assert.Equal(fault == "audit-clean" ? 1 : 0, safe.Writer.Discards);
            Assert.Equal(fault.StartsWith("late-", StringComparison.Ordinal), lateInjected);
            if (fault is "dirty" or "late-dirty") Assert.Same(caller, Assert.Single(safe.Db.ChangeTracker.Entries()).Entity);
            if (fault is "audit" or "late-audit") Assert.True(safe.Writer.HasPendingDrafts);
            if (fault == "borrowed") Assert.Same(borrowed, safe.Db.Database.CurrentTransaction);
            if (ambient is not null) Assert.NotNull(System.Transactions.Transaction.Current);
            // Only the caller releases its own retained work after the preservation assertions.
            if (borrowed is not null && !cleaned) await borrowed.RollbackAsync();
            safe.Db.ChangeTracker.Clear(); safe.Writer.DiscardPending();
        }
        Assert.Equal(0, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(0, await DiscountAuditCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Owned_discount_restore_read_authority_loss_stops_before_the_next_child_query()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeDiscount(run); var reads = new DiscountReadProbe();
        await using var safe = SafeDiscountScope(run, reads);
        safe.Authority.AfterRead = _ => { if (reads.MarginReads > 0) safe.Authority.Granted = false; return Task.CompletedTask; };
        var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata("read-stop")));
        Assert.Equal(403, denied.Status); Assert.Equal(1, reads.MarginReads); Assert.Equal(0, reads.KilogramReads);
        Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
        Assert.Equal(0, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(0, await DiscountAuditCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Owned_discount_saved_domain_fault_rolls_back_without_a_false_terminal_receipt_then_key_retries_once()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeDiscount(run);
        var probe = new DiscountSaveProbe { AfterSaved = () => throw new SalesRuleException(SalesRuleCodes.StateConflict, "SENTETIK accepted-save domain fault") };
        await using (var safe = SafeDiscountScope(run, probe))
        {
            var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata("saved-fault")));
            Assert.Equal(503, failure.Status); Assert.True(probe.Saved); Assert.Equal(1, probe.Saves);
            Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
        }
        Assert.Equal(0, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(0, await DiscountAuditCount(run, draft.Id));
        await using var retry = SafeDiscountScope(run);
        Assert.False((await retry.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata("saved-fault"))).IsReplay);
        Assert.Equal(1, await DiscountDecisionCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Controlled_discount_provider_races_abort_once_then_read_only_the_original_key_or_refuse_without_losing_receipts()
    {
        await using var run = await Run.StartAsync();
        foreach (var (number, providerCompleted) in new[] { (0, false), (1205, false), (1205, true), (1222, false), (2601, false), (2627, false) })
        foreach (var winner in new[] { false, true })
        {
            var draft = await SeedSafeDiscount(run); var key = "SENTETIK-race-" + draft.Id.ToString("N");
            var probe = new DiscountSaveProbe { BeforeSave = () => throw (number == 0 ? new DbUpdateConcurrencyException("SENTETIK controlled EF concurrency") : new DbUpdateException("SENTETIK controlled provider failure", SqlConcurrencyWrapperPolicyTests.SqlFailure(number))) };
            await using (var safe = SafeDiscountScope(run, probe))
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
                    await using var winning = SafeDiscountScope(run);
                    var result = await winning.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata(key, "SENTETIK-original-winner"));
                    Assert.False(result.IsReplay);
                };
                if (winner)
                {
                    var replay = await safe.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata(key));
                    Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-original-winner", replay.OriginalCorrelationId);
                }
                else
                {
                    var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata(key)));
                    Assert.Equal(number is 0 or 2601 or 2627 ? 409 : 503, failure.Status);
                }
                Assert.Equal(1, probe.Saves); Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
                Assert.Null(safe.Db.Database.CurrentTransaction);
            }
            Assert.Equal(winner ? 1 : 0, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(winner ? 1 : 0, await DiscountAuditCount(run, draft.Id));
        }
        // Controlled failures plus real rollback/original-key reads; not evidence of a real simultaneous SQL deadlock.
    }

    [SqlServerFact]
    public async Task Owned_discount_rollback_failure_never_returns_a_readonly_winner_or_a_native_validation_answer()
    {
        await using var run = await Run.StartAsync();
        foreach (var phase in new[] { "rollback", "dispose" })
        {
            var draft = await SeedSafeDiscount(run); var rollback = new DiscountRollbackFault(); var disposal = new DiscountDisposeFault();
            await using var safe = SafeDiscountScope(run, phase == "rollback" ? rollback : disposal);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id,
                SafeDiscountRequest(draft) with { Lines = [new(Assert.Single(draft.Lines).Id, "")] }, SafeDiscountMetadata("cleanup-fault-" + phase)));
            Assert.Contains(failure.InnerExceptions, x => x is InvalidOperationException);
            if (phase == "rollback") Assert.Equal(1, rollback.Rollbacks); else Assert.True(disposal.Failed);
            Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
            Assert.Equal(0, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(0, await DiscountAuditCount(run, draft.Id));
        }
    }

    [SqlServerFact]
    public async Task Owned_discount_terminal_version_refusal_is_atomic_original_and_does_not_change_the_commercial_snapshot()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeDiscount(run);
        var stale = SafeDiscountRequest(draft) with { ExpectedVersion = "1" };
        await using (var safe = SafeDiscountScope(run))
        {
            var first = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id, stale, SafeDiscountMetadata("terminal")));
            Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (first.Status, first.Code)); Assert.False(first.IsReplay);
            var replay = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id, stale, SafeDiscountMetadata("terminal", "SENTETIK-later")));
            Assert.Equal((first.Status, first.Code, true), (replay.Status, replay.Code, replay.IsReplay));
            Assert.Equal(JsonSerializer.Serialize(first.Errors), JsonSerializer.Serialize(replay.Errors));
            Assert.Equal("SENTETIK-discount-original", replay.OriginalCorrelationId);
            Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
        }
        Assert.Equal(draft.Version, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
        Assert.Equal(1, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(0, await DiscountAuditCount(run, draft.Id));
        await using var valid = SafeDiscountScope(run);
        Assert.False((await valid.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata("after-terminal"))).IsReplay);
        Assert.Equal(2, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(1, await DiscountAuditCount(run, draft.Id));
    }

    [SqlServerFact]
    public async Task Owned_discount_post_save_actor_company_membership_and_completed_cancellation_losses_abort_every_effect()
    {
        await using var run = await Run.StartAsync();
        foreach (var fault in new[] { "actor", "company", "membership", "cancel" })
        {
            var draft = await SeedSafeDiscount(run); var save = new DiscountSaveProbe(); using var cancelled = new CancellationTokenSource();
            await using var safe = SafeDiscountScope(run, save); var fired = false;
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
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata("context-" + fault), cancelled.Token));
            else
            {
                var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata("context-" + fault)));
                Assert.Equal(403, denied.Status);
            }
            Assert.True(fired); Assert.True(save.Saved); Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
            Assert.Equal(0, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(0, await DiscountAuditCount(run, draft.Id));
            Assert.Equal(draft.Version, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
        }
    }

    [SqlServerFact]
    public async Task Owned_discount_failed_reason_cleanup_revalidates_after_actual_rollback_or_dispose()
    {
        await using var run = await Run.StartAsync();
        foreach (var phase in new[] { "rollback", "dispose" })
        foreach (var change in new[] { "unchanged", "permission", "membership", "cancel" })
        {
            var draft = await SeedSafeDiscount(run); var probe = new DiscountCleanupReleaseProbe(phase); var save = new DiscountSaveProbe();
            using var cancelled = new CancellationTokenSource();
            await using (var safe = SafeDiscountScope(run, probe, extraInterceptor: save))
            {
                probe.AfterCleanup = () =>
                {
                    if (change == "permission") safe.Authority.Granted = false;
                    if (change == "membership") safe.User.Membership = false;
                    if (change == "cancel") cancelled.Cancel();
                };
                var invalid = SafeDiscountRequest(draft) with { Lines = [new(Assert.Single(draft.Lines).Id, "  ")] };
                Task<SalesOrderDraftResponse> Decide() => safe.Service.ApproveDiscountExceptionsAsync(draft.Id, invalid, SafeDiscountMetadata("cleanup-" + draft.Id.ToString("N")), cancelled.Token);
                if (change == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(Decide);
                else
                {
                    var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(Decide);
                    Assert.Equal(change == "unchanged" ? 422 : 403, refused.Status);
                    if (change == "unchanged") Assert.Equal(SalesRuleCodes.DiscountExceptionReasonRequired, Assert.Single(refused.Errors).Code);
                }
                Assert.Equal(1, probe.Observed); Assert.Equal(0, save.Saves);
                Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
            }
            Assert.Equal(0, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(0, await DiscountAuditCount(run, draft.Id));
            await using var retry = SafeDiscountScope(run);
            Assert.False((await retry.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata("cleanup-" + draft.Id.ToString("N")))).IsReplay);
            Assert.Equal(1, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(1, await DiscountAuditCount(run, draft.Id));
        }
    }

    [SqlServerFact]
    public async Task Owned_discount_final_price_measurement_hides_prices_revoked_by_the_last_approval_fence()
    {
        await using var run = await Run.StartAsync(); var draft = await SeedSafeDiscount(run); var commit = new DiscountCommitProbe();
        await using var safe = SafeDiscountScope(run, commit); var priceReads = 0; var fired = false;
        safe.Authority.AfterRead = permission =>
        {
            if (permission == SalesPermissionManifestV1.OrdersPriceRead) priceReads++;
            // Native response PriceRead=1, postcommit ReplayDisclosure PriceRead=2, then final approval fence.
            if (commit.Commits == 1 && priceReads == 2 && permission == SalesPermissionManifestV1.OrdersDiscountExceptionApprove)
            { safe.Authority.PriceGranted = false; fired = true; }
            return Task.CompletedTask;
        };
        var response = await safe.Service.ApproveDiscountExceptionsAsync(draft.Id, SafeDiscountRequest(draft), SafeDiscountMetadata("final-price"));
        Assert.True(fired); Assert.Equal(3, priceReads); Assert.False(response.IsReplay);
        Assert.All(response.Detail.Lines, line => { Assert.Equal(SalesPriceDisclosure.Withheld, line.PriceDisclosure); Assert.Null(line.UnitPrice); Assert.Null(line.PriceUnitId); Assert.Null(line.DiscountPercent); });
        Assert.Equal(1, await DiscountDecisionCount(run, draft.Id)); Assert.Equal(1, await DiscountAuditCount(run, draft.Id));
        Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
    }

    private static async Task<SalesOrderDraft> SeedSafeDiscount(Run run, bool listed = false)
    {
        var key = Guid.NewGuid().ToString("N");
        var draft = await run.StoredDraftAsync(CompanyA, key, discountPercent: "15");
        if (!listed) return draft; // SAT-D45: native list-less price writer may approve their own discount.
        await using var raw = run.SalesStore(CompanyA); var line = Assert.Single(draft.Lines);
        var reference = new PriceReferenceSnapshot(Guid.NewGuid(), "SENTETIK liste", 1, Day.AddDays(-1), Day.AddDays(30),
            "TRY", KgUnit, "100", null, "ExcludingVat", "0", null);
        var revised = await raw.Store.ReviseDraftCommercialAsync(run.SalesEnvelope(key + "-list"), Hash(key + "-list"), SalesDraftCommands.ReviseCommercialV1,
            draft.Id, (actual, now) => actual.ReviseCommercial(actual.Version,
                [new LineCommercialInput(line.Id, new CommercialDraftFields("100", KgUnit, "15", reference))], run.Actor, now,
                SalesDiscountLimit.Defined(10m, MoneyProfile, 1)),
            new SalesDraftStateAudit("sales.order-draft.commercial-revised.v1", "ReviseCommercial", "SENTETIK liste kontrolü"), _ => Task.CompletedTask);
        return revised!.Draft;
    }
    private static SalesDraftDiscountExceptionApprovalRequest SafeDiscountRequest(SalesOrderDraft draft) => new(draft.Version.ToString(CultureInfo.InvariantCulture),
        draft.Lines.Where(x => x.Commercial.DiscountCheck is { RequiresApproval: true, IsApproved: false })
            .Select(x => new SalesDraftDiscountExceptionApprovalLineV1(x.Id, "  SENTETIK gerekçe  ")).ToArray());
    private static SalesCommandMetadata SafeDiscountMetadata(string key, string correlation = "SENTETIK-discount-original") => new(key, correlation, null, T0);
    private static Task<int> DiscountDecisionCount(Run run, Guid id) => run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id AND CommandName=@cmd", ("@id", id), ("@cmd", SalesDraftCommands.ApproveDiscountExceptionsV1));
    private static Task<int> DiscountAuditCount(Run run, Guid id) => run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'sales.order-draft.discount-exceptions-approved.v1'", ("@id", id.ToString("D")));

    private static DiscountScope SafeDiscountScope(Run run, IInterceptor? interceptor = null, string actor = DiscountApprover, IInterceptor? extraInterceptor = null)
    {
        using var template = run.SalesContext(CompanyA);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = template.Database.GetConnectionString(), ["Audit:RefusedAttempts:Retention:Period"] = "P2Y",
            ["Audit:RefusedAttempts:Retention:PolicyRef"] = "policy-ref-1", ["Audit:RefusedAttempts:Retention:AutoDisposalEnabled"] = "false",
            ["Audit:RefusedAttempts:StandaloneWrite:Timeout"] = "00:00:02",
        }).Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddScoped<ICompanyContext>(_ => new DiscountCompany()); services.AddAuditModule(config);
        var provider = services.BuildServiceProvider(validateScopes: true); var scope = provider.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = scope.ServiceProvider.GetRequiredService<AuditEventCollector>(); Assert.Same(collector, scope.ServiceProvider.GetRequiredService<IAuditEventCollector>());
        var writer = new DiscountWriter(scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        var builder = new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(template.Database.GetConnectionString(), sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        if (extraInterceptor is not null) builder.AddInterceptors(extraInterceptor);
        if (interceptor is DiscountCleanupReleaseProbe cleanup) cleanup.Configure(builder);
        if (interceptor is DiscountDisposeFault disposal) disposal.Configure(builder);
        var db = new SalesDbContext(builder.Options, company); var store = new SalesStore(db, company, collector, TimeProvider.System, officialCancellationAuditWriter: writer);
        var authority = new DiscountAuthority(); var user = new DiscountUser(actor);
        // Approval consumes only the already-recorded native commercial facts; no new Cari/Product/Settings lookup is allowed.
        var service = new SalesDraftService(company, user, authority, new DiscountRoles(), new DiscountCorrelation(), null!, null!, null, store, new SalesDraftServiceBindings(), TimeProvider.System)
            .WithDiscountApprovalSafety();
        return new(provider, scope, db, service, authority, writer, collector, company, user);
    }
    private sealed class DiscountScope(ServiceProvider provider, AsyncServiceScope scope, SalesDbContext db, SalesDraftService service,
        DiscountAuthority authority, DiscountWriter writer, AuditEventCollector collector, ICompanyContext company, DiscountUser user) : IAsyncDisposable
    {
        internal ICompanyContext Company => company; internal DiscountUser User => user;
        internal SalesDbContext Db => db; internal SalesDraftService Service => service; internal DiscountAuthority Authority => authority;
        internal DiscountWriter Writer => writer; internal AuditEventCollector Collector => collector;
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await scope.DisposeAsync(); await provider.DisposeAsync(); }
    }
    private sealed class DiscountAuthority : IErpAuthorityRevalidationService
    {
        internal bool Granted = true; internal bool PriceGranted = true; internal int Reads; internal Func<string, Task>? AfterRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(CompanyA, companyId); Reads++; await Task.Yield(); if (AfterRead is not null) await AfterRead(permission);
            return new(Granted && (permission != SalesPermissionManifestV1.OrdersPriceRead || PriceGranted), true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }
    }
    // Controlled context drift only; production CompanyContext remains immutable.
    private sealed class DiscountCompany : ICompanyContext
    {
        public bool IsResolved { get; private set; }
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId) { CompanyId = companyId; IsResolved = true; }
    }
    private sealed class DiscountUser(string actor) : ICurrentUser
    {
        internal string Actor = actor; internal bool Membership = true;
        public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => Membership ? new HashSet<Guid> { CompanyA } : new HashSet<Guid>();
        public bool CanAccessCompany(Guid companyId) => Membership && companyId == CompanyA;
    }
    private sealed class DiscountRoles : IActiveRoleCodeReader
    { public Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>()); }
    private sealed class DiscountCorrelation : ICorrelationContext
    {
        public bool IsResolved => true; public string CorrelationId => "SENTETIK-discount-original"; public string? CausationId => null;
        public string SourceChannel => "test"; public string? ClientIpAddress => null; public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) => throw new NotSupportedException();
    }
    private sealed class DiscountWriter(IAuditTransactionWriterV1 inner) : IAuditTransactionWriterV1
    {
        internal int Discards;
        public bool HasPendingDrafts => inner.HasPendingDrafts;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default) => inner.WritePendingAsync(transaction, cancellationToken);
        public void DiscardPending() { Discards++; inner.DiscardPending(); }
    }
    private sealed class DiscountSaveProbe : SaveChangesInterceptor
    {
        internal int Saves; internal bool Saved; internal Action? BeforeSave; internal Action? AfterSaved;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Saves++; BeforeSave?.Invoke(); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { Saved = true; AfterSaved?.Invoke(); return ValueTask.FromResult(result); }
    }
    private sealed class DiscountCommitProbe : DbTransactionInterceptor
    {
        internal int Commits; internal Action? AfterCommit;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; }
    }
    private sealed class DiscountReadProbe : DbCommandInterceptor
    {
        internal int MarginReads; internal int KilogramReads;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DraftMarginCosts", StringComparison.Ordinal)) MarginReads++;
            if (command.CommandText.Contains("DraftLineKilogramsPerUnit", StringComparison.Ordinal)) KilogramReads++;
            return ValueTask.FromResult(result);
        }
    }
    private sealed class DiscountCleanupReleaseProbe(string phase) : DbTransactionInterceptor
    {
        internal int Observed; internal Action? AfterCleanup;
        internal void Configure(DbContextOptionsBuilder<SalesDbContext> options) => options.LogTo(
            (id, _) => id == RelationalEventId.TransactionDisposed, _ => { if (phase == "dispose") Changed(); });
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { if (phase == "rollback") Changed(); return Task.CompletedTask; }
        private void Changed() { if (Observed++ == 0) AfterCleanup?.Invoke(); }
    }
    private sealed class DiscountDisposeFault : DbTransactionInterceptor
    {
        internal bool Failed;
        internal void Configure(DbContextOptionsBuilder<SalesDbContext> options) => options.LogTo(
            (id, _) => id == RelationalEventId.TransactionDisposed, _ => { if (!Failed) { Failed = true; throw new InvalidOperationException("SENTETIK discount disposal fault"); } });
    }
    private sealed class DiscountRollbackFault : DbTransactionInterceptor
    {
        internal int Rollbacks;
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        { Rollbacks++; throw new InvalidOperationException("SENTETIK rollback failure"); }
    }
    private static AuditEventDraft DiscountCallerAudit() => new(CompanyA, "SENTETIK.caller.v1", "Sales.OrderDraft", "SENTETIK", "Caller", null, null,
        AuditActorTypes.User, DiscountApprover, SalesPermissionManifestV1.OrdersDiscountExceptionApprove, [], "{}", "{}", "SENTETIK caller", null,
        AuditAiInvolvement.None, null, "test", null, null, "SENTETIK-discount-original", null, "SENTETIK-caller-key");
}

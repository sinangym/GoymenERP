using System.Data;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesDraftApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task Native_order_approve_refuses_at_prestage_without_attempt_or_audit() => DecisionFenceRollbackAsync(true, false);
    [SqlServerFact]
    public Task Native_order_reject_refuses_at_prestage_without_attempt_or_audit() => DecisionFenceRollbackAsync(false, false);
    [SqlServerFact]
    public Task Native_order_approve_refuses_after_real_Save_and_same_key_retries_once() => DecisionFenceRollbackAsync(true, true);
    [SqlServerFact]
    public Task Native_order_reject_refuses_after_real_Save_and_same_key_retries_once() => DecisionFenceRollbackAsync(false, true);

    private static async Task DecisionFenceRollbackAsync(bool approve, bool postSave)
    {
        await using var run = await Run.StartAsync();
        var id = await PrepareDecisionPendingAsync(run);
        var command = approve ? SalesDraftCommands.ApproveV1 : SalesDraftCommands.RejectV1;
        var audit = approve ? Approved : Rejected;
        var key = "native-decision-fence";
        var revoked = false; var saved = new DecisionFenceSave(() => revoked = true);
        var calls = 0;
        // Synthetic authority revocation timing; real SQL transition, audit, attempt, Save and transaction rollback.
        await using (var scope = run.Store(CompanyA, saved))
        {
            var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => scope.Store.UpdateDraftDecisionWithFenceAsync(
                run.Envelope(key), Hash(key), command, id, Transition, audit,
                (_, _) =>
                {
                    calls++;
                    if (postSave ? revoked : calls == 2) throw new SalesDraftServiceException(403, "forbidden");
                    return Task.CompletedTask;
                }));
            Assert.Equal(403, error.Status);
            Assert.Equal(postSave ? 1 : 0, saved.Calls);
            // Same context also must not expose the accepted EF snapshot after the SQL transaction rolled back.
            Assert.Equal((3L, InternalApprovalState.PendingApproval),
                ((await scope.Store.FindDraftAsync(id))!.Version, (await scope.Store.FindDraftAsync(id))!.ApprovalState));
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id AND CommandName=@command", ("@id", id), ("@command", command)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@event", ("@id", id.ToString("D")), ("@event", audit.EventType)));
        await using (var retry = run.Store(CompanyA))
        {
            var result = (await retry.Store.UpdateDraftDecisionWithFenceAsync(run.Envelope(key), Hash(key), command, id,
                Transition, audit, (_, _) => Task.CompletedTask))!;
            Assert.Equal((4L, approve ? InternalApprovalState.Approved : InternalApprovalState.Rejected), (result.Draft.Version, result.Draft.ApprovalState));
            var replay = (await retry.Store.UpdateDraftDecisionWithFenceAsync(run.Envelope(key), Hash(key), command, id,
                (_, _) => throw new InvalidOperationException("replay must not transition"), audit, (_, _) => Task.CompletedTask))!;
            Assert.Equal(SalesDraftWriteOutcome.Replayed, replay.Outcome);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id AND CommandName=@command", ("@id", id), ("@command", command)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@event", ("@id", id.ToString("D")), ("@event", audit.EventType)));
        void Transition(SalesOrderDraft draft, DateTimeOffset now)
        {
            if (approve) draft.Approve(3, "SENTETIK-independent", now);
            else draft.Reject(3, "SENTETIK-independent", "SENTETIK rejection", now);
        }
    }

    [SqlServerFact]
    public async Task Guarded_order_approval_keeps_the_existing_owner_participant_in_one_transaction_and_replay_calls_it_once()
    {
        await using var run = await Run.StartAsync();
        var id = await PrepareDecisionPendingAsync(run);
        Guid marker;
        await using (var create = run.Store(CompanyA))
            marker = (await create.Store.StoreDraftAsync(run.Envelope("decision-marker"), Hash("decision-marker"),
                number => Draft(Owners.For(CompanyA), number, T0))).Draft.Id;
        var ownerCalls = 0; var revoked = false;
        Func<SalesOrderDraft, int?, IDbTransaction, CancellationToken, Task> participant = async (_, _, transaction, token) =>
        {
            ownerCalls++;
            await StageFutureDemandProbeAsync(transaction, marker, token);
        };
        await using (var scope = run.Store(CompanyA, new DecisionFenceSave(() => revoked = true)))
            await Assert.ThrowsAsync<SalesDraftServiceException>(() => scope.Store.UpdateDraftDecisionWithFenceAsync(
                run.Envelope("decision-participant"), Hash("decision-participant"), SalesDraftCommands.ApproveV1, id,
                (draft, now) => draft.Approve(3, "SENTETIK-independent", now), Approved,
                (_, _) => revoked ? throw new SalesDraftServiceException(403, "forbidden") : Task.CompletedTask, participant));
        Assert.Equal(1, ownerCalls);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE Id=@id AND UpdatedAtUtc=@at", ("@id", marker), ("@at", T0.AddDays(20))));
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", id)));
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.UpdateDraftDecisionWithFenceAsync(run.Envelope("decision-participant"), Hash("decision-participant"), SalesDraftCommands.ApproveV1, id,
                (draft, now) => draft.Approve(3, "SENTETIK-independent", now), Approved, (_, _) => Task.CompletedTask, participant);
            await scope.Store.UpdateDraftDecisionWithFenceAsync(run.Envelope("decision-participant"), Hash("decision-participant"), SalesDraftCommands.ApproveV1, id,
                (_, _) => throw new InvalidOperationException("replay"), Approved, (_, _) => Task.CompletedTask,
                (_, _, _, _) => throw new InvalidOperationException("replay cannot repeat owner release"));
        }
        Assert.Equal(2, ownerCalls); // One attempted participant per transaction, none on replay.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE Id=@id AND UpdatedAtUtc=@at", ("@id", marker), ("@at", T0.AddDays(20))));
    }

    [SqlServerFact]
    public Task Native_order_service_approve_rechecks_real_application_authority_after_SQL_Save() => ApplicationDecisionFenceAsync(true);
    [SqlServerFact]
    public Task Native_order_service_reject_rechecks_real_application_authority_after_SQL_Save() => ApplicationDecisionFenceAsync(false);

    private static async Task ApplicationDecisionFenceAsync(bool approve)
    {
        await using var run = await Run.StartAsync();
        var id = await PrepareDecisionPendingAsync(run);
        var grants = new[] { SalesPermissionManifestV1.OrdersApprove, SalesPermissionManifestV1.DraftsRead };
        var authority = new RecordingAuthority(grants); // Existing synthetic authority source; the real application and native SQL store run.
        var saved = new DecisionFenceSave(() => Array.Fill(grants, "SENTETIK-revoked"));
        await using (var scope = run.Store(CompanyA, saved))
        {
            var service = run.Service(scope, "SENTETIK-independent", authority);
            var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => approve
                ? service.ApproveDraftAsync(id, new("3"), run.Metadata("decision-app-fresh"))
                : service.RejectDraftAsync(id, new("3", "SENTETIK rejection"), run.Metadata("decision-app-fresh")));
            Assert.Equal(403, error.Status);
            Assert.Equal(1, saved.Calls);
            var unchanged = (await scope.Store.FindDraftAsync(id))!;
            Assert.Equal((3L, InternalApprovalState.PendingApproval), (unchanged.Version, unchanged.ApprovalState));
        }
        var command = approve ? SalesDraftCommands.ApproveV1 : SalesDraftCommands.RejectV1;
        var audit = approve ? Approved : Rejected;
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id AND CommandName=@command", ("@id", id), ("@command", command)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@event", ("@id", id.ToString("D")), ("@event", audit.EventType)));
        Assert.All(authority.Measured, x => Assert.Equal(CompanyA, x.Company));
    }

    [SqlServerFact]
    public async Task Guarded_order_decision_refuses_ambient_transaction_for_new_key_and_replay_before_any_callback()
    {
        await using var run = await Run.StartAsync();
        var id = await PrepareRecordedGuardDecisionAsync(run);
        await using var context = run.Context(CompanyA);
        var tracked = await context.Drafts.SingleAsync(x => x.Id == id);
        var entries = context.ChangeTracker.Entries().Count();
        var store = GuardStore(context);
        var callback = 0;
        using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            foreach (var key in new[] { "guard-new-key", "guard-recorded-key" })
                await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateDraftDecisionWithFenceAsync(run.Envelope(key), Hash(key),
                    SalesDraftCommands.ApproveV1, id, (_, _) => callback++, Approved, (_, _) => { callback++; return Task.CompletedTask; }));
            Assert.Equal(System.Transactions.TransactionStatus.Active, System.Transactions.Transaction.Current!.TransactionInformation.Status);
            Assert.Null(context.Database.CurrentTransaction);
        }
        Assert.Equal(0, callback);
        Assert.Equal(entries, context.ChangeTracker.Entries().Count());
        Assert.Same(tracked, context.ChangeTracker.Entries().Single().Entity);
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id AND CommandName=@command", ("@id", id), ("@command", SalesDraftCommands.ApproveV1)));
    }

    [SqlServerFact]
    public async Task Guarded_order_decision_preserves_borrowed_transaction_and_tracked_state_for_new_key_and_replay()
    {
        await using var run = await Run.StartAsync();
        var id = await PrepareRecordedGuardDecisionAsync(run);
        await using var context = run.Context(CompanyA);
        var tracked = await context.Drafts.SingleAsync(x => x.Id == id);
        await using var borrowed = await context.Database.BeginTransactionAsync();
        var store = GuardStore(context); var callback = 0;
        foreach (var key in new[] { "guard-new-key", "guard-recorded-key" })
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateDraftDecisionWithFenceAsync(run.Envelope(key), Hash(key),
                SalesDraftCommands.ApproveV1, id, (_, _) => callback++, Approved, (_, _) => { callback++; return Task.CompletedTask; }));
        Assert.Equal(0, callback);
        Assert.Same(borrowed, context.Database.CurrentTransaction);
        Assert.Same(tracked, context.ChangeTracker.Entries().Single().Entity);
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Equal(4L, await context.Drafts.AsNoTracking().Where(x => x.Id == id).Select(x => x.Version).SingleAsync());
        await borrowed.RollbackAsync(); // The caller still owns and can use/dispose its transaction.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id AND CommandName=@command", ("@id", id), ("@command", SalesDraftCommands.ApproveV1)));
    }

    [SqlServerFact]
    public async Task Guarded_order_decision_preserves_dirty_caller_values_for_new_key_and_recorded_replay()
    {
        await using var run = await Run.StartAsync();
        var id = await PrepareRecordedGuardDecisionAsync(run);
        await using var context = run.Context(CompanyA);
        var tracked = await context.Drafts.SingleAsync(x => x.Id == id);
        var original = tracked.UpdatedAtUtc;
        tracked.UpdatedAtUtc = original.AddMinutes(1); // Unrelated caller work; never saved by this test.
        Assert.True(context.ChangeTracker.HasChanges());
        var store = GuardStore(context); var callback = 0;
        foreach (var key in new[] { "guard-new-key", "guard-recorded-key" })
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateDraftDecisionWithFenceAsync(run.Envelope(key), Hash(key),
                SalesDraftCommands.ApproveV1, id, (_, _) => callback++, Approved, (_, _) => { callback++; return Task.CompletedTask; }));
        Assert.Equal(0, callback);
        Assert.Same(tracked, context.ChangeTracker.Entries().Single().Entity);
        Assert.Equal(EntityState.Modified, context.Entry(tracked).State);
        Assert.Equal(original.AddMinutes(1), tracked.UpdatedAtUtc);
        Assert.Equal(original, context.Entry(tracked).Property(x => x.UpdatedAtUtc).OriginalValue);
        Assert.Equal(original, await run.ScalarAsync<DateTimeOffset>("SELECT UpdatedAtUtc FROM sales.Drafts WHERE Id=@id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id AND CommandName=@command", ("@id", id), ("@command", SalesDraftCommands.ApproveV1)));
    }

    [SqlServerFact]
    public async Task Guarded_order_decision_preserves_pending_caller_audit_for_new_key_and_recorded_replay()
    {
        await using var run = await Run.StartAsync();
        var id = await PrepareRecordedGuardDecisionAsync(run);
        await using var context = run.Context(CompanyA);
        var tracked = await context.Drafts.SingleAsync(x => x.Id == id);
        await using var ownerScope = run.Factory.Services.CreateAsyncScope();
        var company = ownerScope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = ownerScope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
        var writer = ownerScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        collector.Enqueue(new AuditEventDraft(CompanyA, "sales.synthetic-caller.v1", "Sales.SyntheticCaller", id.ToString("D"),
            "prepared", null, 1, AuditActorTypes.User, run.Actor, SalesPermissionManifestV1.OrdersApprove,
            [], "{}", "{}", "SENTETIK caller-owned audit", null, AuditAiInvolvement.None, null,
            "Web", null, null, run.Correlation, null, "guard-caller-audit"));
        Assert.True(writer.HasPendingDrafts);
        var store = new SalesStore(context, company, collector, TimeProvider.System, officialCancellationAuditWriter: writer);
        var callback = 0;
        foreach (var key in new[] { "guard-new-key", "guard-recorded-key" })
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateDraftDecisionWithFenceAsync(run.Envelope(key), Hash(key),
                SalesDraftCommands.ApproveV1, id, (_, _) => callback++, Approved, (_, _) => { callback++; return Task.CompletedTask; }));
        Assert.Equal(0, callback);
        Assert.True(writer.HasPendingDrafts);
        Assert.Same(tracked, context.ChangeTracker.Entries().Single().Entity);
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'sales.synthetic-caller.v1'", ("@id", id.ToString("D"))));
        writer.DiscardPending(); // The caller owns its own pending audit, not the refused command.
    }

    private static SalesStore GuardStore(SalesDbContext context)
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        // Guard-only context: no audit interceptor is needed because refusal must precede every callback/read/write.
        return new(context, company, new AuditEventCollector(), TimeProvider.System);
    }

    private static async Task<Guid> PrepareRecordedGuardDecisionAsync(Run run)
    {
        var id = await PrepareDecisionPendingAsync(run);
        await using var scope = run.Store(CompanyA);
        await scope.Store.UpdateDraftDecisionWithFenceAsync(run.Envelope("guard-recorded-key"), Hash("guard-recorded-key"),
            SalesDraftCommands.ApproveV1, id, (draft, now) => draft.Approve(3, "SENTETIK-independent", now), Approved,
            (_, _) => Task.CompletedTask);
        return id;
    }

    private static async Task<Guid> PrepareDecisionPendingAsync(Run run)
    {
        await using var scope = run.Store(CompanyA);
        var draft = (await scope.Store.StoreDraftAsync(run.Envelope("decision-create"), Hash("decision-create"),
            number => Draft(Owners.For(CompanyA), number, T0))).Draft;
        await scope.Store.UpdateDraftStateAsync(run.Envelope("decision-submit"), Hash("decision-submit"), SalesDraftCommands.SubmitV1,
            draft.Id, (value, now) => value.Submit(2, run.Actor, now), Submitted);
        return draft.Id;
    }

    private sealed class DecisionFenceSave(Action saved) : SaveChangesInterceptor
    {
        public int Calls { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Calls++; saved(); return ValueTask.FromResult(result);
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using GoymenERP.BuildingBlocks.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    // Real native store/context/audit/command receipt; the authority callback is deliberately controlled.
    // This does not claim a hosted Work/UI/delegation end-to-end acceptance.
    [SqlServerFact]
    public async Task Plan_rejection_post_save_revocation_rolls_back_business_audit_and_receipt_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var plan = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var seed = run.Store(CompanyA)) await seed.Plans.SavePlanAsync(plan, Metadata(run));
        var probe = new RejectionSaveProbe();
        await using (var scope = run.Store(CompanyA, probe))
        {
            var refusal = await Assert.ThrowsAsync<ManufacturingStoreException>(() => scope.Lifecycle.ExecutePlanRejectionAsync(
                async token =>
                {
                    await scope.Lifecycle.RejectPlanAsync(plan.Id, 1, run.Approver, At, "Gerçek insan gerekçesi",
                        RejectionReceiptMetadata(run, "revoke"), token);
                    return RejectionOk();
                }, _ => probe.Saved
                    ? Task.FromException(new ManufacturingStoreException(ManufacturingStoreCodes.AuthorityDenied, "Revoked after save."))
                    : Task.CompletedTask));
            Assert.Equal(ManufacturingStoreCodes.AuthorityDenied, refusal.Code);
            Assert.Empty(scope.Context.ChangeTracker.Entries());
            Assert.Null(scope.Context.Database.CurrentTransaction);
        }
        await AssertRejectedAttemptAbsent(run, plan.Id);
        // A subsequent command in the same economic scope must not inherit an audit ghost or a terminal 409.
        await using var retry = run.Store(CompanyA);
        var answer = await retry.Lifecycle.ExecutePlanRejectionAsync(async token =>
        {
            await retry.Lifecycle.RejectPlanAsync(plan.Id, 1, run.Approver, At, "Gerçek insan gerekçesi",
                RejectionReceiptMetadata(run, "revoke"), token);
            return RejectionOk();
        }, _ => Task.CompletedTask);
        Assert.Equal(ManufacturingApiStatus.Ok, answer.Status);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanRejections WHERE PlanId=@p", ("@p", plan.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'manufacturing.plan.rejected.v1' AND EntityId=@p", ("@p", plan.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Plan_rejection_real_save_failure_does_not_commit_its_completed_receipt_or_audit()
    {
        await using var run = await Run.StartAsync();
        var plan = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var seed = run.Store(CompanyA)) await seed.Plans.SavePlanAsync(plan, Metadata(run));
        await using var scope = run.Store(CompanyA, new RejectionSaveProbe { Fail = true });
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Lifecycle.ExecutePlanRejectionAsync(async token =>
        {
            await scope.Lifecycle.RejectPlanAsync(plan.Id, 1, run.Approver, At, "Gerçek insan gerekçesi",
                RejectionReceiptMetadata(run, "failure"), token);
            return RejectionOk();
        }, _ => Task.CompletedTask));
        Assert.Empty(scope.Context.ChangeTracker.Entries());
        Assert.Null(scope.Context.Database.CurrentTransaction);
        await AssertRejectedAttemptAbsent(run, plan.Id);
    }

    [SqlServerFact]
    public async Task Plan_rejection_actual_receipt_replay_requires_fresh_authority_after_the_read()
    {
        await using var run = await Run.StartAsync();
        var plan = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var seed = run.Store(CompanyA))
        {
            await seed.Plans.SavePlanAsync(plan, Metadata(run));
            await seed.Lifecycle.ExecutePlanRejectionAsync(async token =>
            {
                await seed.Lifecycle.RejectPlanAsync(plan.Id, 1, run.Approver, At, "Gerçek insan gerekçesi",
                    RejectionReceiptMetadata(run, "replay"), token);
                return RejectionOk();
            }, _ => Task.CompletedTask);
        }
        await using var scope = run.Store(CompanyA);
        var read = false;
        await Assert.ThrowsAsync<ManufacturingStoreException>(() => scope.Lifecycle.ExecutePlanRejectionAsync(async token =>
        {
            Assert.NotNull(await scope.Corrections.FindCommandAsync(ManufacturingCommandNames.RejectPlan, RejectionHash("replay"), token));
            read = true;
            return RejectionOk() with { IsReplay = true };
        }, _ => read
            ? Task.FromException(new ManufacturingStoreException(ManufacturingStoreCodes.AuthorityDenied, "Replay revoked."))
            : Task.CompletedTask));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanRejections WHERE PlanId=@p", ("@p", plan.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands WHERE CommandName=@n", ("@n", ManufacturingCommandNames.RejectPlan)));
    }

    [SqlServerFact]
    public async Task Plan_rejection_ambient_entry_refusal_precedes_reads_and_preserves_the_caller_scope()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Store(CompanyA);
        using var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        var caller = Transaction.Current;
        var called = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Lifecycle.ExecutePlanRejectionAsync(
            _ => { called = true; return Task.FromResult(RejectionOk()); }, _ => { called = true; return Task.CompletedTask; }));
        Assert.False(called);
        Assert.Same(caller, Transaction.Current);
        Assert.Empty(scope.Context.ChangeTracker.Entries());
    }

    [SqlServerFact]
    public async Task Plan_rejection_borrowed_transaction_entry_refusal_preserves_caller_work_even_for_replay()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Store(CompanyA);
        await using var borrowed = await scope.Context.Database.BeginTransactionAsync();
        var called = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Lifecycle.ExecutePlanRejectionAsync(
            _ => { called = true; return Task.FromResult(RejectionOk() with { IsReplay = true }); }, _ => Task.CompletedTask));
        Assert.False(called);
        Assert.Same(borrowed, scope.Context.Database.CurrentTransaction);
        await borrowed.RollbackAsync();
    }

    [SqlServerFact]
    public async Task Plan_rejection_dirty_entry_refusal_keeps_the_callers_tracked_entity_and_modification()
    {
        await using var run = await Run.StartAsync();
        var plan = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var seed = run.Store(CompanyA)) await seed.Plans.SavePlanAsync(plan, Metadata(run));
        await using var scope = run.Store(CompanyA);
        var row = await scope.Context.Plans.SingleAsync(x => x.Id == plan.Id);
        row.UpdatedAtUtc = At.AddMinutes(5);
        var called = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Lifecycle.ExecutePlanRejectionAsync(
            _ => { called = true; return Task.FromResult(RejectionOk()); }, _ => Task.CompletedTask));
        Assert.False(called);
        Assert.Equal(EntityState.Modified, scope.Context.Entry(row).State);
        Assert.Equal(At.AddMinutes(5), row.UpdatedAtUtc);
    }

    [SqlServerFact]
    public async Task Plan_rejection_pending_audit_entry_refusal_preserves_actual_writer_collector_drafts()
    {
        await using var run = await Run.StartAsync();
        await using var services = run.Factory.Services.CreateAsyncScope();
        services.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var collector = services.ServiceProvider.GetRequiredService<AuditEventCollector>();
        var writer = services.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var owner = services.ServiceProvider.GetRequiredService<ManufacturingStore>();
        collector.Enqueue(RejectionCallerAudit(run));
        Assert.True(writer.HasPendingDrafts);
        var called = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecutePlanRejectionAsync(
            _ => { called = true; return Task.FromResult(RejectionOk()); }, _ => Task.CompletedTask));
        Assert.False(called);
        Assert.True(writer.HasPendingDrafts);
        writer.DiscardPending(); // The caller, not the refused owner attempt, cleans up its own test draft.
    }

    [SqlServerFact]
    public async Task Plan_rejection_missing_writer_returns_unavailable_without_consuming_actual_collector_drafts()
    {
        await using var run = await Run.StartAsync();
        await using var services = run.Factory.Services.CreateAsyncScope();
        var company = services.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = services.ServiceProvider.GetRequiredService<AuditEventCollector>();
        var actualWriter = services.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var context = services.ServiceProvider.GetRequiredService<ManufacturingDbContext>();
        collector.Enqueue(RejectionCallerAudit(run));
        var ownerWithoutWriter = new ManufacturingStore(context, company, collector, TimeProvider.System);
        var called = false;
        var result = await ownerWithoutWriter.ExecutePlanRejectionAsync(
            _ => { called = true; return Task.FromResult(RejectionOk()); }, _ => { called = true; return Task.CompletedTask; });
        Assert.Equal(ManufacturingApiStatus.Unavailable, result.Status);
        Assert.False(called);
        Assert.True(actualWriter.HasPendingDrafts);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
        actualWriter.DiscardPending();
    }

    [SqlServerFact]
    public async Task Plan_rejection_owned_pre_save_refusal_discards_real_DI_audit_drafts_and_allows_same_context_retry()
    {
        await using var run = await Run.StartAsync();
        var plan = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var seed = run.Store(CompanyA)) await seed.Plans.SavePlanAsync(plan, Metadata(run));
        await using var services = run.Factory.Services.CreateAsyncScope();
        services.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var owner = services.ServiceProvider.GetRequiredService<ManufacturingStore>();
        var context = services.ServiceProvider.GetRequiredService<ManufacturingDbContext>();
        var audit = services.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        async Task<ManufacturingApiResult<PlanViewV1>> Execute(CancellationToken token)
        {
            await owner.RejectPlanAsync(plan.Id, 1, run.Approver, At, "Gerçek insan gerekçesi",
                RejectionReceiptMetadata(run, "same-context"), token);
            return RejectionOk();
        }
        await Assert.ThrowsAsync<ManufacturingStoreException>(() => owner.ExecutePlanRejectionAsync(Execute,
            _ => audit.HasPendingDrafts
                ? Task.FromException(new ManufacturingStoreException(ManufacturingStoreCodes.AuthorityDenied, "Revoked after staging."))
                : Task.CompletedTask));
        Assert.False(audit.HasPendingDrafts);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
        await AssertRejectedAttemptAbsent(run, plan.Id);
        Assert.Equal(ManufacturingApiStatus.Ok,
            (await owner.ExecutePlanRejectionAsync(Execute, _ => Task.CompletedTask)).Status);
        Assert.False(audit.HasPendingDrafts);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'manufacturing.plan.rejected.v1' AND EntityId=@p",
            ("@p", plan.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Plan_rejection_real_writer_drains_unflushed_audit_in_the_same_owned_transaction_before_commit()
    {
        await using var run = await Run.StartAsync();
        var plan = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var seed = run.Store(CompanyA)) await seed.Plans.SavePlanAsync(plan, Metadata(run));
        await using var services = run.Factory.Services.CreateAsyncScope();
        var company = services.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = services.ServiceProvider.GetRequiredService<AuditEventCollector>();
        var writer = services.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        // Deliberately no Audit interceptor: this verifies the existing writer fallback, not a fake flush.
        await using var context = run.Context(CompanyA);
        var owner = new ManufacturingStore(context, company, collector, TimeProvider.System, writer);
        var answer = await owner.ExecutePlanRejectionAsync(async token =>
        {
            await owner.RejectPlanAsync(plan.Id, 1, run.Approver, At, "Gerçek insan gerekçesi",
                RejectionReceiptMetadata(run, "writer-fallback"), token);
            return RejectionOk();
        }, _ => Task.CompletedTask);
        Assert.Equal(ManufacturingApiStatus.Ok, answer.Status);
        Assert.False(writer.HasPendingDrafts);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'manufacturing.plan.rejected.v1' AND EntityId=@p",
            ("@p", plan.Id.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanRejections WHERE PlanId=@p", ("@p", plan.Id)));
    }

    [SqlServerFact]
    public async Task Plan_rejection_returned_terminal_status_without_a_native_saved_receipt_never_commits_staged_work()
    {
        await using var run = await Run.StartAsync();
        var plan = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var seed = run.Store(CompanyA)) await seed.Plans.SavePlanAsync(plan, Metadata(run));
        await using var scope = run.Store(CompanyA);
        var result = await scope.Lifecycle.ExecutePlanRejectionAsync(async token =>
        {
            // Native business save without a completion is a legacy store call, not a command receipt.
            await scope.Lifecycle.RejectPlanAsync(plan.Id, 1, run.Approver, At, "Gerçek insan gerekçesi", Metadata(run), token);
            return new(ManufacturingApiStatus.Conflict, null, false, GoymenERP.Modules.Manufacturing.Domain.Common.ManufacturingRuleCodes.VersionConflict);
        }, _ => Task.CompletedTask);
        Assert.Equal(ManufacturingApiStatus.Conflict, result.Status);
        await AssertRejectedAttemptAbsent(run, plan.Id);
    }

    private static AuditEventDraft RejectionCallerAudit(Run run) => new(
        CompanyA, "manufacturing.plan.rejected.v1", "Manufacturing.ProductionPlan", Guid.NewGuid().ToString("D"),
        "Reject", 1, 2, AuditActorTypes.User, run.Actor.ToString("D"), "synthetic.manufacturing.test-write",
        ["SYNTHETIC-MANUFACTURING-TEST"], "{}", "{}", "Caller-owned draft", null,
        AuditAiInvolvement.None, null, "Web", null, null, run.Correlation, null, "caller-owned");

    private static ManufacturingApiResult<PlanViewV1> RejectionOk() => new(ManufacturingApiStatus.Ok, null, false, null);
    private static string RejectionHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static ManufacturingCommandMetadata RejectionReceiptMetadata(Run run, string key) => Metadata(run) with
    {
        ActorId = run.Approver.ToString("D"), IdempotencyKey = key,
        Completion = _ => new(ManufacturingCommandNames.RejectPlan, RejectionHash(key), run.Approver.ToString("D"),
            RejectionHash("native-rejection-" + key), JsonSerializer.Serialize(RejectionOk()), run.Correlation),
    };
    private static async Task AssertRejectedAttemptAbsent(Run run, Guid planId)
    {
        await using var verify = run.Context(CompanyA);
        Assert.Equal(1L, (await verify.Plans.AsNoTracking().SingleAsync(x => x.Id == planId)).Version);
        Assert.Empty(await verify.PlanRejections.Where(x => x.PlanId == planId).ToListAsync());
        Assert.Empty(await verify.Commands.Where(x => x.CommandName == ManufacturingCommandNames.RejectPlan).ToListAsync());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'manufacturing.plan.rejected.v1' AND EntityId=@p", ("@p", planId.ToString("D"))));
    }
    private sealed class RejectionSaveProbe : SaveChangesInterceptor
    {
        public bool Saved { get; private set; }
        public bool Fail { get; init; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            Saved = true;
            if (Fail) throw new InvalidOperationException("Synthetic failure after real SQL SaveChanges.");
            return ValueTask.FromResult(result);
        }
    }

}

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Native_plan_rejection_keeps_success_and_terminal_receipts_immutable_under_owned_execution()
    {
        await using var run = await Run.StartAsync(useSqlSettings: true, clockAt: CatalogDay);
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LifecycleGrants);
        using var creator = run.ClientFor(user, CompanyA);
        var tube = await run.WorkCenterOfKindAsync(CompanyA, "TubeProfile");
        var roll = await run.StockedLabelAsync($"SYN-MFG-REJECT-FENCE-{Guid.NewGuid():N}");
        var id = Guid.NewGuid();
        await PlanAsync(creator, HttpStatusCode.Created, $"{Base}/plans",
            new SavePlanRequestV1(id, tube, [new PlannedInputRequestV1(roll.Id, "1000", roll.Version)], null));
        using var decider = await run.ApproverAsync(CompanyA);
        var decided = await PlanAsync(decider, HttpStatusCode.OK, $"{Base}/plans/{id}/rejection",
            new RejectPlanRequestV1(1, "Ýnsan gerekçesi"), "reject-owned-ok");
        Assert.Equal(("Draft", 2L, 1), (decided.Status, decided.Version, decided.Rejections!.Count));
        var missing = Guid.NewGuid();
        foreach (var (planId, status, key) in new[]
                 { (id, HttpStatusCode.Conflict, "reject-owned-stale"),
                   (missing, HttpStatusCode.NotFound, "reject-owned-missing") })
        {
            using var first = await decider.PostWithKeyAsync($"{Base}/plans/{planId}/rejection",
                new RejectPlanRequestV1(1, "Ýnsan gerekçesi"), key);
            Assert.Equal(status, first.StatusCode);
            using var replay = await decider.PostWithKeyAsync($"{Base}/plans/{planId}/rejection",
                new RejectPlanRequestV1(1, "Ýnsan gerekçesi"), key);
            Assert.Equal(status, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        }
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands WHERE CompanyId=@c AND CommandName=@n",
            ("@c", CompanyA), ("@n", ManufacturingCommandNames.RejectPlan)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'manufacturing.plan.rejected.v1' AND EntityId=@p",
            ("@p", id.ToString("D"))));
    }
}

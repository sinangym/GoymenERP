using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Request_decision_rechecks_authority_after_owner_reads_before_staging()
    {
        await using Run run = await Run.StartAsync();
        var id = await SubmittedRequestAsync(run, Owners.For(CompanyA), 100m);
        var before = await run.RequestCountsAsync(id);
        await using var writer = run.Store(CompanyA);
        var checks = 0;
        Task Fence(CancellationToken _)
        {
            if (++checks == 2) throw new PurchasingServiceException(403, "forbidden");
            return Task.CompletedTask;
        }
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => writer.Store.MutateRequestGuardedAsync(
            run.Envelope("late-request-authority", actor: Manager), Hash("late-request-authority"), PurchasingCommands.RequestApproveV1,
            id, (request, _, now) => request.Approve(3, Manager, now), RequestApproved, Fence));
        Assert.Equal(403, denied.Status);
        Assert.Equal(2, checks);
        Assert.Empty(writer.Context.ChangeTracker.Entries());
        Assert.Equal(before, await run.RequestCountsAsync(id));
    }

    [SqlServerFact]
    public async Task Work_delegation_revoked_during_request_save_rolls_back_owner_receipt_and_audit()
    {
        await using Run run = await Run.StartAsync();
        var id = await SubmittedRequestAsync(run, Owners.For(CompanyA), 100m);
        var before = await run.RequestCountsAsync(id);
        var allowed = true;
        var execution = new WorkDecisionExecutionFenceV1();
        using var lease = execution.Enter(_ => Task.FromResult(allowed));
        var authority = new WorkDecisionAuthorityV1(new RequestDecisionAuthority(), execution);
        var saved = new RequestDecisionSavedInterceptor(() => allowed = false);
        await using var writer = run.Store(CompanyA, saved);
        async Task Fence(CancellationToken token)
        {
            var scope = await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.RequestsApprove, CompanyA, token);
            if (!scope.IsGranted) throw new PurchasingServiceException(403, "forbidden");
        }
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => writer.Store.MutateRequestGuardedAsync(
            run.Envelope("late-request-delegation", actor: Manager), Hash("late-request-delegation"), PurchasingCommands.RequestApproveV1,
            id, (request, _, now) => request.Approve(3, Manager, now), RequestApproved, Fence));
        Assert.Equal(403, denied.Status);
        Assert.Equal(1, saved.Calls);
        Assert.Empty(writer.Context.ChangeTracker.Entries());
        Assert.Equal(before, await run.RequestCountsAsync(id));
        // A fresh authorized attempt succeeds after rollback; the refused key did not reserve a receipt or event.
        allowed = true;
        await using var retry = run.Store(CompanyA);
        var accepted = await retry.Store.MutateRequestGuardedAsync(run.Envelope("late-request-delegation", actor: Manager),
            Hash("late-request-delegation"), PurchasingCommands.RequestApproveV1, id,
            (request, _, now) => request.Approve(3, Manager, now), RequestApproved, Fence);
        Assert.Equal(4L, accepted!.Request.Request.Version);
        Assert.Equal((before.Requests, before.Lines, before.Attempts + 1, before.AuditEvents + 1, 4L), await run.RequestCountsAsync(id));
    }

    [SqlServerFact]
    public async Task Guarded_request_replay_retains_original_correlation_and_checks_current_authority()
    {
        await using Run run = await Run.StartAsync();
        var id = await SubmittedRequestAsync(run, Owners.For(CompanyA), 100m);
        await using (var writer = run.Store(CompanyA))
            await writer.Store.MutateRequestGuardedAsync(run.Envelope("guarded-replay", actor: Manager), Hash("guarded-replay"),
                PurchasingCommands.RequestApproveV1, id, (request, _, now) => request.Approve(3, Manager, now), RequestApproved,
                _ => Task.CompletedTask);
        var before = await run.RequestCountsAsync(id);
        await using (var reader = run.Store(CompanyA))
        {
            var checks = 0;
            var answer = await reader.Store.MutateRequestGuardedAsync(run.Envelope("guarded-replay", correlation: "SENTETIK-retry", actor: Manager),
                Hash("guarded-replay"), PurchasingCommands.RequestApproveV1, id, (_, _, _) => throw new InvalidOperationException("Replay cannot mutate"),
                RequestApproved, _ => { checks++; return Task.CompletedTask; });
            Assert.Equal((PurchasingWriteOutcome.Replayed, run.Correlation), (answer!.Outcome, answer.OriginalCorrelationId));
            Assert.True(checks >= 2);
        }
        await using (var denied = run.Store(CompanyA))
        {
            var checks = 0;
            await Assert.ThrowsAsync<PurchasingServiceException>(() => denied.Store.MutateRequestGuardedAsync(
                run.Envelope("guarded-replay", actor: Manager), Hash("guarded-replay"), PurchasingCommands.RequestApproveV1, id,
                (_, _, _) => throw new InvalidOperationException("Replay cannot mutate"), RequestApproved,
                _ => ++checks == 2 ? throw new PurchasingServiceException(403, "forbidden") : Task.CompletedTask));
        }
        Assert.Equal(before, await run.RequestCountsAsync(id));
    }

    private sealed class RequestDecisionAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
    private sealed class RequestDecisionSavedInterceptor(Action afterSave) : SaveChangesInterceptor
    {
        public int Calls { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { Calls++; afterSave(); return ValueTask.FromResult(result); }
    }
}

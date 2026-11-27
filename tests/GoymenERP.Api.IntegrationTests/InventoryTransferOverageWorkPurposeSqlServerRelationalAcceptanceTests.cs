using System.Transactions;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using H = GoymenERP.Api.IntegrationTests.WorkInventoryTransferLossDecisionHostTests;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Work_overage_native_approval_materializes_once_and_replays_the_immutable_original_after_request_renewal()
    {
        await using var run = await Run.StartAsync();
        var (transfer, line, received) = await ReceivedWithOverageAsync(run, "SYN-WORK-OVERAGE-REPLAY");
        var version = received.Transfer.Version; var actor = LossChecker(run).ActorId;
        var delegatedLive = true;
        await using var scope = run.Store(CompanyA); var host = OveragePurposeHost(scope, actor, new OveragePurposeDelegation(() => delegatedLive, actor));
        var id = InventoryWorkInboxFence.TransferLineItemId(WorkDecisionHostV1.InventoryTransferOverage, transfer, line);
        var preview = await host.PreviewAsync(WorkDecisionHostV1.InventoryTransferOverage, id, default);
        Assert.Equal(1, preview.InventoryTransferOverage!.RequestNumber); Assert.Equal("2", preview.InventoryTransferOverage.Kilograms);
        Assert.False(string.IsNullOrWhiteSpace(preview.InventoryTransferOverage.Reason));
        var original = new WorkDecisionRequestV1(version, false, OverageDecisionReason, Guid.NewGuid(), InventoryTransferOverage: new(1, At.AddHours(3)));
        var key = "work-overage-reject-" + Guid.NewGuid().ToString("N");
        var rejected = await host.DecideAsync(WorkDecisionHostV1.InventoryTransferOverage, id, original, key, default);
        await RenewOverageAsync(run, transfer, line, new(version, At.AddHours(4), "1.5", OverageRenewalReason), Metadata(run) with { ActorId = actor });
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(WorkDecisionHostV1.InventoryTransferOverage, id, default))).Status);
        var replay = await host.DecideAsync(WorkDecisionHostV1.InventoryTransferOverage, id, original, key, default);
        Assert.Equal(rejected with { Replayed = true }, replay);
        delegatedLive = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(WorkDecisionHostV1.InventoryTransferOverage, id, original, key, default))).Status);
        delegatedLive = true;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(WorkDecisionHostV1.InventoryTransferOverage, id, original, "different-key", default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(WorkDecisionHostV1.InventoryTransferOverage, id,
            new(version, true, OverageDecisionReason, InventoryTransferOverage: new(2, At.AddHours(5))), "fresh-own", default))).Status);
        // A third actor (neither receiver nor requester) decides renewal 2; the receiver remains excluded.
        var secondActor = Guid.NewGuid().ToString("D"); var second = OveragePurposeHost(scope, secondActor);
        var approvedBody = new WorkDecisionRequestV1(version, true, OverageDecisionReason, InventoryTransferOverage: new(2, At.AddHours(5)));
        var approveKey = "work-overage-approve-" + Guid.NewGuid().ToString("N");
        var approved = await second.DecideAsync(WorkDecisionHostV1.InventoryTransferOverage, id, approvedBody, approveKey, default);
        Assert.Equal(("Approved", false), (approved.State, approved.Replayed));
        Assert.Equal(approved with { Replayed = true }, await second.DecideAsync(WorkDecisionHostV1.InventoryTransferOverage, id, approvedBody, approveKey, default));
        await using var db = run.Context(CompanyA);
        var decisions = await db.TransferOverageDecisions.Where(row => row.TransferId == transfer).OrderBy(row => row.RequestNumber).ToListAsync();
        Assert.Equal([1, 2], decisions.Select(row => row.RequestNumber)); Assert.Null(decisions[0].MovementId); Assert.NotNull(decisions[1].MovementId);
        Assert.Equal(1, await db.Movements.CountAsync(row => row.Id == decisions[1].MovementId));
        Assert.Equal(2, await db.Commands.CountAsync(row => row.CommandName == InventoryStore.DecideTransferOverageCommand));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'inventory.transfer.overage-decided.v1' AND EntityId=@p", ("@p", transfer.ToString("D"))));
        Assert.Equal(version, (await db.Transfers.SingleAsync(row => row.Id == transfer)).Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [SqlServerFact]
    public async Task Work_overage_post_save_delegation_revocation_rolls_back_movement_command_and_actual_audit_then_same_key_succeeds()
    {
        await using var run = await Run.StartAsync();
        var (transfer, line, received) = await ReceivedWithOverageAsync(run, "SYN-WORK-OVERAGE-ROLLBACK");
        var probe = new OveragePurposeSaveProbe(transfer); await using var scope = run.Store(CompanyA, probe);
        var live = true; var delegated = new OveragePurposeDelegation(() => live && !probe.Saved);
        var host = OveragePurposeHost(scope, LossChecker(run).ActorId, delegated);
        var id = InventoryWorkInboxFence.TransferLineItemId(WorkDecisionHostV1.InventoryTransferOverage, transfer, line);
        var body = new WorkDecisionRequestV1(received.Transfer.Version, true, OverageDecisionReason, Guid.NewGuid(), InventoryTransferOverage: new(1, At.AddHours(3)));
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(WorkDecisionHostV1.InventoryTransferOverage, id, body, "post-save-revoked", default))).Status);
        Assert.True(probe.Saved); Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
        await using (var db = run.Context(CompanyA))
        {
            Assert.Empty(await db.TransferOverageDecisions.Where(row => row.TransferId == transfer).ToListAsync());
            Assert.Empty(await db.Commands.Where(row => row.CommandName == InventoryStore.DecideTransferOverageCommand).ToListAsync());
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'inventory.transfer.overage-decided.v1' AND EntityId=@p", ("@p", transfer.ToString("D"))));
        var retry = OveragePurposeHost(scope, LossChecker(run).ActorId);
        Assert.Equal("Approved", (await retry.DecideAsync(WorkDecisionHostV1.InventoryTransferOverage, id, body with { DelegationId = null }, "post-save-revoked", default)).State);
        live = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(WorkDecisionHostV1.InventoryTransferOverage, id, body, "post-save-revoked", default))).Status);
    }

    [SqlServerFact]
    public async Task Overage_decision_and_replay_entry_refusals_preserve_caller_state_without_fences_or_cleanup()
    {
        await using var run = await Run.StartAsync();
        var (transfer, line, received) = await ReceivedWithOverageAsync(run, "SYN-WORK-OVERAGE-ENTRY");
        var request = new DecideInventoryTransferOverageV1(received.Transfer.Version, InventoryTransferDecisionKindV1.Reject, At.AddHours(3), OverageDecisionReason, 1);
        foreach (var replay in new[] { false, true })
        foreach (var boundary in new[] { "missing-writer", "borrowed", "dirty", "audit", "ambient" })
        {
            await using var scope = run.Store(CompanyA);
            var owner = boundary == "missing-writer" ? new InventoryStore(scope.DbContext, scope.CompanyContext, scope.AuditCollector, new InventoryKilogramScale(null!), TimeProvider.System) : scope.Store;
            await using var borrowed = boundary == "borrowed" ? await scope.DbContext.Database.BeginTransactionAsync() : null;
            var row = boundary == "dirty" ? await scope.DbContext.Transfers.SingleAsync(row => row.Id == transfer) : null;
            if (row is not null) row.UpdatedAtUtc = At.AddDays(1);
            if (boundary is "audit" or "missing-writer") scope.AuditCollector.Enqueue(LossSafetyCallerDraft(run));
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var called = false;
            async Task Invoke()
            {
                Task Fence(CancellationToken _) { called = true; return Task.CompletedTask; }
                if (replay) await owner.ReplayDecideOverageAsync(transfer, line, request, LossChecker(run), Fence);
                else await owner.DecideOverageAsync(transfer, line, request, LossChecker(run), Fence);
            }
            var error = await Assert.ThrowsAsync<InventoryTransferException>(Invoke);
            Assert.Equal((503, InventoryTransferCodesV1.Unavailable), (error.Status, error.Code)); Assert.False(called);
            Assert.Same(borrowed, scope.DbContext.Database.CurrentTransaction);
            Assert.Equal(boundary == "dirty", scope.DbContext.ChangeTracker.HasChanges());
            Assert.Equal(boundary is "audit" or "missing-writer", scope.AuditWriter.HasPendingDrafts);
            if (row is not null) Assert.Equal(At.AddDays(1), row.UpdatedAtUtc);
            if (boundary is "audit" or "missing-writer") scope.AuditWriter.DiscardPending();
        }
    }

    [SqlServerFact]
    public async Task Overage_save_race_rolls_back_then_reads_one_real_winner_without_mutation_retry_or_conflict_receipt()
    {
        await using var run = await Run.StartAsync();
        var (transfer, line, received) = await ReceivedWithOverageAsync(run, "SYN-WORK-OVERAGE-RACE");
        var probe = new OveragePurposeSaveProbe(transfer) { Concurrency = true };
        await using var scope = run.Store(CompanyA, probe);
        var body = new DecideInventoryTransferOverageV1(received.Transfer.Version, InventoryTransferDecisionKindV1.Reject, At.AddHours(3), OverageDecisionReason, 1);
        var metadata = LossChecker(run); var boundaries = new HashSet<Guid>();
        Task Count(CancellationToken _) { boundaries.Add(scope.DbContext.Database.CurrentTransaction!.TransactionId); return Task.CompletedTask; }
        var absent = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.DecideOverageAsync(transfer, line, body, metadata, Count));
        Assert.Equal((409, InventoryTransferCodesV1.ConcurrentChange), (absent.Status, absent.Code));
        Assert.Equal(2, boundaries.Count); Assert.Equal(1, probe.SaveAttempts);
        Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE CommandName=@p", ("@p", InventoryStore.DecideTransferOverageCommand)));
        var first = (Guid?)null; var won = false;
        async Task Winner(CancellationToken _)
        {
            var current = scope.DbContext.Database.CurrentTransaction!.TransactionId;
            first ??= current;
            if (current != first && !won)
            {
                Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
                won = true;
                var actual = await DecideOverageAsync(run, transfer, line, body, metadata);
                Assert.False(actual.IsReplay); Assert.True(actual.CommandReceiptProven);
            }
        }
        var result = await scope.Store.DecideOverageAsync(transfer, line, body, metadata, Winner);
        Assert.True(won); Assert.True(result.IsReplay); Assert.True(result.CommandReceiptProven);
        Assert.Equal(2, probe.SaveAttempts); // one failed save in each attempt; no second mutation within either attempt.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferOverageDecisions WHERE TransferId=@p", ("@p", transfer)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'inventory.transfer.overage-decided.v1' AND EntityId=@p", ("@p", transfer.ToString("D"))));
    }

    // Controlled delegation only; owner storage, immutable receipt, writer/interceptor and transaction are real SQL.
    private sealed class OveragePurposeDelegation(Func<bool> allowed, string? actor = null) : GoymenERP.Modules.Work.Infrastructure.Delegations.IWorkDelegationReaderV1
    {
        public Task<GoymenERP.Modules.Work.Infrastructure.Delegations.WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoymenERP.Modules.Work.Infrastructure.Delegations.WorkDelegationViewV1?>(allowed() && (actor is null || !item.Excludes(actor))
                ? new(id, CompanyA, item.Kind, InventoryPermissionManifestV1.StockTransferOverageApprove, Guid.NewGuid(), Guid.NewGuid(),
                    DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), DateTimeOffset.UtcNow,
                    "SENTETIK delegated grant", null, null, null) : null);
    }

    private static WorkDecisionHostV1 OveragePurposeHost(StoreScope scope, string actor, GoymenERP.Modules.Work.Infrastructure.Delegations.IWorkDelegationReaderV1? delegations = null)
    {
        var user = new H.LossUser(CompanyA, actor); var execution = new WorkDecisionExecutionFenceV1();
        var authority = new WorkDecisionAuthorityV1(new H.LossAuthority(), execution);
        var source = new InventoryTransferOverageWorkInboxSourceV1(scope.DbContext, new InventoryWorkInboxFence(scope.CompanyContext, user, authority));
        var destination = System.Reflection.DispatchProxy.Create<IInventoryTransferDestinationPortV1, H.OwnerProxy>();
        var native = new InventoryTransferReceiptCommandService(scope.CompanyContext, user, authority, destination, scope.Store, scope.Store);
        return H.CreateHost(scope.CompanyContext, user, authority, source, native, execution, delegations);
    }
    private sealed class OveragePurposeSaveProbe(Guid transfer) : SaveChangesInterceptor
    {
        public bool Saved; public bool Concurrency; public int SaveAttempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is InventoryDbContext db && db.ChangeTracker.Entries<InventoryTransferOverageDecisionRow>().Any(row => row.Entity.TransferId == transfer))
            { SaveAttempts++; if (Concurrency) throw new DbUpdateConcurrencyException("SENTETIK native overage save race."); }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        {
            if (data.Context is InventoryDbContext db && db.ChangeTracker.Entries<InventoryTransferOverageDecisionRow>().Any(row => row.Entity.TransferId == transfer)) Saved = true;
            return ValueTask.FromResult(result);
        }
    }
}

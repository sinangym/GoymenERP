using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task Loss_owned_rollback_or_disposal_failure_discards_staged_state_and_never_opens_a_replay_transaction() =>
        AssertReceiptCleanupFailureAsync(overage: false);

    [SqlServerFact]
    public Task Overage_owned_rollback_or_disposal_failure_discards_staged_state_and_never_opens_a_replay_transaction() =>
        AssertReceiptCleanupFailureAsync(overage: true);

    private static async Task AssertReceiptCleanupFailureAsync(bool overage)
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { false, true })
        foreach (var failure in new[] { (Rollback: true, Dispose: false), (Rollback: false, Dispose: true), (Rollback: true, Dispose: true) })
        {
            var code = "SYN-OWNED-CLEANUP-" + Guid.NewGuid().ToString("N");
            Guid transfer; Guid line; string version;
            if (overage)
            {
                var received = await ReceivedWithOverageAsync(run, code);
                transfer = received.TransferId; line = received.LineId; version = received.Received.Transfer.Version;
            }
            else
            {
                var dispatched = await DispatchedLossTransferAsync(run, code);
                transfer = dispatched.Plan.TransferId; line = dispatched.LineId; version = "2";
                await LossRequestAsync(run, transfer, line, new(version, At.AddHours(2), LossRenewalExplanation), Metadata(run));
            }
            var movements = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE CompanyId=@p", ("@p", CompanyA));
            var fault = new InventoryOwnedCleanupFaultProbe(failure.Rollback, failure.Dispose);
            SaveChangesInterceptor saveProbe = overage
                ? new OveragePurposeSaveProbe(transfer) { Concurrency = true }
                : new LossSafetySaveProbe(transfer) { Concurrency = true };
            await using var scope = StockCountSafetyCreateStore(run, true, fault, fault, saveProbe);
            var observedStaged = false;
            Task Fence(CancellationToken _)
            {
                if (!scope.AuditWriter.HasPendingDrafts) return Task.CompletedTask;
                observedStaged = true;
                Assert.True(scope.DbContext.ChangeTracker.HasChanges());
                fault.Armed = true;
                // Arm cleanup failure here, but leave the tracker intact: the existing native SavingChanges
                // probe produces the original failure. A callback failure would clear the tracker too early.
                return Task.CompletedTask;
            }
            var metadata = LossChecker(run);
            var decision = approve ? InventoryTransferDecisionKindV1.Approve : InventoryTransferDecisionKindV1.Reject;
            Task<InventoryTransferReceiptResultV1> Invoke() => overage
                ? scope.Store.DecideOverageAsync(transfer, line, new(version, decision, At.AddHours(3), OverageDecisionReason, 1), metadata, Fence)
                : scope.Store.DecideLossAsync(transfer, line, new(version, decision, At.AddHours(3), "SENTETIK loss decision", 1), metadata, Fence);
            await Assert.ThrowsAsync<InventoryOwnedCleanupFaultException>(() => Invoke());
            Assert.True(observedStaged); Assert.Equal(1, fault.Starts);
            Assert.Equal(1, overage ? ((OveragePurposeSaveProbe)saveProbe).SaveAttempts : ((LossSafetySaveProbe)saveProbe).SaveAttempts);
            Assert.Equal(failure.Rollback, fault.RollbackFailed); Assert.Equal(failure.Dispose, fault.DisposeFailed);
            Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
            Assert.Equal(long.Parse(version, System.Globalization.CultureInfo.InvariantCulture),
                await run.ScalarAsync<long>("SELECT Version FROM inventory.Transfers WHERE Id=@p", ("@p", transfer)));
            Assert.Equal(movements, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE CompanyId=@p", ("@p", CompanyA)));
            var command = overage ? InventoryStore.DecideTransferOverageCommand : InventoryStore.DecideTransferLossCommand;
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE CommandName=@c AND TargetId=@p", ("@c", command), ("@p", transfer)));
            var eventType = overage ? "inventory.transfer.overage-decided.v1" : "inventory.transfer.loss-decided.v1";
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=@e AND EntityId=@p", ("@e", eventType), ("@p", transfer.ToString("D"))));
        }
    }
}

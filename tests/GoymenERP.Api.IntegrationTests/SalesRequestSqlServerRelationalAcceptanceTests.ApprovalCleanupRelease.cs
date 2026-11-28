using System.Data.Common;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesRequestSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Owned_request_failed_decision_cleanup_revalidates_authority_and_cancellation_after_rollback_or_dispose()
    {
        await using var run = await Run.StartAsync();
        foreach (var phase in new[] { "rollback", "dispose" })
        foreach (var change in new[] { "unchanged", "permission", "membership", "cancel" })
        {
            var id = await SeedRequest(run); var key = $"cleanup-release-{id:N}";
            using var cancelled = new CancellationTokenSource();
            var probe = new RequestCleanupReleaseProbe(phase); var save = new RequestSaveProbe();
            await using (var safe = ApprovalScope(run, probe, save))
            {
                probe.AfterCleanup = () =>
                {
                    if (change == "permission") safe.Authority.Granted = false;
                    if (change == "membership") safe.User.HasMembership = false;
                    if (change == "cancel") cancelled.Cancel();
                };
                // Invalid native reason fails before any save. Revocation happens only after the real owned rollback/dispose await.
                Task<SalesRequestResponse> Reject() => safe.Service.RejectRequestAsync(id, new("1", null), run.Metadata(key), cancelled.Token);
                if (change == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(Reject);
                else
                {
                    var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(Reject);
                    if (change == "unchanged") Assert.Equal((422, SalesRuleCodes.RequestRejectionReasonRequired), (refusal.Status, refusal.Code));
                    else Assert.Equal(403, refusal.Status);
                }
                Assert.Equal(1, probe.Observed); Assert.Equal(0, save.Saves);
                Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
                Assert.Null(safe.Db.Database.CurrentTransaction);
            }
            Assert.Equal(0, await DecisionCount(run, id)); Assert.Equal(0, await DecisionAuditCount(run, id));
            Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM sales.Requests WHERE Id=@id", ("@id", id)));
            await using var retry = ApprovalScope(run);
            Assert.False((await retry.Service.RejectRequestAsync(id, new("1", "SENTETIK ret"), run.Metadata(key))).IsReplay);
            Assert.Equal(1, await DecisionCount(run, id)); Assert.Equal(1, await DecisionAuditCount(run, id));
        }
    }

    private sealed class RequestCleanupReleaseProbe(string phase) : DbTransactionInterceptor
    {
        internal int Observed; internal Action? AfterCleanup;
        internal void Configure(DbContextOptionsBuilder<SalesDbContext> options) => options.LogTo(
            (id, _) => id == RelationalEventId.TransactionDisposed, _ => { if (phase == "dispose") Changed(); });
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        { if (phase == "rollback") Changed(); return Task.CompletedTask; }
        private void Changed() { if (Observed++ == 0) AfterCleanup?.Invoke(); }
    }
}

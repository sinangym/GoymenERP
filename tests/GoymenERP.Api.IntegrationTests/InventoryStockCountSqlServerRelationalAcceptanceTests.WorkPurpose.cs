using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;
using H = GoymenERP.Api.IntegrationTests.WorkInventoryTransferLossDecisionHostTests;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Stock_count_Work_native_two_decisions_replay_original_receipt_after_later_label_changes_but_never_prove_a_different_key()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-WORK-" + Guid.NewGuid().ToString("N"), 100m);
            var request = Count(source.LabelId, source.LabelVersion, "101");
            await using (var seed = run.Store(CompanyA))
                await seed.Store.RecordStockCountAsync(request, Metadata(run), static _ => Task.CompletedTask);
            await using var scope = run.Store(CompanyA);
            var actor = Guid.NewGuid().ToString("D"); var raw = new StockCountPurposeAuthority();
            var host = StockCountPurposeHost(scope, actor, raw);
            var preview = await host.PreviewAsync(WorkDecisionHostV1.InventoryStockCount, request.CountId, default);
            Assert.True(preview.Pending); Assert.Equal("1", preview.SourceVersion);
            Assert.Equal(source.LabelVersion.ToString(CultureInfo.InvariantCulture), preview.InventoryStockCount!.LabelVersion);
            Assert.True(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
            var body = new WorkDecisionRequestV1("1", approve, "  " + new string('x', 1000) + "  ");
            var key = "stock-count-work-" + Guid.NewGuid().ToString("N");
            var original = await host.DecideAsync(WorkDecisionHostV1.InventoryStockCount, request.CountId, body, key, default);
            Assert.Equal(approve ? "Approved" : "Rejected", original.State); Assert.False(original.Replayed);
            var terminal = await host.PreviewAsync(WorkDecisionHostV1.InventoryStockCount, request.CountId, default);
            Assert.False(terminal.Pending); Assert.Equal("2", terminal.SourceVersion);
            Assert.Equal(preview.InventoryStockCount, terminal.InventoryStockCount);

            // Another count/movement advances the live label; an immutable old decision is not revalidated as a new one.
            var later = Count(source.LabelId, await LabelVersionAsync(run, source.LabelId), "102");
            await using (var another = run.Store(CompanyA))
            {
                await another.Store.RecordStockCountAsync(later, Metadata(run), static _ => Task.CompletedTask);
                await another.Store.DecideStockCountAsync(later.CountId, new(InventoryStockCountDecisionKindV1.Approve, "Sonraki sayım."),
                    Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") }, static _ => Task.CompletedTask);
            }
            var committed = await CountEffectsAsync(run, source.LabelId);
            var replay = await host.DecideAsync(WorkDecisionHostV1.InventoryStockCount, request.CountId, body, key, default);
            Assert.Equal(original with { Replayed = true }, replay);
            var nativeDomain = await scope.Store.DecideStockCountAsync(request.CountId,
                new(approve ? InventoryStockCountDecisionKindV1.Approve : InventoryStockCountDecisionKindV1.Reject, body.Reason!),
                Metadata(run) with { ActorId = actor }, static _ => Task.CompletedTask);
            Assert.True(nativeDomain.IsReplay); Assert.False(nativeDomain.CommandReceiptProven);
            Assert.DoesNotContain("commandReceiptProven", JsonSerializer.Serialize(nativeDomain, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(
                WorkDecisionHostV1.InventoryStockCount, request.CountId, body, "different-key", default))).Status);
            Assert.Equal(409, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(
                WorkDecisionHostV1.InventoryStockCount, request.CountId, body with { ExpectedVersion = "2" }, "wrong-version", default))).Status);
            Assert.Equal(committed, await CountEffectsAsync(run, source.LabelId));
            Assert.All(raw.Permissions, permission => Assert.Equal(InventoryPermissionManifestV1.StockCountApprove, permission));
            raw.Granted = false;
            Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(
                WorkDecisionHostV1.InventoryStockCount, request.CountId, body, key, default))).Status);
            Assert.Equal(committed, await CountEffectsAsync(run, source.LabelId));
        }
    }

    [SqlServerFact]
    public async Task Stock_count_Work_post_save_delegation_loss_reaches_native_guard_rolls_back_actual_audit_and_same_key_can_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-WORK-FENCE-" + Guid.NewGuid().ToString("N"), 100m);
            var request = Count(source.LabelId, source.LabelVersion, "101");
            await using (var seed = run.Store(CompanyA))
                await seed.Store.RecordStockCountAsync(request, Metadata(run), static _ => Task.CompletedTask);
            var baseline = await CountEffectsAsync(run, source.LabelId);
            var probe = new StockCountSafetySaveProbe(request.CountId);
            await using var scope = run.Store(CompanyA, probe);
            var allowRetry = false; var delegation = new StockCountPurposeDelegations(() => allowRetry || !probe.Saved);
            var host = StockCountPurposeHost(scope, Guid.NewGuid().ToString("D"), new(), delegation);
            var body = new WorkDecisionRequestV1("1", approve, "Gerçek sayım gerekçesi.", Guid.NewGuid());
            var key = "stock-count-native-late-" + Guid.NewGuid().ToString("N");
            Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(
                WorkDecisionHostV1.InventoryStockCount, request.CountId, body, key, default))).Status);
            Assert.True(probe.Saved); Assert.True(delegation.Calls > 1);
            Assert.Equal(baseline, await CountEffectsAsync(run, source.LabelId));
            Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
            allowRetry = true;
            var accepted = await host.DecideAsync(WorkDecisionHostV1.InventoryStockCount, request.CountId, body, key, default);
            Assert.False(accepted.Replayed); Assert.Equal(approve ? "Approved" : "Rejected", accepted.State);
            var committed = await CountEffectsAsync(run, source.LabelId);
            Assert.Equal(accepted with { Replayed = true }, await host.DecideAsync(WorkDecisionHostV1.InventoryStockCount, request.CountId, body, key, default));
            Assert.Equal(committed, await CountEffectsAsync(run, source.LabelId));
            Assert.Equal(1, await scope.DbContext.StockCountDecisions.CountAsync(row => row.CountId == request.CountId));
        }
    }

    private static WorkDecisionHostV1 StockCountPurposeHost(StoreScope scope, string actor, StockCountPurposeAuthority raw,
        IWorkDelegationReaderV1? delegation = null)
    {
        var user = new H.LossUser(CompanyA, actor); var execution = new WorkDecisionExecutionFenceV1();
        var authority = new WorkDecisionAuthorityV1(raw, execution);
        var source = new InventoryStockCountWorkInboxSourceV1(scope.DbContext, new InventoryWorkInboxFence(scope.CompanyContext, user, authority));
        var native = new InventoryStockCountCommandService(scope.CompanyContext, user, authority, scope.Store);
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-count-work", null, "Web", null, null);
        return new(scope.CompanyContext, user, correlation, authority, [source], delegation ?? new StockCountPurposeDelegations(() => false),
            execution, null!, null!, stockCounts: native);
    }
    private sealed class StockCountPurposeAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted = true; public List<string> Permissions = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        {
            Permissions.Add(permission);
            return Task.FromResult(new PermissionScope(Granted && permission == InventoryPermissionManifestV1.StockCountApprove, true,
                new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
    private sealed class StockCountPurposeDelegations(Func<bool> allowed) : IWorkDelegationReaderV1
    {
        public int Calls;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default)
        {
            Calls++;
            return Task.FromResult<WorkDelegationViewV1?>(allowed() ? new(id, CompanyA, item.Kind, InventoryPermissionManifestV1.StockCountApprove,
                Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(),
                DateTimeOffset.UtcNow, "SENTETIK delegation", null, null, null) : null);
        }
    }
}

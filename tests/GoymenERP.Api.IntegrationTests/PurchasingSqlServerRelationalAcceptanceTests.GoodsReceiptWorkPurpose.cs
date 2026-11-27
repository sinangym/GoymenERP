using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Native SQL owner + Work composition. User/permission/delegation answers are synthetic,
/// not real role assignment, WP3 assignment or browser acceptance. SQL execution remains a train gate.</summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Batch_purpose_native_reject_post_save_delegation_loss_aborts_and_same_key_retries_then_replays_once()
    {
        await using var run = await Run.StartAsync(); var seed = await BatchRejectSeedAsync(run);
        var before = await BatchRejectCountsAsync(run, seed.BatchId);
        var delegation = new BatchPurposeDelegations();
        var saved = new RequestDecisionSavedInterceptor(() => delegation.Allowed = false);
        await using (var failed = BatchRejectScopeFor(run, CompanyA, saved))
        {
            var host = BatchPurposeHost(failed, OrderApprover, delegation, "SENTETIK-aborted-purpose");
            var error = await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(WorkDecisionHostV1.PurchaseGoodsReceiptBatch,
                seed.BatchId, new("1", false, "SENTETIK red", delegation.Id), "batch-purpose-key", default));
            Assert.Equal(403, error.Status); Assert.Equal(1, saved.Calls);
            Assert.Empty(failed.Database.ChangeTracker.Entries()); Assert.False(failed.AuditWriter.HasPendingDrafts);
        }
        Assert.Equal(before, await BatchRejectCountsAsync(run, seed.BatchId));
        delegation.Allowed = true;
        await using (var retry = BatchRejectScopeFor(run, CompanyA))
        {
            var result = await BatchPurposeHost(retry, OrderApprover, delegation, "SENTETIK-first-purpose").DecideAsync(
                WorkDecisionHostV1.PurchaseGoodsReceiptBatch, seed.BatchId,
                new("1", false, "SENTETIK red", delegation.Id), "batch-purpose-key", default);
            Assert.Equal("Rejected", result.State); Assert.False(result.Replayed); Assert.Equal("SENTETIK-first-purpose", result.OriginalCorrelationId);
        }
        var committed = await BatchRejectCountsAsync(run, seed.BatchId);
        Assert.Equal((2L, before.Attempts + 1, before.Audits + 1, seed.Labels.Count), committed);
        await using var replay = BatchRejectScopeFor(run, CompanyA);
        var replayHost = BatchPurposeHost(replay, OrderApprover, delegation, "SENTETIK-second-purpose");
        var preview = await replayHost.PreviewAsync(WorkDecisionHostV1.PurchaseGoodsReceiptBatch, seed.BatchId, default);
        Assert.False(preview.Pending); Assert.Equal("2", preview.SourceVersion);
        var repeated = await replayHost.DecideAsync(WorkDecisionHostV1.PurchaseGoodsReceiptBatch, seed.BatchId,
            new("1", false, "SENTETIK red", delegation.Id), "batch-purpose-key", default);
        Assert.True(repeated.Replayed); Assert.Equal("SENTETIK-first-purpose", repeated.OriginalCorrelationId);
        Assert.Equal(committed, await BatchRejectCountsAsync(run, seed.BatchId));
    }

    [SqlServerFact]
    public async Task Batch_purpose_real_sql_source_reads_only_own_company_and_native_terminal_snapshot_without_order_browse()
    {
        await using var run = await Run.StartAsync(); var seed = await BatchRejectSeedAsync(run);
        await using (var writer = BatchRejectScopeFor(run, CompanyA))
            await BatchRejectServiceFor(writer, OrderApprover).RejectGoodsReceiptBatchAsync(seed.PurchaseOrderId, seed.BatchId,
                new("1", "SENTETIK red"), new("batch-purpose-source", "SENTETIK-first", null, T0));
        await using (var reader = BatchRejectScopeFor(run, CompanyA))
        {
            var permissions = new BatchPurposeAuthority();
            var source = new PurchaseGoodsReceiptBatchWorkInboxSourceV1(reader.Database,
                new PurchasingWorkInboxFence(reader.Company, new NumberTestUser { UserId = OrderApprover }, permissions));
            var result = await source.ReadDecisionAsync(new(CompanyA, OrderApprover, "SENTETIK-read", null), seed.BatchId, default);
            Assert.NotNull(result); Assert.False(result.Pending); Assert.Equal("2", result.SourceVersion);
            Assert.Equal(run.Actor, result.Item.PreparedBy); Assert.Equal(seed.PurchaseOrderId, Assert.Single(result.Item.References).RecordId);
            Assert.Equal("/onaylarim/purchasing.goods-receipt-batch.v1/" + seed.BatchId.ToString("D"), result.Item.Link);
            Assert.All(permissions.Asked, permission => Assert.Equal(PurchasingPermissionManifestV1.GoodsReceiptsApproveVariance, permission));
        }
        await using var foreign = BatchRejectScopeFor(run, CompanyB);
        var foreignSource = new PurchaseGoodsReceiptBatchWorkInboxSourceV1(foreign.Database,
            new PurchasingWorkInboxFence(foreign.Company, new NumberTestUser { UserId = OrderApprover }, new BatchPurposeAuthority()));
        Assert.Null(await foreignSource.ReadDecisionAsync(new(CompanyB, OrderApprover, "SENTETIK-read", null), seed.BatchId, default));
    }

    [SqlServerFact]
    public async Task Batch_purpose_maker_and_foreign_reads_do_not_write_and_native_version_refusal_receipt_replays_original_correlation()
    {
        await using var run = await Run.StartAsync(); var seed = await BatchRejectSeedAsync(run);
        var before = await BatchRejectCountsAsync(run, seed.BatchId);
        await using (var maker = BatchRejectScopeFor(run, CompanyA))
            Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => BatchPurposeHost(maker, run.Actor, new(), "SENTETIK-maker")
                .PreviewAsync(WorkDecisionHostV1.PurchaseGoodsReceiptBatch, seed.BatchId, default))).Status);
        await using (var foreign = BatchRejectScopeFor(run, CompanyB))
            Assert.Equal(404, (await Assert.ThrowsAsync<WorkDecisionException>(() => BatchPurposeHost(foreign, OrderApprover, new(), "SENTETIK-foreign")
                .PreviewAsync(WorkDecisionHostV1.PurchaseGoodsReceiptBatch, seed.BatchId, default))).Status);
        Assert.Equal(before, await BatchRejectCountsAsync(run, seed.BatchId));
        await using (var wrongVersion = BatchRejectScopeFor(run, CompanyA))
        {
            var error = await Assert.ThrowsAsync<WorkDecisionException>(() => BatchPurposeHost(wrongVersion, OrderApprover, new(), "SENTETIK-version-first")
                .DecideAsync(WorkDecisionHostV1.PurchaseGoodsReceiptBatch, seed.BatchId, new("99", false, "SENTETIK red"), "batch-purpose-version", default));
            Assert.Equal(409, error.Status); Assert.False(error.Replayed);
            Assert.Null(error.OriginalCorrelationId); // Native first terminal refusal adds the original correlation only on replay.
        }
        var terminal = await BatchRejectCountsAsync(run, seed.BatchId);
        Assert.Equal((before.Version, before.Attempts + 1, before.Audits, before.Released), terminal);
        await using var replay = BatchRejectScopeFor(run, CompanyA);
        var repeated = await Assert.ThrowsAsync<WorkDecisionException>(() => BatchPurposeHost(replay, OrderApprover, new(), "SENTETIK-version-second")
            .DecideAsync(WorkDecisionHostV1.PurchaseGoodsReceiptBatch, seed.BatchId, new("99", false, "SENTETIK red"), "batch-purpose-version", default));
        Assert.Equal((409, true, "SENTETIK-version-first"), (repeated.Status, repeated.Replayed, repeated.OriginalCorrelationId));
        Assert.Equal(terminal, await BatchRejectCountsAsync(run, seed.BatchId));
    }

    private static WorkDecisionHostV1 BatchPurposeHost(BatchRejectScope scope, string actor, BatchPurposeDelegations delegations, string correlationId)
    {
        var user = new NumberTestUser { UserId = actor };
        var correlation = new CorrelationContext(); correlation.Set(correlationId, null, "Web", null, null);
        var execution = new WorkDecisionExecutionFenceV1();
        var authority = new WorkDecisionAuthorityV1(new BatchPurposeAuthority(), execution);
        var source = new PurchaseGoodsReceiptBatchWorkInboxSourceV1(scope.Database, new PurchasingWorkInboxFence(scope.Company, user, authority));
        var services = scope.Services;
        var native = new PurchasingService(scope.Company, user, authority, correlation,
            services.GetRequiredService<IPurchasingPartyAccountPortV1>(), services.GetService<IProductPurchaseEligibilityPortV1>(),
            services.GetRequiredService<PurchaseUnitVerifier>(), services.GetRequiredService<IPurchasingStore>(),
            new PurchasingServiceBindings(), TimeProvider.System, batchStore: scope.Store);
        return new(scope.Company, user, correlation, authority, [source], delegations, execution, null!, null!, goodsReceiptBatches: native);
    }
    private sealed class BatchPurposeAuthority : IErpAuthorityRevalidationService
    {
        public List<string> Asked { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Asked.Add(permission);
            return Task.FromResult(permission == PurchasingPermissionManifestV1.GoodsReceiptsApproveVariance
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
        }
    }
    private sealed class BatchPurposeDelegations : IWorkDelegationReaderV1
    {
        public readonly Guid Id = Guid.NewGuid(); public bool Allowed = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Allowed && id == Id ? new(Id, CompanyA, item.Kind, PurchasingPermissionManifestV1.GoodsReceiptsApproveVariance,
                Guid.NewGuid(), Guid.NewGuid(), T0.AddDays(-1), T0.AddDays(1), "1", Guid.NewGuid(), T0, "SENTETIK", null, null, null) : null);
    }
}

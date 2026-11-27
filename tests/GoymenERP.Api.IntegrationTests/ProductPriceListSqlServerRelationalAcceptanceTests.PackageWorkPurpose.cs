using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ProductPriceListSqlServerRelationalAcceptanceTests
{
    // Native SQL fixture, native guarded writer and real source; current grants/delegation observations remain controlled.
    [SqlServerFact]
    public async Task Package_Work_decides_the_exact_native_version_and_original_key_replays_after_a_later_pending_table()
    {
        foreach (var approve in new[] { true, false })
        {
            await using var run = await Run.StartAsync(); await PackageSafetyPrepare(run);
            await using var scope = new PackageDecisionScope(run, Guid.NewGuid());
            var execution = new WorkDecisionExecutionFenceV1();
            var authority = new WorkDecisionAuthorityV1(scope.Authority, execution);
            var service = new GuardedProductPackageStandardService(new(scope.Database, scope.ServiceCompany, scope.User,
                authority, scope.Collector, new FixedClock(PackageClockNow)), scope.Writer);
            var source = new ProductPackageStandardWorkInboxSourceV1(scope.Database,
                new(scope.ServiceCompany, scope.User, authority));
            var correlation = new CorrelationContext(); correlation.Set("SENTETIK-work-package", null, "Web", null, null);
            var host = new WorkDecisionHostV1(scope.ServiceCompany, scope.User, correlation, authority, [source],
                new PackagePurposeNoDelegations(), execution, null!, null!, productPackageStandards: service);
            var id = await scope.Database.PackageStandardVersions.Where(row => row.Version == 1).Select(row => row.VersionId).SingleAsync();
            var preview = await host.PreviewAsync(WorkDecisionHostV1.ProductPackageStandard, id, default);
            Assert.Equal("1", preview.SourceVersion); Assert.True(preview.Pending);
            Assert.Single(preview.ProductPackageStandard!.Entries); Assert.True(preview.ApprovalReasonRequired);
            var request = new WorkDecisionRequestV1("1", approve, "SENTETIK native reason",
                ProductPackageStandard: approve ? new("2026-11-16") : null);
            var result = await host.DecideAsync(WorkDecisionHostV1.ProductPackageStandard, id, request, "SENTETIK-work-key", default);
            Assert.Equal((approve ? "Approved" : "Rejected", false), (result.State, result.Replayed));
            var receipt = await scope.Decide(approve, "SENTETIK-work-key");
            Assert.True(receipt.IsReplay); Assert.NotNull(receipt.DecisionReceipt);
            Assert.Equal((id, 1L), (receipt.DecisionReceipt.VersionId, receipt.DecisionReceipt.Version));
            Assert.DoesNotContain("decisionReceipt", JsonSerializer.Serialize(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await using (var preparation = run.Request())
                Assert.Equal(204, (await PackageService(preparation, run.CreatorActorId, new FixedClock(PackageClockNow))
                    .SetAsync(ProductId, new("1", 13, "SENTETIK later version"), PackageMetadata(run.CreatorActorId, "later-work-table"))).Answer.StatusCode);
            var terminal = await host.PreviewAsync(WorkDecisionHostV1.ProductPackageStandard, id, default);
            Assert.False(terminal.Pending); Assert.Equal("1", terminal.SourceVersion);
            var replay = await host.DecideAsync(WorkDecisionHostV1.ProductPackageStandard, id, request, "SENTETIK-work-key", default);
            Assert.True(replay.Replayed); Assert.Equal(result.OriginalCorrelationId, replay.OriginalCorrelationId);
            var changed = await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(
                WorkDecisionHostV1.ProductPackageStandard, id, request with { Reason = "different body" }, "SENTETIK-work-key", default));
            Assert.Equal((409, ProductPackageStandardProblemCodesV2.IdempotencyConflict), (changed.Status, changed.Code));
            scope.Authority.Granted = false;
            Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(
                WorkDecisionHostV1.ProductPackageStandard, id, request, "SENTETIK-work-key", default))).Status);
            await PackageSafetyDurable(run, 1, 1, 1);
        }
    }

    private sealed class PackagePurposeNoDelegations : IWorkDelegationReaderV1
    {
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) =>
            Task.FromResult<WorkDelegationViewV1?>(null);
    }
}

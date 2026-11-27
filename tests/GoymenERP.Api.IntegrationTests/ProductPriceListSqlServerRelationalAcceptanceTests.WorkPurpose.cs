using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ProductPriceListSqlServerRelationalAcceptanceTests
{
    // Real native commands, SQL and actual audit writer; permission/delegation observations are controlled, not full Identity E2E.
    [SqlServerFact]
    public async Task Price_list_Work_publishes_exact_version_identity_and_original_key_replays_after_a_later_native_draft()
    {
        await using var run = await Run.StartAsync(); var id = await PublishSafetyPrepare(run);
        await using (var editor = run.Request(actor: Guid.NewGuid()))
            await editor.Service.ReviseDraftAsync(id, new("1", "SENTETIK revised", ProductPriceListWire.FormatDate(
                ProductPriceBusinessDayV2.DateOf(DateTimeOffset.UtcNow)), null, [new(Guid.NewGuid(), ProductId, Kg, "41000", "40000")]), Metadata("work-revise"));
        await using var scope = new PublishSafetyScope(run, Guid.NewGuid()); var rig = PricePurposeHost(scope);
        var target = await scope.Database.Versions.Where(row => row.PriceListId == id && row.VersionNo == 1).Select(row => row.VersionId).SingleAsync();
        var preview = await rig.Host.PreviewAsync(WorkDecisionHostV1.ProductPriceList, target, default);
        Assert.Equal("2", preview.SourceVersion); Assert.Equal(1, preview.ProductPriceList!.VersionNo);
        Assert.Equal("41000", Assert.Single(preview.ProductPriceList.Lines).UnitPrice);
        var request = new WorkDecisionRequestV1("2", true, null);
        var result = await rig.Host.DecideAsync(WorkDecisionHostV1.ProductPriceList, target, request, "work-publish", default);
        Assert.Equal(("Published", false), (result.State, result.Replayed));
        await using (var next = run.Request())
            await next.Service.StartVersionAsync(id, new("3", Guid.NewGuid(), ProductPriceListWire.FormatDate(
                ProductPriceBusinessDayV2.DateOf(DateTimeOffset.UtcNow).AddDays(1)), null), Metadata("work-later-draft"));
        var replay = await rig.Host.DecideAsync(WorkDecisionHostV1.ProductPriceList, target, request, "work-publish", default);
        Assert.True(replay.Replayed); Assert.Equal(result.OriginalCorrelationId, replay.OriginalCorrelationId);
        var changed = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(WorkDecisionHostV1.ProductPriceList,
            target, request with { ExpectedVersion = "3" }, "work-publish", default));
        Assert.Equal(503, changed.Status); // No original receipt for another expected header; never remap to the current header.
        await PublishSafetyDurable(run, id, true, 1, 1);
    }

    [SqlServerFact]
    public async Task Price_list_Work_native_post_save_delegation_refusal_rolls_back_real_audit_attempt_and_publication_then_original_retry_has_one_effect()
    {
        foreach (var fault in new[] { "delegation", "human-read" })
        {
            await using var run = await Run.StartAsync(); var id = await PublishSafetyPrepare(run);
            await using var scope = new PublishSafetyScope(run, Guid.NewGuid()); var rig = PricePurposeHost(scope);
            var target = await scope.Database.Versions.Where(row => row.PriceListId == id).Select(row => row.VersionId).SingleAsync();
            var request = new WorkDecisionRequestV1("1", true, null, Guid.NewGuid()); var observed = false;
            scope.Authority.Before = () =>
            {
                if (!scope.Probe.Saved) return;
                observed = true;
                if (fault == "delegation") rig.Delegations.Live = false;
                else rig.Authority.ReadGranted = false;
            };
            Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(WorkDecisionHostV1.ProductPriceList,
                target, request, "delegated-original", default))).Status);
            Assert.True(observed); scope.AssertClean(); await PublishSafetyDurable(run, id, false, 0, 0);
            scope.Reset(); rig.Delegations.Live = true; rig.Authority.ReadGranted = true;
            Assert.False((await rig.Host.DecideAsync(WorkDecisionHostV1.ProductPriceList, target, request, "delegated-original", default)).Replayed);
            await PublishSafetyDurable(run, id, true, 1, 1);
        }
    }

    private static (WorkDecisionHostV1 Host, PricePurposeDelegations Delegations, PricePurposeSqlAuthority Authority) PricePurposeHost(PublishSafetyScope scope)
    {
        var execution = new WorkDecisionExecutionFenceV1(); var delegations = new PricePurposeDelegations();
        var ownerAuthority = new PricePurposeSqlAuthority(scope.Authority);
        var authority = new WorkDecisionAuthorityV1(ownerAuthority, execution);
        var service = new GuardedProductPriceListService(new(scope.ServiceCompany, scope.User, authority,
            new(scope.Database, scope.ServiceCompany, scope.Collector, TimeProvider.System), TimeProvider.System, null, _ => true), scope.Writer);
        var source = new ProductPriceListVersionWorkInboxSourceV1(scope.Database, new(scope.ServiceCompany, scope.User, authority));
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-work-price", null, "Web", null, null);
        return (new(scope.ServiceCompany, scope.User, correlation, authority, [source], delegations, execution, null!, null!, productPriceLists: service), delegations, ownerAuthority);
    }
    private sealed class PricePurposeSqlAuthority(IErpAuthorityRevalidationService publish) : IErpAuthorityRevalidationService
    {
        public bool ReadGranted = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default) =>
            permission == ProductPriceListPermissionsV1.Publish ? publish.RevalidatePermissionScopeAsync(permission, company, token)
                : permission == ProductPriceListPermissionsV1.Read ? Task.FromResult(ReadGranted ? GrantedScope() : PermissionScope.Denied)
                : throw new InvalidOperationException("No extra permission in purpose read.");
    }
    private sealed class PricePurposeDelegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, TestCompanyIds.CompanyA, WorkDecisionHostV1.ProductPriceList,
                ProductPriceListPermissionsV1.Publish, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), DateTimeOffset.UtcNow, "SENTETIK controlled", null, null, null) : null);
    }
}

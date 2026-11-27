using System.Net;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    // Actual native owner + Inventory participant + immutable receipts. Synthetic authority/delegation,
    // no real assignments or public capability activation. SQL execution belongs to train acceptance.
    [SqlServerFact]
    public async Task Staged_reapproval_requires_explicit_labels_rolls_back_late_delegation_loss_and_replays_both_native_command_receipts_after_later_release()
    {
        await using var run = await Run.StartAsync();
        var creatorUser = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        using var creator = run.ClientFor(creatorUser, CompanyA);
        var roll = await run.StockedLabelAsync($"SYN-WP8-REAPPROVAL-{Guid.NewGuid():N}");
        var planId = Guid.NewGuid();
        using (var created = await creator.PostWithKeyAsync($"{Base}/plans", new SavePlanRequestV1(planId,
            await run.WorkCenterOfAsync(CompanyA), [new PlannedInputRequestV1(roll.Id, "600.125", roll.Version)], null)))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await run.EnterTerminAsync(planId);
        var actor = Guid.NewGuid(); var delegation = new FirstPurposeDelegation();
        var saved = new FirstPurposeApprovalSaved(() => delegation.Allowed = false) { Armed = false };
        var raw = new FirstPurposeAuthority();
        await using var staged = run.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUser>(); services.AddScoped<ICurrentUser>(_ => new FirstPurposeActor(actor));
            services.RemoveAll<IWorkDelegationReaderV1>(); services.AddScoped<IWorkDelegationReaderV1>(_ => delegation);
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddScoped<IErpAuthorityRevalidationService>(provider => new WorkDecisionAuthorityV1(raw,
                provider.GetRequiredService<WorkDecisionExecutionFenceV1>()));
            services.AddDbContext<ManufacturingDbContext>((_, options) => options.AddInterceptors(saved));
        }));
        async Task<T> Scoped<T>(string correlation, Func<IServiceProvider, Task<T>> execute)
        {
            await using var scope = staged.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set(correlation, null, "Web", null, null);
            return await execute(scope.ServiceProvider);
        }
        Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1 request, string key, string correlation) =>
            Scoped(correlation, provider => provider.GetRequiredService<WorkDecisionHostV1>()
                .DecideAsync(WorkDecisionHostV1.ManufacturingPlan, planId, request, key, default));
        async Task ReleaseOrder(string key)
        {
            var orderId = Guid.NewGuid();
            var created = await Scoped("SENTETIK-create-" + key, provider => provider.GetRequiredService<ManufacturingCommandService>()
                .CreateOrderAsync(new(orderId, planId), "order-" + key, ManufacturingPermissionManifestV1.OrdersCreate, default));
            Assert.Equal(ManufacturingApiStatus.Created, created.Status);
            var cancelled = await Scoped("SENTETIK-cancel-" + key, provider => provider.GetRequiredService<ManufacturingCommandService>()
                .CancelOrderAsync(orderId, new(created.Resource!.Version, "SENTETIK release"), "cancel-" + key,
                    ManufacturingPermissionManifestV1.OrdersCancel, default));
            Assert.Equal(ManufacturingApiStatus.Ok, cancelled.Status);
        }
        var initial = new WorkDecisionRequestV1("1", true, null, delegation.Id);
        Assert.Equal("Approved", (await Decide(initial, "initial-approval", "SENTETIK-initial")).State);
        await ReleaseOrder("first");
        var releasedVersion = await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id=@p", ("@p", planId));
        var preview = await Scoped("SENTETIK-preview", provider => provider.GetRequiredService<WorkDecisionHostV1>()
            .PreviewAsync(WorkDecisionHostV1.ManufacturingPlan, planId, default));
        Assert.True(preview.Pending); Assert.False(preview.RejectionSupported);
        Assert.Equal([roll.Id], preview.ManufacturingPlanReapproval!.ParentRollIds);
        var oldInitial = await Decide(initial, "initial-approval", "SENTETIK-initial-replay");
        Assert.Equal(("Approved", true, "SENTETIK-initial"), (oldInitial.State, oldInitial.Replayed, oldInitial.OriginalCorrelationId));
        var selection = new WorkDecisionRequestV1(releasedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), true,
            null, delegation.Id, new([roll.Id]));
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => Decide(selection with { ManufacturingPlanReapproval = null }, "missing", "SENTETIK-missing"))).Status);
        foreach (var (labels, key, status) in new (IReadOnlyList<Guid>?, string, int)[]
        {
            (null, "null", 400), ([], "empty", 400), ([Guid.Empty], "empty-id", 400),
            ([roll.Id, roll.Id], "duplicate", 400), ([Guid.NewGuid()], "foreign", 422),
        })
            Assert.Equal(status, (await Assert.ThrowsAsync<WorkDecisionException>(() => Decide(selection with { ManufacturingPlanReapproval = new(labels) }, key, "SENTETIK-" + key))).Status);
        var allocationRowsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId=@p", ("@p", planId));
        var generationRowsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanInputAllocations WHERE PlanId=@p", ("@p", planId));
        saved.Armed = true;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => Decide(selection, "reapproval", "SENTETIK-abort"))).Status);
        Assert.True(saved.Fired);
        Assert.Equal(releasedVersion, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id=@p", ("@p", planId)));
        Assert.Equal(allocationRowsBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId=@p", ("@p", planId)));
        Assert.Equal(generationRowsBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanInputAllocations WHERE PlanId=@p", ("@p", planId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanReapprovals WHERE PlanId=@p", ("@p", planId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands WHERE CommandName=@n", ("@n", ManufacturingCommandNames.ReapprovePlan)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'manufacturing.plan.reapproved.v1' AND EntityId=@p", ("@p", planId.ToString("D"))));
        delegation.Allowed = true; saved.Armed = false;
        var result = await Decide(selection, "reapproval", "SENTETIK-reapproved");
        Assert.Equal(("Approved", false, "SENTETIK-reapproved"), (result.State, result.Replayed, result.OriginalCorrelationId));
        await ReleaseOrder("later");
        var versionAfterLaterRelease = await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id=@p", ("@p", planId));
        var replay = await Decide(selection, "reapproval", "SENTETIK-reapproval-replay");
        Assert.Equal(("Approved", true, "SENTETIK-reapproved"), (replay.State, replay.Replayed, replay.OriginalCorrelationId));
        Assert.Equal(409, (await Assert.ThrowsAsync<WorkDecisionException>(() => Decide(selection with { ManufacturingPlanReapproval = new([Guid.NewGuid()]) }, "reapproval", "SENTETIK-changed-selection"))).Status);
        Assert.Equal(versionAfterLaterRelease, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id=@p", ("@p", planId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanReapprovals WHERE PlanId=@p", ("@p", planId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'manufacturing.plan.reapproved.v1' AND EntityId=@p", ("@p", planId.ToString("D"))));
    }
}

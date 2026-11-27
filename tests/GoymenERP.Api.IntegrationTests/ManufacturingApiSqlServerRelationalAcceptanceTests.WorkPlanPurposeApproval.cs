using System.Net;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    // Existing real SQL/API fixture, native owner/source/allocation host and Inventory participant.
    // Authority and delegation are controlled test answers; public capability/UI remains staged closed.
    [SqlServerFact]
    public async Task Staged_first_plan_approval_uses_native_allocation_transaction_and_replays_only_its_original_plan_receipt()
    {
        await using var run = await Run.StartAsync();
        var creatorUser = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        using var creator = run.ClientFor(creatorUser, CompanyA);
        var roll = await run.StockedLabelAsync($"SYN-WP8-FIRST-APPROVAL-{Guid.NewGuid():N}");
        var planId = Guid.NewGuid();
        using (var created = await creator.PostWithKeyAsync($"{Base}/plans",
            new SavePlanRequestV1(planId, await run.WorkCenterOfAsync(CompanyA),
                [new PlannedInputRequestV1(roll.Id, "600.125", roll.Version)], null)))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await run.EnterTerminAsync(planId);
        var actor = Guid.NewGuid(); var delegation = new FirstPurposeDelegation();
        var saved = new FirstPurposeApprovalSaved(() => delegation.Allowed = false);
        var raw = new FirstPurposeAuthority();
        await using var staged = run.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUser>(); services.AddScoped<ICurrentUser>(_ => new FirstPurposeActor(actor));
            services.RemoveAll<IWorkDelegationReaderV1>(); services.AddScoped<IWorkDelegationReaderV1>(_ => delegation);
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddScoped<IErpAuthorityRevalidationService>(provider =>
                new WorkDecisionAuthorityV1(raw, provider.GetRequiredService<WorkDecisionExecutionFenceV1>()));
            services.AddDbContext<ManufacturingDbContext>((_, options) => options.AddInterceptors(saved));
        }));
        async Task<WorkDecisionResultV1> Decide(string correlation)
        {
            await using var scope = staged.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set(correlation, null, "Web", null, null);
            return await scope.ServiceProvider.GetRequiredService<WorkDecisionHostV1>().DecideAsync(
                WorkDecisionHostV1.ManufacturingPlan, planId, new("1", true, null, delegation.Id), "staged-first-plan", default);
        }
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => Decide("SENTETIK-abort"))).Status);
        Assert.True(saved.Fired);
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id=@p", ("@p", planId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId=@p", ("@p", planId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands WHERE CommandName=@n", ("@n", ManufacturingCommandNames.ApprovePlan)));
        delegation.Allowed = true; saved.Armed = false;
        var first = await Decide("SENTETIK-first");
        Assert.Equal((CompanyA, planId, "Approved", false, "SENTETIK-first"),
            (first.CompanyId, first.ItemId, first.State, first.Replayed, first.OriginalCorrelationId));
        var replay = await Decide("SENTETIK-replay");
        Assert.Equal(("Approved", true, "SENTETIK-first"), (replay.State, replay.Replayed, replay.OriginalCorrelationId));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Plans WHERE Id=@p", ("@p", planId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId=@p", ("@p", planId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanInputAllocations WHERE PlanId=@p", ("@p", planId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'manufacturing.plan.approved.v1' AND EntityId=@p",
            ("@p", planId.ToString("D"))));
        Assert.DoesNotContain(raw.Asked, permission => permission.Contains("price", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FirstPurposeActor(Guid actor) : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([CompanyA]);
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class FirstPurposeAuthority : IErpAuthorityRevalidationService
    {
        public List<string> Asked { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Asked.Add(permission);
            return Task.FromResult(new PermissionScope(companyId == CompanyA, true, new HashSet<string>(),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
    private sealed class FirstPurposeDelegation : IWorkDelegationReaderV1
    {
        public readonly Guid Id = Guid.NewGuid(); public bool Allowed = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Allowed && id == Id ? new(Id, CompanyA, item.Kind,
                ManufacturingPermissionManifestV1.PlansApprove, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), DateTimeOffset.UtcNow, "SENTETIK", null, null, null) : null);
    }
    private sealed class FirstPurposeApprovalSaved(Action afterSave) : SaveChangesInterceptor
    {
        public bool Armed = true, Fired;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is ManufacturingDbContext context
                && context.Plans.Local.Any(row => row.Status == (int)GoymenERP.Modules.Manufacturing.Domain.Plans.PlanStatus.Approved))
            { Fired = true; afterSave(); }
            return ValueTask.FromResult(result);
        }
    }
}

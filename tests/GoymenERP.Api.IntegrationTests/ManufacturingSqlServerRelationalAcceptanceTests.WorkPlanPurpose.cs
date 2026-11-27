using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Native owner/source SQL proofs for a staged purpose. Synthetic authority/delegation answers;
/// not real assignments, published Work UI, Inventory approval or re-approval acceptance.</summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Staged_plan_purpose_native_reject_post_save_delegation_loss_rolls_back_and_same_key_retries_then_replays()
    {
        await using var run = await Run.StartAsync();
        var plan = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var seed = run.Store(CompanyA)) await seed.Plans.SavePlanAsync(plan, Metadata(run));
        var delegations = new PlanPurposeDelegations();
        var saved = new PlanPurposeSaved(() => delegations.Allowed = false);
        await using (var denied = run.Store(CompanyA, saved))
        {
            var error = await Assert.ThrowsAsync<WorkDecisionException>(() => PlanPurposeHost(denied, CompanyA, run.Approver, delegations, "SENTETIK-abort")
                .DecideAsync(WorkDecisionHostV1.ManufacturingPlan, plan.Id, new("1", false, "SENTETIK red", delegations.Id), "plan-purpose-key", default));
            Assert.Equal(403, error.Status); Assert.Equal(1, saved.Saves);
            Assert.Empty(denied.Context.ChangeTracker.Entries());
        }
        await using (var verify = run.Context(CompanyA))
        {
            Assert.Equal(1L, (await verify.Plans.SingleAsync(row => row.Id == plan.Id)).Version);
            Assert.Empty(await verify.PlanRejections.Where(row => row.PlanId == plan.Id).ToListAsync());
            Assert.Empty(await verify.Commands.Where(row => row.CommandName == ManufacturingCommandNames.RejectPlan).ToListAsync());
        }
        delegations.Allowed = true;
        await using (var retry = run.Store(CompanyA))
        {
            var result = await PlanPurposeHost(retry, CompanyA, run.Approver, delegations, "SENTETIK-first")
                .DecideAsync(WorkDecisionHostV1.ManufacturingPlan, plan.Id, new("1", false, "SENTETIK red", delegations.Id), "plan-purpose-key", default);
            Assert.Equal("Draft", result.State); Assert.False(result.Replayed);
        }
        await using (var replay = run.Store(CompanyA))
        {
            var result = await PlanPurposeHost(replay, CompanyA, run.Approver, delegations, "SENTETIK-second")
                .DecideAsync(WorkDecisionHostV1.ManufacturingPlan, plan.Id, new("1", false, "SENTETIK red", delegations.Id), "plan-purpose-key", default);
            Assert.Equal(("Draft", true, "SENTETIK-first"), (result.State, result.Replayed, result.OriginalCorrelationId));
        }
        await using var final = run.Context(CompanyA);
        Assert.Equal(2L, (await final.Plans.SingleAsync(row => row.Id == plan.Id)).Version);
        Assert.Single(await final.PlanRejections.Where(row => row.PlanId == plan.Id).ToListAsync());
        Assert.Single(await final.Commands.Where(row => row.CommandName == ManufacturingCommandNames.RejectPlan).ToListAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'manufacturing.plan.rejected.v1' AND EntityId=@p",
            ("@p", plan.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Staged_plan_purpose_reads_native_released_order_metadata_and_rejects_foreign_company_without_selecting_input_labels()
    {
        await using var run = await Run.StartAsync(); var (plan, orderId) = await run.ApprovedOrderAsync(CompanyA);
        await using (var writer = run.Store(CompanyA))
            await writer.Corrections.CancelOrderAsync(orderId, 1, run.Actor, At.AddHours(1), "SENTETIK cancel", Metadata(run));
        await using (var reader = run.Store(CompanyA))
        {
            var native = await reader.Plans.FindPlanAsync(plan.Id);
            Assert.NotNull(native); Assert.True(native.RequiresReapproval);
            var company = new CompanyContext(); company.Set(CompanyA);
            var source = new ManufacturingPlanWorkInboxSourceV1(reader.Context,
                new ManufacturingWorkInboxFence(company, new PlanPurposeUser(run.Approver), new PlanPurposeAuthority()));
            var snapshot = await source.ReadDecisionAsync(new(CompanyA, run.Approver.ToString("D"), "SENTETIK-read", null), plan.Id, default);
            Assert.NotNull(snapshot); Assert.True(snapshot.Pending); Assert.Equal(native.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), snapshot.SourceVersion);
            Assert.Equal(ManufacturingPlanWorkInboxSourceV1.ReapprovalRule, Assert.Single(snapshot.Item.Rules).RuleCode);
            Assert.Equal(orderId, Assert.Single(snapshot.Item.References).RecordId);
        }
        await using var foreign = run.Store(CompanyB);
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkDecisionException>(() => PlanPurposeHost(foreign, CompanyB, run.Approver, new(), "SENTETIK-foreign")
            .PreviewAsync(WorkDecisionHostV1.ManufacturingPlan, plan.Id, default))).Status);
    }

    private static WorkDecisionHostV1 PlanPurposeHost(StoreScope scope, Guid companyId, Guid actor, PlanPurposeDelegations delegations, string correlationId)
    {
        var company = new CompanyContext(); company.Set(companyId);
        var user = new PlanPurposeUser(actor); var correlation = new CorrelationContext(); correlation.Set(correlationId, null, "Web", null, null);
        var execution = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(new PlanPurposeAuthority(), execution);
        var source = new ManufacturingPlanWorkInboxSourceV1(scope.Context, new ManufacturingWorkInboxFence(company, user, authority));
        var native = new ManufacturingCommandService(scope.Plans, scope.Orders, scope.Corrections, new ManufacturingMassVerifier(null!),
            authority, company, user, correlation, TimeProvider.System, planLifecycle: scope.Lifecycle);
        return new(company, user, correlation, authority, [source], delegations, execution, null!, null!, manufacturingPlans: native);
    }
    private sealed class PlanPurposeUser(Guid actor) : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([CompanyA, CompanyB]);
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class PlanPurposeAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == ManufacturingPermissionManifestV1.PlansApprove
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
    }
    private sealed class PlanPurposeDelegations : IWorkDelegationReaderV1
    {
        public readonly Guid Id = Guid.NewGuid(); public bool Allowed = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Allowed && id == Id ? new(Id, CompanyA, item.Kind, ManufacturingPermissionManifestV1.PlansApprove,
                Guid.NewGuid(), Guid.NewGuid(), At.AddDays(-1), At.AddDays(1), "1", Guid.NewGuid(), At, "SENTETIK", null, null, null) : null);
    }
    private sealed class PlanPurposeSaved(Action afterSave) : SaveChangesInterceptor
    {
        public int Saves;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { Saves++; afterSave(); return ValueTask.FromResult(result); }
    }
}

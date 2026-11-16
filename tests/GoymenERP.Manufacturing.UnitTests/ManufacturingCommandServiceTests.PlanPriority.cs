using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Fact]
    public async Task Priority_changes_use_the_existing_approval_grant_and_replay_the_original_score_without_another_write()
    {
        var fixture = new Fixture(); var plan = AvailabilityPlan(fixture);
        var source = new PriorityStore(fixture, plan); var service = PriorityService(fixture, source);
        var request = new ChangePlanPriorityRequestV1(plan.Version, 0, 7, "Synthetic priority reason");
        var first = await service.ChangePlanPriorityAsync(plan.Id, request, "priority-1", CancellationToken.None);
        var replay = await service.ChangePlanPriorityAsync(plan.Id, request, "priority-1", CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Ok, 7, 1L), (first.Status, first.Resource!.PriorityScore, first.Resource.PriorityRevision));
        Assert.True(replay.IsReplay); Assert.Equal(first.Resource, replay.Resource); Assert.Equal(1, source.Writes);
        Assert.Equal(1L, plan.Version);
        Assert.All(fixture.Authority.Permissions, permission => Assert.Equal(ErpPermissions.ManufacturingPlansApprove, permission));
        Assert.Equal(ErpPermissions.ManufacturingPlansApprove, source.Metadata!.PermissionCode);
    }

    [Fact]
    public async Task Revoking_approval_before_the_write_never_reaches_the_priority_store()
    {
        var fixture = new Fixture(); var plan = AvailabilityPlan(fixture); var source = new PriorityStore(fixture, plan);
        fixture.Authority.Scopes.Enqueue(Scope(true)); fixture.Authority.Scopes.Enqueue(PermissionScope.Denied);
        var answer = await PriorityService(fixture, source).ChangePlanPriorityAsync(plan.Id,
            new(plan.Version, 0, 3, "Synthetic priority"), "priority-denied", CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Denied, answer.Status); Assert.Equal(0, source.Writes);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task A_priority_read_discards_foreign_answers_and_rechecks_the_read_grant()
    {
        var fixture = new Fixture(); var plan = AvailabilityPlan(fixture); var source = new PriorityStore(fixture, plan) { Foreign = true };
        var answer = await PriorityService(fixture, source).GetPlanPriorityAsync(plan.Id, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Unavailable, answer.Status); Assert.Null(answer.Resource);
        source.AfterRead = () => fixture.Authority.Scope = PermissionScope.Denied;
        answer = await PriorityService(fixture, source).GetPlanPriorityAsync(plan.Id, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Denied, answer.Status); Assert.Null(answer.Resource);
    }

    [Fact]
    public async Task The_queue_read_exposes_the_server_clamped_remaining_and_nonblocking_over_plan_flag_once_per_plan()
    {
        var fixture = new Fixture(); var plan = AvailabilityPlan(fixture);
        var target = new ProductionQueueEntryV1(plan.Id, null, 2, At, new(10m), new(11m), 4);
        var queue = new SuggestionQueuePort { Answer = _ => new([target], target, false) };
        var service = new ManufacturingCommandService(fixture.Store, fixture.Store, fixture.Store,
            new ManufacturingMassVerifier(fixture.Settings), fixture.Authority, fixture.CompanyContext, fixture.User,
            fixture.Correlation, new FixedTime(), workCenterQueues: queue);
        var answer = await service.GetPlanWorkCenterQueueAsync(plan.Id, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, answer.Status);
        var line = Assert.Single(answer.Resource!.Items);
        Assert.Equal(("10", "11", "0", true, false, 4L),
            (line.PlannedKg, line.ProcessedKg, line.RemainingKg, line.IsOverPlanned, line.IsQueued, line.PriorityRevision));
        Assert.Empty(fixture.Store.Commands);
    }

    private static ManufacturingCommandService PriorityService(Fixture fixture, PriorityStore source) =>
        new(fixture.Store, fixture.Store, fixture.Store, new ManufacturingMassVerifier(fixture.Settings), fixture.Authority,
            fixture.CompanyContext, fixture.User, fixture.Correlation, new FixedTime(), planPriorities: source);

    private sealed class PriorityStore(Fixture fixture, ProductionPlan plan) : IManufacturingPlanPriorityStore
    {
        public bool Foreign { get; init; }
        public Action? AfterRead { get; set; }
        public int Writes { get; private set; }
        public ManufacturingCommandMetadata? Metadata { get; private set; }
        private ProductionPlanPriorityV1 current = ProductionPlanPriorityV1.Default;
        private PlanPriorityViewV1 View() => new(Foreign ? OtherCompany : Company, plan.Id, plan.Version,
            current.Score, current.Revision, current.Revision == 0 ? null : Actor.ToString(), current.Revision == 0 ? null : At,
            current.Revision == 0 ? null : "Synthetic priority reason");
        public Task<PlanPriorityViewV1?> FindPlanPriorityAsync(Guid id, CancellationToken cancellationToken = default)
        { AfterRead?.Invoke(); return Task.FromResult<PlanPriorityViewV1?>(id == plan.Id ? View() : null); }
        public Task<PlanPriorityViewV1> ChangePlanPriorityAsync(Guid id, long version, long revision, int score,
            string reason, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            if (version != plan.Version) throw new ManufacturingVersionConflictException(version, plan.Version);
            current = current.Change(revision, score, reason); Writes++; Metadata = metadata;
            var result = View(); var command = metadata.Completion!(result);
            fixture.Store.Commands.Add((command.CommandName, command.IdempotencyKeyHash), command);
            return Task.FromResult(result);
        }
    }
}

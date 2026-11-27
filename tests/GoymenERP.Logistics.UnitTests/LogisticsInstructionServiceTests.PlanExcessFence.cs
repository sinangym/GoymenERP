using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Logistics.UnitTests;

public sealed partial class LogisticsInstructionServiceTests
{
    [Fact]
    public async Task Native_plan_approval_and_immutable_replay_both_reach_the_owned_plan_excess_boundary_without_second_mutation()
    {
        var world = new World(); var runner = new FakeRealizationRunner(); var service = world.Service(runner);
        var id = Guid.NewGuid(); var request = new ApproveLogisticsPlanExcessV1("1", "3");
        var original = await service.ApproveAsync(id, request, Metadata("approval-fence-key"));
        world.Store.PlanReplayAnswer = original with { IsReplay = true };
        var replay = await service.ApproveAsync(id, request, Metadata("approval-fence-key"));
        Assert.True(replay.IsReplay); Assert.Equal(original.Exception, replay.Exception);
        Assert.Equal(1, world.Store.PlanApprovalWrites); Assert.Equal(2, runner.Commits);
    }

    [Theory]
    [InlineData("actor")] [InlineData("company")] [InlineData("permission")]
    public async Task Replay_owner_read_losing_fresh_native_authority_never_commits_or_reexecutes_approval(string lost)
    {
        var world = new World(); var runner = new FakeRealizationRunner(); var service = world.Service(runner);
        var id = Guid.NewGuid(); var request = new ApproveLogisticsPlanExcessV1("1", "3");
        var original = await service.ApproveAsync(id, request, Metadata("approval-replay-fence"));
        world.Store.PlanReplayAnswer = original with { IsReplay = true };
        world.Store.BeforePlanReplay = () =>
        {
            if (lost == "actor") world.User.CurrentActor = "SENTETIK-other";
            else if (lost == "company") world.Company.Set(Guid.NewGuid());
            else world.Authority.Granted = false;
        };
        Assert.Equal(403, (await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => service.ApproveAsync(id, request, Metadata("approval-replay-fence")))).Status);
        Assert.Equal(1, runner.Commits); Assert.Equal(1, world.Store.PlanApprovalWrites);
    }
}

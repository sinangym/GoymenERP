using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Logistics.UnitTests;

public sealed partial class LogisticsInstructionServiceTests
{
    [Fact]
    public async Task A_plan_exception_approver_needs_only_its_own_permission_and_may_be_the_proposer()
    {
        var world = new World(); world.Authority.DeniedPermission = ReadPermission;
        var runner = new FakeRealizationRunner(); var service = world.Service(runner);
        var page = await service.ListAsync("approve", null, null);
        Assert.Empty(page.Items);
        var id = Guid.NewGuid(); var response = await service.ApproveAsync(id, new("1", "3"), Metadata("plan-approve"));
        Assert.Equal((id, Actor, Actor, "Approved"), (response.Exception.ExceptionId, response.Exception.ProposedBy, response.Exception.ApprovedBy, response.Exception.Status));
        Assert.Equal(LogisticsPlanExcessV1.ApprovePermission, world.Authority.LastPermission);
        Assert.Equal(0, world.Accounts.Calls); Assert.Equal(0, world.OrderLines.Calls); Assert.Equal(0, world.Settings.Calls);
        Assert.Equal(1, runner.Commits);
    }
    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    [InlineData("permission")]
    public async Task A_plan_exception_reply_losing_current_authority_is_never_published(string lost)
    {
        var world = new World(); var runner = new FakeRealizationRunner();
        world.Store.AfterPlanApproval = () =>
        {
            if (lost == "actor") world.User.CurrentActor = "SENTETIK-other";
            else if (lost == "company") world.Company.Set(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"));
            else world.Authority.Granted = false;
        };
        var error = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service(runner).ApproveAsync(Guid.NewGuid(), new("1", "3"), Metadata("plan-loss")));
        Assert.Equal(403, error.Status); Assert.Equal(0, runner.Commits);
    }
    private sealed partial class FakeStore : ILogisticsPlanExcessStoreV1
    {
        public Action? AfterPlanApproval { get; set; }
        public Task<LogisticsPlanExcessPageV1> ListPlanExcessAsync(Guid? instructionId, string? cursor, CancellationToken token) => Task.FromResult(new LogisticsPlanExcessPageV1([], null));
        public Task<LogisticsPlanExcessResultV1?> FindPlanExcessReplayAsync(LogisticsWriteEnvelope envelope, string hash, string command, CancellationToken token) => Task.FromResult<LogisticsPlanExcessResultV1?>(null);
        public Task<LogisticsPlanExcessResultV1> ProposePlanExcessAsync(LogisticsWriteEnvelope envelope, string hash, Guid instructionId, Guid lineId, ProposeLogisticsPlanExcessV1 request, CancellationToken token) => throw new NotImplementedException();
        public Task<LogisticsPlanExcessResultV1> ApprovePlanExcessAsync(LogisticsWriteEnvelope envelope, string hash, Guid exceptionId, ApproveLogisticsPlanExcessV1 request, CancellationToken token)
        {
            AfterPlanApproval?.Invoke(); var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new LogisticsPlanExcessResultV1(new(Company, exceptionId, Guid.NewGuid(), Guid.NewGuid(), Order, OrderLine, KgUnit,
                "2", "3", "Approved", "100", "20", "0", "0", "SENTETIK", Actor, now, Actor, now, "20", "0"), false, envelope.CorrelationId));
        }
    }
}

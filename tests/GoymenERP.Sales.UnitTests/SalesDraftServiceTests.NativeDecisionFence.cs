using System.Data;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData(true, "store-read")]
    [InlineData(false, "store-read")]
    [InlineData(true, "prestage")]
    [InlineData(false, "prestage")]
    [InlineData(true, "postsave")]
    [InlineData(false, "postsave")]
    public async Task Native_order_decision_rechecks_permission_inside_the_store_and_before_commit(bool approve, string phase)
    {
        var world = new World();
        var created = await world.Service().CreateDraftAsync(Request(), Metadata("decision-create"));
        var id = created.Detail.Draft.Id;
        var submitted = await world.Service().SubmitDraftAsync(id, Command(created.Detail.Draft.Version), Metadata("decision-submit"));
        var before = (await world.Store.FindDraftAsync(id))!;
        var expected = (before.Version, before.ApprovalState, before.Approval);
        var warnings = world.Store.RiskWarnings.Count;
        var attempts = world.Store.Attempts;
        Action revoke = () => world.Authority.Granted = false;
        if (phase == "store-read") world.Store.OnStore = revoke;
        if (phase == "prestage") world.Store.BeforeDecisionStage = revoke;
        if (phase == "postsave") world.Store.AfterDecisionSave = revoke;
        var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => WithIndependentApproverAsync(world, () => approve
            ? world.Service().ApproveDraftAsync(id, Command(submitted.Detail.Draft.Version), Metadata("decision-write"))
            : world.Service().RejectDraftAsync(id, Command(submitted.Detail.Draft.Version) with { Reason = "SENTETIK" }, Metadata("decision-write"))));
        Assert.Equal(403, error.Status);
        var unchanged = (await world.Store.FindDraftAsync(id))!;
        Assert.Equal(expected, (unchanged.Version, unchanged.ApprovalState, unchanged.Approval));
        Assert.DoesNotContain(world.Store.Transitions, x => x.Command is SalesDraftCommands.ApproveV1 or SalesDraftCommands.RejectV1);
        Assert.Equal(warnings, world.Store.RiskWarnings.Count);
        Assert.Equal(attempts, world.Store.Attempts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Native_order_decision_refuses_actor_drift_at_postsave_boundary(bool approve)
    {
        var world = new World();
        var created = await world.Service().CreateDraftAsync(Request(), Metadata("actor-create"));
        var submitted = await world.Service().SubmitDraftAsync(created.Detail.Draft.Id, Command(created.Detail.Draft.Version), Metadata("actor-submit"));
        world.Store.AfterDecisionSave = () => world.User.Id = Actor;
        var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => WithIndependentApproverAsync(world, () => approve
            ? world.Service().ApproveDraftAsync(created.Detail.Draft.Id, Command(submitted.Detail.Draft.Version), Metadata("actor-decision"))
            : world.Service().RejectDraftAsync(created.Detail.Draft.Id, Command(submitted.Detail.Draft.Version) with { Reason = "SENTETIK" }, Metadata("actor-decision"))));
        Assert.Equal(403, error.Status);
        Assert.Equal(submitted.Detail.Draft.Version, (await world.Store.FindDraftAsync(created.Detail.Draft.Id))!.Version.ToString());
        Assert.DoesNotContain(world.Store.Transitions, x => x.Command is SalesDraftCommands.ApproveV1 or SalesDraftCommands.RejectV1);
    }

    private sealed partial class FakeStore
    {
        public Action? BeforeDecisionStage, AfterDecisionSave;
        public async Task<SalesDraftStateResult?> UpdateDraftDecisionWithFenceAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            string commandName, Guid draftId, Action<SalesOrderDraft, DateTimeOffset> mutate, SalesDraftStateAudit audit,
            Func<SalesOrderDraft, CancellationToken, Task> freshDecisionFence,
            Func<SalesOrderDraft, int?, IDbTransaction, CancellationToken, Task>? withinTransaction = null,
            CancellationToken cancellationToken = default, SalesOrderRiskAssessment? riskWarning = null)
        {
            var result = await UpdateDraftStateAsyncCore(envelope, requestHash, commandName, draftId, mutate, audit, null,
                cancellationToken, withinTransaction: withinTransaction is null ? null : async (draft, accepted, token) =>
                {
                    using var transaction = new SyntheticStateTransaction();
                    await withinTransaction(draft, accepted, transaction, token);
                }, freshDecisionFence: freshDecisionFence);
            if (riskWarning is not null && result is { Outcome: SalesDraftWriteOutcome.Recorded } recorded)
                RiskWarnings.Add((commandName, draftId, recorded.Draft.Version, recorded.Draft.Revision, riskWarning, envelope));
            return result;
        }
    }
}

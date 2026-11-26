using System.Data;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Future_demand_is_unchanged_on_edit_submit_internal_approval_and_customer_rejection()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        var participant = Assert.IsType<FakeFutureDemandReduction>(world.FutureDemandReduction);
        await world.Service().ReviseQuantityAsync(approved.Id,
            new("5", "SENTETIK azaltma", [new(line, "1")]), Metadata("future-revision"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("future-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("future-approve")));
        await world.Service().RecordCustomerRejectionAsync(approved.Id, Command("8", "SENTETIK ret"), Metadata("future-reject"));
        Assert.Empty(participant.Requests);
    }

    [Fact]
    public async Task Customer_acceptance_reduces_only_the_new_open_demand_and_the_same_key_replays_without_releasing_twice()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        var (_, loading) = Owners(world);
        loading.Answer = order => new(Company, order, [new(line, KgUnit, 1, 0.25m, 0.25m, 0.25m, 0m)]);
        var participant = Assert.IsType<FakeFutureDemandReduction>(world.FutureDemandReduction);
        await world.Service().ReviseQuantityAsync(approved.Id,
            new("5", "SENTETIK azaltma", [new(line, "1")]), Metadata("future-revision"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("future-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("future-approve")));
        var accepted = await world.Service().RecordCustomerAcceptanceAsync(approved.Id, Command("8"), Metadata("future-accept"));
        var request = Assert.Single(participant.Requests);
        var demand = Assert.Single(request.Lines);
        Assert.Equal((approved.Id, 1, line, KgUnit, "0.75"),
            (request.OrderId, request.Revision, demand.OrderLineId, demand.DemandUnitId, demand.NewOpenQuantityText));
        Assert.Equal((SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord, "future-accept"),
            (request.Envelope.PermissionCode, request.Envelope.IdempotencyKey));
        Assert.Equal("9", accepted.Detail.Draft.Version);
        var replay = await world.Service().RecordCustomerAcceptanceAsync(approved.Id, Command("8"), Metadata("future-accept"));
        Assert.True(replay.IsReplay);
        Assert.Single(participant.Requests);
    }

    [Fact]
    public async Task A_customer_rejected_revision_is_not_the_quantity_basis_of_a_later_accepted_reduction()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        var participant = Assert.IsType<FakeFutureDemandReduction>(world.FutureDemandReduction);
        await world.Service().ReviseQuantityAsync(approved.Id,
            new("5", "SENTETIK reddedilecek artış", [new(line, "2")]), Metadata("future-rejected-revision"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("future-first-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("future-first-approve")));
        await world.Service().RecordCustomerRejectionAsync(approved.Id, Command("8", "SENTETIK ret"), Metadata("future-first-reject"));
        await world.Service().ReviseQuantityAsync(approved.Id,
            new("9", "SENTETIK kabul edilecek miktar", [new(line, "1.5")]), Metadata("future-second-revision"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("10"), Metadata("future-second-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(approved.Id, Command("11"), Metadata("future-second-approve")));
        await world.Service().RecordCustomerAcceptanceAsync(approved.Id, Command("12"), Metadata("future-second-accept"));
        // 1.5 is lower than the rejected 2, but higher than the effective accepted 1.25: no release.
        Assert.Empty(participant.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Owner_refusals_roll_back_acceptance_and_only_terminal_ones_are_stored(bool terminal)
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        var participant = Assert.IsType<FakeFutureDemandReduction>(world.FutureDemandReduction);
        await world.Service().ReviseQuantityAsync(approved.Id,
            new("5", "SENTETIK azaltma", [new(line, "1")]), Metadata("future-revision"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("future-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("future-approve")));
        participant.Failure = new SalesFutureAllocationReleaseException(409, "synthetic_future_refusal", terminal);
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordCustomerAcceptanceAsync(approved.Id, Command("8"), Metadata("future-accept")));
        Assert.Equal((409, "synthetic_future_refusal"), (refused.Status, refused.Code));
        var unchanged = (await world.Store.FindDraftAsync(approved.Id))!;
        Assert.Equal((8L, 0), (unchanged.Version, unchanged.CustomerAcceptance!.Revision));
        Assert.Equal(terminal ? 1 : 0, world.Store.Refusals.Count);
        participant.Failure = null;
        if (terminal)
        {
            await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RecordCustomerAcceptanceAsync(
                approved.Id, Command("8"), Metadata("future-accept")));
            Assert.Single(participant.Requests);
        }
        else
        {
            await world.Service().RecordCustomerAcceptanceAsync(approved.Id, Command("8"), Metadata("future-accept"));
            Assert.Equal(2, participant.Requests.Count);
        }
    }

    [Fact]
    public async Task Missing_participant_leaves_acceptance_unchanged_and_the_same_key_can_retry_after_composition()
    {
        var world = new World();
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        await world.Service().ReviseQuantityAsync(approved.Id,
            new("5", "SENTETIK azaltma", [new(line, "1")]), Metadata("future-revision"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("future-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("future-approve")));
        world.FutureDemandReduction = null;
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordCustomerAcceptanceAsync(approved.Id, Command("8"), Metadata("future-accept")));
        Assert.Equal(503, refused.Status);
        Assert.Empty(world.Store.Refusals);
        Assert.Equal(8L, (await world.Store.FindDraftAsync(approved.Id))!.Version);
        var participant = new FakeFutureDemandReduction();
        world.FutureDemandReduction = participant;
        var retry = await world.Service().RecordCustomerAcceptanceAsync(approved.Id, Command("8"), Metadata("future-accept"));
        Assert.Equal("9", retry.Detail.Draft.Version);
        Assert.Single(participant.Requests);
    }

    [Fact]
    public async Task Legacy_orders_without_customer_acceptance_reduce_demand_only_when_internal_approval_completes()
    {
        var world = new World();
        await world.Service().CreateDraftAsync(Request(), Metadata("future-template"));
        var template = Assert.Single(world.Store.Drafts);
        var source = Assert.Single(template.Lines);
        var legacy = (await world.Store.StoreDraftAsync(world.Store.Envelopes[0] with { IdempotencyKey = "future-legacy-create" },
            new string('a', 64), number =>
            {
                var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, number, template.Header, template.Money,
                    template.CreatedAtUtc, customerAcceptanceRequired: false);
                draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), source.Product, source.OrderUnit,
                    source.QuantityText, source.Quantity.Value, Commercial: source.Commercial), template.CreatedAtUtc);
                return draft;
            })).Draft;
        await world.Service().SubmitDraftAsync(legacy.Id, Command("2"), Metadata("future-legacy-submit0"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(legacy.Id, Command("3"), Metadata("future-legacy-approve0")));
        Owners(world);
        var participant = Assert.IsType<FakeFutureDemandReduction>(world.FutureDemandReduction);
        await world.Service().ReviseQuantityAsync(legacy.Id,
            new("4", "SENTETIK azaltma", [new(legacy.Lines[0].Id, "1")]), Metadata("future-legacy-revise"));
        await world.Service().SubmitDraftAsync(legacy.Id, Command("5"), Metadata("future-legacy-submit1"));
        Assert.Empty(participant.Requests);
        var approved = await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(legacy.Id, Command("6"), Metadata("future-legacy-approve1")));
        Assert.Equal("7", approved.Detail.Draft.Version);
        Assert.Equal("1", Assert.Single(Assert.Single(participant.Requests).Lines).NewOpenQuantityText);
    }

    private sealed class FakeFutureDemandReduction : ISalesOrderFutureDemandReductionParticipantV1
    {
        public List<SalesOrderFutureDemandReductionRequestV1> Requests { get; } = [];
        public Exception? Failure { get; set; }
        public Task ReduceForAcceptedRevisionAsync(IDbTransaction transaction, SalesOrderFutureDemandReductionRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Assert.NotNull(transaction);
            Requests.Add(request);
            if (Failure is { } exception) throw exception;
            return Task.CompletedTask;
        }
    }

    private sealed partial class FakeStore
    {
        public async Task<SalesDraftStateResult?> UpdateDraftStateWithTransactionAsync(SalesDraftWriteEnvelope envelope,
            string requestHash, string commandName, Guid draftId, Action<SalesOrderDraft, DateTimeOffset> mutate,
            SalesDraftStateAudit audit, Func<SalesOrderDraft, int?, IDbTransaction, CancellationToken, Task> withinTransaction,
            CancellationToken cancellationToken = default, SalesOrderRiskAssessment? riskWarning = null)
        {
            var result = await UpdateDraftStateAsyncCore(envelope, requestHash, commandName, draftId, mutate, audit, null,
                cancellationToken, withinTransaction: async (draft, accepted, token) =>
                {
                    using var transaction = new SyntheticStateTransaction();
                    await withinTransaction(draft, accepted, transaction, token);
                });
            if (riskWarning is not null && result is { Outcome: SalesDraftWriteOutcome.Recorded } recorded)
                RiskWarnings.Add((commandName, draftId, recorded.Draft.Version, recorded.Draft.Revision, riskWarning, envelope));
            return result;
        }
    }

    private sealed class SyntheticStateTransaction : IDbTransaction
    {
        public IDbConnection? Connection => null;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public void Commit() { }
        public void Rollback() { }
        public void Dispose() { }
    }
}

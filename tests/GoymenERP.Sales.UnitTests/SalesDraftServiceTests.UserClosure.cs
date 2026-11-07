using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D32 / SAT-D168 / SAT-D215 over SYNTHETIC owners: the user closure of a "kapanmaya hazır" order under its own name
/// (<c>sales.orders.close</c>), without a reason. The precondition is the status read's <c>readyToClose</c>, recomputed inside
/// the save from the same calculation: on main the source, invoicing and financial-closure axes have no owner (NoSource), so
/// the command is the key's terminal 409 <c>sales_order_not_ready_to_close</c>. The ready path is proven through the
/// <see cref="ISalesOrderOwnerAxesSourceV1"/> TEST SEAM (a synthetic owner that calls its axes complete). A closed order is
/// Closed (the force-closure's lifecycle state) with its own record, refuses every later mutation and its lines are no longer
/// an approved demand.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private const string ClosePermission = SalesPermissionManifestV1.OrdersClose;

    [Fact]
    public async Task On_main_the_owner_less_axes_keep_every_order_not_ready_and_the_close_is_the_keys_recorded_409()
    {
        var world = new World();
        var accepted = await CompletedAcceptedAsync(world);
        world.Authority.Measured.Clear();

        var status = await world.Service().GetDraftStatusAsync(accepted.Draft.Id);
        Assert.Equal(("Completed", "Accepted", "NoSource", false), (status.Realization.State, status.CustomerAcceptance.State,
            status.Source.State, status.ReadyToClose));

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseDraftAsync(accepted.Draft.Id,
            new SalesDraftCloseRequest("5"), Metadata("k-close")));
        Assert.Equal((409, SalesDraftProblemCodesV1.OrderNotReadyToClose), Status(refused));
        Assert.Equal(("readyToClose", SalesDraftProblemCodesV1.OrderNotReadyToClose), (refused.Errors![0].Field, refused.Errors[0].Code));
        var refusal = Assert.Single(world.Store.Refusals);
        Assert.Equal((409, SalesDraftProblemCodesV1.OrderNotReadyToClose), (refusal.Refusal.Status, refusal.Refusal.Code));
        Assert.Empty(world.Store.UserClosures);
        Assert.Equal(("Draft", "5"), ((await world.Service().GetDraftDetailAsync(accepted.Draft.Id)).Draft.State,
            (await world.Service().GetDraftDetailAsync(accepted.Draft.Id)).Draft.Version));

        // The same key replays the recorded refusal; nothing is asked or written again.
        var replayed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseDraftAsync(accepted.Draft.Id,
            new SalesDraftCloseRequest("5"), Metadata("k-close")));
        Assert.Equal((409, SalesDraftProblemCodesV1.OrderNotReadyToClose, true), (replayed.Status, replayed.Code, replayed.IsReplay));
        Assert.Single(world.Store.Refusals);
        Assert.Null((await world.Service().GetDraftUserClosureAsync(accepted.Draft.Id)).UserClosure);
    }

    [Fact]
    public async Task A_ready_order_closes_once_on_the_users_action_with_its_own_record_and_its_lines_stop_being_a_demand()
    {
        var world = new World();
        var accepted = await CompletedAcceptedAsync(world);
        var owners = new FakeOwnerAxes(order => Complete(order));
        world.OwnerAxes = owners;
        var line = accepted.Lines[0].LineId;
        var company = new CompanyContext();
        company.Set(Company);
        var port = new SalesOrderLineReferencePort(world.Store, company);
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, (await port.GetApprovedLineAsync(accepted.Draft.Id, line)).Outcome);

        Assert.True((await world.Service().GetDraftStatusAsync(accepted.Draft.Id)).ReadyToClose);
        world.Authority.Measured.Clear();
        owners.Asked.Clear();

        var closed = await world.Service().CloseDraftAsync(accepted.Draft.Id, new SalesDraftCloseRequest("5"), Metadata("k-close"));

        Assert.False(closed.IsReplay);
        Assert.Equal(("Closed", "Approved", "6", SalesDraftApiV1.Rev0), (closed.Detail.Draft.State, closed.Detail.Draft.ApprovalState,
            closed.Detail.Draft.Version, closed.Detail.Draft.Revision));
        Assert.Equal(accepted.Lines.Select(item => (item.LineId, item.Quantity)), closed.Detail.Lines.Select(item => (item.LineId, item.Quantity)));
        // The readiness was recomputed inside the save under the close name only (no client flag): the owners asked once.
        Assert.Equal([accepted.Draft.Id], owners.Asked);
        Assert.True(world.Authority.Measured.Count(permission => permission == ClosePermission) >= 3);
        Assert.DoesNotContain(ForceClosePermission, world.Authority.Measured);
        Assert.DoesNotContain(SalesPermissionManifestV1.OrdersCancel, world.Authority.Measured);
        var transition = world.Store.Transitions[^1];
        Assert.Equal((SalesDraftCommands.CloseV1, ClosePermission, "sales.order-draft.closed.v1", "Close", 5L, 6L),
            (transition.Command, transition.Envelope.PermissionCode, transition.Audit.EventType, transition.Audit.Operation,
                transition.VersionBefore, transition.VersionAfter));
        var facts = Assert.Single(world.Store.UserClosures);
        Assert.Equal((0, Actor), (facts.Revision, facts.ClosedBy));

        // The record: a user closure, not a force-closure (same lifecycle state, another record).
        var record = (await world.Service().GetDraftUserClosureAsync(accepted.Draft.Id)).UserClosure!;
        Assert.Equal((SalesDraftApiV1.Rev0, Actor), (record.Revision, record.ClosedBy));
        Assert.Null((await world.Service().GetDraftForceClosureAsync(accepted.Draft.Id)).ForceClosure);
        var status = await world.Service().GetDraftStatusAsync(accepted.Draft.Id);
        Assert.Equal(("Closed", false), (status.Lifecycle.State, status.ReadyToClose));

        // The same key replays; another body under it is one 409.
        var replay = await world.Service().CloseDraftAsync(accepted.Draft.Id, new SalesDraftCloseRequest("5"), Metadata("k-close", correlation: "retry"));
        Assert.Equal((true, "synthetic-correlation", "Closed", "6"), (replay.IsReplay, replay.OriginalCorrelationId, replay.Detail.Draft.State,
            replay.Detail.Draft.Version));
        Assert.Single(world.Store.UserClosures);
        var other = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseDraftAsync(accepted.Draft.Id,
            new SalesDraftCloseRequest("6"), Metadata("k-close")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(other));

        // Terminal: a second close, a force-close preview and a cancellation see a closed order.
        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseDraftAsync(accepted.Draft.Id,
            new SalesDraftCloseRequest("6"), Metadata("k-close-2")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(again));
        Owners(world);
        Assert.False((await world.Service().GetForceCloseImpactAsync(accepted.Draft.Id)).Closable);

        // The next module: the line port answers the closed order's lines as not approved, as after a force-closure.
        var answer = await port.GetApprovedLineAsync(accepted.Draft.Id, line);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "Closed"), (answer.Outcome, answer.ApprovalState));
        Assert.Empty((await port.ListApprovedLinesAsync(10, null)).Items);
    }

    [Theory]
    [InlineData("owner-says-incomplete")]
    [InlineData("no-source-flagged-complete")]
    [InlineData("unreadable-flagged-complete")]
    [InlineData("policy-unavailable-flagged-complete")]
    [InlineData("another-company")]
    [InlineData("another-order")]
    [InlineData("blank-state")]
    [InlineData("throws")]
    [InlineData("null-answer")]
    public async Task An_owner_axis_that_is_not_complete_or_cannot_be_read_keeps_the_order_not_ready(string fault)
    {
        var world = new World();
        var accepted = await CompletedAcceptedAsync(world);
        world.OwnerAxes = new FakeOwnerAxes(order => fault switch
        {
            "owner-says-incomplete" => Complete(order) with { Invoicing = new("Partial", false) },
            "no-source-flagged-complete" => Complete(order) with { Source = new(SalesDraftStatusAxisStatesV1.NoSource, true) },
            "unreadable-flagged-complete" => Complete(order) with { FinancialClosure = new(SalesDraftStatusAxisStatesV1.Unreadable, true) },
            "policy-unavailable-flagged-complete" => Complete(order) with { Invoicing = new(SalesDraftStatusAxisStatesV1.PolicyUnavailable, true) },
            "another-company" => Complete(order) with { CompanyId = Guid.NewGuid() },
            "another-order" => Complete(order) with { OrderId = Guid.NewGuid() },
            "blank-state" => Complete(order) with { Source = new(" ", true) },
            "throws" => throw new InvalidOperationException("SENTETIK owner outage"),
            "null-answer" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        });

        Assert.False((await world.Service().GetDraftStatusAsync(accepted.Draft.Id)).ReadyToClose);
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseDraftAsync(accepted.Draft.Id,
            new SalesDraftCloseRequest("5"), Metadata("k-close")));
        Assert.Equal((409, SalesDraftProblemCodesV1.OrderNotReadyToClose), Status(refused));
        Assert.Empty(world.Store.UserClosures);
    }

    [Fact]
    public async Task A_short_or_unaccepted_order_is_not_ready_even_with_complete_owner_axes()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        world.OwnerAxes = new FakeOwnerAxes(order => Complete(order));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        var line = approved.Lines[0].LineId;

        // Fully shipped but the customer has not accepted the revision (Q9).
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(line, KgUnit, 1.25m, 1.25m)]));
        var unaccepted = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseDraftAsync(approved.Draft.Id,
            new SalesDraftCloseRequest("4"), Metadata("k-close-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.OrderNotReadyToClose), Status(unaccepted));

        // Accepted but only partly shipped.
        await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("4"), Metadata("k-accept"));
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(line, KgUnit, 1m, 1m)]));
        var partial = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseDraftAsync(approved.Draft.Id,
            new SalesDraftCloseRequest("5"), Metadata("k-close-2")));
        Assert.Equal((409, SalesDraftProblemCodesV1.OrderNotReadyToClose), Status(partial));

        // The delivery owner cannot answer: never ready.
        world.Deliveries = null;
        var unreadable = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseDraftAsync(approved.Draft.Id,
            new SalesDraftCloseRequest("5"), Metadata("k-close-3")));
        Assert.Equal((409, SalesDraftProblemCodesV1.OrderNotReadyToClose), Status(unreadable));
        Assert.Empty(world.Store.UserClosures);
    }

    [Fact]
    public async Task Stale_wrong_state_forbidden_unknown_and_malformed_closes_are_refused_before_any_owner_is_asked()
    {
        var world = new World();
        var accepted = await CompletedAcceptedAsync(world);
        var owners = new FakeOwnerAxes(order => Complete(order));
        world.OwnerAxes = owners;

        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseDraftAsync(accepted.Draft.Id,
            new SalesDraftCloseRequest("4"), Metadata("k-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), Status(stale));
        Assert.Empty(owners.Asked);

        var shape = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseDraftAsync(accepted.Draft.Id,
            new SalesDraftCloseRequest("abc"), Metadata("k-shape")));
        Assert.Equal((400, "validation_failed"), Status(shape));

        var unknown = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseDraftAsync(Guid.NewGuid(),
            new SalesDraftCloseRequest("5"), Metadata("k-unknown")));
        Assert.Equal(404, unknown.Status);

        // The cancel and force-close names do not confer the close name.
        world.Authority.Denied.Add(ClosePermission);
        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseDraftAsync(accepted.Draft.Id,
            new SalesDraftCloseRequest("5"), Metadata("k-forbidden")));
        Assert.Equal(403, forbidden.Status);
        world.Authority.Denied.Clear();

        // An unapproved draft is a state conflict.
        var draft = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create-2"))).Detail.Draft;
        var unapproved = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseDraftAsync(draft.Id,
            new SalesDraftCloseRequest(draft.Version), Metadata("k-unapproved")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(unapproved));
        Assert.Empty(owners.Asked);
        Assert.Empty(world.Store.UserClosures);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    /// <summary>A one-line order approved (v4), fully shipped within the SAT-D5 ladder and accepted by the customer: version 5.</summary>
    private static async Task<SalesOrderDraftDetailView> CompletedAcceptedAsync(World world)
    {
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(line, KgUnit, 1.25m, 1.25m)]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        return (await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("4"), Metadata("k-accept"))).Detail;
    }

    /// <summary>SYNTHETIC owners that call all three of their axes complete (the test seam; nothing on main answers this).</summary>
    private static SalesOrderOwnerAxesV1 Complete(Guid order) => new(Company, order,
        new("SENTETIK-FullySourced", true), new("SENTETIK-FullyInvoiced", true), new("SENTETIK-FinanciallyClosed", true));

    private sealed class FakeOwnerAxes(Func<Guid, SalesOrderOwnerAxesV1?> answer) : ISalesOrderOwnerAxesSourceV1
    {
        public List<Guid> Asked { get; } = [];

        public Task<SalesOrderOwnerAxesV1?> ReadOrderOwnerAxesAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            Asked.Add(orderId);
            return Task.FromResult(answer(orderId));
        }
    }
}

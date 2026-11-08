using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D17 / SAT-D168 / SAT-D215 / SAT-D216 over SYNTHETIC owners: a line with a shortfall is closed under its own permission at
/// the shipped total the user saw, once (replay), with the shipper never the closer; the remainder counts as cancelled and the
/// status read then shows the line as closed. An owner that does not name the shipper refuses (fail-closed, recorded); the block
/// band is 422 (not recorded); a line without a shortfall is the key's recorded 409; an unreadable owner or policy is 503 and
/// unrecorded.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private const string LineClosePermission = SalesPermissionManifestV1.OrdersLineClose;

    [Fact]
    public async Task A_warn_band_shortfall_is_closed_once_by_another_person_and_the_remainder_counts_as_cancelled()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var (first, second) = (approved.Lines[0].LineId, approved.Lines[1].LineId);
        // Line 2: 2.25 ordered, 2.225 shipped is a 1.11 % shortfall (warn band); the delivery owner names who shipped it.
        var deliveries = new FakeDeliveries(order => new(Company, order,
            [new(first, KgUnit, 1.25m, 1.25m, [Shipper]), new(second, KgUnit, 2.225m, 0m, [Shipper])]));
        world.Deliveries = deliveries;
        var policies = new FakeSalesPolicies(Ladder123());
        world.SalesPolicies = policies;
        world.Authority.Measured.Clear();

        var response = await world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.225"), Metadata("k-close-line"));

        Assert.False(response.IsReplay);
        Assert.Equal(("7", "Approved", "Draft"), (response.Detail.Draft.Version, response.Detail.Draft.ApprovalState, response.Detail.Draft.State));
        Assert.Equal(LineClosePermission, world.Authority.Measured[0]);
        Assert.True(world.Authority.Measured.Count(permission => permission == LineClosePermission) >= 5);
        Assert.Equal([approved.Draft.Id], deliveries.Asked);
        Assert.Equal([Day], policies.Asked);
        var transition = world.Store.Transitions[^1];
        Assert.Equal((SalesDraftCommands.CloseLineV1, LineClosePermission, "sales.order-draft.line-closed.v1", "CloseLine", 6L, 7L),
            (transition.Command, transition.Envelope.PermissionCode, transition.Audit.EventType, transition.Audit.Operation,
            transition.VersionBefore, transition.VersionAfter));
        var facts = Assert.Single(world.Store.LineClosureRecords);
        Assert.Equal((second, 0, "2.25", "2.225", "0.025", DeliveryToleranceBand.Warn, Actor),
            (facts.LineId, facts.BasisRevision, facts.Ordered, facts.ShippedAtClosure, facts.CancelledRemainder, facts.Band, facts.ClosedBy));
        Assert.Equal([Shipper], Assert.Single(world.Store.LineClosureInputs).ShippedBy);

        // SAT-D16: the line is measured as closed on the status read and the remainder is shown as the closed remainder.
        var status = await world.Service().GetDraftStatusAsync(approved.Draft.Id);
        Assert.Equal("Completed", status.Realization.Lines[1].State);

        // The same key and content replays the stored answer: no owner is asked, nothing is closed twice.
        var asked = deliveries.Asked.Count;
        var replay = await world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.225"), Metadata("k-close-line", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.Equal((asked, 1), (deliveries.Asked.Count, world.Store.LineClosureRecords.Count));
        var mismatch = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.2"), Metadata("k-close-line")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(mismatch));

        // A closed line is not closed again (a recorded state conflict under a new key).
        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("7", second, "2.225"), Metadata("k-close-line-2")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(again));
        Assert.Single(world.Store.LineClosureRecords);
    }

    [Fact]
    public async Task An_approval_band_shortfall_is_closed_by_the_users_action_without_any_extra_approval()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        // 2.2 shipped of 2.25 is a 2.22 % shortfall (approval band).
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.2m, 0m, [Shipper])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var response = await world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.2"), Metadata("k-close-line"));

        Assert.Equal("7", response.Detail.Draft.Version);
        var facts = Assert.Single(world.Store.LineClosureRecords);
        Assert.Equal((DeliveryToleranceBand.Approval, "0.05"), (facts.Band, facts.CancelledRemainder));
        // SAT-D17: an approved-by-action closure reads "Tamamlandi" (Completed), never ClosedWithRemainder (force-closure only).
        var status = await world.Service().GetDraftStatusAsync(approved.Draft.Id);
        Assert.Equal("Completed", status.Realization.Lines[1].State);
    }

    [Fact]
    public async Task A_within_band_shortfall_closes_and_reads_completed()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        // 2.24 shipped of 2.25 is a 0.44 % shortfall (within band).
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.24m, 0m, [Shipper])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        await world.Service().CloseLineAsync(approved.Draft.Id, new SalesDraftLineClosureRequest("6", second, "2.24"), Metadata("k-close-line"));

        Assert.Equal(DeliveryToleranceBand.Within, Assert.Single(world.Store.LineClosureRecords).Band);
        var status = await world.Service().GetDraftStatusAsync(approved.Draft.Id);
        Assert.Equal("Completed", status.Realization.Lines[1].State);
    }

    [Fact]
    public async Task A_closed_line_is_refused_as_closed_by_the_line_port_while_the_other_line_still_ships()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var first = approved.Lines[0].LineId;
        var second = approved.Lines[1].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.225m, 0m, [Shipper])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        var company = new CompanyContext();
        company.Set(Company);
        var port = new SalesOrderLineReferencePort(world.Store, company);
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, (await port.GetApprovedLineAsync(approved.Draft.Id, second)).Outcome);

        await world.Service().CloseLineAsync(approved.Draft.Id, new SalesDraftLineClosureRequest("6", second, "2.225"), Metadata("k-close-line"));

        var closed = await port.GetApprovedLineAsync(approved.Draft.Id, second);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "Closed"), (closed.Outcome, closed.ApprovalState));
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, (await port.GetApprovedLineAsync(approved.Draft.Id, first)).Outcome);
    }

    [Fact]
    public async Task An_owner_that_does_not_name_the_shipper_refuses_the_closure_fail_closed_as_the_keys_recorded_answer()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.225m, 0m)]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.225"), Metadata("k-close-line")));

        Assert.Equal((409, SalesDraftProblemCodesV1.LineClosureShipperUnverified), Status(refused));
        var refusal = Assert.Single(world.Store.Refusals);
        Assert.Equal((LineClosePermission, (Guid?)approved.Draft.Id), (refusal.Envelope.PermissionCode, refusal.DraftId));
        var replayed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.225"), Metadata("k-close-line", correlation: "retry")));
        Assert.Equal((409, SalesDraftProblemCodesV1.LineClosureShipperUnverified, true), (replayed.Status, replayed.Code, replayed.IsReplay));
        Assert.Empty(world.Store.LineClosureRecords);
    }

    [Fact]
    public async Task The_person_who_shipped_cannot_close_their_own_line()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.225m, 0m, ["synthetic-other", Actor])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.225"), Metadata("k-close-line")));

        Assert.Equal((409, SalesDraftProblemCodesV1.LineClosureSelfClosure), Status(refused));
        Assert.Single(world.Store.Refusals);
        Assert.Empty(world.Store.LineClosureRecords);
    }

    [Theory]
    [InlineData("2.25", 409, SalesDraftProblemCodesV1.LineClosureNotApplicable, true)] // exactly as ordered: no shortfall
    [InlineData("2.3", 409, SalesDraftProblemCodesV1.LineClosureNotApplicable, true)] // over-delivery, not a shortfall
    [InlineData("0", 409, SalesDraftProblemCodesV1.LineClosureNotApplicable, false)] // nothing shipped: not recorded (transient)
    [InlineData("2.18", 422, SalesDraftProblemCodesV1.LineClosureBlocked, false)] // 3.11 %: block band (SAT-D15)
    public async Task Only_a_line_with_a_shortfall_below_the_block_band_is_closable(string shipped, int status, string code, bool recorded)
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        var value = decimal.Parse(shipped, System.Globalization.CultureInfo.InvariantCulture);
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, value, 0m, [Shipper])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, shipped), Metadata("k-close-line")));

        Assert.Equal((status, code), Status(refused));
        Assert.Equal(recorded ? 1 : 0, world.Store.Refusals.Count);
        Assert.Empty(world.Store.LineClosureRecords);
    }

    [Fact]
    public async Task The_block_band_edge_is_an_unrecorded_422_judged_before_the_shippers_and_the_key_stays_reusable()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        // 2.182 of 2.25 is a 3.02 % shortfall: the block band (SAT-D5 ladder 1 / 2 / 3 %). The owner names the closer himself as
        // the shipper: the block band is still the answer, because the band is judged before SAT-D216 and the domain never runs.
        var shipped = 2.182m;
        var deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, shipped, 0m, [Actor])]));
        world.Deliveries = deliveries;
        var policies = new FakeSalesPolicies(Ladder123());
        world.SalesPolicies = policies;

        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.182"), Metadata("k-close-line-edge")));

        Assert.Equal((422, SalesDraftProblemCodesV1.LineClosureBlocked), Status(blocked));
        Assert.Equal([approved.Draft.Id], deliveries.Asked);
        Assert.Equal([Day], policies.Asked);
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.LineClosureRecords);
        Assert.Equal((6L, 0), ((await world.Store.FindDraftAsync(approved.Draft.Id))!.Version, (await world.Store.FindDraftAsync(approved.Draft.Id))!.LineClosures.Count));

        // 2.183 is a 2.98 % shortfall (approval band, one step under the block edge): the same, unburned key now closes the line
        // once the owner names another shipper.
        shipped = 2.183m;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, shipped, 0m, [Shipper])]));
        var closed = await world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.183"), Metadata("k-close-line-edge"));

        Assert.Equal(("7", false), (closed.Detail.Draft.Version, closed.IsReplay));
        var facts = Assert.Single(world.Store.LineClosureRecords);
        Assert.Equal((DeliveryToleranceBand.Approval, "2.183", "0.067"), (facts.Band, facts.ShippedAtClosure, facts.CancelledRemainder));
    }

    [Fact]
    public async Task A_line_is_not_closed_while_a_revision_awaits_the_customers_acceptance_and_the_state_conflict_is_recorded()
    {
        var world = new World();
        Owners(world);
        // ASSUMPTION-LC-3: internally approved (version 5) but the customer has not accepted Rev.0 yet.
        var approved = await ApprovedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        var deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.225m, 0m, [Shipper])]));
        world.Deliveries = deliveries;
        var policies = new FakeSalesPolicies(Ladder123());
        world.SalesPolicies = policies;

        var waiting = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("5", second, "2.225"), Metadata("k-close-line-awaiting")));

        // The domain's precondition refuses before any owner is asked; the refusal is the key's recorded answer.
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(waiting));
        Assert.Empty(deliveries.Asked);
        Assert.Empty(policies.Asked);
        var refusal = Assert.Single(world.Store.Refusals);
        Assert.Equal((LineClosePermission, (Guid?)approved.Draft.Id), (refusal.Envelope.PermissionCode, refusal.DraftId));
        var replayed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("5", second, "2.225"), Metadata("k-close-line-awaiting", correlation: "retry")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict, true), (replayed.Status, replayed.Code, replayed.IsReplay));
        Assert.Empty(world.Store.LineClosureRecords);

        // Once the customer accepted the revision, the same line closes under a new key.
        await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("5"), Metadata("k-accept"));
        var closed = await world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.225"), Metadata("k-close-line-accepted"));
        Assert.Equal("7", closed.Detail.Draft.Version);
        Assert.Single(world.Store.LineClosureRecords);
    }

    [Fact]
    public async Task A_shipped_total_that_changed_since_the_user_saw_it_is_an_unrecorded_409_and_the_key_stays_reusable()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.225m, 0m, [Shipper])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.2"), Metadata("k-close-line")));

        Assert.Equal((409, SalesDraftProblemCodesV1.LineClosureShippedChanged), Status(refused));
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.LineClosureRecords);
        // The same key now closes at the total the user re-read.
        await world.Service().CloseLineAsync(approved.Draft.Id, new SalesDraftLineClosureRequest("6", second, "2.225"), Metadata("k-close-line"));
        Assert.Single(world.Store.LineClosureRecords);
    }

    [Fact]
    public async Task Goods_loaded_but_not_shipped_or_an_active_allocation_refuse_the_closure_as_unrecorded_409s()
    {
        var world = new World();
        var (allocations, loadings) = Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.225m, 0m, [Shipper])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        var request = new SalesDraftLineClosureRequest("6", second, "2.225");

        // SAT-D215: loaded (2.25) is more than shipped (2.225): the goods on the vehicle could still ship after the closure.
        loadings.Answer = order => new(Company, order, [new(second, KgUnit, 1, 2.25m, 2.25m, 2.225m, 0m)]);
        var loaded = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id, request, Metadata("k-close-line")));
        Assert.Equal((409, SalesDraftProblemCodesV1.LineClosureOpenCommitments), Status(loaded));

        // Everything loaded has shipped; an active allocation still holds stock for the line.
        loadings.Answer = order => new(Company, order, [new(second, KgUnit, 1, 2.225m, 2.225m, 2.225m, 0m)]);
        allocations.Answer = order => new(Company, order, [new(second, 1, 0.025m)]);
        var allocated = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id, request, Metadata("k-close-line")));
        Assert.Equal((409, SalesDraftProblemCodesV1.LineClosureOpenCommitments), Status(allocated));
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.LineClosureRecords);

        // Once both are clear, the same key closes.
        allocations.Answer = order => new(Company, order, []);
        await world.Service().CloseLineAsync(approved.Draft.Id, request, Metadata("k-close-line"));
        Assert.Single(world.Store.LineClosureRecords);
    }

    [Fact]
    public async Task An_unreadable_or_uncomposed_loading_or_allocation_owner_is_a_503_and_nothing_is_closed_or_recorded()
    {
        var world = new World();
        var (allocations, loadings) = Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.225m, 0m, [Shipper])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        var request = new SalesDraftLineClosureRequest("6", second, "2.225");

        world.Loadings = null;
        var noLoading = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id, request, Metadata("k-close-line")));
        Assert.Equal(503, noLoading.Status);
        world.Loadings = loadings;
        world.Allocations = null;
        var noAllocation = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id, request, Metadata("k-close-line")));
        Assert.Equal(503, noAllocation.Status);

        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.LineClosureRecords);
        world.Allocations = allocations;
    }

    [Fact]
    public async Task A_closed_line_is_refused_by_quantity_revision_and_remainder_change_until_a_reopen_exists()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var (first, second) = (approved.Lines[0].LineId, approved.Lines[1].LineId);
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.225m, 0m, [Shipper])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        await world.Service().CloseLineAsync(approved.Draft.Id, new SalesDraftLineClosureRequest("6", second, "2.225"), Metadata("k-close-line"));

        // SAT-D167 / SAT-D217: a closed line is changed only through an approved reopen, which this slice does not ship.
        var revision = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("7", "Test", [new(first, "1.25"), new(second, "3")]), Metadata("k-revise")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(revision));
        var remainder = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RequestRemainderChangeAsync(approved.Draft.Id,
            new SalesDraftRemainderChangeRequest("7", second, "Cancel", null, "Test"), Metadata("k-remainder")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(remainder));
    }

    [Fact]
    public async Task Authority_version_line_and_unreadable_owners_refuse_before_anything_is_closed()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        var deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.225m, 0m, [Shipper])]));
        world.Deliveries = deliveries;
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        var request = new SalesDraftLineClosureRequest("6", second, "2.225");

        // Not granted: 403 and no owner is asked; no other Sales name confers it.
        world.Authority.Denied.Add(LineClosePermission);
        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CloseLineAsync(approved.Draft.Id, request, Metadata("k-denied")))));
        Assert.Empty(deliveries.Asked);
        world.Authority.Denied.Clear();

        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CloseLineAsync(approved.Draft.Id, request with { ExpectedVersion = "5" }, Metadata("k-stale")))));
        Assert.Empty(deliveries.Asked);

        var refusals = world.Store.Refusals.Count;
        var unknown = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CloseLineAsync(approved.Draft.Id, request with { LineId = Guid.NewGuid() }, Metadata("k-line")));
        Assert.Equal((422, "validation_failed"), Status(unknown));
        Assert.Equal(refusals, world.Store.Refusals.Count);

        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CloseLineAsync(Guid.NewGuid(), request, Metadata("k-404")))).Status);

        // No tolerance in force: 503, not recorded - never a default band.
        world.SalesPolicies = new FakeSalesPolicies(_ => throw new SettingsPrerequisiteException("settings_provider_unavailable"));
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CloseLineAsync(approved.Draft.Id, request, Metadata("k-policy")))).Status);
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        // A failing delivery owner: 503, not recorded - never a zero.
        deliveries.Answer = _ => throw new InvalidOperationException("SENTETIK delivery owner outage");
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CloseLineAsync(approved.Draft.Id, request, Metadata("k-owner")))).Status);
        Assert.Equal(refusals, world.Store.Refusals.Count);
        Assert.Empty(world.Store.LineClosureRecords);
    }

    [Fact]
    public async Task An_unregistered_line_close_name_is_a_503_setup_fault_and_nothing_is_read_or_recorded()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        var deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.225m, 0m, [Shipper])]));
        world.Deliveries = deliveries;
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        world.Bindings = world.Bindings with { LineClosePermission = "sales.synthetic.unregistered" };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.225"), Metadata("k-close-line")));

        Assert.Equal((503, SalesDraftProblemCodesV1.PermissionUnregistered), Status(refused));
        Assert.Empty(deliveries.Asked);
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.LineClosureRecords);
    }

    [Fact]
    public async Task Line_closure_on_a_force_closed_or_never_approved_order_is_the_recorded_state_conflict_asking_no_owner()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var preview = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);
        await world.Service().ForceCloseDraftAsync(approved.Draft.Id, new SalesDraftForceCloseRequest("6", "SENTETIK neden", preview.ImpactToken),
            Metadata("k-close"));
        var deliveries = new FakeDeliveries(order => new(Company, order, [new(approved.Lines[1].LineId, KgUnit, 2.225m, 0m, [Shipper])]));
        world.Deliveries = deliveries;
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var closed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("7", approved.Lines[1].LineId, "2.225"), Metadata("k-close-line-closed")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(closed));

        var draft = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-create-2"))).Detail;
        var never = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(draft.Draft.Id,
            new SalesDraftLineClosureRequest(draft.Draft.Version, draft.Lines[1].LineId, "2.225"), Metadata("k-close-line-never")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(never));
        Assert.Empty(deliveries.Asked);
        Assert.Empty(world.Store.LineClosureRecords);
    }

    [Fact]
    public async Task A_line_is_not_closed_while_a_revision_awaits_re_approval_and_asks_no_owner()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var (first, second) = (approved.Lines[0].LineId, approved.Lines[1].LineId);
        var deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.225m, 0m, [Shipper])]));
        world.Deliveries = deliveries;
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        // A quantity revision of the OTHER line puts the whole order back into re-approval (the closed quantity must stay settled).
        var revised = await world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("6", "Test", [new(first, "1.5"), new(second, "2.25")]), Metadata("k-revise-open"));

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest(revised.Detail.Draft.Version, second, "2.225"), Metadata("k-close-line-revising")));

        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(refused));
        Assert.Empty(deliveries.Asked);
        Assert.Empty(world.Store.LineClosureRecords);
    }

    [Fact]
    public async Task The_open_order_report_leaves_a_line_closed_with_a_shortfall_out_of_the_open_remainder()
    {
        var world = new World();
        var accepted = await AcceptedTwoLineAsync(world);
        var (first, second) = (accepted.Lines[0].LineId, accepted.Lines[1].LineId);
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.225m, 0m, [Shipper])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        Owners(world);
        await world.Service().CloseLineAsync(accepted.Draft.Id, new SalesDraftLineClosureRequest(accepted.Draft.Version, second, "2.225"),
            Metadata("k-close-line-report"));
        world.Reports!.Orders.Add((await world.Store.FindDraftAsync(accepted.Draft.Id))!);

        var group = Assert.Single((await world.Service().GetOpenOrderReportAsync(new SalesOpenOrderReportQuery(null, null, null, 50, null))).Groups);

        // The closed line's 0.025 is counted as cancelled, so only the untouched first line is open.
        Assert.Equal([first], group.Lines.Select(line => line.LineId));
    }

    [Fact]
    public async Task A_lost_race_at_the_store_with_no_winner_under_the_key_is_409_concurrent_change_and_nothing_is_closed()
    {
        var world = new World();
        Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.225m, 0m, [Shipper])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        world.Store.Race = "version-at-store";

        var raced = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.225"), Metadata("k-close-line")));

        Assert.Equal((409, SalesDraftProblemCodesV1.ConcurrentChange), Status(raced));
        Assert.Empty(world.Store.LineClosureRecords);
        var retried = await world.Service().CloseLineAsync(approved.Draft.Id,
            new SalesDraftLineClosureRequest("6", second, "2.225"), Metadata("k-close-line-2"));
        Assert.Equal("7", retried.Detail.Draft.Version);
        Assert.Single(world.Store.LineClosureRecords);
    }
}

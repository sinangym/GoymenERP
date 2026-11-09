using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D30 / SAT-D168 / SAT-D213 / SAT-D216 over SYNTHETIC owners: an over-delivery in the Settings approval band is approved under
/// its own name at the shipped total the user saw, once (replay), with the shipper never the approver; the status read then shows
/// "Tamamlandı + onaylı fazla teslim" until the shipped total grows (a new approval is needed). An owner that does not name the
/// shipper refuses (fail-closed, recorded); the block band is 422 (not recorded); anything not awaiting approval is the key's
/// recorded 409; an unreadable owner or policy is 503 and unrecorded.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private const string OverDeliveryPermission = SalesPermissionManifestV1.OrdersOverDeliveryApprove;
    private const string Shipper = "synthetic-shipper";

    [Fact]
    public async Task An_approval_band_over_delivery_is_approved_once_by_another_person_and_a_larger_total_needs_a_new_approval()
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var (first, second) = (approved.Lines[0].LineId, approved.Lines[1].LineId);
        // Line 2: 2.25 → 2.295 is exactly 2 % over (approval band); the delivery owner names who shipped it.
        var deliveries = new FakeDeliveries(order => new(Company, order,
            [new(first, KgUnit, 1.25m, 1.25m, [Shipper]), new(second, KgUnit, 2.295m, 0m, [Shipper])]));
        world.Deliveries = deliveries;
        var policies = new FakeSalesPolicies(Ladder123());
        world.SalesPolicies = policies;
        world.Authority.Measured.Clear();

        var response = await world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("5", second, "2.295"), Metadata("k-over"));

        Assert.False(response.IsReplay);
        Assert.Equal(("6", "Approved", "Draft"), (response.Detail.Draft.Version, response.Detail.Draft.ApprovalState, response.Detail.Draft.State));
        // Fenced first, at the draft, after the delivery owner, after Settings and last before the write — under its own name (the
        // response's own disclosure reads follow).
        Assert.Equal(OverDeliveryPermission, world.Authority.Measured[0]);
        Assert.True(world.Authority.Measured.Count(permission => permission == OverDeliveryPermission) >= 5);
        Assert.Equal([approved.Draft.Id], deliveries.Asked);
        Assert.Equal([Day], policies.Asked);
        var transition = world.Store.Transitions[^1];
        Assert.Equal((SalesDraftCommands.ApproveOverDeliveryV1, OverDeliveryPermission, "sales.order-draft.over-delivery-approved.v1",
            "ApproveOverDelivery", 5L, 6L), (transition.Command, transition.Envelope.PermissionCode, transition.Audit.EventType,
            transition.Audit.Operation, transition.VersionBefore, transition.VersionAfter));
        var facts = Assert.Single(world.Store.OverDeliveryApprovals);
        Assert.Equal((second, 1, 0, "2.25", "2.295", Actor), (facts.LineId, facts.ApprovalNo, facts.BasisRevision, facts.Ordered,
            facts.ApprovedShipped, facts.ApprovedBy));
        Assert.Equal([Shipper], Assert.Single(world.Store.OverDeliveryInputs).ShippedBy);

        // SAT-D30: "Tamamlandı + onaylı fazla teslim" on the status read, through the calculator.
        var status = await world.Service().GetDraftStatusAsync(approved.Draft.Id);
        Assert.Equal("Completed", status.Realization.State);
        Assert.Equal(("Completed", "Approval", true), (status.Realization.Lines[1].State, status.Realization.Lines[1].Band,
            status.Realization.Lines[1].ApprovedOverDelivery));

        // The same key and content replays the stored answer: no owner is asked, nothing is approved twice.
        var asked = deliveries.Asked.Count;
        var replay = await world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("5", second, "2.295"), Metadata("k-over", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.Equal((asked, 1), (deliveries.Asked.Count, world.Store.OverDeliveryApprovals.Count));
        // The same key with another body is one 409.
        var mismatch = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("5", second, "2.3"), Metadata("k-over")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(mismatch));

        // SAT-D213: a later shipment makes it 2.31 (2.67 %, still the approval band): the approval no longer covers it.
        deliveries.Answer = order => new(Company, order,
            [new(first, KgUnit, 1.25m, 1.25m, [Shipper]), new(second, KgUnit, 2.31m, 0m, [Shipper, "synthetic-shipper-2"])]);
        var grown = await world.Service().GetDraftStatusAsync(approved.Draft.Id);
        Assert.Equal(("AwaitingApproval", false), (grown.Realization.Lines[1].State, grown.Realization.Lines[1].ApprovedOverDelivery));
        // A command naming the old total is refused (the user did not see the new one) — recorded for the key.
        var changed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("6", second, "2.295"), Metadata("k-over-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.OverDeliveryShippedChanged), Status(changed));
        Assert.Equal(SalesDraftProblemCodesV1.OverDeliveryShippedChanged, world.Store.Refusals[^1].Refusal.Code);
        // The new total is approved as the line's second approval.
        await world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("6", second, "2.31"), Metadata("k-over-2"));
        Assert.Equal([(1, "2.295"), (2, "2.31")], world.Store.OverDeliveryApprovals.Select(item => (item.ApprovalNo, item.ApprovedShipped)));
        Assert.True((await world.Service().GetDraftStatusAsync(approved.Draft.Id)).Realization.Lines[1].ApprovedOverDelivery);
    }

    [Fact]
    public async Task An_owner_that_does_not_name_the_shipper_refuses_the_approval_fail_closed_as_the_keys_recorded_answer()
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        // The Logistics owner on main answers totals only (ShippedBy null): SAT-D216 cannot be checked.
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.295m, 0m)]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("5", second, "2.295"), Metadata("k-over")));

        Assert.Equal((409, SalesDraftProblemCodesV1.OverDeliveryShipperUnverified), Status(refused));
        var refusal = Assert.Single(world.Store.Refusals);
        Assert.Equal((OverDeliveryPermission, (Guid?)approved.Draft.Id), (refusal.Envelope.PermissionCode, refusal.DraftId));
        var replayed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("5", second, "2.295"), Metadata("k-over", correlation: "retry")));
        Assert.Equal((409, SalesDraftProblemCodesV1.OverDeliveryShipperUnverified, true), (replayed.Status, replayed.Code, replayed.IsReplay));
        Assert.Empty(world.Store.OverDeliveryApprovals);
        Assert.Equal("AwaitingApproval", (await world.Service().GetDraftStatusAsync(approved.Draft.Id)).Realization.Lines[1].State);
    }

    [Fact]
    public async Task The_person_who_shipped_cannot_approve_their_own_over_delivery()
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.295m, 0m, ["synthetic-other", Actor])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("5", second, "2.295"), Metadata("k-over")));

        Assert.Equal((409, SalesDraftProblemCodesV1.OverDeliverySelfApproval), Status(refused));
        Assert.Single(world.Store.Refusals);
        Assert.Empty(world.Store.OverDeliveryApprovals);
    }

    [Theory]
    [InlineData("2.25", 409, SalesDraftProblemCodesV1.OverDeliveryNotAwaitingApproval, true)] // exactly as ordered
    [InlineData("2.27", 409, SalesDraftProblemCodesV1.OverDeliveryNotAwaitingApproval, true)] // 0.89 % within
    [InlineData("2.275", 409, SalesDraftProblemCodesV1.OverDeliveryNotAwaitingApproval, true)] // warn band (SAT-D29)
    [InlineData("1.5", 409, SalesDraftProblemCodesV1.OverDeliveryNotAwaitingApproval, true)] // partial
    [InlineData("2.318", 422, SalesDraftProblemCodesV1.OverDeliveryBlocked, false)] // 3.02 %: block band (SAT-D15)
    public async Task Only_a_line_awaiting_approval_is_approvable_and_the_block_band_is_never_approved(string shipped, int status,
        string code, bool recorded)
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        var value = decimal.Parse(shipped, System.Globalization.CultureInfo.InvariantCulture);
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, value, 0m, [Shipper])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("5", second, shipped), Metadata("k-over")));

        Assert.Equal((status, code), Status(refused));
        Assert.Equal(recorded ? 1 : 0, world.Store.Refusals.Count);
        Assert.Empty(world.Store.OverDeliveryApprovals);
    }

    [Fact]
    public async Task Authority_version_line_and_unreadable_owners_refuse_before_anything_is_approved()
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        var deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.295m, 0m, [Shipper])]));
        world.Deliveries = deliveries;
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        var request = new SalesDraftOverDeliveryApprovalRequest("5", second, "2.295");

        // Not granted: 403 and no owner is asked; no other Sales name confers it.
        world.Authority.Denied.Add(OverDeliveryPermission);
        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveOverDeliveryAsync(approved.Draft.Id, request, Metadata("k-denied")))));
        Assert.Empty(deliveries.Asked);
        world.Authority.Denied.Clear();

        // A stale version is the key's recorded 409, asking no owner.
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveOverDeliveryAsync(approved.Draft.Id, request with { ExpectedVersion = "4" }, Metadata("k-stale")))));
        Assert.Empty(deliveries.Asked);

        // A line that is not the order's: 422, not recorded.
        var refusals = world.Store.Refusals.Count;
        var unknown = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveOverDeliveryAsync(approved.Draft.Id, request with { LineId = Guid.NewGuid() }, Metadata("k-line")));
        Assert.Equal((422, "validation_failed"), Status(unknown));
        Assert.Equal(refusals, world.Store.Refusals.Count);

        // Another company's (or an unknown) order: 404.
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveOverDeliveryAsync(Guid.NewGuid(), request, Metadata("k-404")))).Status);

        // No tolerance in force: 503, not recorded — never a default band.
        world.SalesPolicies = new FakeSalesPolicies(_ => throw new SettingsPrerequisiteException("settings_provider_unavailable"));
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveOverDeliveryAsync(approved.Draft.Id, request, Metadata("k-policy")))).Status);
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        // A failing delivery owner: 503, not recorded — never a zero.
        deliveries.Answer = _ => throw new InvalidOperationException("SENTETIK delivery owner outage");
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveOverDeliveryAsync(approved.Draft.Id, request, Metadata("k-owner")))).Status);
        Assert.Equal(refusals, world.Store.Refusals.Count);
        Assert.Empty(world.Store.OverDeliveryApprovals);
    }

    [Fact]
    public async Task An_unregistered_over_delivery_name_is_a_503_setup_fault_and_nothing_is_read_or_recorded()
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        var deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.295m, 0m, [Shipper])]));
        world.Deliveries = deliveries;
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        world.Bindings = world.Bindings with { OverDeliveryApprovePermission = "sales.synthetic.unregistered" };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("5", second, "2.295"), Metadata("k-over")));

        // A setup gap, never a grant and never a 403: nothing is read, nothing is recorded.
        Assert.Equal((503, SalesDraftProblemCodesV1.PermissionUnregistered), Status(refused));
        Assert.Empty(deliveries.Asked);
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.OverDeliveryApprovals);
    }

    [Fact]
    public async Task Authority_lost_between_the_owner_reads_and_the_write_is_403_and_nothing_is_approved()
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        var deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.295m, 0m, [Shipper])]));
        world.Deliveries = deliveries;
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        // Granted for the first measurements (the fence, the replay read, the write fence and the delivery read), lost afterwards:
        // the fence after an owner read refuses before anything is written.
        world.Authority.LostLater = (OverDeliveryPermission, world.Authority.Calls + 3);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("5", second, "2.295"), Metadata("k-over")));

        Assert.Equal(403, refused.Status);
        Assert.Single(deliveries.Asked);
        Assert.Empty(world.Store.OverDeliveryApprovals);
        Assert.Empty(world.Store.Refusals);
        Assert.Equal("5", (await world.Service().GetDraftDetailAsync(approved.Draft.Id)).Draft.Version);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("too-long")]
    [InlineData("one-entry-unnamed")]
    public async Task A_shipper_set_the_owner_does_not_name_fully_is_the_recorded_fail_closed_409(string shape)
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        // The line's shipped total 2.295 (2 % over, approval band), named in one or two entries as the case shapes it.
        SalesOrderLineDeliveryV1[] entries = shape switch
        {
            "empty" => [new(second, KgUnit, 2.295m, 0m, [])],
            "blank" => [new(second, KgUnit, 2.295m, 0m, [" "])],
            "too-long" => [new(second, KgUnit, 2.295m, 0m, [new string('x', 129)])],
            _ => [new(second, KgUnit, 2m, 0m, [Shipper]), new(second, KgUnit, 0.295m, 0m)],
        };
        world.Deliveries = new FakeDeliveries(order => new(Company, order, entries));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("5", second, "2.295"), Metadata("k-over")));

        Assert.Equal((409, SalesDraftProblemCodesV1.OverDeliveryShipperUnverified), Status(refused));
        Assert.Equal(SalesDraftProblemCodesV1.OverDeliveryShipperUnverified, Assert.Single(world.Store.Refusals).Refusal.Code);
        Assert.Empty(world.Store.OverDeliveryApprovals);
    }

    [Fact]
    public async Task No_composed_delivery_owner_is_503_and_nothing_is_recorded()
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        world.Deliveries = null;
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("5", approved.Lines[1].LineId, "2.295"), Metadata("k-over")));

        Assert.Equal((503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable), Status(refused));
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.OverDeliveryApprovals);
    }

    [Fact]
    public async Task A_force_closed_order_and_a_never_approved_order_are_the_recorded_state_conflict_asking_no_owner()
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        Owners(world);
        var preview = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);
        await world.Service().ForceCloseDraftAsync(approved.Draft.Id, new SalesDraftForceCloseRequest("5", "SENTETIK neden", preview.ImpactToken),
            Metadata("k-close"));
        var deliveries = new FakeDeliveries(order => new(Company, order, [new(approved.Lines[1].LineId, KgUnit, 2.295m, 0m, [Shipper])]));
        world.Deliveries = deliveries;
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var closed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("6", approved.Lines[1].LineId, "2.295"), Metadata("k-over-closed")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(closed));

        var draft = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-create-2"))).Detail;
        var never = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(draft.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest(draft.Draft.Version, draft.Lines[1].LineId, "2.295"), Metadata("k-over-never")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(never));
        Assert.Empty(deliveries.Asked);
        Assert.Empty(world.Store.OverDeliveryApprovals);
    }

    [Fact]
    public async Task A_lost_race_at_the_store_with_no_winner_under_the_key_is_409_concurrent_change_and_nothing_is_approved()
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 2.295m, 0m, [Shipper])]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        // Another write moved the order between this command's read and its save (the store's row-version race).
        world.Store.Race = "version-at-store";

        var raced = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("5", second, "2.295"), Metadata("k-over")));

        Assert.Equal((409, SalesDraftProblemCodesV1.ConcurrentChange), Status(raced));
        Assert.Empty(world.Store.OverDeliveryApprovals);
        // Not recorded: the same request with a new key can be decided again.
        var retried = await world.Service().ApproveOverDeliveryAsync(approved.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("5", second, "2.295"), Metadata("k-over-2"));
        Assert.Equal("6", retried.Detail.Draft.Version);
        Assert.Single(world.Store.OverDeliveryApprovals);
    }

    [Fact]
    public async Task A_user_closed_order_refuses_an_over_delivery_approval_as_the_recorded_state_conflict_asking_no_owner()
    {
        var world = new World();
        var accepted = await CompletedAcceptedAsync(world);
        world.OwnerAxes = new FakeOwnerAxes(order => Complete(order));
        // SAT-D32: the user closes the ready order (Closed, a user closure record — not a force-closure).
        await world.Service().CloseDraftAsync(accepted.Draft.Id, new SalesDraftCloseRequest("5"), Metadata("k-close"));
        var line = accepted.Lines[0].LineId;
        var deliveries = new FakeDeliveries(order => new(Company, order, [new(line, KgUnit, 1.5m, 0m, [Shipper])]));
        world.Deliveries = deliveries;
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveOverDeliveryAsync(accepted.Draft.Id,
            new SalesDraftOverDeliveryApprovalRequest("6", line, "1.5"), Metadata("k-over-closed")));

        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(refused));
        Assert.Equal("Closed", (await world.Service().GetDraftDetailAsync(accepted.Draft.Id)).Draft.State);
        Assert.Empty(deliveries.Asked);
        Assert.Empty(world.Store.OverDeliveryApprovals);
    }
}

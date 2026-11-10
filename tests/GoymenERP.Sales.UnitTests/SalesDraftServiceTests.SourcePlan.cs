using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D251..SAT-D255 (EK50) / API-11 over SYNTHETIC owners: the order source plan is saved once under its own permission with
/// the stock part derived from the allocation and the locked part from the delivery owner (never entered); the parts add up to
/// the line quantity (one equality, also on a partially delivered line); a fully delivered line is the key's recorded 409; an open
/// linked supply needs the explicit confirmation; unreadable owners are 503 and unrecorded; the read shows the derived stock, the
/// suggestion and the warnings (a later allocation change is a mismatch warning, never a silent rewrite).
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private const string SourcePlanWritePermission = SalesPermissionManifestV1.OrdersSourcePlanWrite;

    private static SalesDraftSourcePlanRequest PlanOf(string version, Guid line, string production, string purchasing = "0",
        string subcontract = "0", bool confirm = false) =>
        new(version, confirm, [new SalesDraftSourcePlanLineRequestV1(line, production, purchasing, subcontract)]);

    /// <summary>The two-line accepted order (1.25 / 2.25 kg, version 6), line 2 shipped 1 kg and allocated 0.5 kg.</summary>
    private static async Task<(SalesOrderDraftDetailView Approved, FakeDeliveries Deliveries, FakeAllocations Allocations)> PlannedOrderAsync(World world)
    {
        var (allocations, _) = Owners(world);
        var approved = await AcceptedTwoLineAsync(world);
        var second = approved.Lines[1].LineId;
        var deliveries = new FakeDeliveries(order => new(Company, order, [new(second, KgUnit, 1m, 0m)]));
        world.Deliveries = deliveries;
        allocations.Answer = order => new(Company, order, [new SalesOrderLineAllocationV1(second, 1, 0.5m)]);
        return (approved, deliveries, allocations);
    }

    [Fact]
    public async Task A_partially_delivered_line_is_planned_once_with_the_remainder_balanced_and_no_revision()
    {
        var world = new World();
        var (approved, deliveries, allocations) = await PlannedOrderAsync(world);
        var second = approved.Lines[1].LineId;
        world.Authority.Measured.Clear();

        // 1 (locked, shipped) + 0.5 (stock, allocation) + 0.75 production = 2.25: the remainder 1.25 is 0.5 + 0.75 (note (a)).
        var response = await world.Service().RecordSourcePlanAsync(approved.Draft.Id, PlanOf("6", second, "0.75"), Metadata("k-plan"));

        Assert.False(response.IsReplay);
        Assert.Equal(("7", "Approved", "Draft", "Rev.0"),
            (response.Detail.Draft.Version, response.Detail.Draft.ApprovalState, response.Detail.Draft.State, response.Detail.Draft.Revision));
        Assert.Equal(SourcePlanWritePermission, world.Authority.Measured[0]);
        Assert.Equal([approved.Draft.Id], deliveries.Asked);
        Assert.Equal([approved.Draft.Id], allocations.Asked);
        var transition = world.Store.Transitions[^1];
        Assert.Equal((SalesDraftCommands.RecordSourcePlanV1, SourcePlanWritePermission, "sales.order-draft.source-plan-recorded.v1",
            "RecordSourcePlan", 6L, 7L), (transition.Command, transition.Envelope.PermissionCode, transition.Audit.EventType,
            transition.Audit.Operation, transition.VersionBefore, transition.VersionAfter));
        var fact = Assert.Single(world.Store.SourcePlanRecords);
        Assert.Equal((second, 2, 2.25m, 1m, 0.5m, 0.75m, 0m, 0m, false, Actor, 7L),
            (fact.LineId, fact.LineNo, fact.LineQuantity, fact.Locked, fact.Stock, fact.Production, fact.Purchasing, fact.Subcontract,
            fact.LinkedSupplyConfirmed, fact.SavedBy, fact.DraftVersion));
        Assert.Empty(Assert.Single(world.Store.SourcePlanInputs).LinkedSupply);

        // The same key and content replays: no owner is asked again, nothing is recorded twice.
        var replay = await world.Service().RecordSourcePlanAsync(approved.Draft.Id, PlanOf("6", second, "0.75"),
            Metadata("k-plan", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.Equal((1, 1, 1), (deliveries.Asked.Count, allocations.Asked.Count, world.Store.SourcePlanRecords.Count));
        var mismatch = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RecordSourcePlanAsync(approved.Draft.Id,
            PlanOf("6", second, "0.5", "0.25"), Metadata("k-plan")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(mismatch));

        // SAT-D253 (1): free until delivery — a second save under a new key moves the plan again, still no revision.
        var moved = await world.Service().RecordSourcePlanAsync(approved.Draft.Id, PlanOf("7", second, "0", "0.5", "0.25"),
            Metadata("k-plan-2"));
        Assert.Equal(("8", "Rev.0"), (moved.Detail.Draft.Version, moved.Detail.Draft.Revision));
        Assert.Equal(2, world.Store.SourcePlanRecords.Count);
    }

    [Fact]
    public async Task The_read_derives_stock_from_the_allocation_suggests_and_warns_on_a_later_allocation_change()
    {
        var world = new World();
        var (approved, _, allocations) = await PlannedOrderAsync(world);
        var (first, second) = (approved.Lines[0].LineId, approved.Lines[1].LineId);

        var before = await world.Service().GetSourcePlanAsync(approved.Draft.Id);
        Assert.Equal(("6", true), (before.Version, before.Writable));
        Assert.All(before.LinkedSupply, entry => Assert.Equal(SalesCancellationImpactStatusesV1.NoSource, entry.Status));
        var line1 = before.Lines[0];
        // Line 1: nothing shipped, nothing allocated; no free-stock port exists, so no stock suggestion is invented.
        Assert.Equal((first, "0", "1.25", SalesSourcePlanStockStatusesV1.Derived, "0", SalesSourcePlanSuggestionsV1.Unavailable,
            SalesSourcePlanSuggestionReasonsV1.FreeStockUnavailable, true), (line1.LineId, line1.Locked, line1.Remaining, line1.StockStatus,
            line1.Stock, line1.Suggestion, line1.SuggestionReason, line1.Writable));
        Assert.Equal([SalesSourcePlanWarningsV1.NoPlan], line1.Warnings);
        var line2 = before.Lines[1];
        Assert.Equal(("1", "1.25", "0.5", SalesSourcePlanSuggestionReasonsV1.FreeStockUnavailable),
            (line2.Locked, line2.Remaining, line2.Stock, line2.SuggestionReason));

        await world.Service().RecordSourcePlanAsync(approved.Draft.Id, PlanOf("6", second, "0.75"), Metadata("k-plan"));
        var after = await world.Service().GetSourcePlanAsync(approved.Draft.Id);
        var saved = after.Lines[1].Saved!;
        Assert.Equal(("7", "2.25", "1", "0.5", "0.75", "0", "0", false, Actor),
            (saved.DraftVersion, saved.LineQuantity, saved.Locked, saved.Stock, saved.Production, saved.Purchasing, saved.Subcontract,
            saved.LinkedSupplyConfirmed, saved.SavedBy));
        Assert.Empty(after.Lines[1].Warnings);

        // SAT-D255: the allocation now holds the whole remainder — the plan is NOT rewritten, the read warns; the line's own
        // allocation covers the need (note (c): it is subtracted from the need, never counted as free stock).
        allocations.Answer = order => new(Company, order, [new SalesOrderLineAllocationV1(second, 2, 1.25m)]);
        var mismatch = await world.Service().GetSourcePlanAsync(approved.Draft.Id);
        Assert.Equal([SalesSourcePlanWarningsV1.PlanAllocationMismatch], mismatch.Lines[1].Warnings);
        Assert.Equal(("1.25", SalesSourcePlanSuggestionsV1.Stock, SalesSourcePlanSuggestionReasonsV1.AllocationCoversRemainder),
            (mismatch.Lines[1].Stock, mismatch.Lines[1].Suggestion, mismatch.Lines[1].SuggestionReason));
        Assert.Equal("0.5", mismatch.Lines[1].Saved!.Stock);
    }

    [Fact]
    public async Task The_read_shows_unreadable_owners_as_unreadable_and_not_writable_never_as_zero()
    {
        var world = new World();
        var (approved, _, _) = await PlannedOrderAsync(world);
        world.Allocations = null;
        world.Deliveries = null;
        world.LinkedSupplyRecorders.Add(new FakeSupplyRecorder(SalesLinkedSupplyKindsV1.Production));

        var read = await world.Service().GetSourcePlanAsync(approved.Draft.Id);

        Assert.False(read.Writable);
        Assert.Equal(SalesCancellationImpactStatusesV1.Unreadable,
            read.LinkedSupply.Single(entry => entry.Kind == SalesLinkedSupplyKindsV1.Production).Status);
        var line = read.Lines[1];
        Assert.Equal(((string?)null, SalesSourcePlanStockStatusesV1.Unreadable, (string?)null, SalesSourcePlanSuggestionReasonsV1.DeliveryUnreadable,
            false, SalesSourcePlanLockReasonsV1.DeliveryUnreadable), (line.Locked, line.StockStatus, line.Stock, line.SuggestionReason,
            line.Writable, line.LockReason));
        Assert.Contains(SalesSourcePlanWarningsV1.AllocationUnreadable, line.Warnings);
        Assert.Contains(SalesSourcePlanWarningsV1.DeliveryUnreadable, line.Warnings);
        Assert.Contains(SalesSourcePlanWarningsV1.LinkedSupplyUnreadable, line.Warnings);
    }

    [Fact]
    public async Task Parts_that_do_not_add_up_to_the_line_quantity_are_an_unrecorded_422_and_the_key_stays_reusable()
    {
        var world = new World();
        var (approved, _, _) = await PlannedOrderAsync(world);
        var second = approved.Lines[1].LineId;

        // 1 + 0.5 + 0.5 = 2 ≠ 2.25: the delivered part is counted, the remainder must be fully planned.
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RecordSourcePlanAsync(approved.Draft.Id,
            PlanOf("6", second, "0.5"), Metadata("k-plan")));
        Assert.Equal((422, SalesDraftProblemCodesV1.SourcePlanSumMismatch), Status(refused));
        Assert.Equal([new SalesDraftFieldError("lines[0]", SalesDraftProblemCodesV1.SourcePlanSumMismatch)], refused.Errors);
        // Planning the whole line as if nothing were delivered is the same mismatch (1 + 0.5 + 1.75 = 3.25).
        Assert.Equal((422, SalesDraftProblemCodesV1.SourcePlanSumMismatch), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordSourcePlanAsync(approved.Draft.Id, PlanOf("6", second, "1.75"), Metadata("k-plan")))));
        Assert.Empty(world.Store.Refusals);

        var fixedPlan = await world.Service().RecordSourcePlanAsync(approved.Draft.Id, PlanOf("6", second, "0.75"), Metadata("k-plan"));
        Assert.Equal("7", fixedPlan.Detail.Draft.Version);
    }

    [Fact]
    public async Task A_part_wider_than_the_decimal_18_3_column_is_a_400_and_no_owner_is_asked()
    {
        var world = new World();
        var (approved, deliveries, allocations) = await PlannedOrderAsync(world);
        var second = approved.Lines[1].LineId;

        // 16 integer digits, a value near decimal's own limit, and one more than decimal holds: all the same shape refusal.
        foreach (var text in new[] { "1000000000000000", "79000000000000000000000000000", "99999999999999999999999999999999" })
        {
            var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RecordSourcePlanAsync(
                approved.Draft.Id, new SalesDraftSourcePlanRequest("6", false,
                    [new SalesDraftSourcePlanLineRequestV1(second, text, text, "0")]), Metadata("k-wide")));
            Assert.Equal((400, "validation_failed"), Status(refused));
            Assert.Equal([new SalesDraftFieldError("lines[0]", "validation_failed")], refused.Errors);
        }

        Assert.Empty(deliveries.Asked);
        Assert.Empty(allocations.Asked);
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.SourcePlanRecords);
    }

    [Fact]
    public async Task Max_range_parts_beside_an_oversized_allocation_are_an_unrecorded_422_never_a_crash()
    {
        var world = new World();
        var (approved, _, allocations) = await PlannedOrderAsync(world);
        var second = approved.Lines[1].LineId;
        // An allocation near decimal's own limit plus two entered parts at the column maximum: the checked sum would overflow.
        allocations.Answer = order => new(Company, order, [new SalesOrderLineAllocationV1(second, 1, 79_000_000_000_000_000_000_000_000_000m)]);
        const string max = "999999999999999.999";

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RecordSourcePlanAsync(
            approved.Draft.Id, PlanOf("6", second, max, max), Metadata("k-max")));

        Assert.Equal((422, SalesDraftProblemCodesV1.SourcePlanSumMismatch), Status(refused));
        Assert.Equal([new SalesDraftFieldError("lines[0]", SalesDraftProblemCodesV1.SourcePlanSumMismatch)], refused.Errors);
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.SourcePlanRecords);
    }

    [Fact]
    public async Task A_fully_delivered_line_is_the_keys_recorded_409_and_replays_it()
    {
        var world = new World();
        var (approved, deliveries, _) = await PlannedOrderAsync(world);
        var second = approved.Lines[1].LineId;
        deliveries.Answer = order => new(Company, order, [new(second, KgUnit, 2.25m, 2.25m)]);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RecordSourcePlanAsync(approved.Draft.Id,
            PlanOf("6", second, "0"), Metadata("k-plan")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SourcePlanLineLocked), Status(refused));
        var refusal = Assert.Single(world.Store.Refusals);
        Assert.Equal((SourcePlanWritePermission, (Guid?)approved.Draft.Id), (refusal.Envelope.PermissionCode, refusal.DraftId));

        var replayed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RecordSourcePlanAsync(approved.Draft.Id,
            PlanOf("6", second, "0"), Metadata("k-plan", correlation: "retry")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SourcePlanLineLocked, true), (replayed.Status, replayed.Code, replayed.IsReplay));
        Assert.Empty(world.Store.SourcePlanRecords);
        var read = await world.Service().GetSourcePlanAsync(approved.Draft.Id);
        Assert.Equal((false, SalesSourcePlanLockReasonsV1.LineDelivered), (read.Lines[1].Writable, read.Lines[1].LockReason));
    }

    [Fact]
    public async Task An_open_linked_supply_needs_the_explicit_confirmation_which_is_recorded()
    {
        var world = new World();
        var (approved, _, _) = await PlannedOrderAsync(world);
        var second = approved.Lines[1].LineId;
        var supply = Guid.NewGuid();
        world.LinkedSupplies.Add(new FakeLinkedSupply(SalesLinkedSupplyKindsV1.Production, (query, kind) => Answer(query, kind,
            new SalesOrderLinkedSupplyItemV1(supply, Guid.NewGuid(), SalesLinkedSupplyDocumentTypesV1.ProductionOrder, "SENTETIK-1", second,
                "Approved", "0.75", KgUnit, null, null))));
        world.LinkedSupplies.Add(new FakeLinkedSupply(SalesLinkedSupplyKindsV1.Purchasing, (query, kind) => Answer(query, kind)));

        var read = await world.Service().GetSourcePlanAsync(approved.Draft.Id);
        Assert.Contains(SalesSourcePlanWarningsV1.LinkedSupplyOpen, read.Lines[1].Warnings);
        Assert.DoesNotContain(SalesSourcePlanWarningsV1.LinkedSupplyOpen, read.Lines[0].Warnings);

        var unconfirmed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RecordSourcePlanAsync(approved.Draft.Id,
            PlanOf("6", second, "0.75"), Metadata("k-plan")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SourcePlanLinkedSupplyUnconfirmed), Status(unconfirmed));
        Assert.Empty(world.Store.Refusals);

        var confirmed = await world.Service().RecordSourcePlanAsync(approved.Draft.Id, PlanOf("6", second, "0.75", confirm: true),
            Metadata("k-plan-confirmed"));
        Assert.Equal("7", confirmed.Detail.Draft.Version);
        Assert.True(Assert.Single(world.Store.SourcePlanRecords).LinkedSupplyConfirmed);
        var item = Assert.Single(Assert.Single(world.Store.SourcePlanInputs).LinkedSupply);
        Assert.Equal((SalesLinkedSupplyKindsV1.Production, supply, (Guid?)second), (item.Kind, item.SupplyId, item.OrderLineId));
    }

    [Fact]
    public async Task A_confirmation_without_an_open_linked_supply_is_not_recorded_as_confirmed()
    {
        var world = new World();
        var (approved, _, _) = await PlannedOrderAsync(world);
        var second = approved.Lines[1].LineId;

        await world.Service().RecordSourcePlanAsync(approved.Draft.Id, PlanOf("6", second, "0.75", confirm: true), Metadata("k-plan"));

        Assert.False(Assert.Single(world.Store.SourcePlanRecords).LinkedSupplyConfirmed);
    }

    [Fact]
    public async Task Unreadable_allocation_delivery_or_linked_supply_refuse_the_save_with_an_unrecorded_503()
    {
        var world = new World();
        var (approved, deliveries, allocations) = await PlannedOrderAsync(world);
        var second = approved.Lines[1].LineId;
        var request = PlanOf("6", second, "0.75");

        allocations.Answer = _ => throw new InvalidOperationException("SENTETIK allocation owner outage");
        Assert.Equal((503, SalesDraftProblemCodesV1.SourcePlanAllocationUnavailable), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordSourcePlanAsync(approved.Draft.Id, request, Metadata("k-allocation")))));
        allocations.Answer = order => new(Company, order, [new SalesOrderLineAllocationV1(second, 1, 0.5m)]);

        deliveries.Answer = _ => throw new InvalidOperationException("SENTETIK delivery owner outage");
        Assert.Equal((503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordSourcePlanAsync(approved.Draft.Id, request, Metadata("k-delivery")))));
        deliveries.Answer = order => new(Company, order, [new(second, KgUnit, 1m, 0m)]);

        // A supply owner declares it records links but no adapter answers: unreadable, nothing is confirmed blind (SAT-D254).
        world.LinkedSupplyRecorders.Add(new FakeSupplyRecorder(SalesLinkedSupplyKindsV1.Purchasing));
        Assert.Equal((503, SalesDraftProblemCodesV1.SourcePlanLinkedSupplyUnavailable), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordSourcePlanAsync(approved.Draft.Id, request with { ConfirmLinkedSupply = true }, Metadata("k-linked")))));

        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.SourcePlanRecords);
    }

    [Fact]
    public async Task Authority_version_and_line_refuse_before_any_owner_is_asked()
    {
        var world = new World();
        var (approved, deliveries, allocations) = await PlannedOrderAsync(world);
        var second = approved.Lines[1].LineId;
        var request = PlanOf("6", second, "0.75");

        world.Authority.Denied.Add(SourcePlanWritePermission);
        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordSourcePlanAsync(approved.Draft.Id, request, Metadata("k-denied")))));
        world.Authority.Denied.Clear();

        world.User.Authenticated = false;
        Assert.Equal(401, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordSourcePlanAsync(approved.Draft.Id, request, Metadata("k-anonymous")))).Status);
        world.User.Authenticated = true;

        // A stale version is the key's recorded 409 and replays it; no owner is asked.
        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordSourcePlanAsync(approved.Draft.Id, request with { ExpectedVersion = "5" }, Metadata("k-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), Status(stale));
        Assert.Single(world.Store.Refusals);
        var staleAgain = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordSourcePlanAsync(approved.Draft.Id, request with { ExpectedVersion = "5" }, Metadata("k-stale", correlation: "retry")));
        Assert.True(staleAgain.IsReplay);

        Assert.Equal((422, "validation_failed"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordSourcePlanAsync(approved.Draft.Id, PlanOf("6", Guid.NewGuid(), "0.75"), Metadata("k-line")))));
        Assert.Equal((400, "validation_failed"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordSourcePlanAsync(approved.Draft.Id, PlanOf("6", second, "-1"), Metadata("k-negative")))));
        Assert.Equal((400, "validation_failed"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordSourcePlanAsync(approved.Draft.Id, PlanOf("6", second, "0.0001"), Metadata("k-scale")))));
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordSourcePlanAsync(Guid.NewGuid(), request, Metadata("k-404")))).Status);

        Assert.Empty(deliveries.Asked);
        Assert.Empty(allocations.Asked);
        Assert.Single(world.Store.Refusals);
        Assert.Empty(world.Store.SourcePlanRecords);
    }

    [Fact]
    public async Task An_unregistered_source_plan_name_is_a_503_setup_fault_and_nothing_is_read_or_recorded()
    {
        var world = new World();
        var (approved, deliveries, _) = await PlannedOrderAsync(world);
        world.Bindings = world.Bindings with { SourcePlanPermission = "sales.synthetic.unregistered" };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RecordSourcePlanAsync(approved.Draft.Id,
            PlanOf("6", approved.Lines[1].LineId, "0.75"), Metadata("k-plan")));

        Assert.Equal((503, SalesDraftProblemCodesV1.PermissionUnregistered), Status(refused));
        Assert.Empty(deliveries.Asked);
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.SourcePlanRecords);
    }

    [Fact]
    public async Task A_cancelled_order_is_the_recorded_state_conflict_and_reads_not_writable()
    {
        var world = new World();
        Owners(world);
        var approved = await ApprovedAsync(world);
        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);
        await world.Service().CancelDraftAsync(approved.Draft.Id, new SalesDraftCancelRequest("4", "SENTETIK", preview.ImpactToken),
            Metadata("k-cancel"));
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RecordSourcePlanAsync(approved.Draft.Id,
            PlanOf("5", approved.Lines[0].LineId, "1.25"), Metadata("k-plan")));

        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), Status(refused));
        Assert.Single(world.Store.Refusals);
        var read = await world.Service().GetSourcePlanAsync(approved.Draft.Id);
        Assert.Equal((false, SalesSourcePlanLockReasonsV1.OrderState), (read.Writable, read.Lines[0].LockReason));
    }
}

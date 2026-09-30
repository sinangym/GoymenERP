using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// AT-055 through the service: a dispatch-note draft is made from definite shipments only (lines = the shipments' shipped
/// quantities), fenced by its own bound name, replayed on the same key without a second draft, refused as a recorded terminal 409
/// when a shipment is already on a draft (the single link) or the version is stale, shown on the instruction after reopening,
/// and listed for its Sales order with only that order's lines. SENTETIK / NOT_ENV; a registered platform name stands in for
/// <c>logistics.dispatch-notes.create</c> until the central catalogue knows it.
/// </summary>
public sealed partial class LogisticsInstructionServiceTests
{
    private const string DispatchNotePermission = ErpPermissions.CounterpartyPartiesUpdateBasic;

    /// <summary>An issued instruction (10 kg) with 8 kg loaded and two definite shipments (3 kg, 2.5 kg): version 6.</summary>
    private static async Task<(World World, Guid Id, Guid Line, Guid First, Guid Second)> ShippedWorld()
    {
        var world = new World();
        world.Bindings = world.Bindings with { DispatchNotePermission = DispatchNotePermission };
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "10"), Metadata("k-create"));
        var id = created.Detail.Instruction.Id;
        var line = created.Detail.Lines[0].LineId;
        await world.Service().IssueInstructionAsync(id, new IssueLoadingInstructionRequest("2"), Metadata("k-issue"));
        await world.Service().RecordLoadAsync(id, line, new RecordLoadRequest("3", Guid.NewGuid(), "8", "Measured"), Metadata("k-load"));
        // The definite shipment's own path (with its Inventory half) is proven by the realization tests; here the recorded
        // shipments are what a dispatch note reads, so they are put on the stored instruction through the domain.
        var stored = (await world.Store.FindInstructionAsync(id))!.Instruction;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        stored.MarkShipped(4, first, line, Quantity.Of(3m, QuantityUnit.Kilogram), DateTimeOffset.UtcNow);
        stored.MarkShipped(5, second, line, Quantity.Of(2.5m, QuantityUnit.Kilogram), DateTimeOffset.UtcNow);
        return (world, id, line, first, second);
    }

    private static CreateDispatchNoteRequest DispatchNote(string version, params Guid[] shipments) => new(version, Guid.NewGuid(), shipments);

    [Fact]
    public async Task A_draft_from_a_shipment_carries_only_that_shipments_quantity_and_shows_after_reopening_and_replays_without_a_second()
    {
        var (world, id, line, first, _) = await ShippedWorld();
        var request = DispatchNote("6", first);

        var created = await world.Service().CreateDispatchNoteAsync(id, request, Metadata("k-note"));

        Assert.False(created.IsReplay);
        Assert.Equal("7", created.Detail.Instruction.Version);
        var note = Assert.Single(created.Detail.DispatchNotes!);
        Assert.Equal((request.DispatchNoteId, "Draft", Actor), (note.DispatchNoteId, note.Status, note.CreatedBy));
        var noteLine = Assert.Single(note.Lines);
        Assert.Equal((1, first, line, Order, OrderLine, Product, KgUnit, "3"),
            (noteLine.LineNo, noteLine.ShipmentId, noteLine.InstructionLineId, noteLine.SourceOrderId, noteLine.SourceOrderLineId,
                noteLine.ProductId, noteLine.UnitId, noteLine.Quantity));
        // A document, not a share: the shipped / delivered totals do not move.
        Assert.Equal(("5.5", "0"), (created.Detail.Lines[0].Shipped, created.Detail.Lines[0].Delivered));
        Assert.Equal(DispatchNotePermission, world.Authority.LastPermission);
        // No number exists (not decided): the wire view has no number member.
        Assert.Equal(["CreatedAtUtc", "CreatedBy", "DispatchNoteId", "Lines", "Status"],
            typeof(DispatchNoteView).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());

        // Close and reopen: the detail read answers the same draft with its line.
        var reopened = await world.Service().GetInstructionDetailAsync(id);
        Assert.Equal(note.DispatchNoteId, Assert.Single(reopened.DispatchNotes!).DispatchNoteId);
        Assert.Equal(noteLine, reopened.DispatchNotes![0].Lines[0]);

        // The same key again: the stored answer, no second draft and no second version step.
        var replay = await world.Service().CreateDispatchNoteAsync(id, request, Metadata("k-note", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.Equal(("7", "synthetic-correlation"), (replay.Detail.Instruction.Version, replay.OriginalCorrelationId));
        Assert.Single(replay.Detail.DispatchNotes!);
        Assert.Equal(1, world.Store.Transitions.Count(transition => transition.Command == LogisticsCommands.CreateDispatchNoteV1));
    }

    [Fact]
    public async Task A_second_draft_for_an_already_linked_shipment_is_a_recorded_terminal_refusal_and_the_first_stays()
    {
        var (world, id, _, first, second) = await ShippedWorld();
        var drafted = await world.Service().CreateDispatchNoteAsync(id, DispatchNote("6", first), Metadata("k-note-1"));

        // Another draft naming the same shipment (with a still-free one beside it) is refused whole: AT-055.
        var again = DispatchNote("7", second, first);
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CreateDispatchNoteAsync(id, again, Metadata("k-note-2")));
        Assert.Equal((409, LogisticsProblemCodesV1.ShipmentAlreadyDispatched, "shipmentIds", false),
            (refusal.Status, refusal.Code, refusal.Errors[0].Field, refusal.IsReplay));
        var replayed = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CreateDispatchNoteAsync(id, again, Metadata("k-note-2")));
        Assert.Equal((409, LogisticsProblemCodesV1.ShipmentAlreadyDispatched, true), (replayed.Status, replayed.Code, replayed.IsReplay));

        var detail = await world.Service().GetInstructionDetailAsync(id);
        Assert.Equal("7", detail.Instruction.Version);
        Assert.Equal(drafted.Detail.DispatchNotes![0].DispatchNoteId, Assert.Single(detail.DispatchNotes!).DispatchNoteId);

        // The free shipment can still go on its own draft; then both are linked once each.
        var next = await world.Service().CreateDispatchNoteAsync(id, DispatchNote("7", second), Metadata("k-note-3"));
        Assert.Equal(("8", 2), (next.Detail.Instruction.Version, next.Detail.DispatchNotes!.Count));
        Assert.Equal(("2.5", second), (next.Detail.DispatchNotes[1].Lines[0].Quantity, next.Detail.DispatchNotes[1].Lines[0].ShipmentId));
    }

    [Fact]
    public async Task A_stale_version_a_draft_instruction_a_duplicate_id_and_an_unknown_or_non_shipment_id_are_refused_without_a_draft()
    {
        var (world, id, _, first, second) = await ShippedWorld();

        var stale = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CreateDispatchNoteAsync(id, DispatchNote("5", first), Metadata("k-stale")));
        Assert.Equal((409, LogisticsProblemCodesV1.VersionConflict, "expectedVersion"), (stale.Status, stale.Code, stale.Errors[0].Field));
        Assert.Single(world.Store.Refusals);

        var unknown = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateDispatchNoteAsync(id, DispatchNote("6", Guid.NewGuid()), Metadata("k-unknown")));
        Assert.Equal((422, LogisticsProblemCodesV1.ShipmentNotFound), (unknown.Status, unknown.Code));
        // A load share is not a definite shipment.
        var loadShare = (await world.Service().GetInstructionDetailAsync(id)).Shares.Single(share => share.Kind == "Loaded").ShareId;
        var notShipment = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateDispatchNoteAsync(id, DispatchNote("6", loadShare), Metadata("k-load-share")));
        Assert.Equal((422, LogisticsProblemCodesV1.ShipmentNotFound), (notShipment.Status, notShipment.Code));

        var repeated = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateDispatchNoteAsync(id, new CreateDispatchNoteRequest("6", Guid.NewGuid(), [first, first]), Metadata("k-repeat")));
        Assert.Equal((400, "validation_failed"), (repeated.Status, repeated.Code));

        var drafted = DispatchNote("6", first);
        await world.Service().CreateDispatchNoteAsync(id, drafted, Metadata("k-note"));
        var sameId = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateDispatchNoteAsync(id, new CreateDispatchNoteRequest("7", drafted.DispatchNoteId, [second]), Metadata("k-same-id")));
        Assert.Equal((409, LogisticsProblemCodesV1.DuplicateDispatchNote), (sameId.Status, sameId.Code));

        var draftInstruction = (await world.Service().CreateInstructionAsync(Request(), Metadata("k-create-2"))).Detail.Instruction.Id;
        var state = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateDispatchNoteAsync(draftInstruction, DispatchNote("2", first), Metadata("k-draft")));
        Assert.Equal((409, LogisticsProblemCodesV1.InstructionState), (state.Status, state.Code));
        Assert.Single((await world.Service().GetInstructionDetailAsync(id)).DispatchNotes!);
    }

    [Fact]
    public async Task The_command_is_measured_under_its_own_name_and_a_missing_grant_writes_nothing()
    {
        var (world, id, _, first, _) = await ShippedWorld();
        world.Authority.DeniedPermission = DispatchNotePermission;

        var denied = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CreateDispatchNoteAsync(id, DispatchNote("6", first), Metadata("k-note")));

        Assert.Equal((403, "forbidden"), (denied.Status, denied.Code));
        Assert.Empty((await world.Service().GetInstructionDetailAsync(id)).DispatchNotes!);
        Assert.DoesNotContain(world.Store.Transitions, transition => transition.Command == LogisticsCommands.CreateDispatchNoteV1);

        // Unbound (the central catalogue does not know the manifest name yet): a 503 setup problem, never a grant.
        world.Authority.DeniedPermission = null;
        world.Bindings = world.Bindings with { DispatchNotePermission = LogisticsPermissionManifestV1.DispatchNotesCreate };
        if (!ErpPermissions.IsKnown(LogisticsPermissionManifestV1.DispatchNotesCreate))
        {
            var unregistered = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service().CreateDispatchNoteAsync(id, DispatchNote("6", first), Metadata("k-note-2")));
            Assert.Equal((503, LogisticsProblemCodesV1.PermissionUnregistered), (unregistered.Status, unregistered.Code));
        }
    }

    [Fact]
    public async Task The_sales_order_read_lists_the_drafts_on_its_lines_under_the_instruction_read_and_creates_nothing()
    {
        var (world, id, _, first, second) = await ShippedWorld();
        await world.Service().CreateDispatchNoteAsync(id, DispatchNote("6", first, second), Metadata("k-note"));
        var transitions = world.Store.Transitions.Count;

        var view = await world.Service().GetSalesOrderDispatchNotesAsync(Order);

        Assert.Equal((Order, false), (view.SalesOrderId, view.HasMore));
        var note = Assert.Single(view.Items);
        Assert.Equal((id, "Draft"), (note.InstructionId, note.Status));
        Assert.Equal([(1, OrderLine, first, "3"), (2, OrderLine, second, "2.5")],
            note.Lines.Select(line => (line.LineNo, line.SourceOrderLineId, line.ShipmentId, line.Quantity)).ToArray());
        Assert.Equal(ReadPermission, world.Authority.LastPermission);
        Assert.Equal(transitions, world.Store.Transitions.Count);

        Assert.Empty((await world.Service().GetSalesOrderDispatchNotesAsync(Guid.NewGuid())).Items);
        var empty = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetSalesOrderDispatchNotesAsync(Guid.Empty));
        Assert.Equal(400, empty.Status);
        world.Authority.DeniedPermission = ReadPermission;
        var denied = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetSalesOrderDispatchNotesAsync(Order));
        Assert.Equal(403, denied.Status);
    }

    [Fact]
    public void The_wire_request_is_strict_and_names_no_quantity_price_number_or_customer()
    {
        var shipment = Guid.NewGuid();
        var request = LogisticsJson.ReadCreateDispatchNote($$"""{"expectedVersion":"6","dispatchNoteId":"{{Guid.NewGuid()}}","shipmentIds":["{{shipment}}"]}""");
        Assert.Equal(shipment, Assert.Single(request.ShipmentIds));
        foreach (var body in new[]
                 {
                     $$"""{"expectedVersion":"6","dispatchNoteId":"{{Guid.NewGuid()}}","shipmentIds":[]}""",
                     $$"""{"expectedVersion":"6","dispatchNoteId":"{{Guid.NewGuid()}}","shipmentIds":["{{shipment}}","{{shipment}}"]}""",
                     $$"""{"expectedVersion":"0","dispatchNoteId":"{{Guid.NewGuid()}}","shipmentIds":["{{shipment}}"]}""",
                     $$"""{"expectedVersion":"6","dispatchNoteId":"{{Guid.NewGuid()}}","shipmentIds":["{{shipment}}"],"quantity":"3"}""",
                     $$"""{"expectedVersion":"6","dispatchNoteId":"00000000-0000-0000-0000-000000000000","shipmentIds":["{{shipment}}"]}""",
                 })
        {
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => LogisticsJson.ReadCreateDispatchNote(body));
        }

        Assert.Equal(["DispatchNoteId", "ExpectedVersion", "ShipmentIds"],
            typeof(CreateDispatchNoteRequest).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(typeof(DispatchNoteLineView).GetProperties(), property =>
            property.Name.Contains("Price", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Amount", StringComparison.OrdinalIgnoreCase));
        Assert.True(LogisticsCommands.IsMutation(LogisticsCommands.CreateDispatchNoteV1));
        Assert.Equal(DispatchNoteStatus.Draft, Enum.GetValues<DispatchNoteStatus>().Single());
    }
}

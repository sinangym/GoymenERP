using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// AT-055 through the service: a dispatch-note draft is made from definite shipments only (lines = the shipments' shipped
/// quantities), fenced by its own registered name, replayed on the same key without a second draft, refused as a recorded terminal
/// 409 when a shipment is already on a non-cancelled note (the single link) or the version is stale, shown on the instruction after
/// reopening, and listed for its Sales order with only that order's lines. The lifecycle (user decision 2026-09-30): approve gives
/// the company's next system number, cancel (with a reason) releases the shipments for a new note, the e-İrsaliye number is
/// recorded once on an approved note — each under its own name, idempotent, version-fenced. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class LogisticsInstructionServiceTests
{
    private const string DispatchNotePermission = LogisticsPermissionManifestV1.DispatchNotesCreate;
    private const string DispatchNoteApprovePermission = LogisticsPermissionManifestV1.DispatchNotesApprove;
    private const string DispatchNoteCancelPermission = LogisticsPermissionManifestV1.DispatchNotesCancel;

    /// <summary>An issued instruction (10 kg) with 8 kg loaded and two definite shipments (3 kg, 2.5 kg): version 6.</summary>
    private static async Task<(World World, Guid Id, Guid Line, Guid First, Guid Second)> ShippedWorld(World? existing = null)
    {
        var world = existing ?? new World();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "10"), Metadata($"k-create-{suffix}"));
        var id = created.Detail.Instruction.Id;
        var line = created.Detail.Lines[0].LineId;
        await world.Service().IssueInstructionAsync(id, new IssueLoadingInstructionRequest("2"), Metadata($"k-issue-{suffix}"));
        await world.Service().RecordLoadAsync(id, line, new RecordLoadRequest("3", Guid.NewGuid(), "8", "Measured"), Metadata($"k-load-{suffix}"));
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
        var note = Assert.Single(created.Detail.DispatchNotes);
        Assert.Equal((request.DispatchNoteId, "Draft", Actor), (note.DispatchNoteId, note.Status, note.CreatedBy));
        // A draft has no number, no approval, no cancellation and no e-İrsaliye number yet.
        Assert.Equal((null, null, null, null, null, null, null), (note.Number, note.ApprovedBy, note.ApprovedAtUtc, note.CancelledBy, note.CancelledAtUtc,
            note.CancelReason, note.EDispatchNumber));
        var noteLine = Assert.Single(note.Lines);
        Assert.Equal((1, first, line, Order, OrderLine, Product, KgUnit, "3"),
            (noteLine.LineNo, noteLine.ShipmentId, noteLine.InstructionLineId, noteLine.SourceOrderId, noteLine.SourceOrderLineId,
                noteLine.ProductId, noteLine.UnitId, noteLine.Quantity));
        // A document, not a share: the shipped / delivered totals do not move.
        Assert.Equal(("5.5", "0"), (created.Detail.Lines[0].Shipped, created.Detail.Lines[0].Delivered));
        Assert.Equal(DispatchNotePermission, world.Authority.LastPermission);
        // The system number and the e-İrsaliye number are two separate members; no price member exists.
        Assert.Equal(["ApprovedAtUtc", "ApprovedBy", "CancelReason", "CancelledAtUtc", "CancelledBy", "CreatedAtUtc", "CreatedBy", "DispatchNoteId",
                "EDispatchNumber", "Lines", "Number", "Status"],
            typeof(DispatchNoteView).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());

        // Close and reopen: the detail read answers the same draft with its line.
        var reopened = await world.Service().GetInstructionDetailAsync(id);
        Assert.Equal(note.DispatchNoteId, Assert.Single(reopened.DispatchNotes).DispatchNoteId);
        Assert.Equal(noteLine, reopened.DispatchNotes[0].Lines[0]);

        // The same key again: the stored answer, no second draft and no second version step.
        var replay = await world.Service().CreateDispatchNoteAsync(id, request, Metadata("k-note", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.Equal(("7", "synthetic-correlation"), (replay.Detail.Instruction.Version, replay.OriginalCorrelationId));
        Assert.Single(replay.Detail.DispatchNotes);
        Assert.Equal(1, world.Store.Transitions.Count(transition => transition.Command == LogisticsCommands.CreateDispatchNoteV1));
    }

    [Fact]
    public async Task The_same_key_with_another_body_is_refused_without_a_second_draft()
    {
        // #518 P2: the replay identity is the whole body — the same Idempotency-Key with other shipments (or another draft id) is a
        // 409 same_key_payload_mismatch that discloses nothing and creates nothing.
        var (world, id, _, first, second) = await ShippedWorld();
        var request = DispatchNote("6", first);
        await world.Service().CreateDispatchNoteAsync(id, request, Metadata("k-note"));

        foreach (var other in new[] { request with { ShipmentIds = [second] }, request with { DispatchNoteId = Guid.NewGuid() }, request with { ExpectedVersion = "7" } })
        {
            var mismatch = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CreateDispatchNoteAsync(id, other, Metadata("k-note")));
            Assert.Equal((409, LogisticsProblemCodesV1.SameKeyPayloadMismatch, false), (mismatch.Status, mismatch.Code, mismatch.IsReplay));
        }

        var detail = await world.Service().GetInstructionDetailAsync(id);
        Assert.Equal(("7", request.DispatchNoteId), (detail.Instruction.Version, Assert.Single(detail.DispatchNotes).DispatchNoteId));
        Assert.Equal(1, world.Store.Transitions.Count(transition => transition.Command == LogisticsCommands.CreateDispatchNoteV1));

        // The lifecycle commands' keys are body-bound the same way: another note id or another reason under one key is refused.
        var cancel = new CancelDispatchNoteRequest("7", "Yanlış sevk seçildi");
        await world.Service().CancelDispatchNoteAsync(id, request.DispatchNoteId, cancel, Metadata("k-cancel"));
        var otherReason = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CancelDispatchNoteAsync(id, request.DispatchNoteId, cancel with { Reason = "Başka gerekçe" }, Metadata("k-cancel")));
        Assert.Equal((409, LogisticsProblemCodesV1.SameKeyPayloadMismatch), (otherReason.Status, otherReason.Code));
        var otherNote = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CancelDispatchNoteAsync(id, Guid.NewGuid(), cancel, Metadata("k-cancel")));
        Assert.Equal((409, LogisticsProblemCodesV1.SameKeyPayloadMismatch), (otherNote.Status, otherNote.Code));
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
        Assert.Equal(drafted.Detail.DispatchNotes[0].DispatchNoteId, Assert.Single(detail.DispatchNotes).DispatchNoteId);

        // The free shipment can still go on its own draft; then both are linked once each.
        var next = await world.Service().CreateDispatchNoteAsync(id, DispatchNote("7", second), Metadata("k-note-3"));
        Assert.Equal(("8", 2), (next.Detail.Instruction.Version, next.Detail.DispatchNotes.Count));
        Assert.Equal(("2.5", second), (next.Detail.DispatchNotes[1].Lines[0].Quantity, next.Detail.DispatchNotes[1].Lines[0].ShipmentId));

        // An APPROVED note holds its shipments the same way.
        await world.Service().ApproveDispatchNoteAsync(id, drafted.Detail.DispatchNotes[0].DispatchNoteId, new ApproveDispatchNoteRequest("8"), Metadata("k-approve"));
        var onApproved = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateDispatchNoteAsync(id, DispatchNote("9", first), Metadata("k-note-4")));
        Assert.Equal((409, LogisticsProblemCodesV1.ShipmentAlreadyDispatched), (onApproved.Status, onApproved.Code));
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
        Assert.Single((await world.Service().GetInstructionDetailAsync(id)).DispatchNotes);
    }

    [Fact]
    public async Task A_note_id_taken_on_another_instruction_is_the_recorded_terminal_duplicate_not_a_concurrent_change()
    {
        // #518 P2: the store refuses a note id of another instruction of the company with the domain's code (the real store's
        // company-wide check); the service records it under the key and replays it, as for this instruction's own ids.
        var (world, id, _, first, _) = await ShippedWorld();
        world.Store.MutateFault = command => command == LogisticsCommands.CreateDispatchNoteV1
            ? new LogisticsRuleException(LogisticsRuleCodes.DuplicateDispatchNoteId, "synthetic: taken on another instruction")
            : null;
        var request = DispatchNote("6", first);

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CreateDispatchNoteAsync(id, request, Metadata("k-taken")));
        var replayed = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CreateDispatchNoteAsync(id, request, Metadata("k-taken")));

        Assert.Equal((409, LogisticsProblemCodesV1.DuplicateDispatchNote, "dispatchNoteId", false),
            (refusal.Status, refusal.Code, refusal.Errors[0].Field, refusal.IsReplay));
        Assert.Equal((409, LogisticsProblemCodesV1.DuplicateDispatchNote, true), (replayed.Status, replayed.Code, replayed.IsReplay));
        Assert.Single(world.Store.Refusals);
        Assert.Empty((await world.Service().GetInstructionDetailAsync(id)).DispatchNotes);
    }

    [Fact]
    public async Task The_commands_are_measured_under_their_own_names_and_a_missing_grant_writes_nothing()
    {
        var (world, id, _, first, _) = await ShippedWorld();
        world.Authority.DeniedPermission = DispatchNotePermission;

        var denied = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CreateDispatchNoteAsync(id, DispatchNote("6", first), Metadata("k-note")));

        Assert.Equal((403, "forbidden"), (denied.Status, denied.Code));
        Assert.Empty((await world.Service().GetInstructionDetailAsync(id)).DispatchNotes);
        Assert.DoesNotContain(world.Store.Transitions, transition => transition.Command == LogisticsCommands.CreateDispatchNoteV1);

        world.Authority.DeniedPermission = null;
        var request = DispatchNote("6", first);
        await world.Service().CreateDispatchNoteAsync(id, request, Metadata("k-note-ok"));
        Assert.Equal(DispatchNotePermission, world.Authority.LastPermission);

        // Holding create does not approve or cancel; each lifecycle command is its own name.
        world.Authority.DeniedPermission = DispatchNoteApprovePermission;
        var approveDenied = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().ApproveDispatchNoteAsync(id, request.DispatchNoteId, new ApproveDispatchNoteRequest("7"), Metadata("k-approve")));
        Assert.Equal(403, approveDenied.Status);
        var eNumberDenied = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().SetEDispatchNumberAsync(id, request.DispatchNoteId, new SetEDispatchNumberRequest("7", "GYM2026000000001"), Metadata("k-e")));
        Assert.Equal(403, eNumberDenied.Status);
        world.Authority.DeniedPermission = DispatchNoteCancelPermission;
        var cancelDenied = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CancelDispatchNoteAsync(id, request.DispatchNoteId, new CancelDispatchNoteRequest("7", "gerekçe"), Metadata("k-cancel")));
        Assert.Equal(403, cancelDenied.Status);
        Assert.Equal((DispatchNoteStatus.Draft, 7L), (world.Store.Instruction(id).DispatchNotes[0].Status, world.Store.Instruction(id).Version));

        world.Authority.DeniedPermission = null;
        await world.Service().ApproveDispatchNoteAsync(id, request.DispatchNoteId, new ApproveDispatchNoteRequest("7"), Metadata("k-approve-ok"));
        Assert.Equal(DispatchNoteApprovePermission, world.Authority.LastPermission);
        await world.Service().CancelDispatchNoteAsync(id, request.DispatchNoteId, new CancelDispatchNoteRequest("8", "gerekçe"), Metadata("k-cancel-ok"));
        Assert.Equal(DispatchNoteCancelPermission, world.Authority.LastPermission);

        // Unregistered (a name the central catalogue does not know): a 503 setup problem, never a grant — and an unbound name the same.
        const string unregistered = "logistics.dispatch-notes.synthetic-unregistered";
        Assert.False(ErpPermissions.IsKnown(unregistered));
        foreach (var bindings in new[]
                 {
                     world.Bindings with { DispatchNotePermission = unregistered },
                     world.Bindings with { DispatchNotePermission = null },
                 })
        {
            world.Bindings = bindings;
            var setup = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service().CreateDispatchNoteAsync(id, DispatchNote("9", first), Metadata($"k-setup-{bindings.DispatchNotePermission}")));
            Assert.Equal((503, LogisticsProblemCodesV1.PermissionUnregistered), (setup.Status, setup.Code));
        }

        // The registered names are the manifest's own, bound by default.
        Assert.True(ErpPermissions.IsKnown(DispatchNotePermission) && ErpPermissions.IsKnown(DispatchNoteApprovePermission)
            && ErpPermissions.IsKnown(DispatchNoteCancelPermission));
        var defaults = new LogisticsInstructionServiceBindings();
        Assert.Equal((DispatchNotePermission, DispatchNoteApprovePermission, DispatchNoteCancelPermission),
            (defaults.DispatchNotePermission, defaults.DispatchNoteApprovePermission, defaults.DispatchNoteCancelPermission));
    }

    [Fact]
    public async Task Approval_gives_each_note_the_next_company_number_once_and_a_second_approval_is_a_recorded_terminal_refusal()
    {
        var (world, id, _, first, second) = await ShippedWorld();
        var a = DispatchNote("6", first);
        var b = DispatchNote("7", second);
        await world.Service().CreateDispatchNoteAsync(id, a, Metadata("k-a"));
        await world.Service().CreateDispatchNoteAsync(id, b, Metadata("k-b"));

        // Approved out of creation order: the number is the approval order (the company series), not the draft order.
        var approvedB = await world.Service().ApproveDispatchNoteAsync(id, b.DispatchNoteId, new ApproveDispatchNoteRequest("8"), Metadata("k-approve-b"));
        var noteB = approvedB.Detail.DispatchNotes.Single(note => note.DispatchNoteId == b.DispatchNoteId);
        Assert.Equal(("9", "Approved", "IRS-000001", Actor), (approvedB.Detail.Instruction.Version, noteB.Status, noteB.Number, noteB.ApprovedBy));
        Assert.NotNull(noteB.ApprovedAtUtc);
        Assert.Equal(DispatchNoteApprovePermission, world.Authority.LastPermission);

        // Another instruction of the company continues the same series: gap-free, never a duplicate.
        var (_, otherId, _, otherShipment, _) = await ShippedWorld(world);
        var other = DispatchNote("6", otherShipment);
        await world.Service().CreateDispatchNoteAsync(otherId, other, Metadata("k-other"));
        var approvedOther = await world.Service().ApproveDispatchNoteAsync(otherId, other.DispatchNoteId, new ApproveDispatchNoteRequest("7"), Metadata("k-approve-other"));
        Assert.Equal("IRS-000002", Assert.Single(approvedOther.Detail.DispatchNotes).Number);
        var approvedA = await world.Service().ApproveDispatchNoteAsync(id, a.DispatchNoteId, new ApproveDispatchNoteRequest("9"), Metadata("k-approve-a"));
        Assert.Equal("IRS-000003", approvedA.Detail.DispatchNotes.Single(note => note.DispatchNoteId == a.DispatchNoteId).Number);

        // The same key again: the stored answer — no second number, no version step.
        var replay = await world.Service().ApproveDispatchNoteAsync(id, a.DispatchNoteId, new ApproveDispatchNoteRequest("9"), Metadata("k-approve-a"));
        Assert.True(replay.IsReplay);
        Assert.Equal(("10", "IRS-000003"), (replay.Detail.Instruction.Version, replay.Detail.DispatchNotes.Single(note => note.DispatchNoteId == a.DispatchNoteId).Number));

        // Approving an approved note again (another key) is a recorded terminal refusal; the number stays.
        var twice = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().ApproveDispatchNoteAsync(id, a.DispatchNoteId, new ApproveDispatchNoteRequest("10"), Metadata("k-approve-a-2")));
        Assert.Equal((409, LogisticsProblemCodesV1.DispatchNoteState, "status"), (twice.Status, twice.Code, twice.Errors[0].Field));
        var twiceReplayed = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().ApproveDispatchNoteAsync(id, a.DispatchNoteId, new ApproveDispatchNoteRequest("10"), Metadata("k-approve-a-2")));
        Assert.True(twiceReplayed.IsReplay);

        // A stale version and an unknown note: nothing moves.
        var stale = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().ApproveDispatchNoteAsync(id, b.DispatchNoteId, new ApproveDispatchNoteRequest("3"), Metadata("k-approve-stale")));
        Assert.Equal((409, LogisticsProblemCodesV1.VersionConflict), (stale.Status, stale.Code));
        var unknown = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().ApproveDispatchNoteAsync(id, Guid.NewGuid(), new ApproveDispatchNoteRequest("10"), Metadata("k-approve-unknown")));
        Assert.Equal((404, "not_found"), (unknown.Status, unknown.Code));
        Assert.Equal(10L, world.Store.Instruction(id).Version);

        // The Sales order view shows the state and the number.
        var view = await world.Service().GetSalesOrderDispatchNotesAsync(Order);
        Assert.Contains(view.Items, item => item.DispatchNoteId == b.DispatchNoteId && item is { Status: "Approved", Number: "IRS-000001", EDispatchNumber: null });
    }

    [Fact]
    public async Task Cancelling_keeps_the_note_and_its_number_releases_its_shipments_and_a_new_draft_takes_them()
    {
        var (world, id, _, first, second) = await ShippedWorld();
        var draft = DispatchNote("6", first, second);
        await world.Service().CreateDispatchNoteAsync(id, draft, Metadata("k-note"));
        await world.Service().ApproveDispatchNoteAsync(id, draft.DispatchNoteId, new ApproveDispatchNoteRequest("7"), Metadata("k-approve"));

        // The reason is required, trimmed text (the wire refuses a padded one), ≤ 500 characters.
        foreach (var reason in new[] { "", "   ", " padded ", new string('x', 501) })
        {
            var invalid = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service().CancelDispatchNoteAsync(id, draft.DispatchNoteId, new CancelDispatchNoteRequest("8", reason), Metadata($"k-bad-{reason.Length}")));
            Assert.Equal((400, "validation_failed", "reason"), (invalid.Status, invalid.Code, invalid.Errors[0].Field));
        }

        var cancelled = await world.Service().CancelDispatchNoteAsync(id, draft.DispatchNoteId, new CancelDispatchNoteRequest("8", "Yanlış araç"), Metadata("k-cancel"));
        var note = Assert.Single(cancelled.Detail.DispatchNotes);
        Assert.Equal(("9", "Cancelled", "IRS-000001", Actor, "Yanlış araç", 2), (cancelled.Detail.Instruction.Version, note.Status, note.Number,
            note.CancelledBy, note.CancelReason, note.Lines.Count));
        Assert.NotNull(note.CancelledAtUtc);
        Assert.Equal(DispatchNoteCancelPermission, world.Authority.LastPermission);

        // Cancelled once: a second cancel, an approval or an e-İrsaliye number on it is a recorded terminal refusal.
        var again = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CancelDispatchNoteAsync(id, draft.DispatchNoteId, new CancelDispatchNoteRequest("9", "tekrar"), Metadata("k-cancel-2")));
        Assert.Equal((409, LogisticsProblemCodesV1.DispatchNoteState), (again.Status, again.Code));
        var approve = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().ApproveDispatchNoteAsync(id, draft.DispatchNoteId, new ApproveDispatchNoteRequest("9"), Metadata("k-approve-2")));
        Assert.Equal((409, LogisticsProblemCodesV1.DispatchNoteState), (approve.Status, approve.Code));

        // Correction = cancel + new draft: the released shipments go on a new note; the cancelled one stays visible with its number.
        var correction = DispatchNote("9", first);
        var redrafted = await world.Service().CreateDispatchNoteAsync(id, correction, Metadata("k-redraft"));
        Assert.Equal([("Cancelled", "IRS-000001"), ("Draft", (string?)null)],
            redrafted.Detail.DispatchNotes.Select(item => (item.Status, item.Number)).ToArray());
        var approved = await world.Service().ApproveDispatchNoteAsync(id, correction.DispatchNoteId, new ApproveDispatchNoteRequest("10"), Metadata("k-approve-3"));
        Assert.Equal("IRS-000002", approved.Detail.DispatchNotes[1].Number);

        // A cancelled note's id is never reused.
        var reused = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateDispatchNoteAsync(id, new CreateDispatchNoteRequest("11", draft.DispatchNoteId, [second]), Metadata("k-reuse")));
        Assert.Equal((409, LogisticsProblemCodesV1.DuplicateDispatchNote), (reused.Status, reused.Code));

        // A DRAFT can be cancelled too (no number is ever given to it) and releases its shipment.
        var loose = DispatchNote("11", second);
        await world.Service().CreateDispatchNoteAsync(id, loose, Metadata("k-loose"));
        var looseCancelled = await world.Service().CancelDispatchNoteAsync(id, loose.DispatchNoteId, new CancelDispatchNoteRequest("12", "vazgeçildi"), Metadata("k-loose-cancel"));
        Assert.Equal(("Cancelled", (string?)null), (looseCancelled.Detail.DispatchNotes[2].Status, looseCancelled.Detail.DispatchNotes[2].Number));
        Assert.Null(world.Store.Instruction(id).DispatchNoteOf(second));

        // The Sales order view lists every state, cancelled ones included.
        var view = await world.Service().GetSalesOrderDispatchNotesAsync(Order);
        Assert.Equal(["Cancelled", "Approved", "Cancelled"], view.Items.Select(item => item.Status).ToArray());
    }

    [Fact]
    public async Task The_e_dispatch_number_is_recorded_once_on_an_approved_note_in_the_GIB_shape_and_unique()
    {
        var (world, id, _, first, second) = await ShippedWorld();
        var a = DispatchNote("6", first);
        var b = DispatchNote("7", second);
        await world.Service().CreateDispatchNoteAsync(id, a, Metadata("k-a"));
        await world.Service().CreateDispatchNoteAsync(id, b, Metadata("k-b"));

        // On a draft: the state refusal (recorded).
        var onDraft = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().SetEDispatchNumberAsync(id, a.DispatchNoteId, new SetEDispatchNumberRequest("8", "GYM2026000000001"), Metadata("k-e-draft")));
        Assert.Equal((409, LogisticsProblemCodesV1.DispatchNoteState), (onDraft.Status, onDraft.Code));

        await world.Service().ApproveDispatchNoteAsync(id, a.DispatchNoteId, new ApproveDispatchNoteRequest("8"), Metadata("k-approve-a"));
        await world.Service().ApproveDispatchNoteAsync(id, b.DispatchNoteId, new ApproveDispatchNoteRequest("9"), Metadata("k-approve-b"));

        foreach (var text in new[] { "gym2026000000001", "GYM202600000001", "GYM20260000000011", "GY-2026000000001", "GYM2026A00000001" })
        {
            var invalid = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service().SetEDispatchNumberAsync(id, a.DispatchNoteId, new SetEDispatchNumberRequest("10", text), Metadata($"k-e-{text}")));
            Assert.Equal((400, "eDispatchNumber"), (invalid.Status, invalid.Errors[0].Field));
        }

        var recorded = await world.Service().SetEDispatchNumberAsync(id, a.DispatchNoteId, new SetEDispatchNumberRequest("10", "GYM2026000000001"), Metadata("k-e"));
        var note = recorded.Detail.DispatchNotes.Single(item => item.DispatchNoteId == a.DispatchNoteId);
        // Kept apart from the system number.
        Assert.Equal(("11", "GYM2026000000001", "IRS-000001"), (recorded.Detail.Instruction.Version, note.EDispatchNumber, note.Number));
        Assert.Equal(DispatchNoteApprovePermission, world.Authority.LastPermission);

        // Set once; and unique: the same number on another note is refused.
        var twice = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().SetEDispatchNumberAsync(id, a.DispatchNoteId, new SetEDispatchNumberRequest("11", "GYM2026000000002"), Metadata("k-e-2")));
        Assert.Equal((409, LogisticsProblemCodesV1.DispatchNoteState), (twice.Status, twice.Code));
        var taken = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().SetEDispatchNumberAsync(id, b.DispatchNoteId, new SetEDispatchNumberRequest("11", "GYM2026000000001"), Metadata("k-e-3")));
        Assert.Equal((409, LogisticsProblemCodesV1.EDispatchNumberInUse, "eDispatchNumber"), (taken.Status, taken.Code, taken.Errors[0].Field));
        var takenReplayed = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().SetEDispatchNumberAsync(id, b.DispatchNoteId, new SetEDispatchNumberRequest("11", "GYM2026000000001"), Metadata("k-e-3")));
        Assert.True(takenReplayed.IsReplay);

        // The Sales order view carries it.
        var view = await world.Service().GetSalesOrderDispatchNotesAsync(Order);
        Assert.Contains(view.Items, item => item.DispatchNoteId == a.DispatchNoteId && item.EDispatchNumber == "GYM2026000000001");
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
        Assert.Equal((id, "Draft", (string?)null), (note.InstructionId, note.Status, note.Number));
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
    public void The_wire_requests_are_strict_and_name_no_quantity_price_or_customer()
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

        Assert.Equal("7", LogisticsJson.ReadApproveDispatchNote("""{"expectedVersion":"7"}""").ExpectedVersion);
        Assert.Equal("Yanlış araç", LogisticsJson.ReadCancelDispatchNote("""{"expectedVersion":"7","reason":"Yanlış araç"}""").Reason);
        Assert.Equal("ABC2026000000001",
            LogisticsJson.ReadSetEDispatchNumber("""{"expectedVersion":"7","eDispatchNumber":"ABC2026000000001"}""").EDispatchNumber);
        foreach (var (reader, body) in new (Func<string, object>, string)[]
                 {
                     (LogisticsJson.ReadApproveDispatchNote, """{"expectedVersion":"7","number":"IRS-000001"}"""),
                     (LogisticsJson.ReadApproveDispatchNote, """{}"""),
                     (LogisticsJson.ReadCancelDispatchNote, """{"expectedVersion":"7"}"""),
                     (LogisticsJson.ReadCancelDispatchNote, """{"expectedVersion":"7","reason":" x"}"""),
                     (LogisticsJson.ReadCancelDispatchNote, $$"""{"expectedVersion":"7","reason":"{{new string('x', 501)}}"}"""),
                     (LogisticsJson.ReadSetEDispatchNumber, """{"expectedVersion":"7","eDispatchNumber":"abc2026000000001"}"""),
                     (LogisticsJson.ReadSetEDispatchNumber, """{"expectedVersion":"7","eDispatchNumber":"ABC20260000000011"}"""),
                     (LogisticsJson.ReadSetEDispatchNumber, """{"expectedVersion":"7"}"""),
                 })
        {
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => reader(body));
        }

        Assert.Equal(["DispatchNoteId", "ExpectedVersion", "ShipmentIds"],
            typeof(CreateDispatchNoteRequest).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["ExpectedVersion"], typeof(ApproveDispatchNoteRequest).GetProperties().Select(property => property.Name).ToArray());
        Assert.DoesNotContain(typeof(DispatchNoteLineView).GetProperties(), property =>
            property.Name.Contains("Price", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Amount", StringComparison.OrdinalIgnoreCase));
        foreach (var command in new[] { LogisticsCommands.CreateDispatchNoteV1, LogisticsCommands.ApproveDispatchNoteV1, LogisticsCommands.CancelDispatchNoteV1,
                     LogisticsCommands.SetEDispatchNumberV1 })
        {
            Assert.True(LogisticsCommands.IsMutation(command));
        }

        Assert.Equal([DispatchNoteStatus.Draft, DispatchNoteStatus.Approved, DispatchNoteStatus.Cancelled], Enum.GetValues<DispatchNoteStatus>());
    }
}

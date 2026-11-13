using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// F-020 through the service: cancelling a loading instruction is its own registered name, version-fenced, idempotent, refuses a
/// shipped instruction as a recorded terminal 409 and an already cancelled one as a state 409, and writes one transition.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class LogisticsInstructionServiceTests
{
    private const string InstructionCancelPermission = LogisticsPermissionManifestV1.InstructionsCancel;

    [Fact]
    public async Task Cancelling_an_instruction_is_fenced_by_its_own_name_validated_and_a_missing_grant_writes_nothing()
    {
        var world = new World();
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "10"), Metadata("k-create"));
        var id = created.Detail.Instruction.Id;
        world.Authority.DeniedPermission = InstructionCancelPermission;

        var denied = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("2", "Müşteri vazgeçti"), Metadata("k-denied")));
        Assert.Equal((403, "forbidden"), (denied.Status, denied.Code));
        Assert.DoesNotContain(world.Store.Transitions, transition => transition.Command == LogisticsCommands.CancelInstructionV1);

        world.Authority.DeniedPermission = null;
        foreach (var reason in new[] { "", "   ", " padded ", new string('x', 501) })
        {
            var invalid = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("2", reason), Metadata($"k-bad-{reason.Length}")));
            Assert.Equal((400, "validation_failed", "reason"), (invalid.Status, invalid.Code, invalid.Errors[0].Field));
        }

        Assert.Equal(LoadingInstructionStatus.Draft, world.Store.Instruction(id).Status);
        var cancelled = await world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("2", "Müşteri vazgeçti"), Metadata("k-cancel"));
        Assert.Equal(InstructionCancelPermission, world.Authority.LastPermission);
        Assert.Equal("3", cancelled.Detail.Instruction.Version);
        Assert.Equal(LoadingInstructionStatus.Cancelled, world.Store.Instruction(id).Status);
        Assert.Single(world.Store.Transitions, transition => transition.Command == LogisticsCommands.CancelInstructionV1);
    }

    [Fact]
    public async Task Cancelling_replays_on_the_same_key_refuses_a_second_cancel_a_stale_version_and_another_companys_instruction()
    {
        var world = new World();
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "10"), Metadata("k-create"));
        var id = created.Detail.Instruction.Id;
        var request = new CancelInstructionRequest("2", "Müşteri vazgeçti");
        await world.Service().CancelInstructionAsync(id, request, Metadata("k-cancel"));

        var replay = await world.Service().CancelInstructionAsync(id, request, Metadata("k-cancel"));
        Assert.True(replay.IsReplay);
        Assert.Equal(1, world.Store.Transitions.Count(transition => transition.Command == LogisticsCommands.CancelInstructionV1));

        // The same key with another payload is the idempotency mismatch, never a second cancel.
        var mismatch = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("2", "Başka gerekçe"), Metadata("k-cancel")));
        Assert.Equal(409, mismatch.Status);

        // A new key on a cancelled instruction: a state refusal; the cancel marks stay.
        var again = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("3", "Tekrar"), Metadata("k-cancel-2")));
        Assert.Equal((409, LogisticsProblemCodesV1.InstructionState), (again.Status, again.Code));
        Assert.Equal(1, world.Store.Transitions.Count(transition => transition.Command == LogisticsCommands.CancelInstructionV1));

        var unknown = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CancelInstructionAsync(Guid.NewGuid(), new CancelInstructionRequest("1", "Yok"), Metadata("k-unknown")));
        Assert.Equal((404, "not_found"), (unknown.Status, unknown.Code));
    }

    [Fact]
    public async Task A_shipped_instruction_cannot_be_cancelled_and_an_issued_one_with_a_load_can()
    {
        var (shippedWorld, shippedId, _, _, _) = await ShippedWorld();
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            shippedWorld.Service().CancelInstructionAsync(shippedId, new CancelInstructionRequest("6", "Geç kaldı"), Metadata("k-shipped")));
        Assert.Equal((409, LogisticsProblemCodesV1.InstructionShipped), (refusal.Status, refusal.Code));
        Assert.Equal(LoadingInstructionStatus.Issued, shippedWorld.Store.Instruction(shippedId).Status);

        var world = new World();
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "10"), Metadata("k-create"));
        var id = created.Detail.Instruction.Id;
        var line = created.Detail.Lines[0].LineId;
        await world.Service().IssueInstructionAsync(id, new IssueLoadingInstructionRequest("2"), Metadata("k-issue"));
        await world.Service().RecordLoadAsync(id, line, new RecordLoadRequest("3", Guid.NewGuid(), "8", "Measured"), Metadata("k-load"));
        await world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("4", "Araç gelmedi"), Metadata("k-cancel"));
        Assert.Equal(LoadingInstructionStatus.Cancelled, world.Store.Instruction(id).Status);
    }

    [Fact]
    public async Task A_stale_version_cancel_is_a_recorded_terminal_409_replayed_on_the_same_key_and_the_instruction_stays_active()
    {
        var world = new World();
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "10"), Metadata("k-create"));
        var id = created.Detail.Instruction.Id;
        var request = new CancelInstructionRequest("1", "Müşteri vazgeçti");

        var stale = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CancelInstructionAsync(id, request, Metadata("k-stale")));
        Assert.Equal((409, LogisticsProblemCodesV1.VersionConflict, "expectedVersion", false), (stale.Status, stale.Code, stale.Errors[0].Field, stale.IsReplay));
        Assert.Single(world.Store.Refusals);

        var replay = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CancelInstructionAsync(id, request, Metadata("k-stale", correlation: "retry")));
        Assert.Equal((409, LogisticsProblemCodesV1.VersionConflict, true, "synthetic-correlation"),
            (replay.Status, replay.Code, replay.IsReplay, replay.OriginalCorrelationId));
        Assert.Single(world.Store.Refusals);
        Assert.Equal(LoadingInstructionStatus.Draft, world.Store.Instruction(id).Status);
        Assert.DoesNotContain(world.Store.Transitions, transition => transition.Command == LogisticsCommands.CancelInstructionV1);
    }

    [Fact]
    public async Task A_cancelled_instruction_refuses_a_load_a_weighing_a_shipment_and_a_dispatch_note_draft_as_a_state_409()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        world.Bindings = world.Bindings with { WeighingPermission = WeighingPermission };
        await world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("4", "Araç gelmedi"), Metadata("k-cancel"));
        var transitions = world.Store.Transitions.Count;

        var refusals = new[]
        {
            await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service().RecordLoadAsync(id, line, new RecordLoadRequest("5", Guid.NewGuid(), "1", "Measured"), Metadata("k-load-after"))),
            await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service().RecordWeighingAsync(id, Weighing("5"), Metadata("k-weigh-after"))),
            await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service(runner).RecordShipmentAsync(id, line, Shipment() with { ExpectedVersion = "5" }, Metadata("k-ship-after"))),
            await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service().CreateDispatchNoteAsync(id, DispatchNote("5", Guid.NewGuid()), Metadata("k-note-after"))),
            await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service().IssueInstructionAsync(id, new IssueLoadingInstructionRequest("5"), Metadata("k-issue-after"))),
        };

        Assert.All(refusals, refusal => Assert.Equal((409, LogisticsProblemCodesV1.InstructionState), (refusal.Status, refusal.Code)));
        Assert.Empty(runner.Stock.Requests);
        Assert.Equal(transitions, world.Store.Transitions.Count);
        var stored = world.Store.Instruction(id);
        Assert.Equal((LoadingInstructionStatus.Cancelled, 5L, 5m, 0m, null),
            (stored.Status, stored.Version, stored.Lines[0].LoadedQuantity.Value, stored.Lines[0].ShippedQuantity.Value, stored.Weighing));
        Assert.Empty(stored.DispatchNotes);
    }

    [Fact]
    public async Task Loads_on_a_cancelled_instruction_do_not_count_against_another_instruction_of_the_same_order_line()
    {
        // F-020 m.5 (passive release): with the owner's revised cap of 2, A holds 1.5 loaded and B 0.5. One more gram on B is over
        // the cap while A is active; after A is cancelled its 1.5 no longer counts and B can load up to the cap.
        var world = new World { OrderLines = { Fault = "limit" } };
        var first = await world.Service().CreateInstructionAsync(Request("2.5"), Metadata("k-create-a"));
        var second = await world.Service().CreateInstructionAsync(Request("2.5"), Metadata("k-create-b"));
        var (a, b) = (first.Detail.Instruction.Id, second.Detail.Instruction.Id);
        await world.Service().IssueInstructionAsync(a, new IssueLoadingInstructionRequest("2"), Metadata("k-issue-a"));
        await world.Service().IssueInstructionAsync(b, new IssueLoadingInstructionRequest("2"), Metadata("k-issue-b"));
        await world.Service().RecordLoadAsync(a, first.Detail.Lines[0].LineId, new RecordLoadRequest("3", Guid.NewGuid(), "1.5", "Measured"), Metadata("k-load-a"));
        await world.Service().RecordLoadAsync(b, second.Detail.Lines[0].LineId, new RecordLoadRequest("3", Guid.NewGuid(), "0.5", "Measured"), Metadata("k-load-b"));

        world.OrderLines.Fault = "load-limit-revised";
        var over = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().RecordLoadAsync(b, second.Detail.Lines[0].LineId,
            new RecordLoadRequest("4", Guid.NewGuid(), "0.001", "Measured"), Metadata("k-load-b-over")));
        Assert.Equal((409, LogisticsProblemCodesV1.OrderLineAggregateLimitExceeded), (over.Status, over.Code));

        await world.Service().CancelInstructionAsync(a, new CancelInstructionRequest("4", "Araç gelmedi"), Metadata("k-cancel-a"));
        var loaded = await world.Service().RecordLoadAsync(b, second.Detail.Lines[0].LineId,
            new RecordLoadRequest("4", Guid.NewGuid(), "1.5", "Measured"), Metadata("k-load-b-after"));

        Assert.Equal("2.0", loaded.Detail.Lines[0].Loaded);
        var limit = world.Store.LoadLimits[^1];
        Assert.Equal((2m, 1.5m), (limit.MaximumAggregateQuantity, limit.RequestedLoadQuantity));
    }

    [Fact]
    public async Task Cancelling_without_an_identity_is_401_and_with_an_unregistered_or_unbound_cancel_name_is_a_503_setup_problem()
    {
        var world = new World();
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "10"), Metadata("k-create"));
        var id = created.Detail.Instruction.Id;
        var request = new CancelInstructionRequest("2", "Müşteri vazgeçti");

        world.User.CurrentActor = " ";
        var anonymous = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CancelInstructionAsync(id, request, Metadata("k-anonymous")));
        Assert.Equal((401, "unauthenticated"), (anonymous.Status, anonymous.Code));
        world.User.CurrentActor = Actor;

        // A name the central catalogue does not know, or no bound name at all: a setup problem, never a grant.
        const string unregistered = "logistics.loading-instructions.synthetic-unregistered";
        Assert.False(ErpPermissions.IsKnown(unregistered));
        var bound = world.Bindings;
        foreach (var bindings in new[] { bound with { InstructionCancelPermission = unregistered }, bound with { InstructionCancelPermission = null } })
        {
            world.Bindings = bindings;
            var setup = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
                world.Service().CancelInstructionAsync(id, request, Metadata($"k-setup-{bindings.InstructionCancelPermission}")));
            Assert.Equal((503, LogisticsProblemCodesV1.PermissionUnregistered), (setup.Status, setup.Code));
        }

        Assert.Equal(LoadingInstructionStatus.Draft, world.Store.Instruction(id).Status);
        Assert.DoesNotContain(world.Store.Transitions, transition => transition.Command == LogisticsCommands.CancelInstructionV1);
        Assert.True(ErpPermissions.IsKnown(InstructionCancelPermission));
        Assert.Equal(InstructionCancelPermission, new LogisticsInstructionServiceBindings().InstructionCancelPermission);
    }

    [Fact]
    public async Task Cancelling_a_weighed_instruction_keeps_its_weighing_record_unchanged_and_the_detail_still_shows_it()
    {
        // LOJ-D7: the weighbridge record is never deleted or rewritten; it stays on the cancelled instruction.
        var (world, id) = await LoadedWorld();
        var weighed = await world.Service().RecordWeighingAsync(id, Weighing("4", first: "15000", second: "39120.5"), Metadata("k-weigh"));
        var before = weighed.Detail.Weighing!;
        var stored = world.Store.Instruction(id).Weighing!;

        var cancelled = await world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("5", "Araç geri döndü"), Metadata("k-cancel"));

        Assert.Equal(("Cancelled", "6", before), (cancelled.Detail.Instruction.Status, cancelled.Detail.Instruction.Version, cancelled.Detail.Weighing));
        Assert.Equal(before, (await world.Service().GetInstructionDetailAsync(id)).Weighing);
        var after = world.Store.Instruction(id);
        Assert.Equal((LoadingInstructionStatus.Cancelled, stored.WeighingId, stored.Net, stored.LoadedAtWeighing, stored.DifferenceKg, stored.RecordedBy),
            (after.Status, after.Weighing!.WeighingId, after.Weighing.Net, after.Weighing.LoadedAtWeighing, after.Weighing.DifferenceKg, after.Weighing.RecordedBy));
        // The loaded share and total stay as recorded (nothing deleted).
        Assert.Equal(("24000", 1), (cancelled.Detail.Lines[0].Loaded, cancelled.Detail.Shares.Count));
    }
}

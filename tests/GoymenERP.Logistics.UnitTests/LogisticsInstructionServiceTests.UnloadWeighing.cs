using System.Text.Json;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// LOJ-D17 through the service: the unload weighing of a cancelled instruction is fenced by the weighing name, replayed on the
/// same key (no second record), refused as a recorded terminal 409 for a second record, refused as a state 409 on an instruction
/// that is not cancelled or had nothing loaded, and a difference is a warning in the audit reason and the view — never a refusal.
/// The list and the detail mark a cancelled instruction with loaded goods and no unload weighing as "Missing" (indirme kaydı yok).
/// LOJ-D16: neither the cancel nor the unload weighing reaches the Sales order line port. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class LogisticsInstructionServiceTests
{
    private static RecordUnloadWeighingRequest UnloadRequest(string version, string direction = "TareFirst", string first = "15000",
        string second = "39050", Guid? weighingId = null) => new(version, weighingId ?? Guid.NewGuid(), direction, first, second);

    /// <summary>An issued instruction with 24 000 kg loaded, cancelled: version 5.</summary>
    private static async Task<(World World, Guid Id)> CancelledLoadedWorld()
    {
        var (world, id) = await LoadedWorld();
        await world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("4", "Araç geri döndü"), Metadata("k-cancel"));
        return (world, id);
    }

    [Fact]
    public async Task A_cancelled_instruction_with_loaded_goods_is_marked_missing_until_its_unload_weighing_is_recorded()
    {
        var (world, id) = await CancelledLoadedWorld();
        Assert.Equal("Missing", (await world.Service().GetInstructionDetailAsync(id)).Instruction.UnloadRecord);
        Assert.Equal("Missing", Assert.Single((await world.Service().GetInstructionListAsync(new LoadingInstructionListQuery(Status: "Cancelled"))).Items)
            .UnloadRecord);
        Assert.Null((await world.Service().GetInstructionDetailAsync(id)).UnloadWeighing);

        var request = UnloadRequest("5", first: "15000", second: "39050.5");
        var recorded = await world.Service().RecordUnloadWeighingAsync(id, request, Metadata("k-unload"));

        Assert.False(recorded.IsReplay);
        Assert.Equal(("Cancelled", "6", "Recorded"), (recorded.Detail.Instruction.Status, recorded.Detail.Instruction.Version, recorded.Detail.Instruction.UnloadRecord));
        var unload = recorded.Detail.UnloadWeighing!;
        Assert.Equal((request.WeighingId, "TareFirst", "15000", "39050.5", "15000", "39050.5", "24050.5", "24000", "50.5", true, Actor),
            (unload.WeighingId, unload.Direction, unload.FirstWeighingKg, unload.SecondWeighingKg, unload.TareKg, unload.GrossKg, unload.NetKg,
                unload.LoadedKgAtCancel, unload.DifferenceKg, unload.DifferenceWarning, unload.RecordedBy));
        Assert.Equal(WeighingPermission, world.Authority.LastPermission);
        // Not a share and not a loading weighing: the loaded total and the share list stay as recorded.
        Assert.Equal(("24000", 1, (LoadingInstructionWeighingView?)null), (recorded.Detail.Lines[0].Loaded, recorded.Detail.Shares.Count, recorded.Detail.Weighing));

        var reopened = await world.Service().GetInstructionDetailAsync(id);
        Assert.Equal((unload, "Recorded"), (reopened.UnloadWeighing, reopened.Instruction.UnloadRecord));

        var replay = await world.Service().RecordUnloadWeighingAsync(id, request, Metadata("k-unload", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.Equal(("6", "synthetic-correlation", unload), (replay.Detail.Instruction.Version, replay.OriginalCorrelationId, replay.Detail.UnloadWeighing));
        Assert.Equal(1, world.Store.Transitions.Count(transition => transition.Command == LogisticsCommands.RecordUnloadWeighingV1));
    }

    [Fact]
    public async Task The_unload_audit_names_the_difference_warning_and_an_equal_unload_carries_no_warning_in_the_view()
    {
        var (world, id) = await CancelledLoadedWorld();
        var recorded = await world.Service().RecordUnloadWeighingAsync(id, UnloadRequest("5", "GrossFirst", "39000", "15000"), Metadata("k-unload"));

        Assert.Equal(("0", false), (recorded.Detail.UnloadWeighing!.DifferenceKg, recorded.Detail.UnloadWeighing.DifferenceWarning));
        var audit = Assert.Single(world.Store.Audits, entry => entry.Command == LogisticsCommands.RecordUnloadWeighingV1).Audit;
        Assert.Equal(("logistics.loading-instruction.unload-weighing-recorded.v1", "RecordUnloadWeighing", "İndirme tartısı kaydedildi",
                "İndirme tartısında yüklenen kg ile fark var"),
            (audit.EventType, audit.Operation, audit.Reason, audit.WarningReason));
        // The view exposes the backend's warning flag and no threshold, tolerance or approval verdict (LOJ-D17: the threshold is a Settings decision).
        Assert.DoesNotContain(typeof(LoadingInstructionUnloadWeighingView).GetProperties(), property =>
            property.Name.Contains("Tolerance", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Threshold", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Accepted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_lighter_unload_keeps_a_negative_difference_warns_and_never_refuses()
    {
        var (world, id) = await CancelledLoadedWorld();
        var recorded = await world.Service().RecordUnloadWeighingAsync(id, UnloadRequest("5", second: "38880"), Metadata("k-unload"));
        Assert.Equal(("23880", "-120", true, "Cancelled"),
            (recorded.Detail.UnloadWeighing!.NetKg, recorded.Detail.UnloadWeighing.DifferenceKg, recorded.Detail.UnloadWeighing.DifferenceWarning,
                recorded.Detail.Instruction.Status));
    }

    [Fact]
    public async Task A_second_unload_weighing_is_a_recorded_terminal_refusal_and_the_first_record_stays()
    {
        var (world, id) = await CancelledLoadedWorld();
        var first = await world.Service().RecordUnloadWeighingAsync(id, UnloadRequest("5"), Metadata("k-unload-1"));

        var second = UnloadRequest("6", second: "39500");
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordUnloadWeighingAsync(id, second, Metadata("k-unload-2")));
        Assert.Equal((409, LogisticsProblemCodesV1.UnloadWeighingAlreadyRecorded, "weighingId", false),
            (refusal.Status, refusal.Code, refusal.Errors[0].Field, refusal.IsReplay));
        var again = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordUnloadWeighingAsync(id, second, Metadata("k-unload-2")));
        Assert.Equal((409, LogisticsProblemCodesV1.UnloadWeighingAlreadyRecorded, true), (again.Status, again.Code, again.IsReplay));

        var detail = await world.Service().GetInstructionDetailAsync(id);
        Assert.Equal(("6", first.Detail.UnloadWeighing), (detail.Instruction.Version, detail.UnloadWeighing));
    }

    [Fact]
    public async Task An_active_instruction_or_a_cancel_with_nothing_loaded_refuses_the_unload_weighing_as_a_state_409()
    {
        var (world, id) = await LoadedWorld();
        var active = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordUnloadWeighingAsync(id, UnloadRequest("4"), Metadata("k-active")));
        Assert.Equal((409, LogisticsProblemCodesV1.InstructionState), (active.Status, active.Code));
        Assert.Equal("NotApplicable", (await world.Service().GetInstructionDetailAsync(id)).Instruction.UnloadRecord);

        var draft = (await world.Service().CreateInstructionAsync(Request(), Metadata("k-create-2"))).Detail.Instruction.Id;
        await world.Service().CancelInstructionAsync(draft, new CancelInstructionRequest("2", "Müşteri vazgeçti"), Metadata("k-cancel-2"));
        var empty = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordUnloadWeighingAsync(draft, UnloadRequest("3"), Metadata("k-empty")));
        Assert.Equal((409, LogisticsProblemCodesV1.InstructionState), (empty.Status, empty.Code));
        Assert.Equal("NotApplicable", (await world.Service().GetInstructionDetailAsync(draft)).Instruction.UnloadRecord);
        Assert.DoesNotContain(world.Store.Transitions, transition => transition.Command == LogisticsCommands.RecordUnloadWeighingV1);
    }

    [Fact]
    public async Task A_stale_version_is_a_recorded_terminal_409_and_a_pair_against_its_direction_is_refused_before_any_save()
    {
        var (world, id) = await CancelledLoadedWorld();

        var stale = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordUnloadWeighingAsync(id, UnloadRequest("4"), Metadata("k-stale")));
        Assert.Equal((409, LogisticsProblemCodesV1.VersionConflict, "expectedVersion"), (stale.Status, stale.Code, stale.Errors[0].Field));
        var refusals = world.Store.Refusals.Count;

        var inconsistent = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordUnloadWeighingAsync(id, UnloadRequest("5", first: "39050", second: "15000"), Metadata("k-bad")));
        Assert.Equal((422, LogisticsProblemCodesV1.WeighingDirectionInconsistent), (inconsistent.Status, inconsistent.Code));
        Assert.Equal(refusals, world.Store.Refusals.Count);

        var detail = await world.Service().GetInstructionDetailAsync(id);
        Assert.Equal(("5", (LoadingInstructionUnloadWeighingView?)null, "Missing"), (detail.Instruction.Version, detail.UnloadWeighing, detail.Instruction.UnloadRecord));
    }

    [Fact]
    public async Task The_unload_weighing_is_fenced_by_the_weighing_name_and_another_companys_instruction_is_404()
    {
        var (world, id) = await CancelledLoadedWorld();

        var missing = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordUnloadWeighingAsync(Guid.NewGuid(), UnloadRequest("5"), Metadata("k-missing")));
        Assert.Equal(404, missing.Status);

        world.Authority.DeniedPermission = WeighingPermission;
        var denied = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordUnloadWeighingAsync(id, UnloadRequest("5"), Metadata("k-denied")));
        Assert.Equal((403, "forbidden"), (denied.Status, denied.Code));
        world.Authority.DeniedPermission = null;

        world.Bindings = world.Bindings with { WeighingPermission = null };
        var unbound = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordUnloadWeighingAsync(id, UnloadRequest("5"), Metadata("k-unbound")));
        Assert.Equal((503, LogisticsProblemCodesV1.PermissionUnregistered), (unbound.Status, unbound.Code));
        Assert.Null((await world.Service().GetInstructionDetailAsync(id)).UnloadWeighing);
        Assert.DoesNotContain(world.Store.Transitions, transition => transition.Command == LogisticsCommands.RecordUnloadWeighingV1);
    }

    [Fact]
    public async Task Loj_d16_neither_the_cancel_nor_the_unload_weighing_reaches_the_sales_order_line_owner()
    {
        // The allocation stays on the order (LOJ-D16): Logistics never calls the Sales order line port to release it — the cancel
        // only stops the instruction counting against the order line's loading limits.
        var (world, id) = await LoadedWorld();
        world.ResetOwnerCalls();
        await world.Service().CancelInstructionAsync(id, new CancelInstructionRequest("4", "Araç geri döndü"), Metadata("k-cancel"));
        await world.Service().RecordUnloadWeighingAsync(id, UnloadRequest("5"), Metadata("k-unload"));
        Assert.Equal(0, world.OrderLines.Calls);
        Assert.Equal(LoadingInstructionStatus.Cancelled, world.Store.Instruction(id).Status);
    }

    [Fact]
    public void The_unload_weighing_wire_shape_is_strict_and_carries_no_net_difference_or_warning()
    {
        var id = Guid.NewGuid();
        var ok = LogisticsJson.ReadRecordUnloadWeighing(
            $$"""{"expectedVersion":"5","weighingId":"{{id}}","direction":"GrossFirst","firstWeighingKg":"39050.500","secondWeighingKg":"15000"}""");
        Assert.Equal((id, "GrossFirst", "39050.500"), (ok.WeighingId, ok.Direction, ok.FirstWeighingKg));
        foreach (var body in new[]
                 {
                     $$"""{"expectedVersion":"5","weighingId":"{{id}}","direction":"Guessed","firstWeighingKg":"1","secondWeighingKg":"2"}""",
                     $$"""{"expectedVersion":"5","weighingId":"{{id}}","direction":"TareFirst","firstWeighingKg":"0","secondWeighingKg":"2"}""",
                     $$"""{"expectedVersion":"5","weighingId":"{{id}}","direction":"TareFirst","firstWeighingKg":"-1","secondWeighingKg":"2"}""",
                     $$"""{"expectedVersion":"5","weighingId":"{{id}}","direction":"TareFirst","firstWeighingKg":1,"secondWeighingKg":"2"}""",
                     $$"""{"expectedVersion":"5","weighingId":"{{id}}","direction":"TareFirst","firstWeighingKg":"1","secondWeighingKg":"2","differenceKg":"1"}""",
                     $$"""{"expectedVersion":"5","weighingId":"{{id}}","direction":"TareFirst","firstWeighingKg":"1","secondWeighingKg":"2","differenceWarning":true}""",
                     $$"""{"expectedVersion":"0","weighingId":"{{id}}","direction":"TareFirst","firstWeighingKg":"1","secondWeighingKg":"2"}""",
                     """{"expectedVersion":"5","weighingId":"00000000-0000-0000-0000-000000000000","direction":"TareFirst","firstWeighingKg":"1","secondWeighingKg":"2"}""",
                 })
        {
            Assert.ThrowsAny<JsonException>(() => LogisticsJson.ReadRecordUnloadWeighing(body));
        }

        Assert.Equal(["ExpectedVersion", "WeighingId", "Direction", "FirstWeighingKg", "SecondWeighingKg"],
            typeof(RecordUnloadWeighingRequest).GetProperties().Select(property => property.Name).ToArray());
        Assert.True(LogisticsCommands.IsMutation(LogisticsCommands.RecordUnloadWeighingV1));
        Assert.Equal("/api/v1/logistics/loading-instructions/{id}/unload-weighings", LogisticsApiV1.UnloadWeighingsPath);
    }
}

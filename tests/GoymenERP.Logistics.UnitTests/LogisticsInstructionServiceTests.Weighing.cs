using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Application;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// AT-053 through the service: the manual weighbridge record is fenced by its own bound name, replayed on the same key
/// (no second record), refused as a recorded terminal 409 for a second record or a stale version, refused before any save
/// when the pair contradicts its direction, and shown on the instruction's detail with the loaded-vs-weighed difference as a
/// fact. SENTETIK / NOT_ENV; a registered platform name stands in for <c>logistics.weighings.record</c> until the central
/// catalogue knows it.
/// </summary>
public sealed partial class LogisticsInstructionServiceTests
{
    private const string WeighingPermission = ErpPermissions.CounterpartyPartiesUpdateBasic;

    private static RecordWeighingRequest Weighing(string version, string direction = "TareFirst", string first = "15000", string second = "39120",
        Guid? weighingId = null) => new(version, weighingId ?? Guid.NewGuid(), direction, first, second);

    /// <summary>An issued instruction (25 000 kg) with 24 000 kg loaded: version 4.</summary>
    private static async Task<(World World, Guid Id)> LoadedWorld()
    {
        var world = new World();
        world.Bindings = world.Bindings with { WeighingPermission = WeighingPermission };
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "25000"), Metadata("k-create"));
        var id = created.Detail.Instruction.Id;
        await world.Service().IssueInstructionAsync(id, new IssueLoadingInstructionRequest("2"), Metadata("k-issue"));
        await world.Service().RecordLoadAsync(id, created.Detail.Lines[0].LineId, new RecordLoadRequest("3", Guid.NewGuid(), "24000", "Measured"),
            Metadata("k-load"));
        return (world, id);
    }

    [Fact]
    public async Task A_weighing_is_recorded_once_with_the_backends_net_and_difference_and_the_detail_shows_it_after_reopening()
    {
        var (world, id) = await LoadedWorld();
        var request = Weighing("4", first: "15000", second: "39120.5");

        var recorded = await world.Service().RecordWeighingAsync(id, request, Metadata("k-weigh"));

        Assert.False(recorded.IsReplay);
        Assert.Equal("5", recorded.Detail.Instruction.Version);
        var weighing = recorded.Detail.Weighing!;
        Assert.Equal((request.WeighingId, "TareFirst", "15000", "39120.5", "15000", "39120.5", "24120.5", "24000", "120.5", Actor),
            (weighing.WeighingId, weighing.Direction, weighing.FirstWeighingKg, weighing.SecondWeighingKg, weighing.TareKg, weighing.GrossKg,
                weighing.NetKg, weighing.LoadedKgAtWeighing, weighing.DifferenceKg, weighing.RecordedBy));
        // A fact, not a share: the line's shares and the share list are untouched.
        Assert.Equal(("24000", "1000"), (recorded.Detail.Lines[0].Loaded, recorded.Detail.Lines[0].RemainingToLoad));
        Assert.Single(recorded.Detail.Shares);
        Assert.Equal(WeighingPermission, world.Authority.LastPermission);

        // Close and reopen: the detail read answers the same record (the next step — shipment / delivery — reads this detail).
        var reopened = await world.Service().GetInstructionDetailAsync(id);
        Assert.Equal(weighing, reopened.Weighing);

        // The same key again: the stored answer, no second record and no second version step.
        var replay = await world.Service().RecordWeighingAsync(id, request, Metadata("k-weigh", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.Equal(("5", "synthetic-correlation", weighing), (replay.Detail.Instruction.Version, replay.OriginalCorrelationId, replay.Detail.Weighing));
        Assert.Equal(1, world.Store.Transitions.Count(transition => transition.Command == LogisticsCommands.RecordWeighingV1));
    }

    [Fact]
    public async Task A_gross_first_weighing_lighter_than_loaded_keeps_a_negative_difference_and_no_verdict()
    {
        var (world, id) = await LoadedWorld();

        var recorded = await world.Service().RecordWeighingAsync(id, Weighing("4", "GrossFirst", "38880", "15000"), Metadata("k-weigh"));

        var weighing = recorded.Detail.Weighing!;
        Assert.Equal(("GrossFirst", "15000", "38880", "23880", "-120"), (weighing.Direction, weighing.TareKg, weighing.GrossKg, weighing.NetKg, weighing.DifferenceKg));
        Assert.DoesNotContain(typeof(LoadingInstructionWeighingView).GetProperties(), property =>
            property.Name.Contains("Tolerance", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Status", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Accepted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_second_weighing_is_a_recorded_terminal_refusal_and_the_first_record_stays()
    {
        var (world, id) = await LoadedWorld();
        var first = await world.Service().RecordWeighingAsync(id, Weighing("4"), Metadata("k-weigh-1"));

        var second = Weighing("5", first: "15000", second: "39000");
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().RecordWeighingAsync(id, second, Metadata("k-weigh-2")));
        Assert.Equal((409, LogisticsProblemCodesV1.WeighingAlreadyRecorded, "weighingId", false),
            (refusal.Status, refusal.Code, refusal.Errors[0].Field, refusal.IsReplay));
        var again = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().RecordWeighingAsync(id, second, Metadata("k-weigh-2")));
        Assert.Equal((409, LogisticsProblemCodesV1.WeighingAlreadyRecorded, true), (again.Status, again.Code, again.IsReplay));

        var detail = await world.Service().GetInstructionDetailAsync(id);
        Assert.Equal(("5", first.Detail.Weighing), (detail.Instruction.Version, detail.Weighing));
    }

    [Fact]
    public async Task A_stale_version_is_a_recorded_terminal_409_and_a_draft_is_a_state_refusal()
    {
        var (world, id) = await LoadedWorld();

        var stale = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().RecordWeighingAsync(id, Weighing("3"), Metadata("k-stale")));
        Assert.Equal((409, LogisticsProblemCodesV1.VersionConflict, "expectedVersion"), (stale.Status, stale.Code, stale.Errors[0].Field));
        Assert.Single(world.Store.Refusals);
        Assert.Null((await world.Service().GetInstructionDetailAsync(id)).Weighing);

        var draft = (await world.Service().CreateInstructionAsync(Request(), Metadata("k-create-2"))).Detail.Instruction.Id;
        var state = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().RecordWeighingAsync(draft, Weighing("2"), Metadata("k-draft")));
        Assert.Equal((409, LogisticsProblemCodesV1.InstructionState), (state.Status, state.Code));
    }

    [Theory]
    [InlineData("TareFirst", "39120", "15000", 422, LogisticsProblemCodesV1.WeighingDirectionInconsistent, "secondWeighingKg", "logistics_weighing_direction_inconsistent")]
    [InlineData("GrossFirst", "15000", "39120", 422, LogisticsProblemCodesV1.WeighingDirectionInconsistent, "secondWeighingKg", "logistics_weighing_direction_inconsistent")]
    [InlineData("TareFirst", "15000", "15000", 422, LogisticsProblemCodesV1.WeighingDirectionInconsistent, "secondWeighingKg", "logistics_weighing_direction_inconsistent")]
    [InlineData("TareFirst", "15000.0001", "39120", 422, "validation_failed", "firstWeighingKg", "logistics_quantity_scale_exceeded")]
    [InlineData("TareFirst", "15000", "1234567890123456", 422, "validation_failed", "secondWeighingKg", "logistics_value_does_not_fit")]
    [InlineData("TareFirst", "15000", "2.9999999999999999999999999999999", 422, "validation_failed", "secondWeighingKg", "logistics_quantity_scale_exceeded")]
    public async Task A_pair_against_its_direction_or_beyond_the_kilogram_scale_is_refused_before_any_save(string direction, string first, string second,
        int status, string code, string field, string fieldCode)
    {
        var (world, id) = await LoadedWorld();

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordWeighingAsync(id, Weighing("4", direction, first, second), Metadata("k-bad")));

        Assert.Equal((status, code, field, fieldCode), (refusal.Status, refusal.Code, refusal.Errors[0].Field, refusal.Errors[0].Code));
        Assert.Empty(world.Store.Refusals);
        var detail = await world.Service().GetInstructionDetailAsync(id);
        Assert.Equal(("4", (LoadingInstructionWeighingView?)null), (detail.Instruction.Version, detail.Weighing));
    }

    [Fact]
    public async Task The_weighing_name_is_measured_first_and_another_companys_instruction_is_404()
    {
        var (world, id) = await LoadedWorld();

        var missing = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordWeighingAsync(Guid.NewGuid(), Weighing("4"), Metadata("k-missing")));
        Assert.Equal(404, missing.Status);

        world.Authority.DeniedPermission = WeighingPermission;
        var denied = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().RecordWeighingAsync(id, Weighing("4"), Metadata("k-denied")));
        Assert.Equal(403, denied.Status);
        world.Authority.DeniedPermission = null;

        world.Bindings = world.Bindings with { WeighingPermission = null };
        var unbound = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().RecordWeighingAsync(id, Weighing("4"), Metadata("k-unbound")));
        Assert.Equal((503, LogisticsProblemCodesV1.PermissionUnregistered), (unbound.Status, unbound.Code));
        Assert.Null((await world.Service().GetInstructionDetailAsync(id)).Weighing);
        Assert.Equal("logistics.weighings.record", LogisticsPermissionManifestV1.WeighingsRecord);
        Assert.Equal(LogisticsPermissionManifestV1.WeighingsRecord, new LogisticsInstructionServiceBindings().WeighingPermission);
    }

    [Fact]
    public void The_weighing_wire_shape_is_strict_and_carries_no_net_difference_or_tolerance()
    {
        var id = Guid.NewGuid();
        var ok = LogisticsJson.ReadRecordWeighing($$"""{"expectedVersion":"4","weighingId":"{{id}}","direction":"GrossFirst","firstWeighingKg":"39120.500","secondWeighingKg":"15000"}""");
        Assert.Equal((id, "GrossFirst", "39120.500"), (ok.WeighingId, ok.Direction, ok.FirstWeighingKg));
        foreach (var body in new[]
                 {
                     $$"""{"expectedVersion":"4","weighingId":"{{id}}","direction":"Guessed","firstWeighingKg":"1","secondWeighingKg":"2"}""",
                     $$"""{"expectedVersion":"4","weighingId":"{{id}}","direction":"TareFirst","firstWeighingKg":"0","secondWeighingKg":"2"}""",
                     $$"""{"expectedVersion":"4","weighingId":"{{id}}","direction":"TareFirst","firstWeighingKg":"-1","secondWeighingKg":"2"}""",
                     $$"""{"expectedVersion":"4","weighingId":"{{id}}","direction":"TareFirst","firstWeighingKg":1,"secondWeighingKg":"2"}""",
                     $$"""{"expectedVersion":"4","weighingId":"{{id}}","direction":"TareFirst","firstWeighingKg":"1","secondWeighingKg":"2","netKg":"1"}""",
                     $$"""{"expectedVersion":"0","weighingId":"{{id}}","direction":"TareFirst","firstWeighingKg":"1","secondWeighingKg":"2"}""",
                     """{"expectedVersion":"4","weighingId":"00000000-0000-0000-0000-000000000000","direction":"TareFirst","firstWeighingKg":"1","secondWeighingKg":"2"}""",
                 })
        {
            Assert.ThrowsAny<JsonException>(() => LogisticsJson.ReadRecordWeighing(body));
        }

        Assert.Equal(["ExpectedVersion", "WeighingId", "Direction", "FirstWeighingKg", "SecondWeighingKg"],
            typeof(RecordWeighingRequest).GetProperties().Select(property => property.Name).ToArray());
        Assert.True(LogisticsCommands.IsMutation(LogisticsCommands.RecordWeighingV1));
    }
}

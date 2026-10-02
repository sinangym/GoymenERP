using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// LOG-4a service without a database or a host: the fences, the replay, the owners in order (Cari account →
/// APPROVED Sales line → Settings kg profile), the domain's decision, the terminal refusals (§7) and the
/// mapping. SENTETIK / NOT_ENV. Registered platform names stand in for the Logistics names until the central
/// catalogue knows them (the Sales tests' way).
/// </summary>
public sealed partial class LogisticsInstructionServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Party = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Order = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid OrderLine = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgUnit = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid KgProfile = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly DateOnly Day = new(2026, 9, 19);
    private const string Actor = "synthetic-actor";
    private const string CreatePermission = ErpPermissions.CounterpartyPartiesCreate;
    private const string ReadPermission = ErpPermissions.CounterpartyPartiesRead;

    [Fact]
    public async Task A_valid_instruction_is_stored_once_with_the_approved_lines_product_and_revision()
    {
        var world = new World();

        var response = await world.Service().CreateInstructionAsync(Request(), Metadata("k-1"));

        Assert.False(response.IsReplay);
        var instruction = response.Detail.Instruction;
        Assert.Equal((Company, "Draft", "2", Party, Account, Day, 1), (instruction.CompanyId, instruction.Status, instruction.Version,
            instruction.CustomerPartyId, instruction.CompanyAccountId, instruction.EffectiveOn, instruction.LineCount));
        var line = Assert.Single(response.Detail.Lines);
        Assert.Equal((Order, OrderLine, 0, Product, KgUnit, "1.25", "0", "1.25", false),
            (line.SourceOrderId, line.SourceOrderLineId, line.SourceOrderRevision, line.ProductId, line.UnitId, line.Instructed, line.Loaded,
                line.RemainingToLoad, line.IsBlocked));
        var envelope = Assert.Single(world.Store.Envelopes);
        Assert.Equal((Actor, CreatePermission, "synthetic-correlation", "k-1"), (envelope.ActorId, envelope.PermissionCode, envelope.CorrelationId, envelope.IdempotencyKey));
        Assert.True(world.Authority.Calls >= 5, $"measured {world.Authority.Calls} times");
    }

    [Fact]
    public async Task A_resolved_sales_aggregate_limit_is_rechecked_and_forwarded_without_logistics_recalculation()
    {
        var world = new World { OrderLines = { Fault = "limit" } };

        var response = await world.Service().CreateInstructionAsync(Request(quantity: "1.25"), Metadata("k-limit"));

        var line = Assert.Single(response.Detail.Lines);
        var limit = Assert.Single(world.Store.AggregateLimits);
        Assert.Equal(2, world.OrderLines.Calls);
        Assert.Equal((line.LineId, Company, Order, OrderLine, KgUnit, 7L, 5m, "5", "Sales", OrderLine.ToString("D"), "3", Day),
            (limit.InstructionLineId, limit.CompanyId, limit.OrderId, limit.OrderLineId, limit.UnitId, limit.SalesOrderVersion,
                limit.MaximumAggregateQuantity, limit.MaximumAggregateQuantityText, limit.SourceOwner, limit.SourceId,
                limit.SourceVersion, limit.EffectiveOn));
    }

    [Fact]
    public async Task A_create_aggregate_limit_refusal_is_terminal_replayed_and_has_zero_instruction_effect()
    {
        var world = new World { OrderLines = { Fault = "limit" } };
        world.Store.CreateFault = new LogisticsStoreException(LogisticsStoreCodes.AggregateLimitExceeded, "synthetic aggregate cap");
        var request = Request(quantity: "1.25");

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateInstructionAsync(request, Metadata("k-create-cap")));
        Assert.Equal((409, LogisticsProblemCodesV1.OrderLineAggregateLimitExceeded, false, "lines", LogisticsStoreCodes.AggregateLimitExceeded),
            (refusal.Status, refusal.Code, refusal.IsReplay, refusal.Errors[0].Field, refusal.Errors[0].Code));
        var ownerCalls = world.OrderLines.Calls;

        var replay = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateInstructionAsync(request, Metadata("k-create-cap", correlation: "retry-correlation")));
        Assert.Equal((409, LogisticsProblemCodesV1.OrderLineAggregateLimitExceeded, true, "synthetic-correlation"),
            (replay.Status, replay.Code, replay.IsReplay, replay.OriginalCorrelationId));
        Assert.Equal(ownerCalls, world.OrderLines.Calls);
        Assert.Empty(world.Store.Envelopes);
        Assert.Empty(world.Store.AggregateLimits);
        Assert.Single(world.Store.Refusals);
    }

    [Fact]
    public async Task A_sales_limit_revision_change_between_prepare_and_store_is_refused_before_any_write()
    {
        var world = new World { OrderLines = { Fault = "limit-version-race" } };

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateInstructionAsync(Request(), Metadata("k-limit-race")));

        Assert.Equal((409, LogisticsProblemCodesV1.OrderLineState, "lines[0].sourceOrderLineId", "source_changed"),
            (refusal.Status, refusal.Code, refusal.Errors[0].Field, refusal.Errors[0].Code));
        Assert.Equal(2, world.OrderLines.Calls);
        Assert.Empty(world.Store.Envelopes);
        Assert.Empty(world.Store.AggregateLimits);
    }

    [Fact]
    public async Task A_cross_company_sales_limit_snapshot_is_provider_invalid_and_never_stored()
    {
        var world = new World { OrderLines = { Fault = "limit-invalid" } };

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateInstructionAsync(Request(), Metadata("k-limit-invalid")));

        Assert.Equal((503, LogisticsProblemCodesV1.ReferenceProviderInvalid), (refusal.Status, refusal.Code));
        Assert.Empty(world.Store.Envelopes);
        Assert.Empty(world.Store.AggregateLimits);
    }

    [Fact]
    public async Task The_same_key_content_and_actor_replays_before_any_owner_is_asked()
    {
        var world = new World();
        var request = Request();
        var first = await world.Service().CreateInstructionAsync(request, Metadata("k-1"));
        world.ResetOwnerCalls();

        var replay = await world.Service().CreateInstructionAsync(request, Metadata("k-1", correlation: "second"));

        Assert.True(replay.IsReplay);
        Assert.Equal((first.Detail.Instruction.Id, "synthetic-correlation"), (replay.Detail.Instruction.Id, replay.OriginalCorrelationId));
        Assert.Equal((0, 0, 0), (world.Accounts.Calls, world.OrderLines.Calls, world.Settings.Calls));
    }

    [Theory]
    [InlineData("not-approved", 409, LogisticsProblemCodesV1.OrderLineState, "lines[0].sourceOrderLineId", "not_approved")]
    [InlineData("not-found", 409, LogisticsProblemCodesV1.OrderLineState, "lines[0].sourceOrderLineId", "invalid_reference")]
    [InlineData("other-customer", 409, LogisticsProblemCodesV1.OrderLineState, "lines[0].sourceOrderLineId", "customer_mismatch")]
    [InlineData("other-company", 503, LogisticsProblemCodesV1.ReferenceProviderInvalid, null, null)]
    [InlineData("throws", 503, LogisticsProblemCodesV1.SalesProviderUnavailable, null, null)]
    public async Task A_line_that_is_not_an_approved_line_of_this_customer_is_refused_fail_closed_and_nothing_is_stored(
        string fault, int status, string code, string? field, string? fieldCode)
    {
        var world = new World { OrderLines = { Fault = fault } };

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CreateInstructionAsync(Request(), Metadata("k-1")));

        Assert.Equal((status, code), (refusal.Status, refusal.Code));
        if (field is not null)
        {
            Assert.Equal((field, fieldCode), (refusal.Errors[0].Field, refusal.Errors[0].Code));
        }

        Assert.Empty(world.Store.Envelopes);
        Assert.Equal(0, world.Settings.Calls);
    }

    [Fact]
    public async Task A_new_instruction_never_reopens_a_cancelled_remainder()
    {
        var world = new World { OrderLines = { Fault = "remainder-cancelled" } };

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateInstructionAsync(Request("1"), Metadata("k-remainder")));

        Assert.Equal((409, LogisticsProblemCodesV1.OrderLineState, "lines[0].sourceOrderLineId", "remainder_cancelled"),
            (refusal.Status, refusal.Code, refusal.Errors[0].Field, refusal.Errors[0].Code));
        Assert.Empty(world.Store.Envelopes);
    }

    [Theory]
    [InlineData("passive-account", 409, LogisticsProblemCodesV1.AccountState)]
    [InlineData("not-found", 409, LogisticsProblemCodesV1.AccountState)]
    [InlineData("other-company", 409, LogisticsProblemCodesV1.AccountState)]
    [InlineData("denied", 403, "forbidden")]
    [InlineData("unavailable", 503, LogisticsProblemCodesV1.CounterpartyProviderUnavailable)]
    public async Task The_customer_is_the_Cari_owners_answer_read_before_any_line(string fault, int status, string code)
    {
        var world = new World { Accounts = { Fault = fault } };

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CreateInstructionAsync(Request(), Metadata("k-1")));

        Assert.Equal((status, code, 0, 0), (refusal.Status, refusal.Code, world.OrderLines.Calls, world.Settings.Calls));
    }

    [Theory]
    [InlineData("unit-not-found", 422, LogisticsProblemCodesV1.UnsupportedUnit)]
    [InlineData("unit-piece", 422, LogisticsProblemCodesV1.UnsupportedUnit)]
    [InlineData("unit-other-company", 422, LogisticsProblemCodesV1.UnsupportedUnit)]
    [InlineData("unit-access-denied", 403, "forbidden")]
    [InlineData("unit-unavailable", 503, "settings_provider_unavailable")]
    public async Task The_unit_is_the_Settings_kilogram_profile_or_nothing(string fault, int status, string code)
    {
        var world = new World { Settings = { Fault = fault } };

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CreateInstructionAsync(Request(), Metadata("k-1")));

        Assert.Equal((status, code), (refusal.Status, refusal.Code));
        Assert.Empty(world.Store.Envelopes);
    }

    [Fact]
    public async Task A_quantity_beyond_the_profiles_scale_is_refused_never_rounded()
    {
        var world = new World();

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateInstructionAsync(Request(quantity: "1.2345"), Metadata("k-1")));

        Assert.Equal((422, LogisticsProblemCodesV1.UnsupportedUnit, "lines[0].unitId", LoadingUnitCodes.QuantityScaleExceeded),
            (refusal.Status, refusal.Code, refusal.Errors[0].Field, refusal.Errors[0].Code));
    }

    [Fact]
    public async Task Issue_then_a_load_within_the_cap_moves_the_instruction_one_version_each_and_the_cap_is_a_recorded_terminal_refusal()
    {
        var world = new World();
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "4"), Metadata("k-create"));
        var id = created.Detail.Instruction.Id;
        var lineId = created.Detail.Lines[0].LineId;

        var issued = await world.Service().IssueInstructionAsync(id, new IssueLoadingInstructionRequest("2"), Metadata("k-issue"));
        Assert.Equal(("Issued", "3"), (issued.Detail.Instruction.Status, issued.Detail.Instruction.Version));

        var loaded = await world.Service().RecordLoadAsync(id, lineId, new RecordLoadRequest("3", Guid.NewGuid(), "2.5", "Measured"), Metadata("k-load-1"));
        Assert.Equal(("4", "2.5", "1.5"), (loaded.Detail.Instruction.Version, loaded.Detail.Lines[0].Loaded, loaded.Detail.Lines[0].RemainingToLoad));
        var share = Assert.Single(loaded.Detail.Shares);
        Assert.Equal(("Loaded", "2.5", "Measured"), (share.Kind, share.Quantity, share.Measurement));

        // §13: the loaded share is capped by the instructed remainder — terminal for this key (§7), replayed on retry.
        var over = new RecordLoadRequest("4", Guid.NewGuid(), "1.501", "AllocatedFromVehicleWeight");
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().RecordLoadAsync(id, lineId, over, Metadata("k-load-2")));
        Assert.Equal((409, LogisticsProblemCodesV1.LoadExceedsInstructed, false), (refusal.Status, refusal.Code, refusal.IsReplay));
        Assert.Single(world.Store.Refusals);
        var again = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().RecordLoadAsync(id, lineId, over, Metadata("k-load-2", correlation: "retry")));
        Assert.Equal((409, LogisticsProblemCodesV1.LoadExceedsInstructed, true, "synthetic-correlation"), (again.Status, again.Code, again.IsReplay, again.OriginalCorrelationId));

        // A stale version is terminal too; a Draft cannot take a load (state); both leave the instruction as it was.
        var stale = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordLoadAsync(id, lineId, new RecordLoadRequest("3", Guid.NewGuid(), "1", "Measured"), Metadata("k-load-3")));
        Assert.Equal((409, LogisticsProblemCodesV1.VersionConflict, "expectedVersion"), (stale.Status, stale.Code, stale.Errors[0].Field));
        var detail = await world.Service().GetInstructionDetailAsync(id);
        Assert.Equal(("4", "2.5"), (detail.Instruction.Version, detail.Lines[0].Loaded));
        Assert.Equal(2, world.Store.Transitions.Count);
    }

    [Fact]
    public async Task A_load_on_a_draft_instruction_is_a_state_refusal_and_the_permissions_are_the_bound_names()
    {
        var world = new World();
        var created = await world.Service().CreateInstructionAsync(Request(), Metadata("k-create"));
        var id = created.Detail.Instruction.Id;

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordLoadAsync(id, created.Detail.Lines[0].LineId, new RecordLoadRequest("2", Guid.NewGuid(), "1", "Measured"), Metadata("k-load")));

        Assert.Equal((409, LogisticsProblemCodesV1.InstructionState), (refusal.Status, refusal.Code));
        Assert.Equal(CreatePermission, world.Authority.LastPermission); // the record permission binding of this world is the create name
        world.Bindings = world.Bindings with { RecordPermission = null };
        var unbound = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordLoadAsync(id, created.Detail.Lines[0].LineId, new RecordLoadRequest("2", Guid.NewGuid(), "1", "Measured"), Metadata("k-load-b")));
        Assert.Equal((503, LogisticsProblemCodesV1.PermissionUnregistered), (unbound.Status, unbound.Code));
    }

    [Fact]
    public async Task Every_load_re_reads_the_current_sales_remainder_and_never_reopens_a_cancelled_remainder()
    {
        var world = new World();
        var created = await world.Service().CreateInstructionAsync(Request(quantity: "4"), Metadata("k-create-remainder"));
        var id = created.Detail.Instruction.Id;
        var lineId = created.Detail.Lines[0].LineId;
        await world.Service().IssueInstructionAsync(id, new IssueLoadingInstructionRequest("2"), Metadata("k-issue-remainder"));

        world.OrderLines.Fault = "remainder-cancelled";
        var cancelled = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().RecordLoadAsync(id, lineId,
            new RecordLoadRequest("3", Guid.NewGuid(), "0.001", "Measured"), Metadata("k-load-remainder")));
        Assert.Equal((409, LogisticsProblemCodesV1.OrderLineState, "sourceOrderLineId", "remainder_cancelled"),
            (cancelled.Status, cancelled.Code, cancelled.Errors[0].Field, cancelled.Errors[0].Code));
    }

    [Fact]
    public async Task A_revised_cap_atomically_limits_loaded_total_across_instructions_and_replays_the_refusal()
    {
        var world = new World { OrderLines = { Fault = "limit" } };
        var first = await world.Service().CreateInstructionAsync(Request("2.5"), Metadata("k-cap-create-1"));
        var second = await world.Service().CreateInstructionAsync(Request("2.5"), Metadata("k-cap-create-2"));
        await world.Service().IssueInstructionAsync(first.Detail.Instruction.Id, new IssueLoadingInstructionRequest("2"), Metadata("k-cap-issue-1"));
        await world.Service().IssueInstructionAsync(second.Detail.Instruction.Id, new IssueLoadingInstructionRequest("2"), Metadata("k-cap-issue-2"));
        await world.Service().RecordLoadAsync(first.Detail.Instruction.Id, first.Detail.Lines[0].LineId,
            new RecordLoadRequest("3", Guid.NewGuid(), "1.5", "Measured"), Metadata("k-cap-load-1"));
        await world.Service().RecordLoadAsync(second.Detail.Instruction.Id, second.Detail.Lines[0].LineId,
            new RecordLoadRequest("3", Guid.NewGuid(), "0.5", "Measured"), Metadata("k-cap-load-2"));

        world.OrderLines.Fault = "load-limit-revised";
        var callsBefore = world.OrderLines.Calls;
        var request = new RecordLoadRequest("4", Guid.NewGuid(), "0.001", "Measured");
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().RecordLoadAsync(
            second.Detail.Instruction.Id, second.Detail.Lines[0].LineId, request, Metadata("k-cap-over")));
        Assert.Equal((409, LogisticsProblemCodesV1.OrderLineAggregateLimitExceeded, false),
            (refusal.Status, refusal.Code, refusal.IsReplay));

        var replay = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().RecordLoadAsync(
            second.Detail.Instruction.Id, second.Detail.Lines[0].LineId, request, Metadata("k-cap-over", "retry-correlation")));
        Assert.Equal((409, LogisticsProblemCodesV1.OrderLineAggregateLimitExceeded, true, "synthetic-correlation"),
            (replay.Status, replay.Code, replay.IsReplay, replay.OriginalCorrelationId));
        Assert.Equal(callsBefore + 1, world.OrderLines.Calls);
        Assert.Equal(3, world.Store.LoadLimits.Count);
    }

    [Fact]
    public async Task Authority_is_measured_before_the_replay_and_a_lost_grant_refuses_before_the_store()
    {
        var world = new World { Authority = { Granted = false } };

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CreateInstructionAsync(Request(), Metadata("k-1")));

        Assert.Equal(403, refusal.Status);
        Assert.Equal((0, 0, 0), (world.Store.Replays, world.Accounts.Calls, world.Store.Envelopes.Count));
    }

    [Fact]
    public async Task Another_companys_instruction_is_404_and_the_list_filters_on_an_exact_status_name()
    {
        var world = new World();
        await world.Service().CreateInstructionAsync(Request(), Metadata("k-1"));

        var missing = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetInstructionDetailAsync(Guid.NewGuid()));
        Assert.Equal(404, missing.Status);
        var drafts = await world.Service().GetInstructionListAsync(new LoadingInstructionListQuery(Status: "Draft"));
        Assert.Single(drafts.Items);
        var issued = await world.Service().GetInstructionListAsync(new LoadingInstructionListQuery(Status: "Issued"));
        Assert.Empty(issued.Items);
        var bad = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetInstructionListAsync(new LoadingInstructionListQuery(Status: "draft")));
        Assert.Equal((400, "validation_failed"), (bad.Status, bad.Code));
    }

    [Fact]
    public void The_wire_shapes_are_strict()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => LogisticsJson.ReadCreate("{}"));
        Assert.Throws<System.Text.Json.JsonException>(() => LogisticsJson.ReadRecordLoad("{\"expectedVersion\":\"1\",\"loadId\":\"" + Guid.NewGuid() + "\",\"quantity\":\"1.5\",\"measurement\":\"Guessed\"}"));
        Assert.Throws<System.Text.Json.JsonException>(() => LogisticsJson.ReadIssue("{\"expectedVersion\":\"0\"}"));
        Assert.Throws<System.Text.Json.JsonException>(() => LogisticsJson.ReadIssue("{\"expectedVersion\":\"1\",\"extra\":1}"));
        var load = LogisticsJson.ReadRecordLoad("{\"expectedVersion\":\"3\",\"loadId\":\"" + Guid.NewGuid() + "\",\"quantity\":\"2.500\",\"measurement\":\"Measured\"}");
        Assert.Equal("2.500", load.Quantity);
        Assert.NotEqual(LogisticsRequestHash.Compute(Request(quantity: "1.25")), LogisticsRequestHash.Compute(Request(quantity: "1.250")));
    }

    // ---- world ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("2.9999999999999999999999999999999")]
    [InlineData("1.00000000000000000000000000000001")]
    public async Task A_quantity_text_longer_than_a_decimal_holds_is_refused_on_the_text_and_never_rounded(string text)
    {
        // FİNANS #350 P2-1: decimal.Parse would round these past 28 digits (to 3 and 1); the text is refused before any conversion.
        var world = new World();
        var create = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().CreateInstructionAsync(Request(quantity: text), Metadata("k-long")));
        Assert.Equal((422, LogisticsProblemCodesV1.UnsupportedUnit, "lines[0].unitId", LoadingUnitCodes.QuantityScaleExceeded),
            (create.Status, create.Code, create.Errors[0].Field, create.Errors[0].Code));
        Assert.Empty(world.Store.Envelopes);

        var created = await world.Service().CreateInstructionAsync(Request(quantity: "4"), Metadata("k-create"));
        var id = created.Detail.Instruction.Id;
        var lineId = created.Detail.Lines[0].LineId;
        await world.Service().IssueInstructionAsync(id, new IssueLoadingInstructionRequest("2"), Metadata("k-issue"));
        var load = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().RecordLoadAsync(id, lineId, new RecordLoadRequest("3", Guid.NewGuid(), text, "Measured"), Metadata("k-load")));
        Assert.Equal((422, LogisticsProblemCodesV1.UnsupportedUnit, "load.unitId", LoadingUnitCodes.QuantityScaleExceeded),
            (load.Status, load.Code, load.Errors[0].Field, load.Errors[0].Code));
        Assert.Equal(("3", "0"), ((await world.Service().GetInstructionDetailAsync(id)).Instruction.Version, (await world.Service().GetInstructionDetailAsync(id)).Lines[0].Loaded));
    }

    [Fact]
    public async Task A_quantity_with_more_than_fifteen_integer_digits_does_not_fit_the_column_and_is_refused_before_any_owner()
    {
        var world = new World();
        var refused = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service().CreateInstructionAsync(Request(quantity: "1234567890123456"), Metadata("k-wide")));
        Assert.Equal((422, "validation_failed", "lines[0].quantity", LogisticsStoreCodes.ValueDoesNotFit),
            (refused.Status, refused.Code, refused.Errors[0].Field, refused.Errors[0].Code));
        Assert.Empty(world.Store.Envelopes);
        Assert.Equal("1.25", (await world.Service().CreateInstructionAsync(Request(quantity: "000000000000000001.250"), Metadata("k-zeros"))).Detail.Lines[0].Instructed);
    }

    [Fact]
    public async Task A_same_key_race_lost_on_a_terminal_path_answers_the_winners_stored_transition()
    {
        // ADR-011 §8 (FİNANS #350 P3-1): B loads the instruction after A committed under the same key, hits a stale version, and
        // finds the key taken when recording the refusal: A's stored transition is B's answer, as a replay — not concurrent_change.
        var world = new World();
        var id = (await world.Service().CreateInstructionAsync(Request(quantity: "4"), Metadata("k-create"))).Detail.Instruction.Id;
        await world.Service().IssueInstructionAsync(id, new IssueLoadingInstructionRequest("2"), Metadata("k-issue-a"));
        world.Store.BeforeRefusal = (envelope, hash, command) => world.Store.PlantWinner(envelope, hash, command, id, "SENTETIK-winner");

        var answer = await world.Service().IssueInstructionAsync(id, new IssueLoadingInstructionRequest("2"), Metadata("k-issue-b"));

        Assert.Equal((true, "SENTETIK-winner", "Issued", "3"),
            (answer.IsReplay, answer.OriginalCorrelationId, answer.Detail.Instruction.Status, answer.Detail.Instruction.Version));
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task The_approved_order_lines_read_is_price_free_needs_the_create_name_and_is_the_companys_own()
    {
        // Sinan §4 (#66 5738113623): the Sevkiyatçı sees orders, never prices — the wire and port types carry no such member at all.
        foreach (var type in new[] { typeof(LogisticsApprovedOrderLineView), typeof(SalesApprovedLineReferenceV1) })
        {
            Assert.DoesNotContain(type.GetProperties(), property =>
                property.Name.Contains("Price", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Discount", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Payment", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Currency", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Amount", StringComparison.OrdinalIgnoreCase));
        }

        var world = new World();
        var page = await world.Service().GetApprovedOrderLinesAsync(new LogisticsApprovedOrderLinesQuery());
        var item = Assert.Single(page.Items);
        Assert.Equal((Order, "SENTETIK SO-0001", OrderLine, 1, Party, Account, "Sentetik Müşteri", Product, "7", "GDC-001", "Sentetik rulo", KgUnit, "kg", "12.500", new DateOnly(2026, 9, 25), 0, "SENTETIK-next"),
            (item.OrderId, item.DocumentNo, item.LineId, item.LineNo, item.CustomerPartyId, item.CompanyAccountId, item.CustomerName, item.ProductId, item.ProductVersion,
                item.ProductCode, item.ProductName, item.UnitId, item.UnitCode, item.Quantity, item.RequestedDeliveryDate, item.Revision, page.NextCursor));
        Assert.Equal(CreatePermission, world.Authority.LastPermission);
        // Business 5745076917: the customer's NAME comes from the Cari owner (trade name, else legal name). A customer the owner
        // does not answer for (not found) keeps its line under a labelled reference — the #358 case three lines below, never a
        // dropped line; only the owner's DENIAL (403) or OUTAGE (503) is the whole read's answer (both asserted below).
        world.Accounts.Fault = "no-trade-name";
        Assert.Equal("Sentetik Müşteri Anonim Şirketi", Assert.Single((await world.Service().GetApprovedOrderLinesAsync(new LogisticsApprovedOrderLinesQuery())).Items).CustomerName);
        // #358 P1 (coordinator ruling): a customer the Cari owner does not answer (a missing grant reads the same as a missing
        // party there) keeps its line on the page under a labelled reference, never a name and never a dropped line; a
        // genuinely empty approved set is an empty page — the two must never look alike.
        world.Accounts.Fault = "not-found";
        var unreadable = Assert.Single((await world.Service().GetApprovedOrderLinesAsync(new LogisticsApprovedOrderLinesQuery())).Items);
        Assert.Equal((OrderLine, $"ref. …{Party.ToString("D")[^8..]}"), (unreadable.LineId, unreadable.CustomerName));
        Assert.DoesNotContain(Party.ToString("D"), unreadable.CustomerName, StringComparison.OrdinalIgnoreCase);
        world.Accounts.Fault = "";
        world.ResetOwnerCalls();
        world.OrderLines.Fault = "empty";
        var none = await world.Service().GetApprovedOrderLinesAsync(new LogisticsApprovedOrderLinesQuery());
        Assert.Empty(none.Items);
        Assert.Null(none.NextCursor);
        Assert.Equal(0, world.Accounts.Calls);
        world.OrderLines.Fault = "";
        world.Accounts.Fault = "unavailable";
        var cari = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetApprovedOrderLinesAsync(new LogisticsApprovedOrderLinesQuery()));
        Assert.Equal((503, LogisticsProblemCodesV1.CounterpartyProviderUnavailable), (cari.Status, cari.Code));
        world.Accounts.Fault = "";

        // A read-only holder (no create name) is refused before the Sales owner is asked.
        world.Bindings = world.Bindings with { CreatePermission = null };
        var unbound = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetApprovedOrderLinesAsync(new LogisticsApprovedOrderLinesQuery()));
        Assert.Equal((503, LogisticsProblemCodesV1.PermissionUnregistered), (unbound.Status, unbound.Code));
        world.Bindings = world.Bindings with { CreatePermission = CreatePermission };

        // The owner's page must be this company's; an outage is 503; a refused cursor is the caller's 400.
        world.OrderLines.Fault = "other-company";
        var foreign = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetApprovedOrderLinesAsync(new LogisticsApprovedOrderLinesQuery()));
        Assert.Equal((503, LogisticsProblemCodesV1.ReferenceProviderInvalid), (foreign.Status, foreign.Code));
        // FİNANS #358 P2: the page is a LINE page; an owner answering more lines than the cap contradicts the contract.
        world.OrderLines.Fault = "over-cap";
        var overCap = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetApprovedOrderLinesAsync(new LogisticsApprovedOrderLinesQuery(1)));
        Assert.Equal((503, LogisticsProblemCodesV1.ReferenceProviderInvalid), (overCap.Status, overCap.Code));
        world.OrderLines.Fault = "throws";
        var outage = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetApprovedOrderLinesAsync(new LogisticsApprovedOrderLinesQuery()));
        Assert.Equal((503, LogisticsProblemCodesV1.SalesProviderUnavailable), (outage.Status, outage.Code));
        world.OrderLines.Fault = "bad-cursor";
        var cursor = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetApprovedOrderLinesAsync(new LogisticsApprovedOrderLinesQuery(50, "SENTETIK")));
        Assert.Equal((400, LogisticsProblemCodesV1.InvalidCursor), (cursor.Status, cursor.Code));
        var tooWide = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => world.Service().GetApprovedOrderLinesAsync(new LogisticsApprovedOrderLinesQuery(101)));
        Assert.Equal(400, tooWide.Status);
    }

    private static CreateLoadingInstructionRequest Request(string quantity = "1.25") =>
        new(Party, Account, Day, [new LoadingInstructionLineRequest(Guid.NewGuid(), Order, OrderLine, KgUnit, quantity)]);

    private static LogisticsCommandMetadata Metadata(string key, string correlation = "synthetic-correlation") =>
        new(key, correlation, null, DateTimeOffset.UtcNow);

    private sealed class World
    {
        public FixedUser User { get; } = new();
        public FixedAuthority Authority { get; init; } = new();
        public FakeAccounts Accounts { get; init; } = new();
        public FakeOrderLines OrderLines { get; init; } = new();
        public FakeSettings Settings { get; init; } = new();
        public FakeStore Store { get; init; } = new();
        public LogisticsInstructionServiceBindings Bindings { get; set; } = new(CreatePermission, ReadPermission, CreatePermission);

        public LogisticsInstructionService Service(ILogisticsRealizationTransactionRunnerV1? runner = null)
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new LogisticsInstructionService(company, User, Authority, new FixedCorrelation(), Accounts, OrderLines,
                new LoadingUnitVerifier(Settings), Store, Bindings, TimeProvider.System, runner);
        }

        public void ResetOwnerCalls()
        {
            Accounts.Calls = 0;
            OrderLines.Calls = 0;
            Settings.Calls = 0;
        }
    }

    private sealed class FixedUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class FixedAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
        public string? DeniedPermission { get; set; }
        public int? DenyOnCall { get; set; }
        public int Calls { get; private set; }
        public string? LastPermission { get; private set; }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastPermission = permission;
            return Task.FromResult(new PermissionScope(Granted && permission != DeniedPermission && (DenyOnCall is null || Calls < DenyOnCall),
                true, new HashSet<string>(["ROLE-A"], StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "synthetic-correlation";
        public string? CausationId => null;
        public string SourceChannel => "Web";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-agent";

        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent)
        {
        }
    }

    private sealed class FakeAccounts : ILogisticsPartyAccountPortV1
    {
        public string Fault { get; set; } = "";
        public int Calls { get; set; }

        public Task<LogisticsPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Fault switch
            {
                "not-found" => new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.NotFound, null),
                "denied" => new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.Denied, null),
                "unavailable" => new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.Unavailable, null),
                _ => new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.Found, new LogisticsPartyAccountV1(partyId, companyAccountId,
                    Fault == "other-company" ? Guid.NewGuid() : Company, true, Fault != "passive-account", "3", "2",
                    "Sentetik Müşteri Anonim Şirketi", Fault == "no-trade-name" ? null : "Sentetik Müşteri")),
            });
        }
    }

    private sealed class FakeOrderLines : ISalesOrderLineReferencePortV1
    {
        public string Fault { get; set; } = "";
        public int Calls { get; set; }

        /// <summary>LOG-4b: the price-free picker page the Sales owner answers; "other-company" answers a foreign row, "throws" an outage.</summary>
        public Task<SalesApprovedLinePageV1> ListApprovedLinesAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fault switch
            {
                "throws" => Task.FromException<SalesApprovedLinePageV1>(new InvalidOperationException("synthetic outage")),
                "bad-cursor" => Task.FromException<SalesApprovedLinePageV1>(new ArgumentException("synthetic cursor")),
                "empty" => Task.FromResult(new SalesApprovedLinePageV1([], null)),
                "over-cap" => Task.FromResult(new SalesApprovedLinePageV1(
                    [
                        new SalesApprovedLineReferenceV1(Company, Order, "SENTETIK SO-0001", OrderLine, 1, Party, Account, Product, "7", "GDC-001",
                            "Sentetik rulo", KgUnit, "kg", "12.500", new DateOnly(2026, 9, 25), 0),
                        new SalesApprovedLineReferenceV1(Company, Order, "SENTETIK SO-0001", Guid.NewGuid(), 2, Party, Account, Product, "7", "GDC-001",
                            "Sentetik rulo", KgUnit, "kg", "1.000", new DateOnly(2026, 9, 25), 0),
                    ],
                    null)),
                _ => Task.FromResult(new SalesApprovedLinePageV1(
                    [
                        new SalesApprovedLineReferenceV1(Fault == "other-company" ? Guid.NewGuid() : Company, Order, "SENTETIK SO-0001", OrderLine, 1,
                            Party, Account, Product, "7", "GDC-001", "Sentetik rulo", KgUnit, "kg", "12.500", new DateOnly(2026, 9, 25), 0),
                    ],
                    cursor is null ? "SENTETIK-next" : null)),
            };
        }

        public Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default)
        {
            Calls++;
            var aggregateLimit = Fault is "limit" or "limit-version-race" or "limit-invalid" or "load-limit-revised"
                ? new SalesOrderLineLoadingLimitV1(
                    Fault == "limit-invalid" ? Guid.NewGuid() : Company,
                    orderId,
                    lineId,
                    KgUnit,
                    Fault == "limit-version-race" && Calls > 1 ? 8 : 7,
                    Fault == "load-limit-revised" ? "2" : "5",
                    "Sales",
                    lineId.ToString("D"),
                    Fault == "limit-version-race" && Calls > 1 ? "4" : "3",
                    Day)
                : null;
            return Fault switch
            {
                "throws" => Task.FromException<SalesOrderLineReferenceAnswerV1>(new InvalidOperationException("synthetic outage")),
                "not-approved" => Task.FromResult(new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.NotApproved, null, "PendingApproval")),
                "not-found" => Task.FromResult(new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.NotFound, null, null)),
                _ => Task.FromResult(new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.Approved,
                    new SalesOrderLineReferenceV1(Fault == "other-company" ? Guid.NewGuid() : Company, orderId, lineId, 1,
                        Fault == "other-customer" ? Guid.NewGuid() : Party, Account, Product, "7", KgUnit,
                        // The generic approved line must cover the pre-existing weighbridge fixtures (up to 24,000 kg).
                        // The dedicated revised case remains deliberately capped at 2 kg.
                        Fault == "remainder-revised" ? "2" : "100000", 0,
                        Fault == "limit-version-race" && Calls > 1 ? 8 : 7,
                        Fault == "remainder-cancelled" ? SalesRemainderChangeNamesV1.Cancelled
                            : Fault is "remainder-revised" or "load-limit-revised" ? SalesRemainderChangeNamesV1.Revised : SalesRemainderChangeNamesV1.Open,
                        aggregateLimit), null)),
            };
        }
    }

    private sealed class FakeSettings : ISettingsPrerequisiteProvider
    {
        public string Fault { get; set; } = "";
        public int Calls { get; set; }

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fault switch
            {
                "unit-not-found" => Task.FromException<UnitProfileV1>(new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.UnitProfileNotFound)),
                "unit-access-denied" => Task.FromException<UnitProfileV1>(new SettingsPrerequisiteException("settings_access_denied")),
                "unit-unavailable" => Task.FromException<UnitProfileV1>(new SettingsPrerequisiteException("settings_provider_unavailable")),
                _ => Task.FromResult(new UnitProfileV1(Fault == "unit-other-company" ? Guid.NewGuid() : Company, unitId,
                    Fault == "unit-piece" ? "adet" : "kg", Fault == "unit-piece" ? "count" : "weight", Fault != "unit-piece",
                    new ProfileVersionV1(KgProfile, 4), 3, true, new DateOnly(2026, 9, 1), null)),
            };
        }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeStore : ILogisticsInstructionStore, ILogisticsAggregateLoadStoreV1
    {
        private readonly Dictionary<(string Command, string Key), (string Actor, string Hash, StoredLoadingInstruction? Stored, LogisticsTerminalRefusal? Refusal, string Correlation)> _attempts = [];
        private readonly List<StoredLoadingInstruction> _instructions = [];
        public List<LogisticsWriteEnvelope> Envelopes { get; } = [];
        public List<LogisticsOrderLineAggregateLimit> AggregateLimits { get; } = [];
        public List<LogisticsOrderLineAggregateLoadLimit> LoadLimits { get; } = [];
        public List<LogisticsTerminalRefusal> Refusals { get; } = [];
        public List<(string Command, Guid Id, long Before, long After)> Transitions { get; } = [];
        public int Replays { get; private set; }
        private long _lastSequenceNo;
        public Exception? CreateFault { get; set; }
        public Func<string, Exception?>? MutateFault { get; set; }
        public Action<LogisticsWriteEnvelope, string, string>? BeforeRefusal { get; set; }

        /// <summary>Simulates the same key's winner storing its transition between this caller's replay read and its refusal record (§8).</summary>
        public void PlantWinner(LogisticsWriteEnvelope envelope, string requestHash, string commandName, Guid instructionId, string correlation) =>
            _attempts[(commandName, envelope.IdempotencyKey)] =
                (envelope.ActorId, requestHash, _instructions.Single(candidate => candidate.Instruction.Id == instructionId), null, correlation);

        public Task<LogisticsStoreResult> StoreInstructionAsync(LogisticsWriteEnvelope envelope, string requestHash, DateOnly effectiveOn,
            Func<LoadingInstruction> build, CancellationToken cancellationToken = default,
            IReadOnlyList<LogisticsOrderLineAggregateLimit>? aggregateLimits = null)
        {
            if (_attempts.TryGetValue((LogisticsCommands.CreateV1, envelope.IdempotencyKey), out var attempt))
            {
                Check(attempt, envelope, requestHash);
                return Task.FromResult(new LogisticsStoreResult(attempt.Stored!, LogisticsWriteOutcome.Replayed, attempt.Correlation));
            }

            if (CreateFault is { } fault) throw fault;
            var stored = new StoredLoadingInstruction(build(), effectiveOn);
            _instructions.Add(stored);
            _attempts[(LogisticsCommands.CreateV1, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, stored, null, envelope.CorrelationId);
            Envelopes.Add(envelope);
            AggregateLimits.AddRange(aggregateLimits ?? []);
            return Task.FromResult(new LogisticsStoreResult(stored, LogisticsWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<LogisticsReplay?> FindReplayAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName, CancellationToken cancellationToken = default)
        {
            Replays++;
            if (!_attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt))
            {
                return Task.FromResult<LogisticsReplay?>(null);
            }

            Check(attempt, envelope, requestHash);
            return Task.FromResult<LogisticsReplay?>(new LogisticsReplay(attempt.Stored, attempt.Refusal, attempt.Correlation));
        }

        public Task RecordRefusalAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName, LogisticsTerminalRefusal refusal,
            CancellationToken cancellationToken = default)
        {
            BeforeRefusal?.Invoke(envelope, requestHash, commandName);
            if (_attempts.ContainsKey((commandName, envelope.IdempotencyKey)))
            {
                throw new LogisticsStoreException(LogisticsStoreCodes.ConcurrentChange, "synthetic");
            }

            _attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, null, refusal, envelope.CorrelationId);
            Refusals.Add(refusal);
            return Task.CompletedTask;
        }

        public Task<LogisticsStoreResult?> MutateInstructionAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName, Guid instructionId,
            Action<LoadingInstruction, DateTimeOffset> mutate, LogisticsMutationAudit audit, CancellationToken cancellationToken = default)
        {
            if (_attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt))
            {
                Check(attempt, envelope, requestHash);
                if (attempt.Stored is null) throw new LogisticsStoreException(LogisticsStoreCodes.ConcurrentChange, "synthetic refusal under the key");
                return Task.FromResult<LogisticsStoreResult?>(new LogisticsStoreResult(attempt.Stored, LogisticsWriteOutcome.Replayed, attempt.Correlation));
            }

            var stored = _instructions.SingleOrDefault(candidate => candidate.Instruction.Id == instructionId);
            if (stored is null) return Task.FromResult<LogisticsStoreResult?>(null);
            // A store-level refusal (the real store's company-wide checks) raised before anything moves.
            if (MutateFault?.Invoke(commandName) is { } fault) throw fault;
            var before = stored.Instruction.Version;
            mutate(stored.Instruction, TimeProvider.System.GetUtcNow());
            // AT-055: the store gives the note approved by this step the company's next number (gap-free, as the real series).
            foreach (var approved in stored.Instruction.DispatchNotes.Where(note => note is { Status: DispatchNoteStatus.Approved, Number: null }).ToArray())
            {
                stored.Instruction.AssignDispatchNoteNumber(approved.DispatchNoteId, DispatchNoteNumber.Of(DispatchNoteNumber.DefaultSeriesCode, ++_lastSequenceNo));
            }
            _attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, stored, null, envelope.CorrelationId);
            Transitions.Add((commandName, instructionId, before, stored.Instruction.Version));
            return Task.FromResult<LogisticsStoreResult?>(new LogisticsStoreResult(stored, LogisticsWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<LogisticsStoreResult?> MutateInstructionWithAggregateLoadLimitAsync(LogisticsWriteEnvelope envelope, string requestHash,
            string commandName, Guid instructionId, Action<LoadingInstruction, DateTimeOffset> mutate, LogisticsMutationAudit audit,
            LogisticsOrderLineAggregateLoadLimit aggregateLoadLimit, CancellationToken cancellationToken = default)
        {
            if (_attempts.ContainsKey((commandName, envelope.IdempotencyKey)))
            {
                return MutateInstructionAsync(envelope, requestHash, commandName, instructionId, mutate, audit, cancellationToken);
            }

            var loaded = _instructions.SelectMany(stored => stored.Instruction.Lines)
                .Where(line => line.Source.OrderId == aggregateLoadLimit.OrderId
                    && line.Source.LineId == aggregateLoadLimit.OrderLineId && line.UnitId == aggregateLoadLimit.UnitId)
                .Sum(line => line.LoadedQuantity.Value);
            LoadLimits.Add(aggregateLoadLimit);
            if (loaded + aggregateLoadLimit.RequestedLoadQuantity > aggregateLoadLimit.MaximumAggregateQuantity)
            {
                throw new LogisticsStoreException(LogisticsStoreCodes.AggregateLimitExceeded, "synthetic aggregate load limit");
            }

            return MutateInstructionAsync(envelope, requestHash, commandName, instructionId, mutate, audit, cancellationToken);
        }

        public LoadingInstruction Instruction(Guid instructionId) =>
            _instructions.Single(candidate => candidate.Instruction.Id == instructionId).Instruction;

        public Task<StoredLoadingInstruction?> FindInstructionAsync(Guid instructionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_instructions.SingleOrDefault(candidate => candidate.Instruction.Id == instructionId));

        public Task<LogisticsInstructionPage> ListInstructionsAsync(int pageSize, string? cursor, LoadingInstructionStatus? status = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LogisticsInstructionPage([.. _instructions.Where(stored => status is null || stored.Instruction.Status == status)], null));

        public Task<IReadOnlyList<StoredDispatchNoteReference>> ListSalesOrderDispatchNotesAsync(Guid salesOrderId, int limit,
            CancellationToken cancellationToken = default)
        {
            SalesOrderReads++;
            IReadOnlyList<StoredDispatchNoteReference> notes = [.. _instructions.SelectMany(stored => stored.Instruction.DispatchNotes.Select(note =>
                    new StoredDispatchNoteReference(note.DispatchNoteId, stored.Instruction.Id, note.Status, note.CreatedAtUtc,
                        [.. note.Lines.Select(line => (Line: line, Source: stored.Instruction.Lines.Single(candidate => candidate.Id == line.InstructionLineId)))
                            .Where(pair => pair.Source.Source.OrderId == salesOrderId)
                            .Select(pair => new StoredDispatchNoteOrderLine(pair.Line.LineNo, pair.Source.Source.LineId, pair.Line.ShipmentId,
                                pair.Source.UnitId, pair.Line.Quantity.Value))], note.Number, note.EDispatchNumber)))
                .Where(note => note.Lines.Count > 0).Take(limit)];
            return Task.FromResult(notes);
        }

        public int SalesOrderReads { get; private set; }

        public int InvoiceSourceReads { get; private set; }

        public Task<StoredApprovedDispatchInvoiceSource?> FindApprovedDispatchInvoiceSourceAsync(Guid dispatchNoteId,
            CancellationToken cancellationToken = default)
        {
            InvoiceSourceReads++;
            foreach (var stored in _instructions)
            {
                var note = stored.Instruction.DispatchNotes.SingleOrDefault(candidate => candidate.DispatchNoteId == dispatchNoteId);
                if (note is not { Status: DispatchNoteStatus.Approved })
                {
                    continue;
                }

                return Task.FromResult<StoredApprovedDispatchInvoiceSource?>(new StoredApprovedDispatchInvoiceSource(
                    stored.Instruction.CompanyId,
                    note.DispatchNoteId,
                    stored.Instruction.Id,
                    stored.Instruction.Version,
                    stored.Instruction.CustomerPartyId,
                    stored.Instruction.CompanyAccountId,
                    note.Number,
                    note.EDispatchNumber,
                    note.ApprovedAtUtc,
                    [.. note.Lines.Select(line =>
                    {
                        var source = stored.Instruction.Lines.Single(candidate => candidate.Id == line.InstructionLineId);
                        return new StoredApprovedDispatchInvoiceSourceLine(line.LineNo, source.Source.OrderId, source.Source.LineId,
                            line.ShipmentId, source.ProductId, source.UnitId, line.Quantity.Value);
                    })]));
            }

            return Task.FromResult<StoredApprovedDispatchInvoiceSource?>(null);
        }

        private static void Check((string Actor, string Hash, StoredLoadingInstruction? Stored, LogisticsTerminalRefusal? Refusal, string Correlation) attempt,
            LogisticsWriteEnvelope envelope, string requestHash)
        {
            if (attempt.Actor != envelope.ActorId || attempt.Hash != requestHash)
            {
                throw new LogisticsStoreException(LogisticsStoreCodes.SameKeyPayloadMismatch, "synthetic");
            }
        }
    }
}

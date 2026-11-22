using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP-A1 AI gateway (<c>ai-gateway.v1</c>) over the Fake composers and an in-memory Settings policy/ledger double. Pins:
/// a sourced answer whose numbers come only from tools; "cannot answer" without a source (no model, no budget) and for a
/// number the sources do not hold; the monthly ceiling and the empty-ceiling "budget closed"; the per-feature switch; the
/// task × data class table with its fixed placement rules (no cloud above Internal, no document/e-mail text on a cloud
/// model, the runtime cap); the disabled runtime; general chat without ERP data. SENTETIK values only.
/// </summary>
public sealed class AiGatewayTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000a1");
    private static readonly Guid Actor = Guid.Parse("11111111-1111-4111-8111-1111111111a1");
    private static readonly Guid Target = Guid.Parse("cccccccc-0000-4000-8000-0000000000a3");
    private static readonly DateTimeOffset AsOf = new(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_sourced_answer_restates_only_tool_numbers_cites_the_source_and_records_the_used_units()
    {
        var store = new InMemoryAiGatewayStore(Company);
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store);

        var result = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42,\"durum\":\"Yayimlandi\"}")]));

        Assert.Equal(AiGatewayStatusV1.Answered, result.Status);
        var answer = Assert.IsType<AiGatewayAnswerV1>(result.Answer);
        Assert.True(answer.Grounded);
        Assert.Contains("42", answer.Text, StringComparison.Ordinal);
        var citation = Assert.Single(answer.Citations);
        Assert.Equal(Target, citation.TargetId);
        Assert.Equal("rv-9", citation.SourceVersion);
        Assert.Equal(InMemoryAiGatewayStore.LocalDeployment, answer.ModelDeployment);
        Assert.Equal(AiModelPlacementV1.Local, answer.Placement);
        Assert.Equal("ai.gateway-policy:7", answer.PolicyVersion);
        var reservation = Assert.Single(store.Reservations);
        Assert.Equal(AiFeatureV1.Panel, reservation.Feature);
        Assert.Equal(7, reservation.PolicyVersion);
        var entry = Assert.Single(store.Entries.Values);
        Assert.Equal(answer.UsageUnits, entry.Used);
        Assert.True(answer.UsageUnits > 0);
        Assert.Equal(answer.UsageUnits, store.CommittedUnits);
    }

    [Fact]
    public async Task Without_a_source_the_gateway_cannot_answer_and_neither_reserves_budget_nor_calls_a_model()
    {
        var store = new InMemoryAiGatewayStore(Company);
        var composer = new CountingComposer(AiModelPlacementV1.Local);
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store, [composer]);

        var result = await gateway.ComposeAsync(Request(grounding: []));

        Assert.Equal(AiGatewayStatusV1.CannotAnswer, result.Status);
        Assert.Equal(AiGatewayCannotAnswerReasonsV1.NoSource, result.CannotAnswerReason);
        Assert.Null(result.Answer);
        Assert.Empty(store.Reservations);
        Assert.Equal(0, composer.Calls);
    }

    [Theory]
    [InlineData("[1] SENTETIK talimat: toplam 84 adet.")]
    [InlineData("[1] SENTETIK talimat: 42,0 adet.")]
    [InlineData("[1] SENTETIK talimat: yaklasik 40 adet.")]
    public async Task A_number_the_sources_do_not_hold_is_cannot_answer_and_the_used_units_are_still_recorded(string text)
    {
        var store = new InMemoryAiGatewayStore(Company);
        var composer = new CountingComposer(AiModelPlacementV1.Local) { Text = text, Cited = [1], Units = 17 };
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store, [composer]);

        var result = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}")]));

        Assert.Equal(AiGatewayStatusV1.CannotAnswer, result.Status);
        Assert.Equal(AiGatewayCannotAnswerReasonsV1.Unsupported, result.CannotAnswerReason);
        Assert.Null(result.Answer);
        Assert.Equal(17, Assert.Single(store.Entries.Values).Used);
        Assert.Equal(17, store.CommittedUnits);
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 2 })]
    [InlineData(new[] { 1, 1 })]
    public async Task A_text_that_cites_no_existing_source_or_cites_twice_is_cannot_answer(int[] cited)
    {
        var store = new InMemoryAiGatewayStore(Company);
        var composer = new CountingComposer(AiModelPlacementV1.Local) { Text = "SENTETIK talimat: 42.", Cited = cited };
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store, [composer]);

        var result = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}")]));

        Assert.Equal(AiGatewayStatusV1.CannotAnswer, result.Status);
        Assert.Equal(AiGatewayCannotAnswerReasonsV1.Unsupported, result.CannotAnswerReason);
    }

    [Fact]
    public async Task A_reached_monthly_ceiling_closes_the_gateway_before_the_model_is_called()
    {
        var store = new InMemoryAiGatewayStore(Company) { CommittedUnits = 999_600 };
        var composer = new CountingComposer(AiModelPlacementV1.Local);
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store, [composer]);

        var result = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}")]));

        Assert.Equal(AiGatewayStatusV1.BudgetExhausted, result.Status);
        Assert.Equal(0, composer.Calls);
        Assert.Empty(store.Entries);
        Assert.Equal(999_600, store.CommittedUnits);
    }

    [Fact]
    public async Task The_ceiling_is_reached_by_settled_usage_and_the_next_request_is_closed()
    {
        var store = new InMemoryAiGatewayStore(Company);
        store.Policy = store.Policy with { MonthlyUsageCeiling = 600, MaxUnitsPerRequest = 500 };
        var composer = new CountingComposer(AiModelPlacementV1.Local) { Text = "[1] SENTETIK: 42.", Cited = [1], Units = 300 };
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store, [composer]);

        var first = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}")]));
        var second = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}")]));

        Assert.Equal(AiGatewayStatusV1.Answered, first.Status);
        Assert.Equal(AiGatewayStatusV1.BudgetExhausted, second.Status);
        Assert.Equal(1, composer.Calls);
        Assert.Equal(300, store.CommittedUnits);
    }

    [Theory]
    [InlineData(null, 500)]
    [InlineData(1_000_000L, null)]
    public async Task An_empty_ceiling_or_reservation_is_a_closed_budget_never_a_default(long? ceiling, int? perRequest)
    {
        var store = new InMemoryAiGatewayStore(Company);
        store.Policy = store.Policy with { MonthlyUsageCeiling = ceiling, MaxUnitsPerRequest = perRequest };
        var composer = new CountingComposer(AiModelPlacementV1.Local);
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store, [composer]);

        var result = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}")]));

        Assert.Equal(AiGatewayStatusV1.BudgetExhausted, result.Status);
        Assert.Equal(0, composer.Calls);
    }

    [Fact]
    public async Task A_switched_off_feature_is_refused_before_source_budget_or_model_while_another_feature_still_runs()
    {
        var store = new InMemoryAiGatewayStore(Company);
        store.Policy = store.Policy with { EnabledFeatures = new HashSet<AiFeatureV1> { AiFeatureV1.Query } };
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store);

        var panel = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}")]));
        var noSourcePanel = await gateway.ComposeAsync(Request(grounding: []));
        var query = await gateway.ComposeAsync(Request(feature: AiFeatureV1.Query, grounding: [Item("{\"bekleyenTalimat\":42}")]));

        Assert.Equal(AiGatewayStatusV1.FeatureDisabled, panel.Status);
        Assert.Equal(AiGatewayStatusV1.FeatureDisabled, noSourcePanel.Status);
        Assert.Equal(AiGatewayStatusV1.Answered, query.Status);
        Assert.Equal(AiFeatureV1.Query, Assert.Single(store.Reservations).Feature);
    }

    [Fact]
    public async Task A_company_without_a_recorded_policy_has_every_feature_closed()
    {
        var store = new InMemoryAiGatewayStore(Company);
        store.Policy = Modules.Settings.Application.Contracts.V1.SettingsAiGatewayPolicyV1.NotRecorded(Company);
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store);

        foreach (var feature in Enum.GetValues<AiFeatureV1>())
        {
            var result = await gateway.ComposeAsync(feature == AiFeatureV1.Chat
                ? Request(feature: feature, task: AiTaskKindV1.Converse, grounding: [])
                : Request(feature: feature, grounding: [Item("{\"bekleyenTalimat\":42}")]));
            Assert.Equal(AiGatewayStatusV1.FeatureDisabled, result.Status);
        }

        Assert.Empty(store.Reservations);
    }

    [Fact]
    public async Task Strictly_confidential_content_without_a_route_is_refused_and_never_reaches_a_model()
    {
        var store = new InMemoryAiGatewayStore(Company);
        var composer = new CountingComposer(AiModelPlacementV1.Local);
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(AiDataClassV1.StrictlyConfidential), store, [composer]);

        var result = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}", AiDataClassV1.StrictlyConfidential)]));

        Assert.Equal(AiGatewayStatusV1.DataClassRefused, result.Status);
        Assert.Equal(0, composer.Calls);
        Assert.Empty(store.Reservations);
    }

    [Fact]
    public async Task Data_above_the_runtime_cap_is_refused_even_when_the_table_has_a_route()
    {
        var store = new InMemoryAiGatewayStore(Company);
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(AiDataClassV1.Internal), store);

        var source = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}", AiDataClassV1.Confidential)]));
        var question = await gateway.ComposeAsync(Request(contentClass: AiDataClassV1.Confidential,
            grounding: [Item("{\"bekleyenTalimat\":42}")]));

        Assert.Equal(AiGatewayStatusV1.DataClassRefused, source.Status);
        Assert.Equal(AiGatewayStatusV1.DataClassRefused, question.Status);
        Assert.Empty(store.Reservations);
    }

    [Fact]
    public async Task The_highest_data_class_routes_the_call_so_confidential_content_goes_only_to_the_local_route()
    {
        var store = new InMemoryAiGatewayStore(Company);
        var cloud = new CountingComposer(AiModelPlacementV1.Cloud);
        var local = new CountingComposer(AiModelPlacementV1.Local) { Text = "[1] [2] SENTETIK: 42 ve 7.", Cited = [1, 2] };
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(AiDataClassV1.Confidential), store, [cloud, local]);

        var result = await gateway.ComposeAsync(Request(grounding:
        [
            Item("{\"bekleyenTalimat\":42}", AiDataClassV1.Public),
            Item("{\"gecikenTalimat\":7}", AiDataClassV1.Confidential),
        ]));

        Assert.Equal(AiGatewayStatusV1.Answered, result.Status);
        Assert.Equal(0, cloud.Calls);
        Assert.Equal(1, local.Calls);
        Assert.Equal(2, result.Answer!.Citations.Count);
    }

    [Fact]
    public async Task A_cloud_route_above_internal_is_refused_even_if_a_policy_carries_one()
    {
        var store = new InMemoryAiGatewayStore(Company);
        store.Policy = store.Policy with
        {
            Routes = [new(AiTaskKindV1.Answer, AiDataClassV1.Confidential, AiModelPlacementV1.Cloud, InMemoryAiGatewayStore.CloudDeployment)],
        };
        var cloud = new CountingComposer(AiModelPlacementV1.Cloud);
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(AiDataClassV1.Confidential), store, [cloud]);

        var result = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}", AiDataClassV1.Confidential)]));

        Assert.Equal(AiGatewayStatusV1.DataClassRefused, result.Status);
        Assert.Equal(0, cloud.Calls);
    }

    [Theory]
    [InlineData(AiFeatureV1.Document)]
    [InlineData(AiFeatureV1.Email)]
    public async Task Document_and_email_text_is_never_sent_to_a_cloud_route(AiFeatureV1 feature)
    {
        var store = new InMemoryAiGatewayStore(Company);
        var cloud = new CountingComposer(AiModelPlacementV1.Cloud);
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store, [cloud]);

        var result = await gateway.ComposeAsync(Request(feature: feature, task: AiTaskKindV1.Extract,
            grounding: [Item("{\"satir\":\"SENTETIK 42\"}")]));

        Assert.Equal(AiGatewayStatusV1.DataClassRefused, result.Status);
        Assert.Equal(0, cloud.Calls);
        Assert.Empty(store.Reservations);
    }

    [Fact]
    public async Task A_route_whose_placement_has_no_registered_model_is_unavailable_and_reserves_nothing()
    {
        var store = new InMemoryAiGatewayStore(Company);
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store, [new CountingComposer(AiModelPlacementV1.Cloud)]);

        var result = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}")]));

        Assert.Equal(AiGatewayStatusV1.Unavailable, result.Status);
        Assert.Empty(store.Reservations);
    }

    [Fact]
    public async Task A_disabled_runtime_answers_unavailable_without_reading_the_policy()
    {
        var store = new InMemoryAiGatewayStore(Company);
        var options = ReadyOptions();
        options.Enabled = false;
        var gateway = AiGatewayTestDoubles.Gateway(options, store);

        var result = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}")]));

        Assert.Equal(AiGatewayStatusV1.RuntimeUnavailable, result.Status);
        Assert.Equal(0, store.PolicyReads);
    }

    [Fact]
    public async Task The_fake_composer_is_never_admitted_in_production()
    {
        var store = new InMemoryAiGatewayStore(Company);
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store, environmentName: "Production");

        var result = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}")]));

        Assert.Equal(AiGatewayStatusV1.RuntimeUnavailable, result.Status);
        Assert.Equal(0, store.PolicyReads);
    }

    [Fact]
    public async Task A_model_failure_settles_the_reservation_at_zero_and_releases_nothing()
    {
        var store = new InMemoryAiGatewayStore(Company);
        var composer = new CountingComposer(AiModelPlacementV1.Local) { Fail = true };
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store, [composer]);

        var result = await gateway.ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}")]));

        Assert.Equal(AiGatewayStatusV1.Unavailable, result.Status);
        Assert.Null(result.Answer);
        Assert.Equal(0, Assert.Single(store.Entries.Values).Used);
        Assert.Equal(0, store.CommittedUnits);
    }

    [Fact]
    public async Task A_policy_store_failure_or_a_changed_policy_version_is_unavailable()
    {
        var failing = new InMemoryAiGatewayStore(Company) { FailPolicyRead = true };
        var changed = new ChangingPolicyStore(Company);

        var unreadable = await AiGatewayTestDoubles.Gateway(ReadyOptions(), failing)
            .ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}")]));
        var raced = await AiGatewayTestDoubles.Gateway(ReadyOptions(), changed)
            .ComposeAsync(Request(grounding: [Item("{\"bekleyenTalimat\":42}")]));

        Assert.Equal(AiGatewayStatusV1.Unavailable, unreadable.Status);
        Assert.Equal(AiGatewayStatusV1.Unavailable, raced.Status);
        Assert.Empty(changed.Entries);
    }

    [Fact]
    public async Task General_chat_runs_without_erp_data_and_is_marked_ungrounded_while_chat_with_grounding_is_invalid()
    {
        var store = new InMemoryAiGatewayStore(Company);
        var composer = new CountingComposer(AiModelPlacementV1.Cloud) { Text = "SENTETIK genel cevap.", Cited = [] };
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store, [composer]);

        var chat = await gateway.ComposeAsync(Request(feature: AiFeatureV1.Chat, task: AiTaskKindV1.Converse, grounding: []));
        var withErpData = await gateway.ComposeAsync(Request(feature: AiFeatureV1.Chat, task: AiTaskKindV1.Converse,
            grounding: [Item("{\"bekleyenTalimat\":42}")]));
        var panelConverse = await gateway.ComposeAsync(Request(task: AiTaskKindV1.Converse, grounding: [Item("{\"bekleyenTalimat\":42}")]));

        Assert.Equal(AiGatewayStatusV1.Answered, chat.Status);
        Assert.False(chat.Answer!.Grounded);
        Assert.Empty(chat.Answer.Citations);
        Assert.Equal(AiGatewayStatusV1.Invalid, withErpData.Status);
        Assert.Equal(AiGatewayStatusV1.Invalid, panelConverse.Status);
        Assert.Equal(1, composer.Calls);
    }

    [Fact]
    public async Task A_request_without_a_server_caller_or_with_a_source_of_another_tool_is_invalid()
    {
        var store = new InMemoryAiGatewayStore(Company);
        var gateway = AiGatewayTestDoubles.Gateway(ReadyOptions(), store);
        var item = Item("{\"bekleyenTalimat\":42}");

        var noCompany = await gateway.ComposeAsync(Request(grounding: [item]) with
        {
            Caller = new AiGatewayCallerV1(Guid.Empty, Actor.ToString("D"), "corr-sentetik", null),
        });
        var forged = await gateway.ComposeAsync(Request(grounding: [item with { ToolId = "test.other-tool.v1" }]));

        Assert.Equal(AiGatewayStatusV1.Invalid, noCompany.Status);
        Assert.Equal(AiGatewayStatusV1.Invalid, forged.Status);
        Assert.Equal(0, store.PolicyReads);
    }

    [Fact]
    public void The_gateway_contract_identity_is_versioned()
    {
        Assert.Equal("ai-gateway.v1", AiGatewayContractV1.Version);
        Assert.Equal(AiDataClassV1.Internal, AiGatewayPlacementRulesV1.CloudMaximumDataClass);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], Enum.GetValues<AiFeatureV1>().Select(value => (int)value));
        Assert.Equal([1, 2, 3, 4, 5, 6], Enum.GetValues<AiTaskKindV1>().Select(value => (int)value));
        Assert.Equal([1, 2], Enum.GetValues<AiModelPlacementV1>().Select(value => (int)value));
    }

    internal static AiRuntimeOptions ReadyOptions(AiDataClassV1 maxDataClass = AiDataClassV1.Confidential) => new()
    {
        Enabled = true,
        Provider = AiProviderKindsV1.Fake,
        PolicyRef = "TEST-AI-POLICY",
        PolicyVersion = 3,
        MaxQuestionLength = 200,
        MaxArgumentBytes = 256,
        MaxToolCallsPerRequest = 3,
        MaxDataClass = maxDataClass,
        AllowedTools = [],
    };

    private static AiGroundingItemV1 Item(string contentJson, AiDataClassV1 dataClass = AiDataClassV1.Internal) =>
        new("test.pending-instructions.v1", "1", contentJson,
            new AiSourceV1("test.pending-instructions.v1", "1", "loading-instruction", Target, "SENTETIK talimat", "rv-9", AsOf,
                dataClass));

    private static AiGatewayRequestV1 Request(
        IReadOnlyList<AiGroundingItemV1> grounding,
        AiFeatureV1 feature = AiFeatureV1.Panel,
        AiTaskKindV1 task = AiTaskKindV1.Answer,
        AiDataClassV1 contentClass = AiDataClassV1.Internal) =>
        new(feature, task, "SENTETIK bekleyen talimat sayisi nedir?", contentClass, grounding,
            new AiGatewayCallerV1(Company, Actor.ToString("D"), "corr-ai-gateway-sentetik", null));

    private sealed class CountingComposer(AiModelPlacementV1 placement) : IAiAnswerComposerV1
    {
        private readonly FakeAiAnswerComposer inner = new(placement);

        public int Calls { get; private set; }
        public string? Text { get; init; }
        public IReadOnlyList<int>? Cited { get; init; }
        public int Units { get; init; } = 11;
        public bool Fail { get; init; }

        public string ProviderKind => AiProviderKindsV1.Fake;

        public AiModelPlacementV1 Placement { get; } = placement;

        public async Task<AiComposedTextV1> ComposeAsync(AiComposeRequestV1 request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("SENTETIK model failure");
            return Text is null
                ? await inner.ComposeAsync(request, cancellationToken)
                : new AiComposedTextV1(Text, Cited ?? [], Units);
        }
    }

    /// <summary>The policy moves to a new version between the gateway's read and its reservation.</summary>
    private sealed class ChangingPolicyStore(Guid companyId) : Modules.Settings.Application.Contracts.V1.ISettingsAiGatewayPolicyProviderV1,
        Modules.Settings.Application.Contracts.V1.ISettingsAiBudgetLedgerV1
    {
        private readonly InMemoryAiGatewayStore inner = new(companyId);

        public Dictionary<Guid, (int Reserved, int? Used)> Entries => inner.Entries;

        public async Task<Modules.Settings.Application.Contracts.V1.SettingsAiGatewayPolicyV1> GetAiGatewayPolicyAsync(Guid id,
            CancellationToken cancellationToken = default)
        {
            var read = await inner.GetAiGatewayPolicyAsync(id, cancellationToken);
            inner.Policy = inner.Policy with { Version = inner.Policy.Version + 1 };
            return read;
        }

        public Task<Modules.Settings.Application.Contracts.V1.SettingsAiBudgetReservationV1> ReserveAsync(
            Modules.Settings.Application.Contracts.V1.SettingsAiBudgetReservationRequestV1 request,
            CancellationToken cancellationToken = default) => inner.ReserveAsync(request, cancellationToken);

        public Task SettleAsync(Guid id, Guid entryId, int usedUnits, CancellationToken cancellationToken = default) =>
            inner.SettleAsync(id, entryId, usedUnits, cancellationToken);

        public Task<Modules.Settings.Application.Contracts.V1.SettingsAiBudgetMonthUsageV1> GetCurrentMonthUsageAsync(Guid id,
            CancellationToken cancellationToken = default) => inner.GetCurrentMonthUsageAsync(id, cancellationToken);
    }
}

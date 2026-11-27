using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiAskServiceTests
{
    private static AiAskRequestV1 GeneralQuestion(string question = "SENTETIK general question") => new(question, null, null, null, AiAskModesV1.GeneralChat);

    [Fact]
    public async Task General_chat_uses_only_same_gateway_without_planner_catalog_tools_or_ERP_grounding()
    {
        var harness = new Harness(); var composer = new ChatModeComposer(AiModelPlacementV1.Cloud);
        var result = await harness.Service([composer]).AskAsync(GeneralQuestion());
        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        var response = Assert.IsType<AiAskResponseV1>(result.Response);
        Assert.Equal(AiAskModesV1.GeneralChat, response.Mode); Assert.Empty(response.ToolResults);
        Assert.False(response.Answer!.Grounded); Assert.Empty(response.Answer.Citations);
        var request = Assert.Single(composer.Requests); Assert.Equal(AiTaskKindV1.Converse, request.TaskKind); Assert.Empty(request.Sources);
        Assert.Equal("SENTETIK general question", request.Instruction);
        Assert.Equal(0, harness.Provider.Calls); Assert.Empty(harness.Tool.Contexts);
        Assert.Equal(AiFeatureV1.Chat, Assert.Single(harness.GatewayStore.Reservations).Feature);
    }

    [Theory]
    [InlineData("id")] [InlineData("version")] [InlineData("arguments")]
    public async Task General_chat_rejects_any_ERP_tool_input_before_planning_or_gateway_reservation(string field)
    {
        var harness = new Harness(); using var args = JsonDocument.Parse("{}");
        var request = GeneralQuestion() with { ToolId = field == "id" ? ToolId : null,
            ToolVersion = field == "version" ? ToolVersion : null, Arguments = field == "arguments" ? args.RootElement.Clone() : null };
        var result = await harness.Service().AskAsync(request);
        Assert.Equal(AiAskStatusV1.Invalid, result.Status); Assert.Equal([AiInvalidFieldsV1.Tool], result.InvalidFields);
        Assert.Equal(0, harness.Provider.Calls); Assert.Empty(harness.Tool.Contexts); Assert.Empty(harness.GatewayStore.Reservations);
    }

    [Theory]
    [InlineData("")] [InlineData("ERP")] [InlineData("general-chat ")] [InlineData("web")]
    public async Task Unknown_modes_are_invalid_without_normalizing_or_inferencing_an_authority_boundary(string mode)
    {
        var harness = new Harness(); var result = await harness.Service().AskAsync(GeneralQuestion() with { Mode = mode });
        Assert.Equal(AiAskStatusV1.Invalid, result.Status); Assert.Equal([AiInvalidFieldsV1.Mode], result.InvalidFields);
        Assert.Equal(0, harness.Provider.Calls); Assert.Empty(harness.Tool.Contexts); Assert.Empty(harness.GatewayStore.Reservations);
    }

    [Fact]
    public async Task Actual_fake_general_chat_is_unavailable_without_invented_text_tools_or_usage_reservation()
    {
        var harness = new Harness(); var result = await harness.Service().AskAsync(GeneralQuestion());
        Assert.Equal(AiAskStatusV1.Unavailable, result.Status); Assert.Null(result.Response);
        Assert.Equal(0, harness.Provider.Calls); Assert.Empty(harness.Tool.Contexts); Assert.Empty(harness.GatewayStore.Reservations);
    }

    [Theory]
    [InlineData("SENTETIK contact person@example.invalid")]
    [InlineData("SENTETIK 12 TRY")]
    [InlineData("SENTETIK Sayın Test")]
    public async Task General_question_sensitive_text_reuses_existing_screens_and_never_reaches_a_cloud_composer(string question)
    {
        var harness = new Harness(); var composer = new ChatModeComposer(AiModelPlacementV1.Cloud);
        var result = await harness.Service([composer]).AskAsync(GeneralQuestion(question));
        Assert.Equal(AiAskStatusV1.Refused, result.Status); Assert.Empty(composer.Requests); Assert.Empty(harness.GatewayStore.Reservations);
        Assert.Equal(0, harness.Provider.Calls); Assert.Empty(harness.Tool.Contexts);
    }

    [Theory]
    [InlineData("company")] [InlineData("permission")]
    public async Task General_chat_keeps_the_server_company_and_existing_panel_permission_gate(string fault)
    {
        var harness = new Harness(); var composer = new ChatModeComposer(AiModelPlacementV1.Cloud);
        if (fault == "company") { var other = new CompanyContext(); other.Set(OtherCompany); harness.CompanyScope = other; }
        else harness.Authority.Revoke(ErpPermissions.AiPanelUse);
        var result = await harness.Service([composer]).AskAsync(GeneralQuestion());
        Assert.Equal(AiAskStatusV1.Refused, result.Status); Assert.Empty(composer.Requests); Assert.Empty(harness.GatewayStore.Reservations);
        Assert.Equal(0, harness.Provider.Calls); Assert.Empty(harness.Tool.Contexts);
    }

    [Fact]
    public async Task Fresh_panel_permission_loss_during_general_composition_releases_no_answer()
    {
        var harness = new Harness(); var composer = new ChatModeComposer(AiModelPlacementV1.Cloud)
            { BeforeAnswer = () => harness.Authority.Revoke(ErpPermissions.AiPanelUse) };
        var result = await harness.Service([composer]).AskAsync(GeneralQuestion());
        Assert.Equal(AiAskStatusV1.Refused, result.Status); Assert.Null(result.Response); Assert.Single(composer.Requests);
        Assert.NotNull(Assert.Single(harness.GatewayStore.Entries.Values).Used);
    }

    [Fact]
    public async Task ERP_tool_permission_revoked_while_composing_releases_no_previous_tool_results()
    {
        var harness = new Harness(); var composer = new ChatModeComposer(AiModelPlacementV1.OnPremises)
            { BeforeAnswer = () => harness.Authority.Revoke(ToolPermission) };
        var result = await harness.Service([composer]).AskAsync(Ask());
        Assert.Equal(AiAskStatusV1.Refused, result.Status); Assert.Null(result.Response);
        Assert.Single(harness.Tool.Contexts); Assert.Single(composer.Requests);
    }

    [Fact]
    public async Task Mode_switch_on_the_same_service_carries_no_prior_ERP_sources_or_general_context_into_the_next_request()
    {
        var harness = new Harness();
        var erp = new ChatModeComposer(AiModelPlacementV1.OnPremises); var chat = new ChatModeComposer(AiModelPlacementV1.Cloud);
        var service = harness.Service([erp, chat]);
        var first = await service.AskAsync(Ask());
        var middle = await service.AskAsync(GeneralQuestion());
        var last = await service.AskAsync(Ask() with { Mode = AiAskModesV1.Erp });
        Assert.Equal(AiAskStatusV1.Answered, first.Status); Assert.Equal(AiAskStatusV1.Answered, middle.Status); Assert.Equal(AiAskStatusV1.Answered, last.Status);
        Assert.Equal(2, harness.Provider.Calls); Assert.Equal(2, harness.Tool.Contexts.Count);
        Assert.Empty(Assert.Single(chat.Requests).Sources); Assert.Equal(2, erp.Requests.Count);
        Assert.All(erp.Requests, request => Assert.Equal(AiTaskKindV1.Answer, request.TaskKind));
        Assert.All(erp.Requests, request => Assert.NotEmpty(request.Sources));
        Assert.Null(first.Response!.Mode); Assert.Null(last.Response!.Mode); Assert.Null(last.Response.Answer!.Grounded);
    }

    [Fact]
    public void Legacy_four_field_request_JSON_roundtrips_without_a_mode_or_new_response_fields()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacy = "{\"question\":\"SENTETIK\",\"toolId\":null,\"toolVersion\":null,\"arguments\":null}";
        var request = JsonSerializer.Deserialize<AiAskRequestV1>(legacy, options)!;
        Assert.Null(request.Mode); Assert.Equal(legacy, JsonSerializer.Serialize(request, options));
        var answer = new AiAskResponseV1(AiAskOutcomesV1.Answered, [], new("SENTETIK", [], "test", "test"));
        var json = JsonSerializer.Serialize(answer, options);
        Assert.DoesNotContain("\"mode\":", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("grounded", json, StringComparison.OrdinalIgnoreCase);
    }

    // Synthetic opt-in composer only: no actual provider/web connection and no invented ERP values.
    private sealed class ChatModeComposer(AiModelPlacementV1 placement) : IAiAnswerComposerV1
    {
        public string ProviderKind => AiProviderKindsV1.Fake; public AiModelPlacementV1 Placement => placement;
        public bool SupportsGeneralChat => true; public List<AiComposeRequestV1> Requests = [];
        public Action? BeforeAnswer;
        public async Task<AiComposedTextV1> ComposeAsync(AiComposeRequestV1 request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request); BeforeAnswer?.Invoke();
            return request.TaskKind == AiTaskKindV1.Converse ? new("SENTETIK ungrounded conversation fixture", [], 1)
                : await new FakeAiAnswerComposer(placement).ComposeAsync(request, cancellationToken);
        }
    }
}

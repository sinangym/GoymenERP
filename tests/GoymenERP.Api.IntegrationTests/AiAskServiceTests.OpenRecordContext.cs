using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Infrastructure.Ai;
using GoymenERP.Modules.Logistics.Application.Ai;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiAskServiceTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task Open_record_context_binds_real_owner_tool_descriptor_id_argument_source_and_citation(bool logistics, bool expectedVersion)
    {
        var rig = new ContextRig(logistics);
        AiModelRequestV1? planned = null;
        rig.Common.Provider.Plan = request =>
        {
            planned = request;
            return new([new(request.Hint!.ToolId, request.Hint.ToolVersion, request.Hint.ArgumentsJson)]);
        };
        var result = await rig.Service().AskAsync(rig.Ask(expectedVersion));
        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        var answer = Assert.IsType<AiAnswerV1>(result.Response!.Answer);
        var citation = Assert.Single(answer.Citations);
        Assert.Equal((rig.Kind, Target, ContextRig.Version), (citation.TargetKind, citation.TargetId, citation.SourceVersion));
        Assert.Equal(rig.Tool.Descriptor.ToolId, citation.ToolId);
        Assert.Equal(rig.Tool.Descriptor.ToolId, Assert.Single(planned!.AvailableTools).ToolId);
        using var arguments = JsonDocument.Parse(Assert.Single(rig.Tool.Inputs).ArgumentsJson);
        Assert.Equal(Target.ToString("D"), Assert.Single(arguments.RootElement.EnumerateObject()).Value.GetString());
        Assert.Equal(rig.IdField, Assert.Single(arguments.RootElement.EnumerateObject()).Name);
        Assert.Equal(Company, Assert.Single(rig.Tool.Contexts).CompanyId);
        Assert.Equal(InMemoryAiGatewayStore.LocalDeployment, answer.ModelDeployment);
        Assert.Equal(Company, Assert.Single(rig.Common.GatewayStore.Reservations).CompanyId);
        rig.Common.AssertEveryEventIsValid();
    }

    [Theory]
    [InlineData("kind")] [InlineData("id")] [InlineData("row-version")] [InlineData("digest-case")] [InlineData("blank-version")]
    public async Task Unknown_record_reference_or_non_owner_source_digest_is_invalid_before_planning(string fault)
    {
        var rig = new ContextRig(false); var context = rig.Context();
        context = fault switch
        {
            "kind" => context with { TargetKind = "counterparty.party" },
            "id" => context with { TargetId = Guid.Empty },
            "row-version" => context with { ExpectedSourceVersion = "4" },
            "digest-case" => context with { ExpectedSourceVersion = ContextRig.Version.ToUpperInvariant() },
            _ => context with { ExpectedSourceVersion = " " },
        };
        var result = await rig.Service().AskAsync(rig.Ask() with { OpenRecordContext = context });
        Assert.Equal(AiAskStatusV1.Invalid, result.Status);
        Assert.Equal([AiInvalidFieldsV1.OpenRecordContext], result.InvalidFields);
        Assert.Equal(0, rig.Common.Provider.Calls); Assert.Empty(rig.Tool.Inputs);
        Assert.Empty(rig.Common.GatewayStore.Reservations);
    }

    [Theory]
    [InlineData("tool")] [InlineData("version")] [InlineData("id")] [InlineData("extra")] [InlineData("duplicate")]
    public async Task Contradictory_client_hint_is_not_overwritten_or_used_as_a_record_authority(string fault)
    {
        var rig = new ContextRig(false); var hint = rig.Hint();
        var arguments = fault switch
        {
            "id" => JsonSerializer.Serialize(new { partyId = Guid.NewGuid() }),
            "extra" => JsonSerializer.Serialize(new { partyId = Target, companyId = Company }),
            "duplicate" => "{\"partyId\":\"" + Target + "\",\"partyId\":\"" + Target + "\"}",
            _ => hint.ArgumentsJson,
        };
        using var json = JsonDocument.Parse(arguments);
        var request = rig.Ask() with { ToolId = fault == "tool" ? "counterparty.party-contacts.v1" : hint.ToolId,
            ToolVersion = fault == "version" ? "2" : hint.ToolVersion, Arguments = json.RootElement.Clone() };
        Assert.Equal(AiAskStatusV1.Invalid, (await rig.Service().AskAsync(request)).Status);
        Assert.Equal(0, rig.Common.Provider.Calls); Assert.Empty(rig.Tool.Inputs);
    }

    [Theory]
    [InlineData("tool")] [InlineData("version")] [InlineData("id")] [InlineData("extra")] [InlineData("duplicate")]
    [InlineData("json")] [InlineData("zero")] [InlineData("mixed-plan")] [InlineData("byte-limit")]
    public async Task Entire_model_plan_must_match_context_before_any_owner_read(string fault)
    {
        var rig = new ContextRig(true);
        rig.Common.Provider.Plan = _ =>
        {
            var hint = rig.Hint();
            var arguments = fault switch
            {
                "id" => JsonSerializer.Serialize(new { instructionId = Guid.NewGuid() }),
                "extra" => JsonSerializer.Serialize(new { instructionId = Target, title = "SENTETIK untrusted" }),
                "duplicate" => "{\"instructionId\":\"" + Target + "\",\"instructionId\":\"" + Target + "\"}",
                "json" => "[]", "byte-limit" => new string(' ', 257) + hint.ArgumentsJson, _ => hint.ArgumentsJson,
            };
            var call = new AiPlannedToolCallV1(fault == "tool" ? CounterpartyPartySummaryAiTool.ToolId : hint.ToolId,
                fault == "version" ? "2" : hint.ToolVersion, arguments);
            return fault switch { "zero" => new([]), "mixed-plan" => new([new(hint.ToolId, hint.ToolVersion, hint.ArgumentsJson), call with { ToolId = CounterpartyPartySummaryAiTool.ToolId }]), _ => new([call]) };
        };
        Assert.Equal(AiAskStatusV1.Refused, (await rig.Service(options => options.MaxToolCallsPerRequest = 2).AskAsync(rig.Ask())).Status);
        Assert.Empty(rig.Tool.Inputs); Assert.Empty(rig.Common.GatewayStore.Reservations);
    }

    [Theory]
    [InlineData("kind")] [InlineData("id")] [InlineData("version")] [InlineData("no-source")]
    [InlineData("source-tool")] [InlineData("source-class")] [InlineData("malformed-version")]
    public async Task Owner_source_outside_the_reviewed_record_or_expected_digest_never_reaches_gateway(string fault)
    {
        var rig = new ContextRig(false); var source = rig.Tool.NextResult!.Source!;
        source = fault switch
        {
            "kind" => source with { TargetKind = LogisticsInstructionShipmentStatusAiTool.TargetKind },
            "id" => source with { TargetId = Guid.NewGuid() },
            "version" => source with { SourceVersion = "sha256:" + new string('b', 64) },
            "source-tool" => source with { ToolId = "foreign.tool.v1" },
            "source-class" => source with { DataClass = AiDataClassV1.Confidential },
            "malformed-version" => source with { SourceVersion = null! }, _ => source,
        };
        rig.Tool.NextResult = new(AiToolOutcomeV1.Succeeded, "{\"name\":\"SENTETIK\"}", fault == "no-source" ? null : source);
        var result = await rig.Service().AskAsync(rig.Ask(expectedVersion: true));
        Assert.Equal(AiAskStatusV1.ToolUnavailable, result.Status); Assert.Null(result.Response);
        Assert.Single(rig.Tool.Inputs); Assert.Empty(rig.Common.GatewayStore.Reservations);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task General_chat_cannot_carry_even_a_valid_ERP_record_context(bool logistics)
    {
        var rig = new ContextRig(logistics);
        var result = await rig.Service().AskAsync(rig.Ask() with { Mode = AiAskModesV1.GeneralChat });
        Assert.Equal(AiAskStatusV1.Invalid, result.Status); Assert.Equal([AiInvalidFieldsV1.OpenRecordContext], result.InvalidFields);
        Assert.Equal(0, rig.Common.Provider.Calls); Assert.Empty(rig.Tool.Inputs); Assert.Empty(rig.Common.GatewayStore.Reservations);
    }

    [Theory]
    [InlineData("panel")] [InlineData("owner")] [InlineData("pin")] [InlineData("allowlist")]
    [InlineData("limit")] [InlineData("session")]
    public async Task Context_never_creates_tool_permission_pin_data_or_session_access(string fault)
    {
        var rig = new ContextRig(false);
        if (fault is "panel" or "owner") rig.Authority.Revoke(fault == "panel" ? ErpPermissions.AiPanelUse : rig.Permission);
        var service = rig.Service(options =>
        {
            if (fault == "pin") options.AllowedTools[0].DescriptionSha256 = new string('0', 64);
            if (fault == "allowlist") options.AllowedTools.Clear();
            if (fault == "limit") options.MaxArgumentBytes = 8;
        }, missingSession: fault == "session");
        var result = await service.AskAsync(rig.Ask());
        Assert.Equal(fault == "limit" ? AiAskStatusV1.Invalid : AiAskStatusV1.Refused, result.Status);
        Assert.Empty(rig.Tool.Inputs); Assert.Empty(rig.Common.GatewayStore.Reservations);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Company_or_authority_loss_during_owner_read_releases_no_context_result(bool changeCompany)
    {
        var rig = new ContextRig(false);
        rig.Tool.OnRead = () => { if (changeCompany) rig.Common.CompanyScope.Set(OtherCompany); else rig.Authority.Revoke(rig.Permission); };
        var result = await rig.Service().AskAsync(rig.Ask());
        Assert.Equal(AiAskStatusV1.Refused, result.Status); Assert.Null(result.Response);
        Assert.Single(rig.Tool.Inputs); Assert.Empty(rig.Common.GatewayStore.Reservations);
    }

    [Fact]
    public async Task Company_change_during_planning_is_refused_before_the_bound_owner_read()
    {
        var rig = new ContextRig(false);
        rig.Common.Provider.Plan = request =>
        {
            rig.Common.CompanyScope.Set(OtherCompany);
            return new([new(request.Hint!.ToolId, request.Hint.ToolVersion, request.Hint.ArgumentsJson)]);
        };
        var result = await rig.Service().AskAsync(rig.Ask());
        Assert.Equal(AiAskStatusV1.Refused, result.Status); Assert.Null(result.Response);
        Assert.Empty(rig.Tool.Inputs); Assert.Empty(rig.Common.GatewayStore.Reservations);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Not_found_owner_has_no_grounding_answer_or_budget_reservation(bool logistics)
    {
        var rig = new ContextRig(logistics); rig.Tool.NextResult = new(AiToolOutcomeV1.NotFound);
        var result = await rig.Service().AskAsync(rig.Ask());
        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        Assert.Equal(AiAskOutcomesV1.CannotAnswer, result.Response!.Outcome); Assert.Null(result.Response.Answer);
        Assert.Null(Assert.Single(result.Response.ToolResults).Source); Assert.Empty(rig.Common.GatewayStore.Reservations);
    }

    [Theory]
    [InlineData("companyId")] [InlineData("actorId")] [InlineData("title")] [InlineData("text")] [InlineData("amount")]
    public void Typed_context_rejects_non_reference_fields_and_legacy_request_omits_the_new_field(string field)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["targetKind"] = CounterpartyPartySummaryAiTool.TargetKind,
            ["targetId"] = Target, [field] = "SENTETIK untrusted value" }, options);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AiOpenRecordContextV1>(json, options));
        Assert.DoesNotContain("openRecordContext", JsonSerializer.Serialize(Ask(), options));
    }

    [Theory]
    [InlineData("route")] [InlineData("budget")] [InlineData("feature")]
    public async Task Bound_record_context_keeps_existing_gateway_route_budget_and_feature_refusals(string boundary)
    {
        var rig = new ContextRig(false);
        rig.Common.GatewayStore.Policy = boundary switch
        {
            "route" => rig.Common.GatewayStore.Policy with { Routes = [] },
            "budget" => rig.Common.GatewayStore.Policy with { MonthlyUsageCeiling = null },
            _ => rig.Common.GatewayStore.Policy with { EnabledFeatures = new HashSet<AiFeatureV1>() },
        };
        var result = await rig.Service().AskAsync(rig.Ask());
        Assert.Equal(boundary switch { "route" => AiAskStatusV1.Refused, "budget" => AiAskStatusV1.BudgetExhausted, _ => AiAskStatusV1.FeatureDisabled }, result.Status);
        Assert.Null(result.Response); Assert.Single(rig.Tool.Inputs);
    }

    [Fact]
    public async Task Wrong_citation_cannot_turn_a_matching_record_source_into_a_grounded_answer()
    {
        var rig = new ContextRig(false);
        var result = await rig.Service(composers: [new ContextWrongCitationComposer()]).AskAsync(rig.Ask());
        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        Assert.Equal(AiAskOutcomesV1.CannotAnswer, result.Response!.Outcome); Assert.Null(result.Response.Answer);
        Assert.Equal(rig.Kind, Assert.Single(result.Response.ToolResults).Source!.TargetKind);
    }

    // Reuse the existing Ask harness, provider, fake owner seam, authority and real gateway test composition.
    // Only the descriptor/context differs; no second runtime or provider is introduced.
    private sealed class ContextRig
    {
        public static readonly string Version = "sha256:" + new string('a', 64);
        public Harness Common { get; } = new();
        public FakeReadTool Tool { get; }
        public FakeAuthority Authority { get; }
        public string Kind { get; }
        public string IdField { get; }
        public string Permission => Assert.Single(Tool.Descriptor.RequiredPermissions);
        public ContextRig(bool logistics)
        {
            var descriptor = logistics
                ? new LogisticsInstructionShipmentStatusAiTool(null!, TimeProvider.System).Descriptor
                : new CounterpartyPartySummaryAiTool(null!, TimeProvider.System).Descriptor;
            Kind = logistics ? LogisticsInstructionShipmentStatusAiTool.TargetKind : CounterpartyPartySummaryAiTool.TargetKind;
            IdField = logistics ? LogisticsInstructionShipmentStatusAiTool.InstructionIdField : CounterpartyPartySummaryAiTool.PartyIdField;
            Tool = new FakeReadTool(descriptor) { NextResult = new(AiToolOutcomeV1.Succeeded, "{\"name\":\"SENTETIK kayıt\"}",
                new AiSourceV1(descriptor.ToolId, descriptor.Version, Kind, Target, "SENTETIK kaynak", Version,
                    new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero), AiDataClassV1.Internal)) };
            Authority = new(ErpPermissions.AiPanelUse, Permission);
        }
        public AiOpenRecordContextV1 Context() => new(Kind, Target);
        public AiToolHintV1 Hint() => new(Tool.Descriptor.ToolId, Tool.Descriptor.Version,
            JsonSerializer.Serialize(new Dictionary<string, string> { [IdField] = Target.ToString("D") }));
        public AiAskRequestV1 Ask(bool expectedVersion = false) => new("SENTETIK kayıt özeti nedir?", null, null, null,
            OpenRecordContext: Context() with { ExpectedSourceVersion = expectedVersion ? Version : null });
        public AiAskService Service(Action<AiRuntimeOptions>? configure = null, bool missingSession = false, IEnumerable<IAiAnswerComposerV1>? composers = null)
        {
            var options = Harness.ReadyOptions();
            options.AllowedTools = [new() { ToolId = Tool.Descriptor.ToolId, Version = Tool.Descriptor.Version,
                DescriptionSha256 = AiToolDescriptionPinV1.Compute(Tool.Descriptor) }];
            configure?.Invoke(options);
            return new(Options.Create(options), new TestHostEnvironment("Development"), Common.Provider,
                new AiToolCatalog([Tool], Options.Create(options)), new TestUser(Actor, Company), Common.CompanyScope, Authority,
                missingSession ? new ContextMissingSession() : new FixedSessionResolver(), Common.Correlation, Common.Writer,
                TimeProvider.System, AiGatewayTestDoubles.Gateway(options, Common.GatewayStore, composers));
        }
    }
    private sealed class ContextWrongCitationComposer : IAiAnswerComposerV1
    {
        public string ProviderKind => AiProviderKindsV1.Fake;
        public AiModelPlacementV1 Placement => AiModelPlacementV1.OnPremises;
        public Task<AiComposedTextV1> ComposeAsync(AiComposeRequestV1 request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AiComposedTextV1("SENTETIK unsupported source", [2], 1));
    }
    private sealed class ContextMissingSession : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
}

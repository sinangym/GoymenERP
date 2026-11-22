using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.External;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AI-3 READ runtime over synthetic owners (no database, no network). Pins the ERP-000J §8 order and the Group E
/// security events: disabled leaves the ERP alone, no company context never reaches the provider, an undefined tool
/// is refused, tool arguments never carry the company, Started is fail-closed and every refusal is an event. AI-D9
/// pins the shipped starting limits and their fail-closed reading; ASSUMPTION-AI-POLICYREF-1 pins the policy reference
/// every Group E event carries.
/// </summary>
public sealed class AiAskServiceTests
{
    private const string ToolId = "test.item-summary.v1";
    private const string ToolVersion = "1";
    private const string ToolPermission = "test.items.read";
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid Actor = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Target = Guid.Parse("cccccccc-0000-4000-8000-000000000003");

    [Fact]
    public async Task A_disabled_runtime_answers_disabled_never_calls_the_provider_and_records_the_refusal()
    {
        var harness = new Harness(options => options.Enabled = false);

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Disabled, result.Status);
        Assert.Null(result.Response);
        Assert.Equal(0, harness.Provider.Calls);
        Assert.Empty(harness.Tool.Contexts);
        var refusal = Assert.Single(harness.Writer.Events);
        Assert.Equal(SecurityEventTypesV1.AiRequestRefused, refusal.EventType);
        Assert.Equal(SecurityEventReasonCodesV1.PolicyRefused, refusal.ReasonCode);
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task Without_a_server_company_context_the_provider_is_never_called_and_the_refusal_is_platform_scoped()
    {
        var harness = new Harness();
        harness.CompanyScope = new CompanyContext();

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        Assert.Equal(0, harness.Provider.Calls);
        var refusal = Assert.Single(harness.Writer.Events);
        Assert.Equal(SecurityEventTypesV1.AiRequestRefused, refusal.EventType);
        Assert.Equal(SecurityEventReasonCodesV1.CompanyContextMismatch, refusal.ReasonCode);
        Assert.Equal(SecurityEventScopeKindsV1.Platform, refusal.ScopeKind);
        Assert.Null(refusal.CompanyId);
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task A_company_the_actor_cannot_access_is_refused_before_the_provider()
    {
        var harness = new Harness();
        var inaccessibleScope = new CompanyContext();
        inaccessibleScope.Set(OtherCompany);
        harness.CompanyScope = inaccessibleScope;
        Assert.Equal(OtherCompany, harness.CompanyScope.CompanyId);

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        Assert.Equal(0, harness.Provider.Calls);
        Assert.Equal(SecurityEventReasonCodesV1.CompanyContextMismatch, Assert.Single(harness.Writer.Events).ReasonCode);
    }

    [Fact]
    public async Task A_missing_panel_capability_records_ai_authz_denied_and_never_calls_the_provider()
    {
        var harness = new Harness();
        harness.Authority.Revoke(ErpPermissions.AiPanelUse);

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        Assert.Equal(0, harness.Provider.Calls);
        var denied = Assert.Single(harness.Writer.Events);
        Assert.Equal(SecurityEventTypesV1.AiAuthorizationDenied, denied.EventType);
        Assert.Equal(SecurityEventReasonCodesV1.PermissionMissing, denied.ReasonCode);
        Assert.Equal(ErpPermissions.AiPanelUse, denied.PermissionCode);
        Assert.Equal(Company, denied.CompanyId);
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task An_undefined_tool_is_refused_with_ai_tool_not_allowed_and_no_tool_runs()
    {
        var harness = new Harness();

        var result = await harness.Service().AskAsync(Ask(toolId: "test.unknown-tool.v1"));

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        Assert.Empty(harness.Tool.Contexts);
        var refusal = Assert.Single(harness.Writer.Events);
        Assert.Equal(SecurityEventReasonCodesV1.AiToolNotAllowed, refusal.ReasonCode);
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task A_registered_tool_that_configuration_does_not_allowlist_is_refused()
    {
        var harness = new Harness(options => options.AllowedTools = []);

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        Assert.Empty(harness.Tool.Contexts);
        Assert.Equal(SecurityEventReasonCodesV1.AiToolNotAllowed, Assert.Single(harness.Writer.Events).ReasonCode);
    }

    [Fact]
    public async Task An_allowed_read_records_started_then_completed_and_the_tool_receives_the_server_company()
    {
        var harness = new Harness();

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        var toolResult = Assert.Single(result.Response!.ToolResults);
        Assert.Equal(AiToolResultOutcomesV1.Succeeded, toolResult.Outcome);
        Assert.Equal(ToolId, toolResult.ToolId);
        Assert.Equal(Target, toolResult.Source!.TargetId);
        var context = Assert.Single(harness.Tool.Contexts);
        Assert.Equal(Company, context.CompanyId);
        Assert.Equal(Actor.ToString("D"), context.ActorId);
        Assert.Equal("{\"itemId\":\"SENTETIK-1\"}", Assert.Single(harness.Tool.Inputs).ArgumentsJson);
        Assert.Collection(
            harness.Writer.Events,
            started =>
            {
                Assert.Equal(SecurityEventTypesV1.AiToolCalled, started.EventType);
                Assert.Equal(SecurityEventPhasesV1.Started, started.Phase);
                Assert.Equal(ToolId, started.AiToolId);
                Assert.Equal(AiToolRiskClassV1.Read, started.AiRiskClass);
            },
            completed =>
            {
                Assert.Equal(SecurityEventTypesV1.AiToolCalled, completed.EventType);
                Assert.Equal(SecurityEventPhasesV1.Completed, completed.Phase);
                Assert.Equal(SecurityEventOutcomesV1.Allowed, completed.Outcome);
            });
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task A_company_argument_is_refused_before_the_tool_runs()
    {
        var harness = new Harness(allowlist: ["itemId", "companyId"]);

        var result = await harness.Service().AskAsync(
            Ask(arguments: $"{{\"itemId\":\"SENTETIK-1\",\"companyId\":\"{OtherCompany:D}\"}}"));

        Assert.Equal(AiAskStatusV1.Invalid, result.Status);
        Assert.Equal(AiInvalidFieldsV1.Arguments, Assert.Single(result.InvalidFields));
        Assert.Empty(harness.Tool.Contexts);
        Assert.Equal(SecurityEventReasonCodesV1.PolicyRefused, Assert.Single(harness.Writer.Events).ReasonCode);
        harness.AssertEveryEventIsValid();
    }

    [Theory]
    [InlineData("{\"unlisted\":\"SENTETIK\"}")]
    [InlineData("{\"itemId\":\"SENTETIK-1\",\"itemId\":\"SENTETIK-2\"}")]
    [InlineData("{\"itemId\":\"SENTETIK-WAY-TOO-LONG-FOR-THE-CONFIGURED-ARGUMENT-BUDGET\"}")]
    public async Task Arguments_outside_the_owner_allowlist_duplicated_or_over_budget_are_refused(string arguments)
    {
        var harness = new Harness(options => options.MaxArgumentBytes = 48);

        var result = await harness.Service().AskAsync(Ask(arguments: arguments));

        Assert.Equal(AiAskStatusV1.Invalid, result.Status);
        Assert.Empty(harness.Tool.Contexts);
        Assert.Single(harness.Writer.Events);
    }

    [Fact]
    public async Task A_missing_tool_permission_records_ai_authz_denied_with_the_tool_fields()
    {
        var harness = new Harness();
        harness.Authority.Revoke(ToolPermission);

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        Assert.Empty(harness.Tool.Contexts);
        var denied = Assert.Single(harness.Writer.Events);
        Assert.Equal(SecurityEventTypesV1.AiAuthorizationDenied, denied.EventType);
        Assert.Equal(ToolPermission, denied.PermissionCode);
        Assert.Equal(ToolId, denied.AiToolId);
        Assert.Equal(AiDataClassV1.Internal, denied.AiDataClass);
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task A_tool_above_the_configured_data_class_ceiling_is_refused()
    {
        var harness = new Harness(options => options.MaxDataClass = AiDataClassV1.Public);

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        Assert.Empty(harness.Tool.Contexts);
        Assert.Equal(SecurityEventReasonCodesV1.AiDataClassNotAllowed, Assert.Single(harness.Writer.Events).ReasonCode);
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task When_the_started_event_cannot_be_recorded_the_tool_does_not_run()
    {
        var harness = new Harness();
        harness.Writer.FailStarted = true;

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Unavailable, result.Status);
        Assert.Empty(harness.Tool.Contexts);
    }

    [Fact]
    public async Task Authority_revoked_while_the_tool_runs_releases_nothing()
    {
        var harness = new Harness();
        harness.Tool.OnRead = () => harness.Authority.Revoke(ErpPermissions.AiPanelUse);

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        Assert.Null(result.Response);
        Assert.Single(harness.Tool.Contexts);
        Assert.Equal(SecurityEventTypesV1.AiAuthorizationDenied, harness.Writer.Events[^1].EventType);
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task A_failing_owner_tool_is_completed_as_failed_and_nothing_is_released()
    {
        var harness = new Harness();
        harness.Tool.NextResult = new AiReadResultV1(AiToolOutcomeV1.Unavailable);

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.ToolUnavailable, result.Status);
        var completed = harness.Writer.Events[^1];
        Assert.Equal(SecurityEventPhasesV1.Completed, completed.Phase);
        Assert.Equal(SecurityEventOutcomesV1.Failed, completed.Outcome);
        Assert.Equal(SecurityEventReasonCodesV1.OperationFailed, completed.ReasonCode);
        harness.AssertEveryEventIsValid();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_question_is_invalid_and_the_provider_is_not_called(string question)
    {
        var harness = new Harness();

        var result = await harness.Service().AskAsync(Ask(question: question));

        Assert.Equal(AiAskStatusV1.Invalid, result.Status);
        Assert.Equal(AiInvalidFieldsV1.Question, Assert.Single(result.InvalidFields));
        Assert.Equal(0, harness.Provider.Calls);
    }

    [Fact]
    public async Task A_question_over_the_configured_length_is_invalid()
    {
        var harness = new Harness(options => options.MaxQuestionLength = 4);

        var result = await harness.Service().AskAsync(Ask(question: "SENTETIK"));

        Assert.Equal(AiAskStatusV1.Invalid, result.Status);
        Assert.Equal(0, harness.Provider.Calls);
    }

    [Fact]
    public async Task A_missing_limit_while_enabled_keeps_the_runtime_unavailable()
    {
        var harness = new Harness(options => options.MaxToolCallsPerRequest = null);

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Unavailable, result.Status);
        Assert.Equal(0, harness.Provider.Calls);
    }

    [Theory]
    [InlineData("External", "Development")]
    [InlineData("Fake", "Production")]
    public void An_external_provider_or_the_fake_provider_in_production_is_refused(string provider, string environment)
    {
        var options = Harness.ReadyOptions();
        options.Provider = provider;

        var state = AiRuntimeStateEvaluator.Evaluate(options, new TestHostEnvironment(environment), new FakeAiModelProvider());

        Assert.Equal(AiRuntimeAvailability.ProviderRefused, state.Availability);
        Assert.False(state.IsReady);
    }

    [Fact]
    public async Task An_external_egress_refusal_is_refused_with_policy_refused_and_no_tool_runs()
    {
        var harness = new Harness();
        harness.Provider.Failure = () => new AiExternalEgressRefusedException();

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        Assert.Null(result.Response);
        Assert.Equal(1, harness.Provider.Calls);
        Assert.Empty(harness.Tool.Contexts);
        var refusal = Assert.Single(harness.Writer.Events);
        Assert.Equal(SecurityEventTypesV1.AiRequestRefused, refusal.EventType);
        Assert.Equal(SecurityEventReasonCodesV1.PolicyRefused, refusal.ReasonCode);
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task An_external_provider_failure_is_unavailable_with_provider_failed_and_no_tool_runs()
    {
        var harness = new Harness();
        harness.Provider.Failure = () => new AiExternalProviderException(AiExternalProviderFailuresV1.Timeout);

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Unavailable, result.Status);
        Assert.Empty(harness.Tool.Contexts);
        var refusal = Assert.Single(harness.Writer.Events);
        Assert.Equal(SecurityEventReasonCodesV1.AiProviderFailed, refusal.ReasonCode);
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public void The_runtime_is_disabled_when_no_configuration_enables_it()
    {
        var state = AiRuntimeStateEvaluator.Evaluate(new AiRuntimeOptions(), new TestHostEnvironment("Development"), new FakeAiModelProvider());

        Assert.Equal(AiRuntimeAvailability.Disabled, state.Availability);
    }

    [Fact]
    public void The_catalog_admits_only_allowlisted_read_tools_and_drops_duplicated_ids()
    {
        var read = new FakeReadTool(Descriptor(ToolId, AiToolRiskClassV1.Read, ["itemId"]));
        var duplicateA = new FakeReadTool(Descriptor("test.duplicate.v1", AiToolRiskClassV1.Read, ["itemId"]));
        var duplicateB = new FakeReadTool(Descriptor("test.duplicate.v1", AiToolRiskClassV1.Read, ["itemId"]));
        var draft = new FakeReadTool(Descriptor("test.draft-tool.v1", AiToolRiskClassV1.Draft, ["itemId"]));
        var notAllowlisted = new FakeReadTool(Descriptor("test.not-listed.v1", AiToolRiskClassV1.Read, ["itemId"]));
        var options = Harness.ReadyOptions();
        options.AllowedTools =
        [
            new AiAllowedToolOptions { ToolId = ToolId, Version = ToolVersion, DescriptionSha256 = AiToolDescriptionPinV1.Compute(read.Descriptor) },
            new AiAllowedToolOptions { ToolId = "test.duplicate.v1", Version = ToolVersion, DescriptionSha256 = AiToolDescriptionPinV1.Compute(duplicateA.Descriptor) },
            new AiAllowedToolOptions { ToolId = "test.draft-tool.v1", Version = ToolVersion, DescriptionSha256 = AiToolDescriptionPinV1.Compute(draft.Descriptor) },
        ];

        var catalog = new AiToolCatalog([read, duplicateA, duplicateB, draft, notAllowlisted], Options.Create(options));

        var admitted = Assert.Single(catalog.Descriptors);
        Assert.Equal(ToolId, admitted.ToolId);
        Assert.Null(catalog.Find("test.duplicate.v1", ToolVersion));
        Assert.Null(catalog.Find("test.draft-tool.v1", ToolVersion));
        Assert.Null(catalog.Find(ToolId, "2"));
    }

    [Fact]
    public async Task Capabilities_report_disabled_without_tools_while_the_runtime_is_off()
    {
        var harness = new Harness(options => options.Enabled = false);

        var capabilities = await harness.CapabilityService().GetAsync();

        Assert.Equal(AiAvailabilityValuesV1.Disabled, capabilities.Availability);
        Assert.False(capabilities.PanelAvailable);
        Assert.Empty(capabilities.Tools);
    }

    [Fact]
    public async Task Capabilities_list_only_tools_whose_permissions_the_actor_holds()
    {
        var harness = new Harness();

        var granted = await harness.CapabilityService().GetAsync();
        harness.Authority.Revoke(ToolPermission);
        var withoutToolPermission = await harness.CapabilityService().GetAsync();
        harness.Authority.Revoke(ErpPermissions.AiPanelUse);
        var withoutPanel = await harness.CapabilityService().GetAsync();

        Assert.True(granted.PanelAvailable);
        Assert.Equal(ToolId, Assert.Single(granted.Tools).ToolId);
        Assert.True(withoutToolPermission.PanelAvailable);
        Assert.Empty(withoutToolPermission.Tools);
        Assert.False(withoutPanel.PanelAvailable);
        Assert.Empty(harness.Writer.Events);
        Assert.Equal(0, harness.Provider.Calls);
    }

    [Fact]
    public void The_shipped_configuration_carries_the_ai_d9_starting_limits_and_keeps_the_runtime_off()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "backend", "GoymenERP.Api", "appsettings.json");
        var shipped = new ConfigurationBuilder().AddJsonFile(path, optional: false, reloadOnChange: false).Build()
            .GetSection(AiRuntimeOptions.SectionName).Get<AiRuntimeOptions>();

        Assert.NotNull(shipped);
        Assert.False(shipped.Enabled);
        Assert.Equal(2000, shipped.MaxQuestionLength);
        Assert.Equal(5, shipped.MaxToolCallsPerRequest);
        Assert.Equal(16384, shipped.MaxArgumentBytes);
        Assert.Equal(AiDataClassV1.Internal, shipped.MaxDataClass);
        Assert.Empty(shipped.AllowedTools);
        // No AI policy is published in the repository: the events say default-deny and nothing runs (AI-D6).
        Assert.Null(shipped.PolicyRef);
        Assert.Null(shipped.PolicyVersion);
        Assert.Equal(AiRuntimeAvailability.Disabled,
            AiRuntimeStateEvaluator.Evaluate(shipped, new TestHostEnvironment("Development"), new FakeAiModelProvider()).Availability);

        // Turned on in development with a published policy, the shipped limits are the ones the runtime enforces.
        shipped.Enabled = true;
        shipped.Provider = AiProviderKindsV1.Fake;
        var unpublished = AiRuntimeStateEvaluator.Evaluate(shipped, new TestHostEnvironment("Development"), new FakeAiModelProvider());
        shipped.PolicyRef = "TEST-AI-POLICY";
        shipped.PolicyVersion = 1;
        var ready = AiRuntimeStateEvaluator.Evaluate(shipped, new TestHostEnvironment("Development"), new FakeAiModelProvider());

        Assert.Equal(AiRuntimeAvailability.Misconfigured, unpublished.Availability);
        Assert.True(ready.IsReady);
        Assert.Equal(new AiRuntimeLimits(2000, 16384, 5, AiDataClassV1.Internal), ready.Limits);
    }

    [Fact]
    public async Task A_question_at_the_decided_length_is_asked_and_one_character_more_is_refused()
    {
        var harness = new Harness(UseDecidedLimits);

        var atLimit = await harness.Service().AskAsync(Ask(question: new string('S', 2000)));
        var overLimit = await harness.Service().AskAsync(Ask(question: new string('S', 2001)));

        Assert.Equal(AiAskStatusV1.Answered, atLimit.Status);
        Assert.Equal(AiAskStatusV1.Invalid, overLimit.Status);
        Assert.Equal(AiInvalidFieldsV1.Question, Assert.Single(overLimit.InvalidFields));
        Assert.Equal(1, harness.Provider.Calls);
        Assert.Equal(SecurityEventReasonCodesV1.PolicyRefused, harness.Writer.Events[^1].ReasonCode);
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task Arguments_at_the_decided_byte_budget_run_and_one_byte_more_is_refused()
    {
        const string prefix = "{\"itemId\":\"";
        const string suffix = "\"}";
        var fill = 16384 - prefix.Length - suffix.Length;
        var harness = new Harness(UseDecidedLimits);

        var atBudget = await harness.Service().AskAsync(Ask(arguments: prefix + new string('S', fill) + suffix));
        var overBudget = await harness.Service().AskAsync(Ask(arguments: prefix + new string('S', fill + 1) + suffix));

        Assert.Equal(AiAskStatusV1.Answered, atBudget.Status);
        Assert.Equal(AiAskStatusV1.Invalid, overBudget.Status);
        Assert.Equal(AiInvalidFieldsV1.Arguments, Assert.Single(overBudget.InvalidFields));
        Assert.Single(harness.Tool.Contexts);
        harness.AssertEveryEventIsValid();
    }

    [Theory]
    [InlineData(5, AiAskStatusV1.Answered)]
    [InlineData(6, AiAskStatusV1.Refused)]
    public async Task A_plan_within_the_decided_tool_call_ceiling_runs_and_one_call_more_runs_nothing(int calls, AiAskStatusV1 expected)
    {
        var harness = new Harness(UseDecidedLimits);
        harness.Provider.Plan = _ => new AiModelPlanV1(
            [.. Enumerable.Repeat(new AiPlannedToolCallV1(ToolId, ToolVersion, "{\"itemId\":\"SENTETIK-1\"}"), calls)]);

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(expected, result.Status);
        if (expected == AiAskStatusV1.Answered)
        {
            Assert.Equal(calls, result.Response!.ToolResults.Count);
            Assert.Equal(calls, harness.Tool.Contexts.Count);
        }
        else
        {
            Assert.Empty(harness.Tool.Contexts);
            Assert.Equal(SecurityEventReasonCodesV1.PolicyRefused, Assert.Single(harness.Writer.Events).ReasonCode);
        }

        harness.AssertEveryEventIsValid();
    }

    [Theory]
    [InlineData("MaxDataClass", "Gizli")]
    [InlineData("MaxDataClass", "9")]
    [InlineData("MaxQuestionLength", "iki bin")]
    [InlineData("MaxQuestionLength", "0")]
    [InlineData("MaxToolCallsPerRequest", "-1")]
    [InlineData("MaxArgumentBytes", "")]
    [InlineData("PolicyVersion", "birinci")]
    public void A_configured_value_that_is_missing_or_cannot_be_read_keeps_the_runtime_unavailable_without_throwing(string key, string value)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Ai:Enabled"] = "true",
            ["Ai:Provider"] = AiProviderKindsV1.Fake,
            ["Ai:PolicyRef"] = "TEST-AI-POLICY",
            ["Ai:PolicyVersion"] = "1",
            ["Ai:MaxQuestionLength"] = "2000",
            ["Ai:MaxToolCallsPerRequest"] = "5",
            ["Ai:MaxArgumentBytes"] = "16384",
            ["Ai:MaxDataClass"] = "Internal",
            ["Ai:AllowedTools:0:ToolId"] = ToolId,
            ["Ai:AllowedTools:0:Version"] = ToolVersion,
            ["Ai:AllowedTools:0:DescriptionSha256"] = AiToolDescriptionPinV1.Compute(Descriptor(ToolId, AiToolRiskClassV1.Read, ["itemId"])),
        };
        var valid = BoundOptions(settings);
        settings[$"Ai:{key}"] = value;
        var broken = BoundOptions(settings);
        var tool = new FakeReadTool(Descriptor(ToolId, AiToolRiskClassV1.Read, ["itemId"]));

        var validState = AiRuntimeStateEvaluator.Evaluate(valid, new TestHostEnvironment("Development"), new FakeAiModelProvider());
        var brokenState = AiRuntimeStateEvaluator.Evaluate(broken, new TestHostEnvironment("Development"), new FakeAiModelProvider());

        Assert.True(validState.IsReady);
        Assert.Equal(AiRuntimeAvailability.Misconfigured, brokenState.Availability);
        Assert.Null(brokenState.Limits);
        Assert.Single(new AiToolCatalog([tool], valid).Descriptors);
        // An unreadable section admits no tool; a readable one with an invalid value is refused by the state alone.
        if (!AiRuntimeOptionsReader.TryRead(broken, out _))
        {
            Assert.Empty(new AiToolCatalog([tool], broken).Descriptors);
        }
    }

    [Fact]
    public void The_policy_reference_contract_v1_is_pinned()
    {
        Assert.Equal("default-deny", AiPolicyReferenceV1.DefaultDenyPolicyId);
        Assert.Equal(1, AiPolicyReferenceV1.DefaultDenyPolicyVersion);
        Assert.Equal(new AiPolicyReference("default-deny", 1), AiPolicyReferenceV1.Resolve(null));
        Assert.Equal(new AiPolicyReference("default-deny", 1), AiPolicyReferenceV1.Resolve(new AiRuntimeOptions()));
        Assert.Equal(new AiPolicyReference("TEST-AI-POLICY", 3), AiPolicyReferenceV1.Resolve(Harness.ReadyOptions()));
    }

    [Fact]
    public async Task Every_ai_event_carries_the_published_policy_version()
    {
        var answered = new Harness();
        var denied = new Harness();
        denied.Authority.Revoke(ToolPermission);

        await answered.Service().AskAsync(Ask());
        await denied.Service().AskAsync(Ask());

        Assert.Equal(2, answered.Writer.Events.Count);
        Assert.Single(denied.Writer.Events);
        foreach (var securityEvent in answered.Writer.Events.Concat(denied.Writer.Events))
        {
            Assert.Equal("TEST-AI-POLICY", securityEvent.PolicyId);
            Assert.Equal(3, securityEvent.PolicyVersion);
        }

        answered.AssertEveryEventIsValid();
        denied.AssertEveryEventIsValid();
    }

    [Theory]
    [InlineData(false, "TEST-AI-POLICY", 3, AiAskStatusV1.Disabled)]
    [InlineData(false, null, null, AiAskStatusV1.Disabled)]
    [InlineData(true, null, null, AiAskStatusV1.Unavailable)]
    [InlineData(true, "TEST-AI-POLICY", null, AiAskStatusV1.Unavailable)]
    [InlineData(true, "TEST-AI-POLICY", 0, AiAskStatusV1.Unavailable)]
    [InlineData(true, "TEST AI POLICY", 3, AiAskStatusV1.Unavailable)]
    [InlineData(true, "default-deny", 3, AiAskStatusV1.Unavailable)]
    public async Task Without_a_published_policy_the_refusal_says_default_deny_and_the_runtime_stays_closed(
        bool enabled, string? policyRef, int? policyVersion, AiAskStatusV1 expected)
    {
        var harness = new Harness(options =>
        {
            options.Enabled = enabled;
            options.PolicyRef = policyRef;
            options.PolicyVersion = policyVersion;
        });
        var published = enabled is false && policyRef is not null;

        var result = await harness.Service().AskAsync(Ask());

        Assert.Equal(expected, result.Status);
        Assert.Equal(0, harness.Provider.Calls);
        var refusal = Assert.Single(harness.Writer.Events);
        Assert.Equal(SecurityEventTypesV1.AiRequestRefused, refusal.EventType);
        Assert.Equal(published ? policyRef : AiPolicyReferenceV1.DefaultDenyPolicyId, refusal.PolicyId);
        Assert.Equal(published ? policyVersion : AiPolicyReferenceV1.DefaultDenyPolicyVersion, refusal.PolicyVersion);
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task An_unreadable_configuration_section_refuses_under_default_deny_and_never_throws()
    {
        var options = BoundOptions(new Dictionary<string, string?>
        {
            ["Ai:Enabled"] = "true",
            ["Ai:Provider"] = AiProviderKindsV1.Fake,
            ["Ai:PolicyRef"] = "TEST-AI-POLICY",
            ["Ai:PolicyVersion"] = "1",
            ["Ai:MaxDataClass"] = "Gizli",
        });
        var harness = new Harness();
        var correlation = new CorrelationContext();
        correlation.Set("corr-ai-sentetik", null, SourceChannels.Web, null, null);
        var companyScope = new CompanyContext();
        companyScope.Set(Company);
        var service = new AiAskService(options, new TestHostEnvironment("Development"), harness.Provider,
            new AiToolCatalog([harness.Tool], options), new TestUser(Actor, Company), companyScope, harness.Authority,
            new FixedSessionResolver(), correlation, harness.Writer, TimeProvider.System);

        var result = await service.AskAsync(Ask());

        Assert.Equal(AiAskStatusV1.Unavailable, result.Status);
        Assert.Equal(0, harness.Provider.Calls);
        var refusal = Assert.Single(harness.Writer.Events);
        Assert.Equal(AiPolicyReferenceV1.DefaultDenyPolicyId, refusal.PolicyId);
        Assert.Equal(AiPolicyReferenceV1.DefaultDenyPolicyVersion, refusal.PolicyVersion);
        harness.AssertEveryEventIsValid();
    }

    private static void UseDecidedLimits(AiRuntimeOptions options)
    {
        options.MaxQuestionLength = 2000;
        options.MaxToolCallsPerRequest = 5;
        options.MaxArgumentBytes = 16384;
        options.MaxDataClass = AiDataClassV1.Internal;
    }

    private static IOptions<AiRuntimeOptions> BoundOptions(IReadOnlyDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddOptions<AiRuntimeOptions>().Bind(configuration.GetSection(AiRuntimeOptions.SectionName));
        return services.BuildServiceProvider().GetRequiredService<IOptions<AiRuntimeOptions>>();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static AiAskRequestV1 Ask(
        string question = "SENTETIK kalem özeti nedir?",
        string? toolId = ToolId,
        string arguments = "{\"itemId\":\"SENTETIK-1\"}")
    {
        using var document = JsonDocument.Parse(arguments);
        return new AiAskRequestV1(question, toolId, ToolVersion, document.RootElement.Clone());
    }

    private static AiToolDescriptorV1 Descriptor(string toolId, AiToolRiskClassV1 risk, IEnumerable<string> allowlist) =>
        new(toolId, ToolVersion, "Test", risk, AiDataClassV1.Internal, [ToolPermission], allowlist, null,
            new AiContractRevisionV1("in-1", "out-1", "field-1", "limit-1", "catalogue-1", "authz-1", "source-1", "redaction-1"));

    private sealed class Harness
    {
        private readonly AiRuntimeOptions options;

        public Harness(Action<AiRuntimeOptions>? configure = null, IEnumerable<string>? allowlist = null)
        {
            options = ReadyOptions();
            configure?.Invoke(options);
            Tool = new FakeReadTool(Descriptor(ToolId, AiToolRiskClassV1.Read, allowlist ?? ["itemId"]));
            // WP-A6: the harness approves the pin of the card it registers, unless a test configured its own pin.
            foreach (var entry in options.AllowedTools.Where(entry => entry.ToolId == ToolId && entry.Version == ToolVersion
                         && entry.DescriptionSha256 is null))
            {
                entry.DescriptionSha256 = AiToolDescriptionPinV1.Compute(Tool.Descriptor);
            }

            CompanyScope.Set(Company);
            Correlation.Set("corr-ai-sentetik", null, SourceChannels.Web, null, null);
        }

        public CountingProvider Provider { get; } = new();
        public FakeReadTool Tool { get; }
        public RecordingWriter Writer { get; } = new();
        public FakeAuthority Authority { get; } = new(ErpPermissions.AiPanelUse, ToolPermission);
        public ICompanyContext CompanyScope { get; set; } = new CompanyContext();
        public CorrelationContext Correlation { get; } = new();

        public static AiRuntimeOptions ReadyOptions() => new()
        {
            Enabled = true,
            Provider = AiProviderKindsV1.Fake,
            PolicyRef = "TEST-AI-POLICY",
            PolicyVersion = 3,
            MaxQuestionLength = 200,
            MaxArgumentBytes = 256,
            MaxToolCallsPerRequest = 1,
            MaxDataClass = AiDataClassV1.Internal,
            AllowedTools = [new AiAllowedToolOptions { ToolId = ToolId, Version = ToolVersion }],
        };

        public AiAskService Service() => new(
            Options.Create(options),
            new TestHostEnvironment("Development"),
            Provider,
            new AiToolCatalog([Tool], Options.Create(options)),
            new TestUser(Actor, Company),
            CompanyScope,
            Authority,
            new FixedSessionResolver(),
            Correlation,
            Writer,
            TimeProvider.System);

        public AiCapabilityService CapabilityService() => new(
            Options.Create(options),
            new TestHostEnvironment("Development"),
            Provider,
            new AiToolCatalog([Tool], Options.Create(options)),
            new TestUser(Actor, Company),
            CompanyScope,
            Authority);

        public void AssertEveryEventIsValid()
        {
            foreach (var securityEvent in Writer.Events)
            {
                Assert.Empty(SecurityEventV1Validator.Validate(securityEvent));
                Assert.Equal(SourceChannels.AiAssisted, securityEvent.SourceChannel);
            }
        }
    }

    private sealed class CountingProvider : IAiModelProviderV1
    {
        private readonly FakeAiModelProvider inner = new();

        public int Calls { get; private set; }

        /// <summary>Replaces the hinted plan, standing in for untrusted model output.</summary>
        public Func<AiModelRequestV1, AiModelPlanV1>? Plan { get; set; }

        public string ProviderKind => inner.ProviderKind;

        public Func<Exception>? Failure { get; set; }

        public Task<AiModelPlanV1> PlanAsync(AiModelRequestV1 request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Failure is { } failure)
            {
                throw failure();
            }

            return Plan is { } plan ? Task.FromResult(plan(request)) : inner.PlanAsync(request, cancellationToken);
        }
    }

    private sealed class FakeReadTool(AiToolDescriptorV1 descriptor) : IAiOwnerReadToolV1
    {
        public AiToolDescriptorV1 Descriptor { get; } = descriptor;
        public List<AiServerContextV1> Contexts { get; } = [];
        public List<AiToolInputV1> Inputs { get; } = [];
        public Action? OnRead { get; set; }
        public AiReadResultV1? NextResult { get; set; }

        public Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
        {
            Inputs.Add(input);
            Contexts.Add(context);
            OnRead?.Invoke();
            return Task.FromResult(NextResult ?? new AiReadResultV1(
                AiToolOutcomeV1.Succeeded,
                "{\"name\":\"SENTETIK kalem\"}",
                new AiSourceV1(Descriptor.ToolId, Descriptor.Version, "item", Target, "SENTETIK kalem", "rv-1",
                    new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), AiDataClassV1.Internal)));
        }
    }

    private sealed class FakeAuthority(params string[] granted) : IErpAuthorityRevalidationService, IErpAuthorizationService
    {
        private readonly HashSet<string> grants = new(granted, StringComparer.Ordinal);

        public void Revoke(string permission) => grants.Remove(permission);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScopeOf(permission, companyId));

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScopeOf(permission, companyId));

        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScopeOf(permission, companyId).IsGranted);

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The AI runtime measures scopes, not snapshots.");

        private PermissionScope ScopeOf(string permission, Guid companyId) =>
            companyId == Company && grants.Contains(permission)
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal) { "ROLE-SENTETIK" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied;
    }

    private sealed class RecordingWriter : ISecurityEventWriterV1
    {
        public List<SecurityEventV1> Events { get; } = [];
        public bool FailStarted { get; set; }

        public Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default)
        {
            var mode = SecurityEventWriteFailureModesV1.Resolve(securityEvent.EventType, securityEvent.Phase);
            if (FailStarted && securityEvent.Phase == SecurityEventPhasesV1.Started)
            {
                return Task.FromResult(SecurityEventWriteResultV1.NotWritten(mode, SecurityEventWriteFailureKindsV1.StoreUnavailable));
            }

            Events.Add(securityEvent);
            return Task.FromResult(SecurityEventWriteResultV1.Written(mode));
        }
    }

    private sealed class TestUser(Guid userId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class FixedSessionResolver : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("v1.session-sentetik");
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

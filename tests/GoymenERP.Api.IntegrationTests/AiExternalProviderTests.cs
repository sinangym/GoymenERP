using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.External;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security.SecretStore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Issue #66 NS-06 / OPEN-020: the provider-neutral external adapter behind <c>Ai:Enabled=false</c>. Only test
/// transports are used: no network, no real key (generated at run time), no provider selected by default. The external
/// provider is admitted only when every human gate input is present, and a sensitive question never leaves.
/// </summary>
public sealed class AiExternalProviderTests
{
    private const string SecretName = "SENTETIK_AI_PROVIDER_KEY";
    private const string ProviderName = "sentetik-provider";
    private const string ToolId = "test.item-summary.v1";
    private const string ToolVersion = "1";
    private const string CleanQuestion = "SENTETIK aktif kalemleri listele";
    private static readonly string ApiKey = "SENTETIK-KEY-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void The_default_registration_selects_no_provider_has_no_egress_and_keeps_the_external_policy_closed()
    {
        using var root = BuildDefaultServices(new Dictionary<string, string?>());
        using var scope = root.CreateScope();

        Assert.IsType<FakeAiModelProvider>(scope.ServiceProvider.GetRequiredService<IAiModelProviderV1>());
        var external = scope.ServiceProvider.GetRequiredService<ExternalAiModelProvider>();
        Assert.Equal(AiExternalReadinessV1.NotSelected, external.Evaluate(new TestHostEnvironment(Environments.Development)).Readiness);
        Assert.False(scope.ServiceProvider.GetRequiredService<IAiExternalTransportV1>().EgressAvailable);
        Assert.False(scope.ServiceProvider.GetRequiredService<IAiExternalEgressPolicySourceV1>().Current().ExternalEnabled);
        Assert.Equal(2, scope.ServiceProvider.GetServices<IAiExternalProtocolAdapterV1>().Count());
        Assert.Null(scope.ServiceProvider.GetRequiredService<ILoginIdentifierHashServiceV1>().TryHash("sentetik-kullanici"));
    }

    [Fact]
    public async Task Selecting_the_external_kind_with_the_default_registration_is_refused_and_nothing_is_sent()
    {
        using var root = BuildDefaultServices(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = AiProviderKindsV1.External,
            ["Ai:External:ActiveProvider"] = ProviderName,
        });
        using var scope = root.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAiModelProviderV1>();
        var options = ReadyRuntimeOptions();

        Assert.IsType<ExternalAiModelProvider>(provider);
        var state = AiRuntimeStateEvaluator.Evaluate(options, new TestHostEnvironment(Environments.Development), provider);
        Assert.Equal(AiRuntimeAvailability.ProviderRefused, state.Availability);
        await Assert.ThrowsAsync<AiExternalProviderException>(() => provider.PlanAsync(Request()));
    }

    [Fact]
    public void A_complete_configuration_with_attestations_key_and_egress_is_ready_and_the_runtime_admits_it()
    {
        var harness = new Harness();

        Assert.Equal(AiExternalReadinessV1.Ready, harness.Provider().Evaluate(new TestHostEnvironment(Environments.Development)).Readiness);
        var state = AiRuntimeStateEvaluator.Evaluate(ReadyRuntimeOptions(), new TestHostEnvironment(Environments.Development), harness.Provider());
        Assert.True(state.IsReady);
    }

    public static TheoryData<string, AiExternalReadinessV1> MissingInputs => new()
    {
        { "no-active-provider", AiExternalReadinessV1.NotSelected },
        { "duplicate-name", AiExternalReadinessV1.Misconfigured },
        { "unknown-protocol", AiExternalReadinessV1.Misconfigured },
        { "http-endpoint", AiExternalReadinessV1.Misconfigured },
        { "endpoint-user-info", AiExternalReadinessV1.Misconfigured },
        { "no-model", AiExternalReadinessV1.Misconfigured },
        { "bad-secret-name", AiExternalReadinessV1.Misconfigured },
        { "no-protocol-version", AiExternalReadinessV1.Misconfigured },
        { "no-timeout", AiExternalReadinessV1.Misconfigured },
        { "no-response-limit", AiExternalReadinessV1.Misconfigured },
        { "no-policy-ref", AiExternalReadinessV1.Misconfigured },
        { "no-retention-attestation", AiExternalReadinessV1.AttestationMissing },
        { "no-training-attestation", AiExternalReadinessV1.AttestationMissing },
        { "no-kvkk-assessment", AiExternalReadinessV1.AttestationMissing },
        { "no-egress", AiExternalReadinessV1.EgressUnavailable },
        { "no-key", AiExternalReadinessV1.SecretMissing },
    };

    [Theory]
    [MemberData(nameof(MissingInputs))]
    public void Any_missing_gate_input_keeps_the_external_provider_closed_and_the_runtime_refuses_it(
        string missing, AiExternalReadinessV1 expected)
    {
        var harness = new Harness(AiExternalProtocolsV1.AnthropicMessages);
        var entry = harness.Options.Providers[0];
        switch (missing)
        {
            case "no-active-provider": harness.Options.ActiveProvider = null; break;
            case "duplicate-name": harness.Options.Providers.Add(Entry(AiExternalProtocolsV1.OpenAiChatCompletions)); break;
            case "unknown-protocol": entry.Protocol = "Unknown"; break;
            case "http-endpoint": entry.Endpoint = "http://ai.example.invalid/v1"; break;
            case "endpoint-user-info": entry.Endpoint = "https://user@ai.example.invalid/v1"; break;
            case "no-model": entry.Model = " "; break;
            case "bad-secret-name": entry.ApiKeySecretName = "lower-case"; break;
            case "no-protocol-version": entry.ProtocolVersion = null; break;
            case "no-timeout": entry.TimeoutSeconds = 0; break;
            case "no-response-limit": entry.MaxResponseBytes = null; break;
            case "no-policy-ref": entry.PolicyRef = null; break;
            case "no-retention-attestation": entry.NoRetentionAttestationRef = null; break;
            case "no-training-attestation": entry.NoTrainingAttestationRef = " "; break;
            case "no-kvkk-assessment": entry.KvkkTransferAssessmentRef = null; break;
            case "no-egress": harness.Transport.EgressAvailable = false; break;
            case "no-key": harness.Secrets.Values.Clear(); break;
            default: throw new ArgumentOutOfRangeException(nameof(missing));
        }

        var provider = harness.Provider();

        Assert.Equal(expected, provider.Evaluate(new TestHostEnvironment(Environments.Development)).Readiness);
        Assert.Equal(
            AiRuntimeAvailability.ProviderRefused,
            AiRuntimeStateEvaluator.Evaluate(ReadyRuntimeOptions(), new TestHostEnvironment(Environments.Development), provider).Availability);
    }

    [Fact]
    public void Production_needs_a_recorded_activation_reference()
    {
        var harness = new Harness();
        var production = new TestHostEnvironment(Environments.Production);

        Assert.Equal(AiExternalReadinessV1.ProductionActivationMissing, harness.Provider().Evaluate(production).Readiness);
        harness.Options.ProductionActivationRef = "SENTETIK-ACTIVATION";
        Assert.Equal(AiExternalReadinessV1.Ready, harness.Provider().Evaluate(production).Readiness);
    }

    [Fact]
    public async Task The_openai_adapter_sends_only_the_question_and_tool_identities_asks_for_no_storage_and_parses_the_plan()
    {
        var harness = new Harness(AiExternalProtocolsV1.OpenAiChatCompletions);
        harness.Transport.Respond(200, OpenAiBody(PlanJson()));

        var plan = await harness.Provider().PlanAsync(Request());

        var call = Assert.Single(plan.ToolCalls);
        Assert.Equal(ToolId, call.ToolId);
        Assert.Equal(ToolVersion, call.ToolVersion);
        Assert.Equal("{\"itemId\":\"SENTETIK-1\"}", call.ArgumentsJson);
        var sent = Assert.Single(harness.Transport.Requests);
        Assert.Equal($"Bearer {ApiKey}", sent.Headers["Authorization"]);
        Assert.DoesNotContain(ApiKey, sent.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, sent.JsonBody, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(sent.JsonBody);
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("sentetik-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(64, body.RootElement.GetProperty("max_completion_tokens").GetInt32());
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        AssertUserContent(messages[1].GetProperty("content").GetString()!);
    }

    [Fact]
    public async Task The_anthropic_adapter_sends_the_configured_version_header_and_reads_text_blocks()
    {
        var harness = new Harness(AiExternalProtocolsV1.AnthropicMessages);
        harness.Transport.Respond(200, AnthropicBody(PlanJson()));

        var plan = await harness.Provider().PlanAsync(Request());

        Assert.Equal(ToolId, Assert.Single(plan.ToolCalls).ToolId);
        var sent = Assert.Single(harness.Transport.Requests);
        Assert.Equal(ApiKey, sent.Headers["x-api-key"]);
        Assert.Equal("SENTETIK-VERSION", sent.Headers["anthropic-version"]);
        Assert.False(sent.Headers.ContainsKey("Authorization"));
        using var body = JsonDocument.Parse(sent.JsonBody);
        Assert.Equal(64, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(ExternalAiModelProvider.SystemInstruction, body.RootElement.GetProperty("system").GetString());
        AssertUserContent(body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!);
    }

    [Theory]
    [InlineData("TR00 0000 0000 0000 0000 0000 00 kimin hesabi")]
    [InlineData("sentetik@example.invalid kimdir")]
    [InlineData("fiyati 1.250,00 TL olan kalemler")]
    public async Task A_question_holding_a_sensitive_value_is_refused_before_anything_is_sent(string question)
    {
        var harness = new Harness();

        await Assert.ThrowsAsync<AiExternalEgressRefusedException>(() => harness.Provider().PlanAsync(Request(question)));

        Assert.Empty(harness.Transport.Requests);
    }

    [Fact]
    public async Task A_closed_settings_policy_refuses_even_a_clean_question_and_nothing_is_sent()
    {
        var harness = new Harness();
        harness.Policy = AiExternalEgressPolicyV1.Disabled("SENTETIK-POLICY");

        await Assert.ThrowsAsync<AiExternalEgressRefusedException>(() => harness.Provider().PlanAsync(Request()));

        Assert.Empty(harness.Transport.Requests);
    }

    [Fact]
    public async Task A_caller_hint_is_planned_without_calling_the_provider()
    {
        var harness = new Harness();
        var hint = new AiToolHintV1(ToolId, ToolVersion, "{\"itemId\":\"SENTETIK-1\"}");

        var plan = await harness.Provider().PlanAsync(new AiModelRequestV1(CleanQuestion, Tools(), hint));

        Assert.Equal(ToolId, Assert.Single(plan.ToolCalls).ToolId);
        Assert.Empty(harness.Transport.Requests);
    }

    [Theory]
    [InlineData(500, "{\"error\":\"SENTETIK-PROVIDER-DETAIL\"}", AiExternalProviderFailuresV1.HttpStatus)]
    [InlineData(200, "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"{}\"}}]}", AiExternalProviderFailuresV1.Truncated)]
    [InlineData(200, "{\"choices\":[{\"message\":{\"refusal\":\"SENTETIK\",\"content\":null}}]}", AiExternalProviderFailuresV1.ModelRefused)]
    [InlineData(200, "not json", AiExternalProviderFailuresV1.MalformedResponse)]
    [InlineData(200, "{\"choices\":[{\"message\":{\"content\":\"plain words\"}}]}", AiExternalProviderFailuresV1.MalformedPlan)]
    [InlineData(200, "{\"choices\":[{\"message\":{\"content\":\"{\\\"toolCalls\\\":[],\\\"answer\\\":\\\"x\\\"}\"}}]}", AiExternalProviderFailuresV1.MalformedPlan)]
    [InlineData(200, "{\"choices\":[{\"message\":{\"content\":\"{\\\"toolCalls\\\":[{\\\"toolId\\\":\\\"t\\\",\\\"toolVersion\\\":\\\"1\\\",\\\"arguments\\\":\\\"x\\\"}]}\"}}]}", AiExternalProviderFailuresV1.MalformedPlan)]
    public async Task A_failed_or_malformed_answer_fails_closed_with_a_code_and_never_echoes_the_body(
        int status, string responseBody, string expected)
    {
        var harness = new Harness(AiExternalProtocolsV1.OpenAiChatCompletions);
        harness.Transport.Respond(status, responseBody);

        var failure = await Assert.ThrowsAsync<AiExternalProviderException>(() => harness.Provider().PlanAsync(Request()));

        Assert.Equal(expected, failure.FailureCode);
        Assert.DoesNotContain("SENTETIK-PROVIDER-DETAIL", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_response_over_the_configured_size_is_refused()
    {
        var harness = new Harness(AiExternalProtocolsV1.OpenAiChatCompletions);
        harness.Options.Providers[0].MaxResponseBytes = 16;
        harness.Transport.Respond(200, OpenAiBody(PlanJson()));

        var failure = await Assert.ThrowsAsync<AiExternalProviderException>(() => harness.Provider().PlanAsync(Request()));

        Assert.Equal(AiExternalProviderFailuresV1.ResponseTooLarge, failure.FailureCode);
    }

    [Fact]
    public async Task A_transport_cancellation_the_caller_did_not_ask_for_is_a_timeout()
    {
        var harness = new Harness();
        harness.Transport.Failure = () => new OperationCanceledException();

        var failure = await Assert.ThrowsAsync<AiExternalProviderException>(() => harness.Provider().PlanAsync(Request()));

        Assert.Equal(AiExternalProviderFailuresV1.Timeout, failure.FailureCode);
    }

    [Fact]
    public async Task The_registered_no_egress_transport_never_sends()
    {
        var transport = new NoEgressAiExternalTransport();
        var request = new AiExternalHttpRequestV1(new Uri("https://ai.example.invalid/v1"), new Dictionary<string, string>(), "{}");

        Assert.False(transport.EgressAvailable);
        var failure = await Assert.ThrowsAsync<AiExternalProviderException>(() => transport.SendAsync(request, CancellationToken.None));
        Assert.Equal(AiExternalProviderFailuresV1.EgressUnavailable, failure.FailureCode);
    }

    private static void AssertUserContent(string content)
    {
        using var user = JsonDocument.Parse(content);
        Assert.Equal(CleanQuestion, user.RootElement.GetProperty("question").GetString());
        Assert.Equal(ToolId, user.RootElement.GetProperty("tools")[0].GetProperty("toolId").GetString());
        Assert.Equal(2, user.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain("company", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actor", content, StringComparison.OrdinalIgnoreCase);
    }

    private static string PlanJson() =>
        "{\"toolCalls\":[{\"toolId\":\"" + ToolId + "\",\"toolVersion\":\"" + ToolVersion + "\",\"arguments\":{\"itemId\":\"SENTETIK-1\"}}]}";

    private static string OpenAiBody(string text) =>
        JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { content = text } } } });

    private static string AnthropicBody(string text) =>
        JsonSerializer.Serialize(new { stop_reason = "end_turn", content = new[] { new { type = "text", text } } });

    private static IReadOnlyList<AiModelToolV1> Tools() =>
        [new AiModelToolV1(ToolId, ToolVersion, AiDataClassV1.Internal, ["itemId"])];

    private static AiModelRequestV1 Request(string question = CleanQuestion) => new(question, Tools(), null);

    private static AiRuntimeOptions ReadyRuntimeOptions() => new()
    {
        Enabled = true,
        Provider = AiProviderKindsV1.External,
        PolicyRef = "TEST-AI-POLICY",
        MaxQuestionLength = 200,
        MaxArgumentBytes = 256,
        MaxToolCallsPerRequest = 1,
        MaxDataClass = AiDataClassV1.Internal,
        AllowedTools = [new AiAllowedToolOptions { ToolId = ToolId, Version = ToolVersion }],
    };

    private static AiExternalProviderOptions Entry(string protocol) => new()
    {
        Name = ProviderName,
        Protocol = protocol,
        Endpoint = "https://ai.example.invalid/v1",
        Model = "sentetik-model",
        ApiKeySecretName = SecretName,
        ProtocolVersion = "SENTETIK-VERSION",
        MaxOutputTokens = 64,
        TimeoutSeconds = 5,
        MaxResponseBytes = 4096,
        PolicyRef = "SENTETIK-PROVIDER-POLICY",
        NoRetentionAttestationRef = "SENTETIK-NO-RETENTION",
        NoTrainingAttestationRef = "SENTETIK-NO-TRAINING",
        KvkkTransferAssessmentRef = "SENTETIK-KVKK",
    };

    private static ServiceProvider BuildDefaultServices(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Development));
        services.AddAiRuntime(configuration);
        services.AddErpSecretStore(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class Harness
    {
        public Harness(string protocol = AiExternalProtocolsV1.OpenAiChatCompletions)
        {
            Options = new AiExternalOptions { ActiveProvider = ProviderName, Providers = [Entry(protocol)] };
            Secrets.Values[SecretName] = ApiKey;
            Policy = new AiExternalEgressPolicyV1(true, [], false, "SENTETIK-POLICY");
        }

        public AiExternalOptions Options { get; }
        public RecordingTransport Transport { get; } = new();
        public DictionarySecretReader Secrets { get; } = new();
        public AiExternalEgressPolicyV1 Policy { get; set; }

        public ExternalAiModelProvider Provider() => new(
            new StaticOptionsMonitor<AiExternalOptions>(Options),
            [new OpenAiChatCompletionsProtocolAdapter(), new AnthropicMessagesProtocolAdapter()],
            Transport,
            Secrets,
            new FixedPolicySource(Policy),
            new TestHostEnvironment(Environments.Development));
    }

    private sealed class RecordingTransport : IAiExternalTransportV1
    {
        private AiExternalHttpResponseV1 next = new(200, "{}");

        public bool EgressAvailable { get; set; } = true;
        public List<AiExternalHttpRequestV1> Requests { get; } = [];
        public Func<Exception>? Failure { get; set; }

        public void Respond(int status, string body) => next = new AiExternalHttpResponseV1(status, body);

        public Task<AiExternalHttpResponseV1> SendAsync(AiExternalHttpRequestV1 request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Failure is { } failure)
            {
                throw failure();
            }

            return Task.FromResult(next);
        }
    }

    private sealed class DictionarySecretReader : IErpSecretReaderV1
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public ErpSecretV1? TryRead(string secretName) =>
            Values.TryGetValue(secretName, out var value) ? new ErpSecretV1(secretName, value) : null;
    }

    private sealed class FixedPolicySource(AiExternalEgressPolicyV1 policy) : IAiExternalEgressPolicySourceV1
    {
        public AiExternalEgressPolicyV1 Current() => policy;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

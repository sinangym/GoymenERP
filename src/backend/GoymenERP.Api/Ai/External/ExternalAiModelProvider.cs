using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security.SecretStore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai.External;

/// <summary>A provider that can say whether it may be used right now. The runtime admits it only when it is ready.</summary>
public interface IAiProviderReadinessV1
{
    bool IsReady(IHostEnvironment environment);
}

/// <summary>
/// The external-AI data policy for the current request (AI-D6: the administrator's Settings policy). The policy
/// runtime belongs to the settings owner; until it exists the registered source returns a closed policy.
/// </summary>
public interface IAiExternalEgressPolicySourceV1
{
    AiExternalEgressPolicyV1 Current();
}

/// <summary>The registered default: external egress is closed for every request (fail-closed, AI-D1).</summary>
public sealed class UnconfiguredAiExternalEgressPolicySource : IAiExternalEgressPolicySourceV1
{
    public const string PolicyReference = "AI-D1-default-deny";

    public AiExternalEgressPolicyV1 Current() => AiExternalEgressPolicyV1.Disabled(PolicyReference);
}

/// <summary>
/// The provider-neutral external model provider (Issue #66 NS-06, OPEN-020). It plans READ tool calls only; every
/// planned call is re-checked by <see cref="AiAskService"/> exactly like the fake provider's output. It is closed
/// unless a provider entry is selected and complete, its three attestations are recorded, an egress transport is
/// registered, the API key is in the secret store and, in Production, a production activation reference exists.
/// Only the user's question and the tool identities leave; company, actor, session and authority never do. A
/// question holding a sensitive value, or one the Settings policy does not let out, is refused before any call.
/// </summary>
public sealed class ExternalAiModelProvider(
    IOptionsMonitor<AiExternalOptions> options,
    IEnumerable<IAiExternalProtocolAdapterV1> adapters,
    IAiExternalTransportV1 transport,
    IErpSecretReaderV1 secrets,
    IAiExternalEgressPolicySourceV1 policySource,
    IHostEnvironment environment) : IAiModelProviderV1, IAiProviderReadinessV1
{
    private const string QuestionFieldName = "question";

    /// <summary>The provider-neutral planning instruction. Not user text; it fixes the only output shape accepted.</summary>
    internal const string SystemInstruction =
        "You plan read-only ERP tool calls. Use only the tools listed in the user content, with their exact toolId and "
        + "version. Never answer from your own knowledge, never compute totals and never invent values. Reply with one "
        + "JSON object and nothing else: {\"toolCalls\":[{\"toolId\":\"...\",\"toolVersion\":\"...\",\"arguments\":{}}]}. "
        + "Reply {\"toolCalls\":[]} when no listed tool fits.";

    private readonly IAiExternalProtocolAdapterV1[] adapters = adapters.ToArray();

    public string ProviderKind => AiProviderKindsV1.External;

    public bool IsReady(IHostEnvironment hostEnvironment) => Evaluate(hostEnvironment).Readiness == AiExternalReadinessV1.Ready;

    public AiExternalResolutionV1 Evaluate(IHostEnvironment hostEnvironment)
    {
        ArgumentNullException.ThrowIfNull(hostEnvironment);
        var current = options.CurrentValue;
        var resolution = AiExternalProviderResolver.Resolve(current, adapters);
        if (resolution.Readiness != AiExternalReadinessV1.Ready || resolution.Config is null)
        {
            return resolution;
        }

        if (!transport.EgressAvailable)
        {
            return new AiExternalResolutionV1(AiExternalReadinessV1.EgressUnavailable, null);
        }

        if (hostEnvironment.IsProduction() && string.IsNullOrWhiteSpace(current.ProductionActivationRef))
        {
            return new AiExternalResolutionV1(AiExternalReadinessV1.ProductionActivationMissing, null);
        }

        return secrets.TryRead(resolution.Config.ApiKeySecretName) is null
            ? new AiExternalResolutionV1(AiExternalReadinessV1.SecretMissing, null)
            : resolution;
    }

    public async Task<AiModelPlanV1> PlanAsync(AiModelRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // A caller hint is untrusted input that the runtime re-checks anyway; it needs no model and nothing leaves.
        if (request.Hint is { } hint)
        {
            return new AiModelPlanV1([new AiPlannedToolCallV1(hint.ToolId, hint.ToolVersion, hint.ArgumentsJson)]);
        }

        var resolution = Evaluate(environment);
        if (resolution.Readiness != AiExternalReadinessV1.Ready || resolution.Config is not { } config)
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.NotReady);
        }

        var question = ReleaseQuestion(request.Question);
        var adapter = adapters.Single(candidate => string.Equals(candidate.Protocol, config.Protocol, StringComparison.Ordinal));
        var apiKey = secrets.TryRead(config.ApiKeySecretName)
            ?? throw new AiExternalProviderException(AiExternalProviderFailuresV1.NotReady);
        var outbound = adapter.BuildRequest(config, apiKey, SystemInstruction, BuildUserContent(question, request.AvailableTools));

        AiExternalHttpResponseV1 response;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(config.Timeout);
            try
            {
                response = await transport.SendAsync(outbound, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AiExternalProviderException(AiExternalProviderFailuresV1.Timeout);
            }
        }

        if (response is null || response.Body is null)
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.MalformedResponse);
        }

        if (Encoding.UTF8.GetByteCount(response.Body) > config.MaxResponseBytes)
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.ResponseTooLarge);
        }

        return ParsePlan(adapter.ReadText(response));
    }

    /// <summary>
    /// AI-D4: the free-text screen runs first (any sensitive value refuses), then the Settings field policy decides
    /// whether the question may leave at all. A refusal is raised before anything is built or sent.
    /// </summary>
    private string ReleaseQuestion(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (!AiOutboundTextScreenV1.Screen(question).IsClean)
        {
            throw new AiExternalEgressRefusedException();
        }

        using var pseudonymizer = new AiPseudonymizerV1();
        var payload = AiEgressFilterV1.Apply(
            [new AiOutboundFieldV1(new AiFieldTagV1(QuestionFieldName, AiDataClassV1.Internal, AiSensitiveDataTypeV1.None), question)],
            policySource.Current(),
            pseudonymizer);
        if (payload.DeniedFieldNames.Count > 0 || !payload.Fields.TryGetValue(QuestionFieldName, out var released))
        {
            throw new AiExternalEgressRefusedException();
        }

        return released;
    }

    private static string BuildUserContent(string question, IReadOnlyList<AiModelToolV1> tools)
    {
        var toolArray = new JsonArray();
        foreach (var tool in tools ?? [])
        {
            var fields = new JsonArray();
            foreach (var field in tool.FieldAllowlist ?? [])
            {
                fields.Add(field);
            }

            toolArray.Add(new JsonObject
            {
                ["toolId"] = tool.ToolId,
                ["version"] = tool.Version,
                ["fields"] = fields,
            });
        }

        return new JsonObject { ["question"] = question, ["tools"] = toolArray }.ToJsonString();
    }

    /// <summary>Strict reader for the only accepted output shape. Anything else is a malformed plan, never a guess.</summary>
    internal static AiModelPlanV1 ParsePlan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Count() != 1
                || !root.TryGetProperty("toolCalls", out var calls)
                || calls.ValueKind != JsonValueKind.Array)
            {
                throw new AiExternalProviderException(AiExternalProviderFailuresV1.MalformedPlan);
            }

            var planned = new List<AiPlannedToolCallV1>();
            foreach (var call in calls.EnumerateArray())
            {
                if (call.ValueKind != JsonValueKind.Object
                    || call.EnumerateObject().Count() != 3
                    || AiExternalJson.ReadString(call, "toolId") is not { Length: > 0 } toolId
                    || AiExternalJson.ReadString(call, "toolVersion") is not { Length: > 0 } toolVersion
                    || !call.TryGetProperty("arguments", out var arguments)
                    || arguments.ValueKind != JsonValueKind.Object)
                {
                    throw new AiExternalProviderException(AiExternalProviderFailuresV1.MalformedPlan);
                }

                planned.Add(new AiPlannedToolCallV1(toolId, toolVersion, arguments.GetRawText()));
            }

            return new AiModelPlanV1(planned.AsReadOnly());
        }
        catch (JsonException)
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.MalformedPlan);
        }
    }
}

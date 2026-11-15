using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.Ai;

/// <summary>A tool the model may plan to call: identity only, never company, actor or authority.</summary>
public sealed record AiModelToolV1(string ToolId, string Version, AiDataClassV1 DataClass, IReadOnlyList<string> FieldAllowlist);

/// <summary>A caller-supplied tool suggestion. It is untrusted input exactly like model output.</summary>
public sealed record AiToolHintV1(string ToolId, string ToolVersion, string ArgumentsJson);

/// <summary>
/// What the provider sees. It deliberately carries no company id, actor id, session or authority revision: the
/// provider can only plan, and every planned call is re-checked by the runtime before an owner tool runs.
/// </summary>
public sealed record AiModelRequestV1(string Question, IReadOnlyList<AiModelToolV1> AvailableTools, AiToolHintV1? Hint);

public sealed record AiPlannedToolCallV1(string ToolId, string ToolVersion, string ArgumentsJson);

/// <summary>Untrusted model output: a list of planned READ tool calls.</summary>
public sealed record AiModelPlanV1(IReadOnlyList<AiPlannedToolCallV1> ToolCalls);

public interface IAiModelProviderV1
{
    string ProviderKind { get; }

    Task<AiModelPlanV1> PlanAsync(AiModelRequestV1 request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic in-process provider for development and tests (AI-3). It never reaches the network, never invents
/// an answer and never totals values: it plans exactly the hinted call, or nothing. External providers are refused
/// until OPEN-020 / AI-D4 is decided.
/// </summary>
public sealed class FakeAiModelProvider : IAiModelProviderV1
{
    public string ProviderKind => AiProviderKindsV1.Fake;

    public Task<AiModelPlanV1> PlanAsync(AiModelRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AiPlannedToolCallV1> calls = request.Hint is { } hint
            ? [new AiPlannedToolCallV1(hint.ToolId, hint.ToolVersion, hint.ArgumentsJson)]
            : [];
        return Task.FromResult(new AiModelPlanV1(calls));
    }
}

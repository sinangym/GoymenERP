using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.Ai.Gateway;

/// <summary>
/// WP-A1: the versioned contract every AI package (B1–B5, C*) uses to turn grounded content into text. A caller hands the
/// gateway the tool results it already obtained under its own authority; the gateway decides whether the feature may run,
/// which model may see the content (task kind × data class table), whether the month's budget allows it, and whether the
/// composed text is supported by the sources. It never runs a tool, never writes a business record and never sees an
/// authority it could widen. See <c>src/backend/GoymenERP.Api/Ai/README.md</c>.
/// </summary>
public static class AiGatewayContractV1
{
    /// <summary>The contract identity; a breaking change is a new version beside this one, never an in-place edit.</summary>
    public const string Version = "ai-gateway.v1";
}

/// <summary>
/// Who asks, as resolved by the server. Never taken from a request body or from model output: the caller builds it from
/// the server company context, the authenticated actor and the correlation metadata.
/// </summary>
public sealed record AiGatewayCallerV1(Guid CompanyId, string ActorId, string CorrelationId, string? CausationId);

/// <summary>
/// One piece of grounding: a READ tool result the caller released under its own authority (permission, company, data class
/// already checked). The gateway trusts nothing in it beyond its declared data class, which it re-measures.
/// </summary>
public sealed record AiGroundingItemV1(string ToolId, string ToolVersion, string ContentJson, AiSourceV1 Source);

/// <summary>
/// One gateway call. <see cref="ContentDataClass"/> is the caller's classification of the question/instruction text
/// itself (AI-D34 d: by content, not by document name); the effective class is the highest of it and every source.
/// </summary>
public sealed record AiGatewayRequestV1(
    AiFeatureV1 Feature,
    AiTaskKindV1 TaskKind,
    string Instruction,
    AiDataClassV1 ContentDataClass,
    IReadOnlyList<AiGroundingItemV1> Grounding,
    AiGatewayCallerV1 Caller);

public enum AiGatewayStatusV1
{
    /// <summary>A text supported by the sources (or, for <see cref="AiFeatureV1.Chat"/>, an ungrounded chat reply).</summary>
    Answered = 1,

    /// <summary>"I cannot answer": no source, or the composed text was not supported by the sources. Not an error.</summary>
    CannotAnswer = 2,

    /// <summary>The runtime is off (<c>Ai:Enabled=false</c>) or not ready.</summary>
    RuntimeUnavailable = 3,

    /// <summary>The feature's switch is off in the policy in force (or no policy is recorded).</summary>
    FeatureDisabled = 4,

    /// <summary>The month's ceiling is reached, or no ceiling is recorded (budget closed).</summary>
    BudgetExhausted = 5,

    /// <summary>No route may carry this data class for this task and feature (AI-D24, AI-D34 d).</summary>
    DataClassRefused = 6,

    /// <summary>The request itself is malformed (no caller, empty instruction, undefined enum, chat with grounding).</summary>
    Invalid = 7,

    /// <summary>The policy store, the ledger or the model failed; nothing was released.</summary>
    Unavailable = 8,
}

/// <summary>Why the gateway answered <see cref="AiGatewayStatusV1.CannotAnswer"/>; a stable code, never model text.</summary>
public static class AiGatewayCannotAnswerReasonsV1
{
    public const string NoSource = "no_source";
    public const string Unsupported = "unsupported_by_sources";
}

/// <summary>A cited source of an answer, as the caller can show it next to the text.</summary>
public sealed record AiCitationV1(string ToolId, string ToolVersion, string TargetKind, Guid TargetId, string Title,
    string SourceVersion, DateTimeOffset AsOfUtc);

public sealed record AiGatewayAnswerV1(
    string Text,
    bool Grounded,
    IReadOnlyList<AiCitationV1> Citations,
    string ModelDeployment,
    AiModelPlacementV1 Placement,
    string PolicyVersion,
    int UsageUnits);

public sealed record AiGatewayResultV1(AiGatewayStatusV1 Status, AiGatewayAnswerV1? Answer, string? CannotAnswerReason)
{
    public static AiGatewayResultV1 Of(AiGatewayStatusV1 status) => new(status, null, null);

    public static AiGatewayResultV1 CannotAnswer(string reason) => new(AiGatewayStatusV1.CannotAnswer, null, reason);
}

/// <summary>The gateway every AI package calls. One implementation, <see cref="AiGateway"/>.</summary>
public interface IAiGatewayV1
{
    Task<AiGatewayResultV1> ComposeAsync(AiGatewayRequestV1 request, CancellationToken cancellationToken = default);
}

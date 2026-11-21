using System.Text.Json;

namespace GoymenERP.Api.Ai;

/// <summary>
/// <c>POST /api/v1/ai/ask</c> body. Everything here is untrusted input: there is no company id, actor or authority
/// field, and a tool hint is re-checked exactly like model output (ERP-000J §8).
/// </summary>
public sealed record AiAskRequestV1(string? Question, string? ToolId, string? ToolVersion, JsonElement? Arguments);

public static class AiAskOutcomesV1
{
    public const string Answered = "answered";
}

public static class AiToolResultOutcomesV1
{
    public const string Succeeded = "succeeded";
    public const string NotFound = "not_found";
}

public static class AiAvailabilityValuesV1
{
    public const string Disabled = "disabled";
    public const string Unavailable = "unavailable";
    public const string Ready = "ready";
}

/// <summary>
/// The answer of a READ-only ask: what each owner tool released and where it came from. The runtime adds no totals
/// or derived values (data class §5a); text composition by a model is a later slice.
/// </summary>
public sealed record AiAskResponseV1(string Outcome, IReadOnlyList<AiToolResultV1> ToolResults);

public sealed record AiToolResultV1(
    string ToolId,
    string ToolVersion,
    string Outcome,
    string? ContentJson,
    AiToolSourceV1? Source);

public sealed record AiToolSourceV1(
    string TargetKind,
    Guid TargetId,
    string Title,
    string SourceVersion,
    DateTimeOffset AsOfUtc,
    string DataClass);

/// <summary><c>GET /api/v1/ai/capabilities</c>: whether the panel may ask in the current company context.</summary>
public sealed record AiCapabilitiesV1(string Availability, bool PanelAvailable, IReadOnlyList<AiToolCapabilityV1> Tools);

public sealed record AiToolCapabilityV1(string ToolId, string Version, string OwnerModule, string DataClass);

public enum AiAskStatusV1
{
    Answered = 1,
    Disabled = 2,
    Unavailable = 3,
    Invalid = 4,
    Refused = 5,
    ToolUnavailable = 6,
}

public sealed record AiAskResultV1(AiAskStatusV1 Status, AiAskResponseV1? Response, IReadOnlyList<string> InvalidFields)
{
    public static AiAskResultV1 Of(AiAskStatusV1 status) => new(status, null, []);

    public static AiAskResultV1 InvalidInput(params string[] fields) => new(AiAskStatusV1.Invalid, null, fields);

    public static AiAskResultV1 Answered(AiAskResponseV1 response) => new(AiAskStatusV1.Answered, response, []);
}

/// <summary>Stable field codes for an invalid ask; never echo the rejected value.</summary>
public static class AiInvalidFieldsV1
{
    public const string Question = "question";
    public const string Tool = "tool";
    public const string Arguments = "arguments";
}

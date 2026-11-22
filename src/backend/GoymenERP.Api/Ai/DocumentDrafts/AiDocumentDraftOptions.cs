using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai.DocumentDrafts;

/// <summary>
/// WP-B3 configuration (section <c>Ai:DocumentDrafts</c>). Off by default; it also needs <c>Ai:Enabled</c>. The bounds
/// are technical parser limits (ASSUMPTION-AI-DRAFT-LIMITS-1: aligned with the Purchasing invoice reader's technical
/// bounds), changeable from configuration; while enabled, a missing or non-positive bound keeps the feature
/// unavailable instead of falling back to an invented value. The local model (tier 4) has its own switch and stays
/// off until the hardware decision (OPEN-014).
/// </summary>
public sealed class AiDocumentDraftOptions
{
    public const string SectionName = "Ai:DocumentDrafts";

    public bool Enabled { get; set; }

    public bool LocalModelEnabled { get; set; }

    public int? MaxDocumentBytes { get; set; }

    public long? MaxExpandedBytes { get; set; }

    public int? MaxLines { get; set; }

    public int? MaxCellCharacters { get; set; }

    public int? MaxTextLength { get; set; }
}

public sealed record AiDocumentDraftLimits(
    int MaxDocumentBytes,
    long MaxExpandedBytes,
    int MaxLines,
    int MaxCellCharacters,
    int MaxTextLength,
    bool LocalModelEnabled);

public static class AiDocumentDraftOptionsReader
{
    /// <summary>Null while disabled, unreadable or incomplete (fail-closed); the caller reports which.</summary>
    public static AiDocumentDraftLimits? TryResolve(IOptions<AiDocumentDraftOptions> options, out bool enabled)
    {
        ArgumentNullException.ThrowIfNull(options);
        AiDocumentDraftOptions? value;
        try
        {
            value = options.Value;
        }
        catch (InvalidOperationException)
        {
            enabled = true; // an unreadable section is misconfigured, never silently "off"
            return null;
        }

        enabled = value?.Enabled == true;
        if (value is not { Enabled: true }
            || value.MaxDocumentBytes is not > 0
            || value.MaxExpandedBytes is not > 0
            || value.MaxLines is not > 0
            || value.MaxCellCharacters is not > 0
            || value.MaxTextLength is not > 0)
        {
            return null;
        }

        return new AiDocumentDraftLimits(value.MaxDocumentBytes.Value, value.MaxExpandedBytes.Value, value.MaxLines.Value,
            value.MaxCellCharacters.Value, value.MaxTextLength.Value, value.LocalModelEnabled);
    }
}

namespace GoymenERP.Api.Ai.Commands;

/// <summary>
/// WP-C1 command pipeline configuration (section <c>Ai:Commands</c>). Closed by default: while <see cref="Enabled"/> is
/// false, or the AI runtime itself is not ready, no proposal, approval or execution runs. No command family is opened by
/// this package (C.2 is separate): <see cref="AllowedTools"/> ships empty, so even an enabled pipeline admits no tool.
/// <see cref="PreviewLifetime"/> carries decision 156 (2026-10-21: "AI önizleme geçerliliği 15 dk") in
/// <c>appsettings.json</c>; a missing or non-positive value keeps the pipeline unavailable instead of inventing one.
/// </summary>
public sealed class AiCommandOptions
{
    public const string SectionName = "Ai:Commands";

    public bool Enabled { get; set; }

    public TimeSpan? PreviewLifetime { get; set; }

    /// <summary>Default-deny WRITE tool allowlist: exact tool id + version pairs. Empty means no command may run.</summary>
    public List<AiAllowedToolOptions> AllowedTools { get; set; } = [];
}

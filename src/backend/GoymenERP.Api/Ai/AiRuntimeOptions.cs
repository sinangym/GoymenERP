using GoymenERP.Api.Ai.External;
using GoymenERP.BuildingBlocks.Ai;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.Ai;

/// <summary>
/// AI-3 runtime configuration (section <c>Ai</c>). Every budget and ceiling is configuration, never a code constant
/// (ERP-000J §8, AI_KARAR_PAKETI_2026-11). The runtime is off by default: while <see cref="Enabled"/> is false the ERP
/// is unaffected and no answer, mock or otherwise, is produced. A missing limit while enabled keeps the runtime
/// unavailable (fail-closed) instead of falling back to an invented value.
/// </summary>
public sealed class AiRuntimeOptions
{
    public const string SectionName = "Ai";

    public bool Enabled { get; set; }

    /// <summary>
    /// The model provider kind. <see cref="AiProviderKindsV1.Fake"/> is refused in the Production environment.
    /// <see cref="AiProviderKindsV1.External"/> is admitted only while its own readiness holds (provider selected and
    /// complete, attestations recorded, egress transport registered, key in the secret store, production activation
    /// recorded in Production); none of that is configured by default (OPEN-020, AI-D5/AI-D6/AI-D7).
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>Reviewed policy reference that admitted the values below (traceability, not authority).</summary>
    public string? PolicyRef { get; set; }

    public int? MaxQuestionLength { get; set; }

    public int? MaxArgumentBytes { get; set; }

    public int? MaxToolCallsPerRequest { get; set; }

    /// <summary>The highest data class a tool may release through the panel (§5a). Required while enabled.</summary>
    public AiDataClassV1? MaxDataClass { get; set; }

    /// <summary>Default-deny tool allowlist: exact tool id + version pairs. Empty means no tool may run.</summary>
    public List<AiAllowedToolOptions> AllowedTools { get; set; } = [];
}

public sealed class AiAllowedToolOptions
{
    public string? ToolId { get; set; }

    public string? Version { get; set; }
}

public static class AiProviderKindsV1
{
    public const string Fake = "Fake";

    /// <summary>The provider-neutral external adapter (section <c>Ai:External</c>). Closed until a human enables it.</summary>
    public const string External = "External";
}

public enum AiRuntimeAvailability
{
    Disabled = 1,
    Misconfigured = 2,
    ProviderRefused = 3,
    Ready = 4,
}

/// <summary>The resolved, validated runtime limits. Only constructed when the runtime is ready.</summary>
public sealed record AiRuntimeLimits(
    int MaxQuestionLength,
    int MaxArgumentBytes,
    int MaxToolCallsPerRequest,
    AiDataClassV1 MaxDataClass);

public sealed record AiRuntimeState(AiRuntimeAvailability Availability, AiRuntimeLimits? Limits)
{
    public bool IsReady => Availability == AiRuntimeAvailability.Ready && Limits is not null;
}

public static class AiRuntimeStateEvaluator
{
    public static AiRuntimeState Evaluate(AiRuntimeOptions? options, IHostEnvironment environment, IAiModelProviderV1? provider)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (options is null || !options.Enabled)
        {
            return new AiRuntimeState(AiRuntimeAvailability.Disabled, null);
        }

        // The configured kind and the resolved provider must agree. The in-process fake provider is never admitted in
        // Production; the external provider only while its own readiness holds (OPEN-020 / AI-D4..AI-D7).
        var admitted = provider is not null
            && string.Equals(provider.ProviderKind, options.Provider, StringComparison.Ordinal)
            && options.Provider switch
            {
                AiProviderKindsV1.Fake => !environment.IsProduction(),
                AiProviderKindsV1.External => provider is IAiProviderReadinessV1 readiness && readiness.IsReady(environment),
                _ => false,
            };
        if (!admitted)
        {
            return new AiRuntimeState(AiRuntimeAvailability.ProviderRefused, null);
        }

        if (string.IsNullOrWhiteSpace(options.PolicyRef)
            || options.MaxQuestionLength is not > 0
            || options.MaxArgumentBytes is not > 0
            || options.MaxToolCallsPerRequest is not > 0
            || options.MaxDataClass is not { } maxDataClass
            || !Enum.IsDefined(maxDataClass)
            || options.AllowedTools is null)
        {
            return new AiRuntimeState(AiRuntimeAvailability.Misconfigured, null);
        }

        return new AiRuntimeState(
            AiRuntimeAvailability.Ready,
            new AiRuntimeLimits(
                options.MaxQuestionLength.Value,
                options.MaxArgumentBytes.Value,
                options.MaxToolCallsPerRequest.Value,
                maxDataClass));
    }
}

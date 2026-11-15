using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai;

/// <summary>
/// AI-3 runtime configuration (section <c>Ai</c>). Every budget and ceiling is configuration, never a code constant
/// (ERP-000J §8, AI_KARAR_PAKETI_2026-11). The runtime is off by default: while <see cref="Enabled"/> is false the ERP
/// is unaffected and no answer, mock or otherwise, is produced. A missing limit while enabled keeps the runtime
/// unavailable (fail-closed) instead of falling back to an invented value. AI-D9 sets the starting values in
/// <c>appsettings.json</c> (MaxQuestionLength 2000, MaxToolCallsPerRequest 5, MaxArgumentBytes 16384, MaxDataClass
/// Internal); they stay changeable from configuration (KLN-D51: applied on restart until the typed Settings provider).
/// A value that is missing, non-positive, undefined or cannot be bound keeps the runtime unavailable.
/// </summary>
public sealed class AiRuntimeOptions
{
    public const string SectionName = "Ai";

    public bool Enabled { get; set; }

    /// <summary>
    /// The model provider kind. Only <see cref="AiProviderKindsV1.Fake"/> exists in this slice. An external provider
    /// is refused until OPEN-020 / AI-D4 is decided; the fake provider is refused in the Production environment.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Version identity of the published AI policy setting that admitted the values below (traceability, not
    /// authority). With <see cref="PolicyVersion"/> it is the PolicyId/PolicyVersion pair every Group E AI security
    /// event carries (ASSUMPTION-AI-POLICYREF-1); while either is missing or malformed no policy counts as published,
    /// the events carry <see cref="AiPolicyReferenceV1.DefaultDenyPolicyId"/> and the runtime stays unavailable.
    /// </summary>
    public string? PolicyRef { get; set; }

    /// <summary>The published version of <see cref="PolicyRef"/>; required (positive) while enabled.</summary>
    public int? PolicyVersion { get; set; }

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

/// <summary>
/// ASSUMPTION-AI-POLICYREF-1 (V1): the policy reference of a Group E AI security event is the published AI policy
/// setting's version identity (<see cref="AiRuntimeOptions.PolicyRef"/> + <see cref="AiRuntimeOptions.PolicyVersion"/>),
/// or <see cref="DefaultDenyPolicyId"/> version <see cref="DefaultDenyPolicyVersion"/> when no policy is published.
/// Pinned by a versioned contract test; changing a value here is a contract change.
/// </summary>
public static partial class AiPolicyReferenceV1
{
    public const string DefaultDenyPolicyId = "default-deny";

    /// <summary>The single version of the built-in default-deny policy; the event contract requires a version.</summary>
    public const int DefaultDenyPolicyVersion = 1;

    public static AiPolicyReference Resolve(AiRuntimeOptions? options) =>
        IsPublished(options)
            ? new AiPolicyReference(options!.PolicyRef!, options.PolicyVersion!.Value)
            : new AiPolicyReference(DefaultDenyPolicyId, DefaultDenyPolicyVersion);

    /// <summary>A published reference is a bounded security-event token with a positive version.</summary>
    public static bool IsPublished(AiRuntimeOptions? options) =>
        options?.PolicyRef is { Length: > 0 and <= SecurityEventV1.PolicyIdMaxLength } policyRef
        && PolicyTokenPattern().IsMatch(policyRef)
        && !string.Equals(policyRef, DefaultDenyPolicyId, StringComparison.Ordinal)
        && options.PolicyVersion is > 0;

    // Same alphabet as the security-event token validator, so a published reference is always storable.
    [GeneratedRegex("\\A[A-Za-z0-9._:@/+=-]+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyTokenPattern();
}

public sealed record AiPolicyReference(string PolicyId, int PolicyVersion);

/// <summary>
/// Reads the bound <c>Ai</c> section without letting a value that cannot be bound (e.g. an unknown data class name)
/// escape as an exception: an unreadable section is reported as unreadable and the caller stays fail-closed.
/// </summary>
public static class AiRuntimeOptionsReader
{
    public static bool TryRead(IOptions<AiRuntimeOptions> options, out AiRuntimeOptions? value)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            value = options.Value;
            return value is not null;
        }
        catch (InvalidOperationException)
        {
            value = null;
            return false;
        }
    }
}

public static class AiRuntimeStateEvaluator
{
    /// <summary>A section that cannot be bound is misconfigured, never disabled-and-ignored and never a crash.</summary>
    public static AiRuntimeState Evaluate(IOptions<AiRuntimeOptions> options, IHostEnvironment environment, IAiModelProviderV1? provider) =>
        AiRuntimeOptionsReader.TryRead(options, out var value)
            ? Evaluate(value, environment, provider)
            : new AiRuntimeState(AiRuntimeAvailability.Misconfigured, null);

    public static AiRuntimeState Evaluate(AiRuntimeOptions? options, IHostEnvironment environment, IAiModelProviderV1? provider)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (options is null || !options.Enabled)
        {
            return new AiRuntimeState(AiRuntimeAvailability.Disabled, null);
        }

        // OPEN-020 / AI-D4: no external provider. Only the in-process fake provider is admitted, and never in Production.
        if (!string.Equals(options.Provider, AiProviderKindsV1.Fake, StringComparison.Ordinal)
            || provider is null
            || !string.Equals(provider.ProviderKind, options.Provider, StringComparison.Ordinal)
            || environment.IsProduction())
        {
            return new AiRuntimeState(AiRuntimeAvailability.ProviderRefused, null);
        }

        if (!AiPolicyReferenceV1.IsPublished(options)
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

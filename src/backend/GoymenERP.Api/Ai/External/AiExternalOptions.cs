using GoymenERP.BuildingBlocks.Security.SecretStore;

namespace GoymenERP.Api.Ai.External;

/// <summary>
/// External model provider configuration (section <c>Ai:External</c>; Issue #66 NS-06, OPEN-020). One provider-neutral
/// adapter serves many providers; each provider has its own entry. Nothing here selects a provider: with no
/// <see cref="ActiveProvider"/>, no API key in the secret store, a missing attestation or no egress transport, the
/// external provider stays closed. Choosing the provider, adding its key and activating it in production are human
/// gates (AI-D5/AI-D6/AI-D7); this file carries no default for any of them.
/// </summary>
public sealed class AiExternalOptions
{
    public const string SectionName = "Ai:External";

    /// <summary>The <see cref="AiExternalProviderOptions.Name"/> of the provider in use. Unset means none is selected.</summary>
    public string? ActiveProvider { get; set; }

    /// <summary>Recorded human production activation reference. Required in the Production environment.</summary>
    public string? ProductionActivationRef { get; set; }

    public List<AiExternalProviderOptions> Providers { get; set; } = [];
}

public sealed class AiExternalProviderOptions
{
    /// <summary>Configuration name of this entry (unique, chosen by the operator).</summary>
    public string? Name { get; set; }

    /// <summary>Wire protocol; one of <see cref="AiExternalProtocolsV1"/>.</summary>
    public string? Protocol { get; set; }

    /// <summary>Absolute https endpoint without user info or fragment.</summary>
    public string? Endpoint { get; set; }

    public string? Model { get; set; }

    /// <summary>Secret store reference name of the API key. The key itself is never in configuration.</summary>
    public string? ApiKeySecretName { get; set; }

    /// <summary>Protocol version header value, where the protocol requires one.</summary>
    public string? ProtocolVersion { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? TimeoutSeconds { get; set; }

    public int? MaxResponseBytes { get; set; }

    /// <summary>Reviewed policy reference that admitted this entry (traceability, not authority).</summary>
    public string? PolicyRef { get; set; }

    /// <summary>AI-D5 / AI_KARAR_PAKETI A: the provider's no-retention attestation reference. Fail-closed when missing.</summary>
    public string? NoRetentionAttestationRef { get; set; }

    /// <summary>AI-D5 / AI_KARAR_PAKETI A: the provider's no-training attestation reference. Fail-closed when missing.</summary>
    public string? NoTrainingAttestationRef { get; set; }

    /// <summary>AI_KARAR_PAKETI A: the KVKK cross-border transfer assessment reference. Fail-closed when missing.</summary>
    public string? KvkkTransferAssessmentRef { get; set; }
}

public static class AiExternalProtocolsV1
{
    public const string OpenAiChatCompletions = "OpenAiChatCompletions";
    public const string AnthropicMessages = "AnthropicMessages";
}

public enum AiExternalReadinessV1
{
    NotSelected = 1,
    Misconfigured = 2,
    AttestationMissing = 3,
    EgressUnavailable = 4,
    ProductionActivationMissing = 5,
    SecretMissing = 6,
    Ready = 7,
}

/// <summary>A validated provider entry. Only built when every required value is present and well formed.</summary>
public sealed record AiExternalProviderConfigV1(
    string Name,
    string Protocol,
    Uri Endpoint,
    string Model,
    string ApiKeySecretName,
    string? ProtocolVersion,
    int MaxOutputTokens,
    TimeSpan Timeout,
    int MaxResponseBytes,
    string PolicyRef);

public sealed record AiExternalResolutionV1(AiExternalReadinessV1 Readiness, AiExternalProviderConfigV1? Config);

public static class AiExternalProviderResolver
{
    /// <summary>
    /// Resolves the selected provider entry from configuration alone (no secret, no network). The result is
    /// <see cref="AiExternalReadinessV1.AttestationMissing"/> unless all three AI-D5 / KVKK references are recorded.
    /// </summary>
    public static AiExternalResolutionV1 Resolve(AiExternalOptions? options, IEnumerable<IAiExternalProtocolAdapterV1> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        if (options is null || string.IsNullOrWhiteSpace(options.ActiveProvider))
        {
            return new AiExternalResolutionV1(AiExternalReadinessV1.NotSelected, null);
        }

        var matches = (options.Providers ?? [])
            .Where(entry => entry is not null && string.Equals(entry.Name, options.ActiveProvider, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            return new AiExternalResolutionV1(AiExternalReadinessV1.Misconfigured, null);
        }

        var entry = matches[0];
        var adapter = adapters.SingleOrDefault(candidate => string.Equals(candidate.Protocol, entry.Protocol, StringComparison.Ordinal));
        if (adapter is null
            || !TryReadEndpoint(entry.Endpoint, out var endpoint)
            || string.IsNullOrWhiteSpace(entry.Model)
            || !ErpSecretNamesV1.IsValid(entry.ApiKeySecretName)
            || (adapter.RequiresProtocolVersion && string.IsNullOrWhiteSpace(entry.ProtocolVersion))
            || entry.MaxOutputTokens is not > 0
            || entry.TimeoutSeconds is not > 0
            || entry.MaxResponseBytes is not > 0
            || string.IsNullOrWhiteSpace(entry.PolicyRef))
        {
            return new AiExternalResolutionV1(AiExternalReadinessV1.Misconfigured, null);
        }

        if (string.IsNullOrWhiteSpace(entry.NoRetentionAttestationRef)
            || string.IsNullOrWhiteSpace(entry.NoTrainingAttestationRef)
            || string.IsNullOrWhiteSpace(entry.KvkkTransferAssessmentRef))
        {
            return new AiExternalResolutionV1(AiExternalReadinessV1.AttestationMissing, null);
        }

        return new AiExternalResolutionV1(
            AiExternalReadinessV1.Ready,
            new AiExternalProviderConfigV1(
                entry.Name!,
                entry.Protocol!,
                endpoint,
                entry.Model.Trim(),
                entry.ApiKeySecretName!,
                adapter.RequiresProtocolVersion ? entry.ProtocolVersion!.Trim() : null,
                entry.MaxOutputTokens.Value,
                TimeSpan.FromSeconds(entry.TimeoutSeconds.Value),
                entry.MaxResponseBytes.Value,
                entry.PolicyRef.Trim()));
    }

    private static bool TryReadEndpoint(string? value, out Uri endpoint)
    {
        endpoint = null!;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        endpoint = parsed;
        return true;
    }
}

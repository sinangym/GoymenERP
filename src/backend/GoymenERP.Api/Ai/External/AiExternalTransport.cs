namespace GoymenERP.Api.Ai.External;

/// <summary>
/// One outbound provider call. Headers may carry the API key, so <see cref="ToString"/> prints header names only and
/// the body is never printed.
/// </summary>
public sealed record AiExternalHttpRequestV1(Uri Endpoint, IReadOnlyDictionary<string, string> Headers, string JsonBody)
{
    public override string ToString() =>
        $"{nameof(AiExternalHttpRequestV1)}({Endpoint.GetLeftPart(UriPartial.Authority)}, headers: {string.Join(",", Headers.Keys.Order(StringComparer.Ordinal))}, body: redacted)";
}

/// <summary>The provider's raw answer. The body is untrusted and is never echoed into errors, logs or audit.</summary>
public sealed record AiExternalHttpResponseV1(int StatusCode, string Body)
{
    public override string ToString() => $"{nameof(AiExternalHttpResponseV1)}({StatusCode}, body: redacted)";
}

/// <summary>
/// The only way an external model call leaves the process. <see cref="EgressAvailable"/> false keeps the external
/// provider closed. Live network egress is a human gate (OPEN-020, AI-D7): this slice ships only the closed default and
/// test transports, no network implementation.
/// </summary>
public interface IAiExternalTransportV1
{
    bool EgressAvailable { get; }

    Task<AiExternalHttpResponseV1> SendAsync(AiExternalHttpRequestV1 request, CancellationToken cancellationToken);
}

/// <summary>The registered default: no egress. Reaching it is a programming error and nothing is sent.</summary>
public sealed class NoEgressAiExternalTransport : IAiExternalTransportV1
{
    public bool EgressAvailable => false;

    public Task<AiExternalHttpResponseV1> SendAsync(AiExternalHttpRequestV1 request, CancellationToken cancellationToken) =>
        throw new AiExternalProviderException(AiExternalProviderFailuresV1.EgressUnavailable);
}

public static class AiExternalProviderFailuresV1
{
    public const string NotReady = "not_ready";
    public const string EgressUnavailable = "egress_unavailable";
    public const string Timeout = "timeout";
    public const string HttpStatus = "http_status";
    public const string ResponseTooLarge = "response_too_large";
    public const string Truncated = "truncated";
    public const string ModelRefused = "model_refused";
    public const string MalformedResponse = "malformed_response";
    public const string MalformedPlan = "malformed_plan";
}

/// <summary>A provider call failed. The message names only a closed failure code, never the provider body or the key.</summary>
public sealed class AiExternalProviderException(string failureCode)
    : Exception($"External AI provider call failed: {failureCode}.")
{
    public string FailureCode { get; } = failureCode;
}

/// <summary>
/// The question may not leave: the external policy is closed or the text holds a sensitive value
/// (AI-D4). The runtime refuses the request and reminds the user; nothing is sent.
/// </summary>
public sealed class AiExternalEgressRefusedException() : Exception("External AI egress refused by policy.");

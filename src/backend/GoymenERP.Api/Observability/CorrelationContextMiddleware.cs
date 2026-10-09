using GoymenERP.Api.Errors;
using GoymenERP.BuildingBlocks.Observability;

namespace GoymenERP.Api.Observability;

public sealed class CorrelationContextMiddleware(
    RequestDelegate next,
    ILogger<CorrelationContextMiddleware> logger,
    C01E2eRequestEvidenceRecorder? requestEvidenceRecorder = null)
{
    public const string CorrelationHeaderName = "X-Correlation-Id";

    public const string CausationHeaderName = "X-Causation-Id";

    private const int MaximumIdentifierLength = 128;

    public async Task InvokeAsync(
        HttpContext httpContext,
        ICorrelationContext correlationContext)
    {
        if (!TryReadIdentifierHeader(
                httpContext.Request.Headers,
                CorrelationHeaderName,
                out var correlationId)
            || !TryReadIdentifierHeader(
                httpContext.Request.Headers,
                CausationHeaderName,
                out var causationId))
        {
            var rejectedRequestCorrelationId = Guid.NewGuid().ToString("N");
            ResolveContext(
                httpContext,
                correlationContext,
                rejectedRequestCorrelationId,
                causationId: null);
            httpContext.Response.Headers[CorrelationHeaderName] =
                rejectedRequestCorrelationId;

            using (logger.BeginScope(CreateLoggingScope(correlationContext)))
            {
                logger.LogWarning(
                    "Request metadata was rejected at the correlation boundary. ErrorCode={ErrorCode}",
                    ErpProblemCodes.CorrelationMetadataInvalid);
                await ErpRequestMetadataProblem
                    .WriteCorrelationMetadataInvalidAsync(httpContext);
            }

            if (requestEvidenceRecorder is not null)
            {
                await requestEvidenceRecorder.RecordAsync(httpContext, rejectedRequestCorrelationId);
            }

            return;
        }

        correlationId ??= Guid.NewGuid().ToString("N");
        ResolveContext(httpContext, correlationContext, correlationId, causationId);
        httpContext.Response.Headers[CorrelationHeaderName] = correlationId;

        using (logger.BeginScope(CreateLoggingScope(correlationContext)))
        {
            await next(httpContext);
        }

        if (requestEvidenceRecorder is not null)
        {
            await requestEvidenceRecorder.RecordAsync(httpContext, correlationId);
        }
    }

    private static void ResolveContext(
        HttpContext httpContext,
        ICorrelationContext correlationContext,
        string correlationId,
        string? causationId)
    {
        var userAgent = httpContext.Request.Headers.UserAgent.ToString().Trim();
        if (userAgent.Length > 512)
        {
            userAgent = userAgent[..512];
        }

        correlationContext.Set(
            correlationId,
            causationId,
            SourceChannels.Web,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent);
    }

    private static Dictionary<string, object?> CreateLoggingScope(
        ICorrelationContext correlationContext)
    {
        return new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationContext.CorrelationId,
            ["CausationId"] = correlationContext.CausationId,
            ["SourceChannel"] = correlationContext.SourceChannel,
        };
    }

    private static bool TryReadIdentifierHeader(
        IHeaderDictionary headers,
        string headerName,
        out string? identifier)
    {
        if (!headers.TryGetValue(headerName, out var values))
        {
            identifier = null;
            return true;
        }

        if (values.Count != 1)
        {
            identifier = null;
            return false;
        }

        identifier = values[0];
        return identifier is not null && IsValidIdentifier(identifier);
    }

    private static bool IsValidIdentifier(string value)
    {
        return value.Length is > 0 and <= MaximumIdentifierLength
            && value.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.' or ':');
    }
}

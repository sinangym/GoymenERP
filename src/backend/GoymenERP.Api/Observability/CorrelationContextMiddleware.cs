using GoymenERP.BuildingBlocks.Observability;

namespace GoymenERP.Api.Observability;

public sealed class CorrelationContextMiddleware(
    RequestDelegate next,
    ILogger<CorrelationContextMiddleware> logger)
{
    public const string CorrelationHeaderName = "X-Correlation-Id";

    public const string CausationHeaderName = "X-Causation-Id";

    private const int MaximumIdentifierLength = 128;

    public async Task InvokeAsync(
        HttpContext httpContext,
        ICorrelationContext correlationContext)
    {
        var correlationId = httpContext.Request.Headers[CorrelationHeaderName]
            .ToString()
            .Trim();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        var causationId = httpContext.Request.Headers[CausationHeaderName]
            .ToString()
            .Trim();
        if (!IsValidIdentifier(correlationId)
            || (!string.IsNullOrWhiteSpace(causationId) && !IsValidIdentifier(causationId)))
        {
            await Results.Problem(
                    title: "Korelasyon bilgisi geçersiz.",
                    detail: "Korelasyon ve neden kimlikleri en fazla 128 güvenli karakter içermelidir.",
                    statusCode: StatusCodes.Status400BadRequest)
                .ExecuteAsync(httpContext);
            return;
        }

        var userAgent = httpContext.Request.Headers.UserAgent.ToString().Trim();
        if (userAgent.Length > 512)
        {
            userAgent = userAgent[..512];
        }

        correlationContext.Set(
            correlationId,
            string.IsNullOrWhiteSpace(causationId) ? null : causationId,
            SourceChannels.Web,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent);
        httpContext.Response.Headers[CorrelationHeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["CausationId"] = correlationContext.CausationId,
            ["SourceChannel"] = correlationContext.SourceChannel,
        }))
        {
            await next(httpContext);
        }
    }

    private static bool IsValidIdentifier(string value)
    {
        return value.Length is > 0 and <= MaximumIdentifierLength
            && value.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.' or ':');
    }
}

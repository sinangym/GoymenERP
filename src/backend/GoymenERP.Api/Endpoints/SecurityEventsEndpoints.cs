using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Reading;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// ADR-SEC-001 section 5: the company's read of its security event channel. Read-only; the read is recorded as
/// security.events.read before any row is read (fail-closed: 503 when it cannot be recorded).
/// </summary>
public static class SecurityEventsEndpoints
{
    private const int CursorMaxLength = 128;
    private const int EventTypeMaxLength = 64;
    private const int CorrelationIdMaxLength = 128;
    private const int DefaultPageSize = 25;

    public static IEndpointRouteBuilder MapSecurityEventsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/security-events", async (
                int? pageSize,
                string? cursor,
                string? eventType,
                string? correlationId,
                ISecurityEventQueryServiceV1 service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                httpContext.Response.Headers.Pragma = "no-cache";
                if (IsTooLong(cursor, CursorMaxLength)
                    || IsTooLong(eventType, EventTypeMaxLength)
                    || IsTooLong(correlationId, CorrelationIdMaxLength))
                {
                    return InvalidFilter();
                }

                var result = await service.GetEventsAsync(
                    pageSize ?? DefaultPageSize,
                    cursor,
                    eventType,
                    correlationId,
                    cancellationToken);
                return result.Status switch
                {
                    SecurityEventQueryStatusV1.Ok when result.Page is not null => Results.Ok(result.Page),
                    SecurityEventQueryStatusV1.InvalidCursor => Results.Problem(
                        title: "Güvenlik olayı cursor bilgisi geçersiz.",
                        detail: "Güvenlik olayı listesini ilk sayfadan yeniden açın.",
                        statusCode: StatusCodes.Status400BadRequest),
                    SecurityEventQueryStatusV1.InvalidFilter => InvalidFilter(),
                    // The read could not be recorded in the security channel; nothing was read.
                    SecurityEventQueryStatusV1.Unavailable => Results.Problem(
                        title: "Güvenlik olayları şu anda okunamıyor.",
                        detail: "Okuma kaydı yazılamadığı için liste açılmadı. Daha sonra yeniden deneyin.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        extensions: new Dictionary<string, object?> { ["code"] = ErpProblemCodes.ServiceUnavailable }),
                    // Authority missing or revoked before or during the read; nothing is released.
                    _ => Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        extensions: new Dictionary<string, object?> { ["code"] = ErpProblemCodes.Forbidden }),
                };
            })
            .RequireAuthorization(ErpPermissions.PlatformSecurityEventsRead)
            .RequireCompanyContext()
            .WithTags("Security")
            .WithName("GetSecurityEventsV1")
            .Produces<SecurityEventPageV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .AllowsCodeLessErpBadRequestBody()
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpServiceUnavailable();

        return endpoints;
    }

    private static IResult InvalidFilter() => Results.Problem(
        title: "Güvenlik olayı filtresi geçersiz.",
        detail: "Olay türü tanımlı bir tür olmalı; filtreler izin verilen uzunluğu aşmamalı.",
        statusCode: StatusCodes.Status400BadRequest);

    private static bool IsTooLong(string? value, int maximumLength)
    {
        return value?.Trim().Length > maximumLength;
    }
}

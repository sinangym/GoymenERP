using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/audit/events", async (
                int? pageSize,
                string? cursor,
                string? correlationId,
                string? entityType,
                string? entityId,
                IAuditQueryService service,
                CancellationToken cancellationToken) =>
            {
                if (IsTooLong(correlationId, 128)
                    || IsTooLong(cursor, 128)
                    || IsTooLong(entityType, 160)
                    || IsTooLong(entityId, 128))
                {
                    return Results.Problem(
                        title: "Audit filtresi geçersiz.",
                        detail: "Audit filtrelerinden biri izin verilen uzunluğu aşıyor.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var result = await service.GetEventsAsync(
                    pageSize ?? 25,
                    cursor,
                    correlationId,
                    entityType,
                    entityId,
                    cancellationToken);
                return result.Status switch
                {
                    AuditEventQueryStatus.Ok => Results.Ok(result.Page),
                    AuditEventQueryStatus.InvalidCursor => Results.Problem(
                        title: "Audit cursor bilgisi geçersiz.",
                        detail: "Audit listesini ilk sayfadan yeniden açın.",
                        statusCode: StatusCodes.Status400BadRequest),
                    // BLG-03: authority revoked before or during the read; nothing is released.
                    _ => Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        extensions: new Dictionary<string, object?> { ["code"] = ErpProblemCodes.Forbidden }),
                };
            })
            .RequireAuthorization(ErpPermissions.IdentityAuditRead)
            .RequireCompanyContext()
            .WithTags("Audit")
            .WithName("GetAuditEventsV1")
            .Produces<AuditEventPage>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .AllowsCodeLessErpBadRequestBody()
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static bool IsTooLong(string? value, int maximumLength)
    {
        return value?.Trim().Length > maximumLength;
    }
}

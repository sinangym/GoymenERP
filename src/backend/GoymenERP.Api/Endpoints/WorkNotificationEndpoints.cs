using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.Endpoints;

public static class WorkNotificationEndpoints
{
    public static IEndpointRouteBuilder MapWorkNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/work/notifications", async (bool? includePassive, Guid? beforeId, HttpContext context,
            IWorkNotificationReadV1 reader, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";
            try { return Results.Ok(await reader.ListMineAsync(includePassive == true, token, beforeId)); }
            catch (UnauthorizedAccessException) { return Results.Problem(statusCode: StatusCodes.Status403Forbidden); }
            catch (ArgumentException) { return Results.Problem(statusCode: StatusCodes.Status400BadRequest); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable); }
        }).WithName("GetMyWorkNotificationsV1").WithTags("Work").RequireAuthorization().RequireCompanyContext()
          .Produces<WorkNotificationPageV1>().ProducesErpAuthenticatedProblems().ProducesErpBadRequest().AllowsCodeLessErpBadRequestBody()
          .ProducesErpServiceUnavailable();
        return endpoints;
    }
}

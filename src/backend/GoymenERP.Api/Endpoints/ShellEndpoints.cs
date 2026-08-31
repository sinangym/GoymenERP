using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.Shell;
using Microsoft.Net.Http.Headers;

namespace GoymenERP.Api.Endpoints;

public static class ShellEndpoints
{
    public static IEndpointRouteBuilder MapShellEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/shell")
            .RequireAuthorization()
            .WithTags("Shell");

        group.MapGet("/capabilities", async (
                ShellCapabilityService service,
                HttpContext httpContext,
                ILogger<ShellCapabilityService> logger,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response.Headers);
                try
                {
                    return Results.Ok(
                        await service.GetSnapshotAsync(cancellationToken));
                }
                catch (ShellCapabilityUnavailableException)
                {
                    logger.LogWarning(
                        "Shell capability snapshot failed closed. CorrelationId={CorrelationId}",
                        httpContext.Response.Headers["X-Correlation-Id"].ToString());
                    return Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .RequireCompanyContext()
            .WithName("GetShellCapabilitiesV1")
            .Produces<ShellCapabilitySnapshot>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static void SetPrivateNoStore(IHeaderDictionary headers)
    {
        headers[HeaderNames.CacheControl] = "private, no-store";
        headers[HeaderNames.Pragma] = "no-cache";
    }
}

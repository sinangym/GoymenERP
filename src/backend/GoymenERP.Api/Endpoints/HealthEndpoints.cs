using GoymenERP.Api.Errors;
using GoymenERP.Api.OpenApi;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GoymenERP.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapErpHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", async (
                HttpContext httpContext,
                HealthCheckService healthCheckService,
                CancellationToken cancellationToken) =>
            {
                var report = await healthCheckService.CheckHealthAsync(cancellationToken);
                httpContext.Response.StatusCode = report.Status == HealthStatus.Unhealthy
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status200OK;
                await ErpHealthCheckResponseWriter.WriteAsync(httpContext, report);
            })
            .AllowAnonymous()
            .WithTags("Health")
            .WithName("GetLivenessV1")
            .Produces<string>(StatusCodes.Status200OK, "text/plain")
            .ProducesErpBadRequest()
            .ProducesErpInternalError()
            .ProducesErpServiceUnavailable();

        return endpoints;
    }
}

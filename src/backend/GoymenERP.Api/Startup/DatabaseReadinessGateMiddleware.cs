using GoymenERP.Api.Errors;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GoymenERP.Api.Startup;

internal sealed class DatabaseReadinessGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        HealthCheckService healthCheckService)
    {
        if (IsDiagnosticEndpoint(httpContext.Request.Path))
        {
            await next(httpContext);
            return;
        }

        var report = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains(
                DatabaseSchemaCompatibilityHealthCheck.ReadinessTag),
            httpContext.RequestAborted);
        if (report.Status == HealthStatus.Healthy)
        {
            await next(httpContext);
            return;
        }

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await ErpHealthCheckResponseWriter.WriteAsync(httpContext, report);
    }

    private static bool IsDiagnosticEndpoint(PathString path) =>
        path.Equals(new PathString("/health/live"))
        || path.Equals(new PathString("/health/ready"));
}

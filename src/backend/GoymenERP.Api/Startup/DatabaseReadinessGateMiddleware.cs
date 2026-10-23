using GoymenERP.Api.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GoymenERP.Api.Startup;

internal sealed class DatabaseReadinessGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        HealthCheckService healthCheckService)
    {
        // AllowAnonymous does not skip authentication or DB-backed claims transformation.
        // Complete only the selected public readiness endpoint before that dependency.
        var isPublicReadinessEndpoint = httpContext.Request.Path.Equals(
                new PathString("/health/ready"))
            && httpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        if (IsDiagnosticEndpoint(httpContext.Request.Path) && !isPublicReadinessEndpoint)
        {
            await next(httpContext);
            return;
        }

        var report = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains(
                DatabaseSchemaCompatibilityHealthCheck.ReadinessTag),
            httpContext.RequestAborted);
        if (isPublicReadinessEndpoint)
        {
            httpContext.Response.StatusCode = report.Status == HealthStatus.Unhealthy
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status200OK;
            await ErpHealthCheckResponseWriter.WriteAsync(httpContext, report);
            return;
        }

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

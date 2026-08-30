using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GoymenERP.Api.Errors;

internal static class ErpHealthCheckResponseWriter
{
    public static async Task WriteAsync(HttpContext httpContext, HealthReport report)
    {
        if (httpContext.Response.StatusCode != StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.ContentType = "text/plain; charset=utf-8";
            await httpContext.Response.WriteAsync(
                report.Status.ToString(),
                httpContext.RequestAborted);
            return;
        }

        var problemDetailsService = httpContext.RequestServices
            .GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
            },
        });
    }
}

using GoymenERP.BuildingBlocks.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Errors;

public sealed class ErpUnhandledExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ErpUnhandledExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var correlationContext = httpContext.RequestServices
            .GetService<ICorrelationContext>();
        if (correlationContext?.IsResolved == true)
        {
            logger.LogError(
                "Unhandled request failure was redacted. Code={ErrorCode} CorrelationId={CorrelationId}",
                ErpProblemCodes.InternalError,
                correlationContext.CorrelationId);
        }
        else
        {
            logger.LogError(
                "Unhandled request failure was redacted. Code={ErrorCode}",
                ErpProblemCodes.InternalError);
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
            },
        });
        return true;
    }
}

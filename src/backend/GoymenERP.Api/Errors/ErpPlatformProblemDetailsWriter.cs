using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace GoymenERP.Api.Errors;

internal sealed class ErpPlatformProblemDetailsWriter(
    IOptions<HttpJsonOptions> jsonOptions) : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context)
    {
        return ErpProblemDetailsExtensions.IsPlatformProblem(context);
    }

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        ErpProblemDetailsExtensions.CustomizeProblemDetails(context);
        var response = context.HttpContext.Response;
        var statusCode = context.ProblemDetails.Status
            ?? response.StatusCode;
        response.StatusCode = statusCode;

        return new ValueTask(response.WriteAsJsonAsync(
            context.ProblemDetails,
            jsonOptions.Value.SerializerOptions,
            "application/problem+json",
            context.HttpContext.RequestAborted));
    }
}

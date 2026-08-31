using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Errors;

internal static class ErpRequestMetadataProblem
{
    private static readonly object OwnershipMarkerKey = new();

    public static Task WriteCompanyContextInvalidAsync(HttpContext httpContext)
    {
        return CompanyContextInvalid().ExecuteAsync(httpContext);
    }

    public static IResult CompanyContextInvalid()
    {
        return new RequestMetadataProblemResult(ErpProblemCodes.CompanyContextInvalid);
    }

    public static Task WriteCorrelationMetadataInvalidAsync(HttpContext httpContext)
    {
        return new RequestMetadataProblemResult(ErpProblemCodes.CorrelationMetadataInvalid)
            .ExecuteAsync(httpContext);
    }

    private static Task WriteAsync(HttpContext httpContext, string code)
    {
        httpContext.Items[OwnershipMarkerKey] = new OwnershipMarker(code);
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
            }).ExecuteAsync(httpContext);
    }

    private sealed class RequestMetadataProblemResult(string code) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            return WriteAsync(httpContext, code);
        }
    }

    internal static bool TryGetOwnedCode(
        HttpContext httpContext,
        ProblemDetails problemDetails,
        out string code)
    {
        if (httpContext.Items.TryGetValue(OwnershipMarkerKey, out var markerValue)
            && markerValue is OwnershipMarker marker
            && problemDetails.Extensions.TryGetValue("code", out var codeValue)
            && codeValue is string candidate
            && string.Equals(marker.Code, candidate, StringComparison.Ordinal))
        {
            code = candidate;
            return true;
        }

        code = string.Empty;
        return false;
    }

    private sealed record OwnershipMarker(string Code);
}

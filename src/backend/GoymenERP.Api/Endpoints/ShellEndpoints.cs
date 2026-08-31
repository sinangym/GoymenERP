using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.Errors;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace GoymenERP.Api.Endpoints;

public static class ShellEndpoints
{
    public static IEndpointRouteBuilder MapShellEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/shell")
            .RequireAuthorization()
            .AddEndpointFilter(ResolveOptionalCompanyContextAsync)
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
            .UsesOptionalCompanyContext(usesPlatformInvalidCode: true)
            .WithName("GetShellCapabilitiesV1")
            .Produces<ShellCapabilitySnapshot>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static async ValueTask<object?> ResolveOptionalCompanyContextAsync(
        EndpointFilterInvocationContext invocationContext,
        EndpointFilterDelegate next)
    {
        var httpContext = invocationContext.HttpContext;
        if (!httpContext.Request.Headers.TryGetValue(
                CompanyContextMiddleware.CompanyHeaderName,
                out var headerValue))
        {
            return await next(invocationContext);
        }

        if (headerValue.Count != 1
            || !Guid.TryParse(headerValue[0], out var companyId)
            || companyId == Guid.Empty)
        {
            return ErpRequestMetadataProblem.CompanyContextInvalid();
        }

        var currentUser = httpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!currentUser.CanAccessCompany(companyId))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
        }

        httpContext.RequestServices
            .GetRequiredService<ICompanyContext>()
            .Set(companyId);

        return await next(invocationContext);
    }

    private static void SetPrivateNoStore(IHeaderDictionary headers)
    {
        headers[HeaderNames.CacheControl] = "private, no-store";
        headers[HeaderNames.Pragma] = "no-cache";
    }
}

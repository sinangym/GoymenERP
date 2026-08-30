using GoymenERP.Api.Errors;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.Multitenancy;

public sealed class CompanyContextMiddleware(RequestDelegate next)
{
    public const string CompanyHeaderName = "X-Company-Id";

    public async Task InvokeAsync(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext)
    {
        var requiresContext = httpContext.GetEndpoint()?
            .Metadata.GetMetadata<RequiresCompanyContextAttribute>() is not null;

        if (!requiresContext || !currentUser.IsAuthenticated)
        {
            await next(httpContext);
            return;
        }

        if (!httpContext.Request.Headers.TryGetValue(CompanyHeaderName, out var headerValue)
            || headerValue.Count != 1
            || !Guid.TryParse(headerValue[0], out var companyId)
            || companyId == Guid.Empty)
        {
            await ErpRequestMetadataProblem
                .WriteCompanyContextInvalidAsync(httpContext);
            return;
        }

        if (!currentUser.CanAccessCompany(companyId))
        {
            await Results.Problem(
                    title: "Erişim reddedildi.",
                    detail: "Seçili şirket için erişim yetkiniz bulunmuyor.",
                    statusCode: StatusCodes.Status403Forbidden)
                .ExecuteAsync(httpContext);
            return;
        }

        companyContext.Set(companyId);
        await next(httpContext);
    }
}

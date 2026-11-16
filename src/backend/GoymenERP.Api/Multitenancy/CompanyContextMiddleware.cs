using GoymenERP.Api.Errors;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Multitenancy;

public sealed class CompanyContextMiddleware(RequestDelegate next)
{
    public const string CompanyHeaderName = "X-Company-Id";

    public async Task InvokeAsync(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICompanyLifecycleReadPortV1 lifecycle,
        IIdentityCompanySetupServiceV1 companySetup)
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
            // F023-2: an unreadable company header is a company context refusal; no company is stored for it.
            await AuthorizationDenialHttp.RecordAsync(
                httpContext,
                AuthorizationDenialHttp.CompanyContextMismatch(requestedCompanyId: null));
            await ErpRequestMetadataProblem
                .WriteCompanyContextInvalidAsync(httpContext);
            return;
        }

        var firstAdmin = httpContext.GetEndpoint()?.Metadata.GetMetadata<FirstCompanyAdminEndpointMetadata>() is not null;
        // Do not disclose a foreign company's setup state through its status or through an owner-read failure.
        // Only the existing first-Admin recovery endpoint may cross this fence, under a fresh founder check.
        if (!currentUser.CanAccessCompany(companyId)
            && !(firstAdmin && await companySetup.IsActiveFounderAsync(httpContext.RequestAborted)))
        {
            await RejectCompanyAccessAsync(httpContext, companyId);
            return;
        }

        CompanyLifecycleViewV1? company;
        try { company = await lifecycle.ReadAsync(companyId, httpContext.RequestAborted); }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            await Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Şirket durumu doğrulanamadı.").ExecuteAsync(httpContext);
            return;
        }
        if (company is null || company.CompanyId != companyId || company.Version < 1)
        {
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(httpContext);
            return;
        }
        if (company.State == CompanyLifecycleStateV1.SetupPending)
        {
            if (firstAdmin && await companySetup.IsActiveFounderAsync(httpContext.RequestAborted))
            {
                companyContext.Set(companyId);
                await next(httpContext);
                return;
            }
            await Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Şirket kurulumu bekliyor.",
                detail: "İlk Admin ataması tamamlanmadan bu şirkette işlem yapılamaz.",
                extensions: new Dictionary<string, object?> { ["code"] = "organization.company.setup_pending" })
                .ExecuteAsync(httpContext);
            return;
        }
        if (company.State != CompanyLifecycleStateV1.Active)
        {
            await Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(httpContext);
            return;
        }
        if (!currentUser.CanAccessCompany(companyId))
        {
            await RejectCompanyAccessAsync(httpContext, companyId);
            return;
        }

        companyContext.Set(companyId);
        await next(httpContext);
    }

    private static async Task RejectCompanyAccessAsync(HttpContext httpContext, Guid companyId)
    {
        // F023-2 scenario 7: platform scope, no company id; the requested company is kept for the user actor.
        await AuthorizationDenialHttp.RecordAsync(
            httpContext,
            AuthorizationDenialHttp.CompanyContextMismatch(companyId));
        await Results.Problem(
                title: "Erişim reddedildi.",
                detail: "Seçili şirket için erişim yetkiniz bulunmuyor.",
                statusCode: StatusCodes.Status403Forbidden)
            .ExecuteAsync(httpContext);
    }
}

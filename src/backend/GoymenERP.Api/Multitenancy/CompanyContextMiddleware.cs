using GoymenERP.Api.Errors;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.Multitenancy;

public sealed class CompanyContextMiddleware(RequestDelegate next)
{
    public const string CompanyHeaderName = "X-Company-Id";

    public async Task InvokeAsync(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICompanyLifecycleReadPortV1 lifecycle,
        ICompanySetupGroupAuthorityV1 companySetupAuthority)
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
        CompanySetupGroupAuthoritySnapshotV1? setupAuthority = null;
        // Check the exact group operation BEFORE an owner lookup, independently of company membership.
        // A foreign target's existence, setup state and owner-reader failure remain undisclosed on denial.
        if (firstAdmin)
        {
            try { setupAuthority = await companySetupAuthority.ReadAsync(CompanySetupGroupOperationV1.AssignFirstAdmin, httpContext.RequestAborted); }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception) { await Results.Problem(statusCode: 503).ExecuteAsync(httpContext); return; }
            if (!ValidSetupAuthority(setupAuthority, currentUser)) { await RejectCompanyAccessAsync(httpContext, companyId); return; }
        }
        else if (!currentUser.CanAccessCompany(companyId))
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
        if (firstAdmin)
        {
            CompanySetupGroupAuthoritySnapshotV1 refreshed;
            try { refreshed = await companySetupAuthority.ReadAsync(CompanySetupGroupOperationV1.AssignFirstAdmin, httpContext.RequestAborted); }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception) { await Results.Problem(statusCode: 503).ExecuteAsync(httpContext); return; }
            if (!ValidSetupAuthority(refreshed, currentUser) || refreshed.ActorUserId != setupAuthority!.ActorUserId
                || refreshed.Revision != setupAuthority.Revision) { await RejectCompanyAccessAsync(httpContext, companyId); return; }
            // Keep this exact command reachable for authorized same-key replay after setup completes.
            // The service refuses any new assignment when an Admin exists; normal operations get no exception.
            if (company.State is CompanyLifecycleStateV1.SetupPending or CompanyLifecycleStateV1.Active)
            {
                companyContext.Set(companyId);
                await next(httpContext);
                return;
            }
        }
        if (company.State == CompanyLifecycleStateV1.SetupPending)
        {
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

    private static bool ValidSetupAuthority(CompanySetupGroupAuthoritySnapshotV1 value, ICurrentUser currentUser) =>
        currentUser.IsAuthenticated && Guid.TryParse(currentUser.UserId, out var actor) && actor != Guid.Empty
        && value.IsGranted && value.Operation == CompanySetupGroupOperationV1.AssignFirstAdmin
        && value.ActorUserId == actor && !string.IsNullOrWhiteSpace(value.Revision);

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

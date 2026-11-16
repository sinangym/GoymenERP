using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Modules.Identity.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.Endpoints;

public static partial class IdentityEndpoints
{
    private static void MapFirstCompanyAdmin(RouteGroupBuilder group)
    {
        group.MapPost("/company-setup/first-admin", async (AssignFirstCompanyAdminV1 request, HttpContext httpContext,
            IIdentityCompanySetupServiceV1 service, CancellationToken cancellationToken) =>
        {
            try { return await AccessCommandAsync(httpContext, metadata => service.AssignFirstAdminAsync(request, metadata, cancellationToken)); }
            catch (ArgumentException) { return Results.Problem(statusCode: 400, title: "İlk Admin isteği geçersiz."); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException)
            { return Results.Problem(statusCode: 503, title: "İlk Admin ataması doğrulanamadı."); }
        })
            .RequireCompanyContext()
            .WithMetadata(new FirstCompanyAdminEndpointMetadata())
            // Authentication is inherited; this one narrow endpoint proves founder authority in the service.
            .WithName("AssignFirstCompanyAdminV1")
            .WithIdempotencyContract(201, 409)
            .Accepts<AssignFirstCompanyAdminV1>("application/json")
            .Produces<IdentityAccessCommandOutcomeV1>(201)
            .ProducesErpBadRequest(IdentityAccessProblemCodesV1.IdempotencyKeyInvalid, IdentityAccessProblemCodesV1.RequestInvalid)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict("organization.company.setup_pending", "identity.company.admin_already_assigned", "organization.company.setup_pending_required",
                IdentityAccessProblemCodesV1.VersionConflict, IdentityAccessProblemCodesV1.IdempotencyConflict, IdentityAccessProblemCodesV1.UserNotAssignable)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }
}

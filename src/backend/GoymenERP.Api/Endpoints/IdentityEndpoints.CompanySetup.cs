using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Modules.Identity.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.Endpoints;

public static partial class IdentityEndpoints
{
    private static void MapFirstCompanyAdmin(RouteGroupBuilder group)
    {
        group.MapGet("/company-setup/candidates", async (string? search, HttpContext context,
            IIdentityFirstAdminCandidatesV1 candidates, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try { return Results.Ok(await candidates.ReadCandidatesAsync(search, cancellationToken)); }
            catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
            catch (ArgumentException) { return Results.Problem(statusCode: 400); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return Results.Problem(statusCode: 503); }
        }).WithName("GetFirstCompanyAdminCandidatesV1")
            .Produces<IReadOnlyList<FirstCompanyAdminCandidateV1>>()
            .ProducesErpBadRequest().AllowsCodeLessErpBadRequestBody()
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
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
            // Authentication is inherited; this narrow endpoint proves explicit group authority in middleware and in the service.
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

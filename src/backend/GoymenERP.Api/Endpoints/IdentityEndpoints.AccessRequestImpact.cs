using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class IdentityEndpoints
{
    // Authorized read using a POST body; it does not create an access request, assignment or policy version.
    private static void MapAccessRequestImpact(RouteGroupBuilder group)
    {
        group.MapPost("/access/role-request-impact", async (IdentityRoleRequestImpactQueryV1 query,
            HttpContext http, ICompanyContext company, IIdentityAccessRequestImpactReadV1 service, CancellationToken token) =>
        {
            NoStore(http);
            if (http.Request.Query.Count != 0 || query.UserId == Guid.Empty || query.RoleId == Guid.Empty)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
            var answer = await service.ReadAsync(query, token);
            if (answer.Outcome == IdentityAccessRequestImpactOutcomeV1.Available
                && answer.Impact is { } impact && company.IsResolved && impact.CompanyId == company.CompanyId
                && impact.UserId == query.UserId && impact.RoleId == query.RoleId)
                return Results.Ok(impact);
            return Results.Problem(statusCode: answer.Outcome switch
            {
                IdentityAccessRequestImpactOutcomeV1.Denied => StatusCodes.Status403Forbidden,
                IdentityAccessRequestImpactOutcomeV1.NotFound => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status503ServiceUnavailable,
            });
        })
            .RequireCompanyContext()
            .RequireAuthorization(IdentityPermissionManifestV1.AssignmentsRead)
            .WithName("ReadIdentityRoleRequestCodeImpactV1")
            .Accepts<IdentityRoleRequestImpactQueryV1>("application/json")
            .Produces<IdentityRoleRequestImpactV1>()
            .ProducesErpBadRequest().AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType().ProducesErpCodeLessNotFound()
            .ProducesErpAuthenticatedProblems().ProducesErpServiceUnavailable();
    }
}

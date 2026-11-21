using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class OrganizationEndpoints
{
    private static void MapCompanyOpening(RouteGroupBuilder group)
    {
        // Authenticated group operations deliberately do not require an existing company membership/context.
        group.MapGet("/company-setup/capabilities", async (HttpContext context, IOrganizationCompanyOpeningV1 service,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try { return Results.Ok(await service.ReadCapabilitiesAsync(cancellationToken)); }
            catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return Results.Problem(statusCode: 503); }
        }).WithName("GetCompanySetupCapabilitiesV1").Produces<CompanySetupCapabilitiesV1>()
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapGet("/company-setup/companies", async (HttpContext context, IOrganizationCompanyOpeningV1 service,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try { return Results.Ok(await service.ReadPendingAsync(cancellationToken)); }
            catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return Results.Problem(statusCode: 503); }
        }).WithName("GetPendingCompanySetupsV1").Produces<IReadOnlyList<CompanySetupCompanyV1>>()
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapPost("/companies", async (OpenCompanyV1 request, HttpContext context,
            ICurrentUser currentUser, ICorrelationContext correlation, IOrganizationCompanyOpeningV1 service,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!TryReadIdempotencyKey(context.Request.Headers, out var key)) return Results.Problem(statusCode: 400);
            if (!correlation.IsResolved || string.IsNullOrWhiteSpace(currentUser.UserId)) return Results.Problem(statusCode: 503);
            try
            {
                var result = await service.OpenAsync(request, new(currentUser.UserId, key, correlation.CorrelationId,
                    correlation.CausationId, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent), cancellationToken);
                if (result.IsReplay)
                {
                    context.Response.Headers["Idempotency-Replayed"] = "true";
                    context.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
                }
                return result.Answer.StatusCode == 201 && result.Answer.Company is { } company
                    ? Results.Json(company, statusCode: 201)
                    : Results.Problem(statusCode: 409, extensions: new Dictionary<string, object?> { ["code"] = result.Answer.Code });
            }
            catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
            catch (ArgumentException) { return Results.Problem(statusCode: 400); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return Results.Problem(statusCode: 503); }
        }).WithName("OpenCompanyV1").WithIdempotencyContract(201, 409)
            .Accepts<OpenCompanyV1>("application/json").Produces<CompanySetupCompanyV1>(201)
            .ProducesErpBadRequest().AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict("organization.company.already_exists", "organization.company.code_in_use",
                "organization.company.tax_id_in_use", "organization.company.idempotency_conflict")
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
    }
}

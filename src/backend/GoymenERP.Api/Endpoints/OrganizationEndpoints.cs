using GoymenERP.Api.Multitenancy;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/organization")
            .RequireAuthorization()
            .WithTags("Organization");

        group.MapGet("/companies", async (
            IOrganizationService service,
            CancellationToken cancellationToken) =>
        {
            var companies = await service.GetAuthorizedCompaniesAsync(cancellationToken);
            return Results.Ok(companies);
        });

        group.MapGet("/context", (ICompanyContext context) =>
                Results.Ok(new { companyId = context.CompanyId }))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationContextRead);

        group.MapGet("/sites", async (
                IOrganizationService service,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.GetSitesAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationSitesRead);

        group.MapGet("/locations", async (
                int? page,
                int? pageSize,
                IOrganizationService service,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.GetLocationsAsync(
                page ?? 1,
                pageSize ?? 25,
                cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationLocationsRead);

        group.MapGet("/locations/{id:guid}", async (
                Guid id,
                IOrganizationService service,
                CancellationToken cancellationToken) =>
        {
            var location = await service.GetLocationAsync(id, cancellationToken);
            return location is null
                ? NotFound()
                : Results.Ok(location);
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationLocationsRead);

        group.MapPut("/locations/{id:guid}/active-state", async (
                Guid id,
                SetLocationActiveState command,
                IOrganizationService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (!TryReadCommandMetadata(
                    httpContext,
                    currentUser,
                    companyContext,
                    out var metadata))
            {
                return Results.Problem(
                    title: "Command bilgisi eksik.",
                    detail: "İşlem için geçerli Idempotency-Key ve correlation bilgisi gerekir.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.SetLocationActiveStateAsync(
                id,
                command,
                metadata,
                cancellationToken);

            return result switch
            {
                CommandResult.Succeeded => Results.NoContent(),
                CommandResult.NotFound => NotFound(),
                CommandResult.VersionConflict => Results.Problem(
                    title: "Kayıt güncellendi.",
                    detail: "Kayıt başka bir işlem tarafından değiştirildi; veriyi yenileyin.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            };
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationLocationsStatusUpdate);

        group.MapGet("/work-centers", async (
                IOrganizationService service,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.GetWorkCentersAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationWorkCentersRead);

        return endpoints;
    }

    private static IResult NotFound()
    {
        return Results.Problem(
            title: "Kayıt bulunamadı.",
            detail: "İstenen kayıt bulunamadı veya bu bağlamda kullanılamıyor.",
            statusCode: StatusCodes.Status404NotFound);
    }

    private static bool TryReadCommandMetadata(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        out CommandMetadata metadata)
    {
        const int maximumMetadataLength = 128;
        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString().Trim();
        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].ToString().Trim();

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = httpContext.TraceIdentifier;
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Length > maximumMetadataLength
            || correlationId.Length > maximumMetadataLength
            || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            metadata = null!;
            return false;
        }

        httpContext.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new CommandMetadata(
            companyContext.CompanyId,
            currentUser.UserId,
            correlationId,
            idempotencyKey);
        return true;
    }
}

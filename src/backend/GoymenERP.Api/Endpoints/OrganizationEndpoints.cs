using GoymenERP.Api.Multitenancy;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static class OrganizationEndpoints
{
    private const string IdempotencyHeaderName = "Idempotency-Key";

    private const int MaximumIdempotencyKeyLength = 128;

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
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(command.Reason)
                || command.Reason.Trim().Length > 1000)
            {
                return Results.Problem(
                    title: "Gerekçe gerekli.",
                    detail: "Kritik değişiklik için 1-1000 karakter arasında bir gerekçe girin.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!TryReadCommandMetadata(
                    httpContext,
                    currentUser,
                    companyContext,
                    correlationContext,
                    out var metadata))
            {
                return Results.Problem(
                    title: "Command bilgisi eksik.",
                    detail: "İşlem için geçerli Idempotency-Key ve correlation bilgisi gerekir.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var execution = await service.SetLocationActiveStateAsync(
                id,
                command,
                metadata,
                cancellationToken);

            if (execution.IsReplay)
            {
                httpContext.Response.Headers["Idempotency-Replayed"] = "true";
                httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] =
                    execution.OriginalCorrelationId;
            }

            return ToHttpResult(execution.Response);
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

    private static IResult ToHttpResult(CommandResponseSnapshot response)
    {
        return response.StatusCode == StatusCodes.Status204NoContent
            ? Results.NoContent()
            : Results.Problem(
                title: response.Title,
                detail: response.Detail,
                statusCode: response.StatusCode,
                extensions: response.Conflict is null
                    ? null
                    : new Dictionary<string, object?>
                    {
                        ["conflict"] = response.Conflict,
                    });
    }

    private static bool TryReadCommandMetadata(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        out CommandMetadata metadata)
    {
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey)
            || !correlationContext.IsResolved
            || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            metadata = null!;
            return false;
        }

        metadata = new CommandMetadata(
            companyContext.CompanyId,
            currentUser.UserId,
            correlationContext.CorrelationId,
            correlationContext.CausationId,
            correlationContext.SourceChannel,
            correlationContext.ClientIpAddress,
            correlationContext.UserAgent,
            idempotencyKey);
        return true;
    }

    private static bool TryReadIdempotencyKey(
        IHeaderDictionary headers,
        out string idempotencyKey)
    {
        if (!headers.TryGetValue(IdempotencyHeaderName, out var values)
            || values.Count != 1
            || values[0] is not { } rawValue
            || !TryCanonicalizeIdempotencyKey(rawValue, out idempotencyKey))
        {
            idempotencyKey = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryCanonicalizeIdempotencyKey(
        string rawValue,
        out string canonicalValue)
    {
        canonicalValue = rawValue.Trim();
        return canonicalValue.Length is > 0 and <= MaximumIdempotencyKeyLength
            && !canonicalValue.Contains(',', StringComparison.Ordinal);
    }
}

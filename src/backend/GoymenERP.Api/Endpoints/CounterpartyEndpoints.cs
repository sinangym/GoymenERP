using GoymenERP.Api.Multitenancy;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.Endpoints;

public static class CounterpartyEndpoints
{
    public static IEndpointRouteBuilder MapCounterpartyEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/counterparties")
            .RequireAuthorization()
            .AddEndpointFilter(ResolveOptionalCompanyContextAsync)
            .WithTags("Counterparty");

        group.MapGet("/", async (
            Guid? companyId,
            ICounterpartyReadService service,
            CancellationToken cancellationToken) =>
        {
            var parties = await service.GetAuthorizedPartiesAsync(
                companyId,
                cancellationToken);
            return Results.Ok(parties);
        });

        group.MapGet("/{partyId:guid}", async (
            Guid partyId,
            Guid? companyId,
            ICounterpartyReadService service,
            CancellationToken cancellationToken) =>
        {
            var party = await service.GetAuthorizedPartyAsync(
                partyId,
                companyId,
                cancellationToken);
            return party is null
                ? NotFound()
                : Results.Ok(party);
        });

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

        if (!Guid.TryParse(headerValue.ToString(), out var companyId)
            || companyId == Guid.Empty)
        {
            return Results.Problem(
                title: "Geçersiz şirket seçimi.",
                detail: "X-Company-Id geçerli ve boş olmayan bir şirket kimliği olmalıdır.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var currentUser = httpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!currentUser.CanAccessCompany(companyId))
        {
            return Results.Problem(
                title: "Erişim reddedildi.",
                detail: "Seçili şirket için erişim yetkiniz bulunmuyor.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        httpContext.RequestServices
            .GetRequiredService<ICompanyContext>()
            .Set(companyId);

        return await next(invocationContext);
    }

    private static IResult NotFound()
    {
        return Results.Problem(
            title: "Kayıt bulunamadı.",
            detail: "İstenen cari kayıt bulunamadı veya yetkili şirket kapsamınızda kullanılamıyor.",
            statusCode: StatusCodes.Status404NotFound);
    }
}

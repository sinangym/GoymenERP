using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static class CounterpartyEndpoints
{
    public static IEndpointRouteBuilder MapCounterpartyEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/counterparties")
            .RequireAuthorization()
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

    private static IResult NotFound()
    {
        return Results.Problem(
            title: "Kayıt bulunamadı.",
            detail: "İstenen cari kayıt bulunamadı veya yetkili şirket kapsamınızda kullanılamıyor.",
            statusCode: StatusCodes.Status404NotFound);
    }
}

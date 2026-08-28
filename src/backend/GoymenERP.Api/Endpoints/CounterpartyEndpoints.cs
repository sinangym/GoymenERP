using GoymenERP.Api.Multitenancy;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static class CounterpartyEndpoints
{
    public static IEndpointRouteBuilder MapCounterpartyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/cari");

        group.MapGet("/parties", async (
                ICounterpartyService service,
                int page = 1,
                int pageSize = 50,
                CancellationToken cancellationToken = default) =>
            {
                try
                {
                    return Results.Ok(await service.GetPartiesAsync(
                        page,
                        pageSize,
                        cancellationToken));
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    return BadRequest(exception.Message);
                }
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead);

        group.MapGet("/parties/{partyId:guid}", async (
                Guid partyId,
                ICounterpartyService service,
                CancellationToken cancellationToken) =>
            {
                var party = await service.GetPartyAsync(partyId, cancellationToken);
                return party is null ? NotFound() : Results.Ok(party);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead);

        group.MapPost("/parties", async (
                CreatePartyCommand command,
                HttpContext httpContext,
                ICounterpartyService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                CancellationToken cancellationToken) =>
            {
                if (!TryReadCommandMetadata(
                        httpContext,
                        currentUser,
                        companyContext,
                        correlationContext,
                        out var metadata))
                {
                    return BadRequest(
                        "Yazma işlemi için geçerli Idempotency-Key, kullanıcı ve korelasyon bağlamı zorunludur.");
                }

                CreatePartyExecutionResult execution;
                try
                {
                    execution = await service.CreatePartyAsync(
                        command,
                        metadata,
                        cancellationToken);
                }
                catch (ArgumentException exception)
                {
                    return BadRequest(exception.Message);
                }

                if (execution.IsReplay)
                {
                    httpContext.Response.Headers["Idempotency-Replayed"] = "true";
                    httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] =
                        execution.OriginalCorrelationId;
                }

                return execution.Result switch
                {
                    CreatePartyResult.Succeeded when execution.Response is not null =>
                        Results.Created(
                            $"/api/v1/cari/parties/{execution.Response.PartyId}",
                            execution.Response),
                    CreatePartyResult.IdempotencyConflict => Results.Problem(
                        title: "Idempotency-Key çakışması.",
                        detail: "Aynı Idempotency-Key farklı bir içerik veya kullanıcıyla tekrar kullanılamaz.",
                        statusCode: StatusCodes.Status409Conflict),
                    _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
                };
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesCreate);

        return endpoints;
    }

    private static IResult NotFound()
    {
        return Results.Problem(
            title: "Kayıt bulunamadı.",
            detail: "İstenen cari kaydı bulunamadı veya bu şirket bağlamında kullanılamıyor.",
            statusCode: StatusCodes.Status404NotFound);
    }

    private static IResult BadRequest(string detail)
    {
        return Results.Problem(
            title: "Geçersiz istek.",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static bool TryReadCommandMetadata(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        out CounterpartyCommandMetadata metadata)
    {
        const int maximumMetadataLength = 128;
        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString().Trim();

        if (string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Length > maximumMetadataLength
            || !companyContext.IsResolved
            || !correlationContext.IsResolved
            || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            metadata = null!;
            return false;
        }

        metadata = new CounterpartyCommandMetadata(
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
}

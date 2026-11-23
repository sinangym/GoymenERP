using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// FNS-D121: the cari card's IBAN list. 401/403/404/503 are code-less; 409/422 carry the command result with its code.
/// The idempotency header follows the Counterparty reader (trimmed, single value).
/// </summary>
public static partial class CounterpartyEndpoints
{
    private static void MapPartyIbanEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{partyId:guid}/ibans", async (Guid partyId, ICounterpartyPartyIbanService service, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0) return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
            var result = await service.ListAsync(partyId, cancellationToken);
            return result.Outcome switch
            {
                CounterpartyPartyIbanOutcomeV1.Succeeded => Results.Ok(result.List),
                CounterpartyPartyIbanOutcomeV1.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound),
                CounterpartyPartyIbanOutcomeV1.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
                _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
            };
        })
            .RequireCompanyContext()
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("ListCounterpartyPartyIbansV1")
            .Produces<CounterpartyPartyIbanListV1>(StatusCodes.Status200OK)
            .ProducesErpAuthenticatedProblems().ProducesErpBadRequest().AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable()
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode);

        group.MapPost("/{partyId:guid}/ibans", async (Guid partyId, AddCounterpartyPartyIbanV1 request,
            ICounterpartyPartyIbanService service, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0
                || !TryReadCommandMetadata(context, user, company, correlation, out var metadata))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
            return PartyIbanCommand(context, await service.AddAsync(partyId, request, metadata.IdempotencyKey,
                metadata.CorrelationId, metadata.CausationId, metadata.SourceChannel, metadata.ClientIpAddress,
                metadata.UserAgent, cancellationToken));
        })
            .RequireCompanyContext()
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("AddCounterpartyPartyIbanV1")
            .Accepts<AddCounterpartyPartyIbanV1>("application/json")
            .Produces<CounterpartyPartyIbanCommandResultV1>(StatusCodes.Status200OK)
            .Produces<CounterpartyPartyIbanCommandResultV1>(StatusCodes.Status409Conflict)
            .Produces<CounterpartyPartyIbanCommandResultV1>(StatusCodes.Status422UnprocessableEntity)
            .ProducesErpAuthenticatedProblems().ProducesErpBadRequest().AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable();

        group.MapPost("/{partyId:guid}/ibans/{ibanId:guid}/deactivate", async (Guid partyId, Guid ibanId,
            DeactivateCounterpartyPartyIbanV1 request, ICounterpartyPartyIbanService service, ICurrentUser user,
            ICompanyContext company, ICorrelationContext correlation, HttpContext context, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0
                || !TryReadCommandMetadata(context, user, company, correlation, out var metadata))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
            return PartyIbanCommand(context, await service.DeactivateAsync(partyId, ibanId, request, metadata.IdempotencyKey,
                metadata.CorrelationId, metadata.CausationId, metadata.SourceChannel, metadata.ClientIpAddress,
                metadata.UserAgent, cancellationToken));
        })
            .RequireCompanyContext()
            .RequiresNonEmptyGuidPathParameter("partyId")
            .RequiresNonEmptyGuidPathParameter("ibanId")
            .WithName("DeactivateCounterpartyPartyIbanV1")
            .Accepts<DeactivateCounterpartyPartyIbanV1>("application/json")
            .Produces<CounterpartyPartyIbanCommandResultV1>(StatusCodes.Status200OK)
            .Produces<CounterpartyPartyIbanCommandResultV1>(StatusCodes.Status409Conflict)
            .Produces<CounterpartyPartyIbanCommandResultV1>(StatusCodes.Status422UnprocessableEntity)
            .ProducesErpAuthenticatedProblems().ProducesErpBadRequest().AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody().ProducesErpCodeLessNotFound().ProducesErpServiceUnavailable();
    }

    private static IResult PartyIbanCommand(HttpContext context, CounterpartyPartyIbanCommandResultV1 result)
    {
        if (result.Replayed) context.Response.Headers["Idempotency-Replayed"] = "true";
        return result.Outcome switch
        {
            CounterpartyPartyIbanOutcomeV1.Succeeded => Results.Ok(result),
            CounterpartyPartyIbanOutcomeV1.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound),
            CounterpartyPartyIbanOutcomeV1.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
            CounterpartyPartyIbanOutcomeV1.VersionConflict or CounterpartyPartyIbanOutcomeV1.IdempotencyConflict =>
                Results.Json(result, statusCode: StatusCodes.Status409Conflict),
            CounterpartyPartyIbanOutcomeV1.ValidationFailed => Results.Json(result, statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }
}

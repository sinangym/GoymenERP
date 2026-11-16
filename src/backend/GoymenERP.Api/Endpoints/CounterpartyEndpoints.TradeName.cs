using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// F-027 / C-02 Cari card name edit (the direct part). Under the Counterparty group, so the client-contract filter,
/// company context and private no-store apply as for every Cari route.
/// <list type="bullet">
/// <item><c>GET …/{partyId}/trade-name</c> — the card code, the official legal name (read-only here), the trade name,
/// the PARTY version as decimal text and whether this reader may change the trade name.</item>
/// <item><c>PUT …/{partyId}/trade-name</c> — change or clear the trade name (204). One Idempotency-Key; the stored
/// terminal answers (204, 404, 409) replay for the same key and body.</item>
/// </list>
/// The legal name never travels here: it has its own route and permission (<c>…/legal-name</c>, ALS-D111/D113).
/// </summary>
public static partial class CounterpartyEndpoints
{
    private static void MapTradeNameEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{partyId:guid}/trade-name", async (
            Guid partyId, ICounterpartyTradeNameService service,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari ad isteği geçersiz.",
                    "Şirket kapsamı yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
            }

            try
            {
                var view = await service.GetTradeNameAsync(partyId, cancellationToken);
                return view is null ? NotFound() : Results.Ok(view);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("GetCounterpartyTradeNameV1")
            .Produces<CounterpartyTradeNameView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/{partyId:guid}/trade-name", async (
            Guid partyId, UpdateCounterpartyTradeName command,
            ICounterpartyTradeNameService service, ICurrentUser user,
            ICompanyContext company, ICorrelationContext correlation, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari ad isteği geçersiz.",
                    "Sorgu parametresi kabul edilmez; şirket kapsamı sunucu tarafından belirlenir.");
            }

            if (!TryReadCommandMetadata(context, user, company, correlation, out var metadata))
            {
                return InvalidCommandMetadata();
            }

            CounterpartyCommandExecution execution;
            try
            {
                execution = await service.UpdateTradeNameAsync(partyId, command, metadata, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ArgumentException exception)
            {
                return ValidationProblem("Cari ticari ad geçersiz.", exception.Message);
            }

            ApplyReplayHeaders(context, execution);
            // Do not serialize an Int64 version through a JavaScript number; the fresh GET supplies it as text.
            return execution.Response.Result == CounterpartyCommandResult.VersionConflict
                ? CounterpartyProblem(StatusCodes.Status409Conflict, CounterpartyProblemCodesV1.VersionConflict,
                    execution.Response.Title, execution.Response.Detail)
                : ToHttpResult(execution.Response);
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesUpdateBasic)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("UpdateCounterpartyTradeNameV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<UpdateCounterpartyTradeName>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyProblemCodesV1.VersionConflict,
                CounterpartyProblemCodesV1.IdempotencyConflict,
                CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();
    }
}

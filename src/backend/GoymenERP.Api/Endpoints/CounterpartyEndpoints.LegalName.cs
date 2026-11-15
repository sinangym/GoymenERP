using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// ALS-D95 (confirmed by SINAN-CEVAP 19.22) Cari official legal name (resmi/hukuki unvan) change. Under the Counterparty
/// group, so the client-contract filter, company context and private no-store apply as for every Cari route.
/// <list type="bullet">
/// <item><c>GET …/{partyId}/legal-name</c> — the card code, the legal name, the PARTY version as decimal text and
/// whether this reader holds the separate legal-name permission.</item>
/// <item><c>PUT …/{partyId}/legal-name</c> — change the legal name in one step, without approval (204). One
/// Idempotency-Key; the stored terminal answers (204, 404, 409) replay for the same key and body. The audit event carries
/// the old and the new legal name.</item>
/// </list>
/// The trade name stays on <c>…/trade-name</c> (ALS-D97); VKN/TCKN, MERSİS and IBAN stay maker-checker.
/// </summary>
public static partial class CounterpartyEndpoints
{
    private static void MapLegalNameEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{partyId:guid}/legal-name", async (
            Guid partyId, ICounterpartyLegalNameService service,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari resmi unvan isteği geçersiz.",
                    "Şirket kapsamı yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
            }

            try
            {
                var view = await service.GetLegalNameAsync(partyId, cancellationToken);
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
            .WithName("GetCounterpartyLegalNameV1")
            .Produces<CounterpartyLegalNameView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/{partyId:guid}/legal-name", async (
            Guid partyId, UpdateCounterpartyLegalName command,
            ICounterpartyLegalNameService service, ICurrentUser user,
            ICompanyContext company, ICorrelationContext correlation, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari resmi unvan isteği geçersiz.",
                    "Sorgu parametresi kabul edilmez; şirket kapsamı sunucu tarafından belirlenir.");
            }

            if (!TryReadCommandMetadata(context, user, company, correlation, out var metadata))
            {
                return InvalidCommandMetadata();
            }

            CounterpartyCommandExecution execution;
            try
            {
                execution = await service.UpdateLegalNameAsync(partyId, command, metadata, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ArgumentException exception)
            {
                return ValidationProblem("Cari resmi unvan geçersiz.", exception.Message);
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
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesLegalNameUpdate)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("UpdateCounterpartyLegalNameV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<UpdateCounterpartyLegalName>("application/json")
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

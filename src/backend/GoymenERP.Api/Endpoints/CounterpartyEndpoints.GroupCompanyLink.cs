using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// YOL-D31 (WP14): the party card's group-company link. <c>GET …/{partyId}/group-company-link</c> answers the link, the PARTY
/// version as decimal text and whether this reader may change it; <c>PUT</c> sets or clears it (204, one Idempotency-Key; the
/// stored terminal answers replay). The target must be an active group company; a tax-number match never links.
/// </summary>
public static partial class CounterpartyEndpoints
{
    private static void MapGroupCompanyLinkEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{partyId:guid}/group-company-link", async (
            Guid partyId, ICounterpartyGroupCompanyLinkService service,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari grup şirketi isteği geçersiz.",
                    "Şirket kapsamı yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
            }

            try
            {
                var view = await service.GetGroupCompanyLinkAsync(partyId, cancellationToken);
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
            .WithName("GetCounterpartyGroupCompanyLinkV1")
            .Produces<CounterpartyGroupCompanyLinkView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/{partyId:guid}/group-company-link", async (
            Guid partyId, UpdateCounterpartyGroupCompanyLink command,
            ICounterpartyGroupCompanyLinkService service, ICurrentUser user,
            ICompanyContext company, ICorrelationContext correlation, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari grup şirketi isteği geçersiz.",
                    "Sorgu parametresi kabul edilmez; şirket kapsamı sunucu tarafından belirlenir.");
            }

            if (!TryReadCommandMetadata(context, user, company, correlation, out var metadata))
            {
                return InvalidCommandMetadata();
            }

            CounterpartyCommandExecution execution;
            try
            {
                execution = await service.UpdateGroupCompanyLinkAsync(partyId, command, metadata, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ArgumentException exception)
            {
                return ValidationProblem("Cari grup şirketi bağlantısı geçersiz.", exception.Message);
            }
            catch (InvalidOperationException)
            {
                // The company directory is not composed: the target cannot be checked, nothing is linked.
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            ApplyReplayHeaders(context, execution);
            return execution.Response.Result == CounterpartyCommandResult.VersionConflict
                ? CounterpartyProblem(StatusCodes.Status409Conflict, CounterpartyProblemCodesV1.VersionConflict,
                    execution.Response.Title, execution.Response.Detail)
                : ToHttpResult(execution.Response);
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesGroupCompanyLinkUpdate)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("UpdateCounterpartyGroupCompanyLinkV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<UpdateCounterpartyGroupCompanyLink>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyProblemCodesV1.VersionConflict,
                CounterpartyProblemCodesV1.IdempotencyConflict,
                CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }
}

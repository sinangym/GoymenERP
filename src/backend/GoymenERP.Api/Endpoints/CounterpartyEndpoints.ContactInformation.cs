using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class CounterpartyEndpoints
{
    private static void MapContactInformationEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{partyId:guid}/contact-information", async (
            Guid partyId, ICounterpartyContactInformationService service,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari iletişim isteği geçersiz.",
                    "Şirket kapsamı yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
            }

            var information = await service.GetContactInformationAsync(partyId, cancellationToken);
            return information is null ? NotFound() : Results.Ok(information);
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("GetCounterpartyContactInformationV1")
            .Produces<CounterpartyContactInformationView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/{partyId:guid}/contact-information", async (
            Guid partyId, UpdateCounterpartyContactInformation command,
            ICounterpartyContactInformationService service, ICurrentUser user,
            ICompanyContext company, ICorrelationContext correlation, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari iletişim isteği geçersiz.",
                    "Sorgu parametresi kabul edilmez; şirket kapsamı sunucu tarafından belirlenir.");
            }

            if (!TryReadCommandMetadata(context, user, company, correlation, out var metadata))
            {
                return InvalidCommandMetadata();
            }

            CounterpartyCommandExecution execution;
            try
            {
                execution = await service.UpdateContactInformationAsync(
                    partyId, command, metadata, cancellationToken);
            }
            catch (ArgumentException exception)
            {
                // Field paths are stable; no submitted values are echoed.
                return ValidationProblem("Cari iletişim bilgisi geçersiz.", exception.Message);
            }

            ApplyReplayHeaders(context, execution);
            // Do not serialize an Int64 version through a JavaScript number.
            // The fresh GET supplies the exact current account version as text.
            return execution.Response.Result == CounterpartyCommandResult.VersionConflict
                ? CounterpartyProblem(StatusCodes.Status409Conflict, CounterpartyProblemCodesV1.VersionConflict,
                    execution.Response.Title, execution.Response.Detail)
                : ToHttpResult(execution.Response);
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesUpdateBasic)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("UpdateCounterpartyContactInformationV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<UpdateCounterpartyContactInformation>("application/json")
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

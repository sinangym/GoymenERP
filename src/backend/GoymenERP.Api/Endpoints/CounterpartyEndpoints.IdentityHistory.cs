using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// KLN-D38 (Sinan, 2026-11-14) Cari VKN/TCKN change flow. Under the Counterparty group, so the client-contract filter,
/// company context and private no-store apply as for every Cari route.
/// <list type="bullet">
/// <item><c>GET …/{partyId}/identity-history</c> — the dated VKN/TCKN, legal name and e-belge status history, the
/// successions of the active company, the predecessor account version as decimal text and whether this reader may record
/// a succession.</item>
/// <item><c>PUT …/{partyId}/succession</c> — a NEW legal person's cari succeeds this cari in the active company: this
/// cari's account becomes passive and the link is stored with its legal basis (204). It moves no balance; the balance
/// moves only by an approved transfer voucher through a Finance port. One Idempotency-Key; the stored terminal answers
/// (204, 404, 409, 422) replay for the same key and body.</item>
/// </list>
/// A wrong VKN/TCKN on the same cari stays the CARI-DEC-004 change request (…/tax-identity); OPEN-030 is out of scope.
/// </summary>
public static partial class CounterpartyEndpoints
{
    private static void MapIdentityHistoryEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{partyId:guid}/identity-history", async (
            Guid partyId, ICounterpartyIdentityHistoryService service,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari kimlik geçmişi isteği geçersiz.",
                    "Şirket kapsamı yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
            }

            try
            {
                var view = await service.GetIdentityHistoryAsync(partyId, cancellationToken);
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
            .WithName("GetCounterpartyIdentityHistoryV1")
            .Produces<CounterpartyIdentityHistoryView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/{partyId:guid}/succession", async (
            Guid partyId, RecordCounterpartyPartySuccession command,
            ICounterpartyIdentityHistoryService service, ICurrentUser user,
            ICompanyContext company, ICorrelationContext correlation, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari halef kaydı isteği geçersiz.",
                    "Sorgu parametresi kabul edilmez; şirket kapsamı sunucu tarafından belirlenir.");
            }

            if (!TryReadCommandMetadata(context, user, company, correlation, out var metadata))
            {
                return InvalidCommandMetadata();
            }

            CounterpartyCommandExecution execution;
            try
            {
                execution = await service.RecordPartySuccessionAsync(partyId, command, metadata, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ArgumentException exception)
            {
                return ValidationProblem("Cari halef kaydı geçersiz.", exception.Message);
            }
            catch (CounterpartyBusinessDayUnavailableException)
            {
                // The Europe/Istanbul business day could not be resolved; nothing was stored, retry with the same key.
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            ApplyReplayHeaders(context, execution);
            return ToSuccessionHttpResult(execution.Response);
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(CounterpartyPartySuccessionPermissionsV1.Record)
            .RequireAuthorization(ErpPermissions.CounterpartyAccountsStatusUpdate)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("RecordCounterpartyPartySuccessionV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordCounterpartyPartySuccession>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(
                CounterpartyProblemCodesV1.VersionConflict,
                CounterpartyProblemCodesV1.IdempotencyConflict,
                CounterpartyProblemCodesV1.SuccessionSuccessorInactive,
                CounterpartyProblemCodesV1.SuccessionCycle,
                CounterpartyProblemCodesV1.SuccessionAlreadyRecorded,
                CounterpartyClientContract.RequiredCode,
                CounterpartyClientContract.UnsupportedCode)
            .ProducesErpUnprocessableEntity(
                CounterpartyProblemCodesV1.SuccessionSameParty,
                CounterpartyProblemCodesV1.SuccessionSameTaxIdentity)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>The stored answer as HTTP; the version conflict carries no Int64 <c>conflict</c> member (the GET has it as text).</summary>
    private static IResult ToSuccessionHttpResult(CounterpartyCommandResponse response)
    {
        if (response.Result == CounterpartyCommandResult.Succeeded
            && response.StatusCode == StatusCodes.Status204NoContent)
        {
            return Results.NoContent();
        }

        var code = CounterpartyProblemCodesV1.RequiredForProblem(response.Result);
        var expectedStatus = response.Result switch
        {
            CounterpartyCommandResult.NotFound => StatusCodes.Status404NotFound,
            CounterpartyCommandResult.SuccessionSameParty
                or CounterpartyCommandResult.SuccessionSameTaxIdentity => StatusCodes.Status422UnprocessableEntity,
            CounterpartyCommandResult.VersionConflict
                or CounterpartyCommandResult.IdempotencyConflict
                or CounterpartyCommandResult.SuccessionSuccessorInactive
                or CounterpartyCommandResult.SuccessionCycle
                or CounterpartyCommandResult.SuccessionAlreadyRecorded => StatusCodes.Status409Conflict,
            _ => throw new InvalidOperationException($"Halef kaydı komutu bu sonucu üretmez: {response.Result}."),
        };
        if (response.StatusCode != expectedStatus)
        {
            throw new InvalidOperationException("Halef kaydı terminal sonucu HTTP durumuyla eşleşmiyor.");
        }

        return CounterpartyProblem(response.StatusCode, code, response.Title, response.Detail);
    }
}

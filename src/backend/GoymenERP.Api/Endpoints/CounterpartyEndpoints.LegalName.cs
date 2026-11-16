using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// ALS-D111 + ALS-D113 (replacing the one-step ALS-D95/ALS-D110 PUT) Cari official legal name (resmi/hukuki unvan) change
/// by maker-checker. Under the Counterparty group, so the client-contract filter, company context and private no-store
/// apply as for every Cari route.
/// <list type="bullet">
/// <item><c>GET …/{partyId}/legal-name</c> — the card code, the legal name, the PARTY version as decimal text, the pending
/// proposal, the last decided one and what this reader may do (propose / approve / is the proposer).</item>
/// <item><c>POST …/{partyId}/legal-name/change-requests</c> — propose a new name with a reason (204); the name does not
/// change.</item>
/// <item><c>POST …/{partyId}/legal-name/change-decisions</c> — another person approves or rejects (204); only an
/// approval writes the new name.</item>
/// </list>
/// One Idempotency-Key per command; the stored terminal answers (204, 404, 409) replay for the same key and body. The
/// trade name stays on <c>…/trade-name</c> (ALS-D97); VKN/TCKN keeps its own maker-checker flow.
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

        MapLegalNameCommand<ProposeCounterpartyLegalNameChange>(group,
            "/{partyId:guid}/legal-name/change-requests",
            "ProposeCounterpartyLegalNameChangeV1", CounterpartyLegalNamePermissionsV1.Propose,
            [
                CounterpartyProblemCodesV1.LegalNameUnchanged,
                CounterpartyProblemCodesV1.LegalNameChangePending,
            ],
            (service, partyId, command, metadata, cancellationToken) =>
                service.ProposeLegalNameChangeAsync(partyId, command, metadata, cancellationToken));
        MapLegalNameCommand<DecideCounterpartyLegalNameChange>(group,
            "/{partyId:guid}/legal-name/change-decisions",
            "DecideCounterpartyLegalNameChangeV1", CounterpartyLegalNamePermissionsV1.Approve,
            [
                CounterpartyProblemCodesV1.LegalNameChangeNotPending,
                CounterpartyProblemCodesV1.LegalNameChangeStale,
                CounterpartyProblemCodesV1.LegalNameSelfDecision,
            ],
            (service, partyId, command, metadata, cancellationToken) =>
                service.DecideLegalNameChangeAsync(partyId, command, metadata, cancellationToken));
    }

    private static void MapLegalNameCommand<TCommand>(
        RouteGroupBuilder group,
        string pattern,
        string name,
        string permission,
        string[] own409Codes,
        Func<ICounterpartyLegalNameService, Guid, TCommand, CounterpartyCommandMetadata, CancellationToken,
            Task<CounterpartyCommandExecution>> execute)
        where TCommand : class
    {
        group.MapPost(pattern, async (
            Guid partyId, TCommand command,
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
                execution = await execute(service, partyId, command, metadata, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ArgumentException exception)
            {
                return ValidationProblem("Cari resmi unvan isteği geçersiz.", exception.Message);
            }

            ApplyReplayHeaders(context, execution);
            return ToLegalNameHttpResult(execution.Response);
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(permission)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName(name)
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<TCommand>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(
            [
                CounterpartyProblemCodesV1.VersionConflict,
                CounterpartyProblemCodesV1.IdempotencyConflict,
                CounterpartyClientContract.RequiredCode,
                CounterpartyClientContract.UnsupportedCode,
                .. own409Codes,
            ])
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// The stored answer as HTTP. The version conflict carries no <c>conflict</c> member: an Int64 version must not
    /// travel as a JavaScript number, and the fresh GET supplies the current version as text.
    /// </summary>
    private static IResult ToLegalNameHttpResult(CounterpartyCommandResponse response)
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
            CounterpartyCommandResult.VersionConflict
                or CounterpartyCommandResult.IdempotencyConflict
                or CounterpartyCommandResult.LegalNameUnchanged
                or CounterpartyCommandResult.LegalNameChangePending
                or CounterpartyCommandResult.LegalNameChangeNotPending
                or CounterpartyCommandResult.LegalNameChangeStale
                or CounterpartyCommandResult.LegalNameSelfDecision => StatusCodes.Status409Conflict,
            _ => throw new InvalidOperationException("Resmi unvan komutu bu sonucu üretmez."),
        };
        if (response.StatusCode != expectedStatus)
        {
            throw new InvalidOperationException("Cari resmi unvan terminal sonucu HTTP durumuyla eşleşmiyor.");
        }

        return CounterpartyProblem(response.StatusCode, code, response.Title, response.Detail);
    }
}

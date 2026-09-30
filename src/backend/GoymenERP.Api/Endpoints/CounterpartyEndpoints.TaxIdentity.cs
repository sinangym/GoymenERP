using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.Contracts;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// AT-009 VKN/TCKN on the Cari card (user decision 2026-09-30). Under the Counterparty group, so the client-contract
/// filter, company context and private no-store apply as for every Cari route.
/// <list type="bullet">
/// <item><c>GET …/{partyId}/tax-identity</c> — the number, its kind, the duplicate WARNING (other cards of the active
/// company with the same number), the pending change and the last decided one, and what this reader may do.</item>
/// <item><c>GET …/{partyId}/tax-identity/duplicates?number=</c> — the same warning for a candidate before saving; an
/// invalid number is 400 <c>tax_identity_invalid</c>.</item>
/// <item><c>PUT …/{partyId}/tax-identity</c> — first entry (204). A party that already has a number answers 409
/// <c>tax_identity_change_requires_approval</c>.</item>
/// <item><c>PUT …/{partyId}/tax-identity/change-request</c> — propose a change (204); it stays pending.</item>
/// <item><c>PUT …/{partyId}/tax-identity/change-request/decision</c> — another person approves or rejects (204); the
/// proposer is refused 409 <c>tax_identity_self_decision</c> (maker-checker).</item>
/// </list>
/// Commands carry one Idempotency-Key and the PARTY version as decimal text; the stored terminal answers (204, 404,
/// 409) replay for the same key and body.
/// </summary>
public static partial class CounterpartyEndpoints
{
    private static readonly string[] TaxIdentityCommon409Codes =
    [
        CounterpartyProblemCodesV1.VersionConflict,
        CounterpartyProblemCodesV1.IdempotencyConflict,
        CounterpartyClientContract.RequiredCode,
        CounterpartyClientContract.UnsupportedCode,
    ];

    private static void MapTaxIdentityEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{partyId:guid}/tax-identity", async (
            Guid partyId, ICounterpartyTaxIdentityService service,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari vergi/kimlik isteği geçersiz.",
                    "Şirket kapsamı yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
            }

            try
            {
                var view = await service.GetTaxIdentityAsync(partyId, cancellationToken);
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
            .WithName("GetCounterpartyTaxIdentityV1")
            .Produces<CounterpartyTaxIdentityView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{partyId:guid}/tax-identity/duplicates", async (
            Guid partyId, string? number, ICounterpartyTaxIdentityService service,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            var query = context.Request.Query;
            if (query.Count != 1 || !query.TryGetValue("number", out var numbers) || numbers.Count != 1)
            {
                return ValidationProblem("Cari vergi/kimlik kontrolü geçersiz.",
                    "Yalnız tek bir 'number' sorgu parametresi kabul edilir.");
            }

            try
            {
                var check = await service.CheckDuplicatesAsync(partyId, number ?? string.Empty, cancellationToken);
                return check is null ? NotFound() : Results.Ok(check);
            }
            catch (CounterpartyTaxIdentityInvalidException exception)
            {
                return TaxIdentityInvalid(exception.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("CheckCounterpartyTaxIdentityDuplicatesV1")
            .Produces<CounterpartyTaxIdentityDuplicateCheck>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed, CounterpartyProblemCodesV1.TaxIdentityInvalid)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();

        MapTaxIdentityCommand<EnterCounterpartyTaxIdentity>(group, "/{partyId:guid}/tax-identity",
            "EnterCounterpartyTaxIdentityV1", CounterpartyTaxIdentityPermissionsV1.Update, takesNumber: true,
            [CounterpartyProblemCodesV1.TaxIdentityChangeRequiresApproval],
            (service, partyId, command, metadata, cancellationToken) =>
                service.EnterTaxIdentityAsync(partyId, command, metadata, cancellationToken));
        MapTaxIdentityCommand<RequestCounterpartyTaxIdentityChange>(group, "/{partyId:guid}/tax-identity/change-request",
            "RequestCounterpartyTaxIdentityChangeV1", CounterpartyTaxIdentityPermissionsV1.Update, takesNumber: true,
            [
                CounterpartyProblemCodesV1.TaxIdentityNotSet,
                CounterpartyProblemCodesV1.TaxIdentityUnchanged,
                CounterpartyProblemCodesV1.TaxIdentityChangePending,
            ],
            (service, partyId, command, metadata, cancellationToken) =>
                service.RequestTaxIdentityChangeAsync(partyId, command, metadata, cancellationToken));
        MapTaxIdentityCommand<DecideCounterpartyTaxIdentityChange>(group,
            "/{partyId:guid}/tax-identity/change-request/decision",
            "DecideCounterpartyTaxIdentityChangeV1", CounterpartyTaxIdentityPermissionsV1.Approve, takesNumber: false,
            [
                CounterpartyProblemCodesV1.TaxIdentityChangeNotPending,
                CounterpartyProblemCodesV1.TaxIdentityChangeStale,
                CounterpartyProblemCodesV1.TaxIdentitySelfDecision,
            ],
            (service, partyId, command, metadata, cancellationToken) =>
                service.DecideTaxIdentityChangeAsync(partyId, command, metadata, cancellationToken));
    }

    private static void MapTaxIdentityCommand<TCommand>(
        RouteGroupBuilder group,
        string pattern,
        string name,
        string permission,
        bool takesNumber,
        string[] own409Codes,
        Func<ICounterpartyTaxIdentityService, Guid, TCommand, CounterpartyCommandMetadata, CancellationToken,
            Task<CounterpartyCommandExecution>> execute)
        where TCommand : class
    {
        group.MapPut(pattern, async (
            Guid partyId, TCommand command,
            ICounterpartyTaxIdentityService service, ICurrentUser user,
            ICompanyContext company, ICorrelationContext correlation, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari vergi/kimlik isteği geçersiz.",
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
            catch (CounterpartyTaxIdentityInvalidException exception)
            {
                return TaxIdentityInvalid(exception.Message);
            }
            catch (ArgumentException exception)
            {
                return ValidationProblem("Cari vergi/kimlik isteği geçersiz.", exception.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }

            ApplyReplayHeaders(context, execution);
            return ToTaxIdentityHttpResult(execution.Response);
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
            .ProducesErpBadRequest(takesNumber
                ? [CounterpartyProblemCodesV1.ValidationFailed, CounterpartyProblemCodesV1.CommandMetadataInvalid,
                    CounterpartyProblemCodesV1.TaxIdentityInvalid]
                : [CounterpartyProblemCodesV1.ValidationFailed, CounterpartyProblemCodesV1.CommandMetadataInvalid])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict([.. TaxIdentityCommon409Codes, .. own409Codes])
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// The stored answer as HTTP. The version conflict carries no <c>conflict</c> member: an Int64 version must not
    /// travel as a JavaScript number, and the fresh GET supplies the current version as text.
    /// </summary>
    private static IResult ToTaxIdentityHttpResult(CounterpartyCommandResponse response)
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
            CounterpartyCommandResult.AlreadyLinked => throw new InvalidOperationException(
                "Vergi/kimlik komutu bağlama sonucu üretmez."),
            _ => StatusCodes.Status409Conflict,
        };
        if (response.StatusCode != expectedStatus)
        {
            throw new InvalidOperationException("Cari vergi/kimlik terminal sonucu HTTP durumuyla eşleşmiyor.");
        }

        return CounterpartyProblem(response.StatusCode, code, response.Title, response.Detail);
    }

    private static IResult TaxIdentityInvalid(string detail) => CounterpartyProblem(
        StatusCodes.Status400BadRequest,
        CounterpartyProblemCodesV1.TaxIdentityInvalid,
        "Vergi/kimlik numarası geçersiz.",
        detail);
}

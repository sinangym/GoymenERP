using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// SAT-D102 / SAT-D119 / SAT-D120 e-belge profile on the Cari card. The profile is the customer's group-level,
/// VKN-keyed record (no CompanyId), reached ONLY through the active company's account for the party. Under the
/// Counterparty group, so the client-contract filter, company context and private no-store apply as for every Cari route.
/// <list type="bullet">
/// <item><c>GET …/{partyId}/company-account/e-document</c> — the VKN-shared e-Fatura / e-Arşiv status (Unknown until
/// checked), when it was checked (who, only inside the checking company), the scenario override, the active company's
/// Settings code list and default for today, the effective scenario (override → Settings default → Unresolved), and the
/// fence: the PROFILE version and the opaque profile token.</item>
/// <item><c>PUT …/{partyId}/company-account/e-document/status</c> — record the status a user checked (204).</item>
/// <item><c>PUT …/{partyId}/company-account/e-document/scenario</c> — set a listed scenario code as the customer's
/// override, or clear it with an explicit null (204).</item>
/// </list>
/// Commands carry one Idempotency-Key and the fence the read returned (PROFILE version as decimal text, "0" creates,
/// plus the profile token); the stored terminal answers replay for the same key and body. A party with no VKN is 422; a
/// read of another profile than the one the client saw (e.g. after a VKN change) is 409. A Settings read that fails
/// while validating a new override is 503 and stores nothing.
/// </summary>
public static partial class CounterpartyEndpoints
{
    private const string EDocumentPath = "/{partyId:guid}/company-account/e-document";

    private static readonly string[] EDocumentCommon409Codes =
    [
        CounterpartyProblemCodesV1.IdempotencyConflict,
        CounterpartyClientContract.RequiredCode,
        CounterpartyClientContract.UnsupportedCode,
        CounterpartyProblemCodesV1.VersionConflict,
    ];

    private static void MapEDocumentProfileEndpoints(RouteGroupBuilder group)
    {
        group.MapGet(EDocumentPath, async (
            Guid partyId, ICounterpartyEDocumentProfileService service, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari e-belge isteği geçersiz.",
                    "Şirket kapsamı yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
            }

            try
            {
                var view = await service.GetAccountEDocumentProfileAsync(partyId, cancellationToken);
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
            .WithName("GetCounterpartyAccountEDocumentProfileV1")
            .Produces<CounterpartyAccountEDocumentView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();

        group.MapPut(EDocumentPath + "/status", async (
            Guid partyId, RecordCounterpartyEDocumentStatus command, ICounterpartyEDocumentProfileService service,
            ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, HttpContext context,
            CancellationToken cancellationToken) =>
            await ExecuteEDocumentCommandAsync(context, user, company, correlation, metadata =>
                service.RecordAccountEDocumentStatusAsync(partyId, command, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(CounterpartyEDocumentPermissionsV1.Update)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("RecordCounterpartyAccountEDocumentStatusV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordCounterpartyEDocumentStatus>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(EDocumentCommon409Codes)
            .ProducesErpUnprocessableEntity(
                CounterpartyProblemCodesV1.EDocumentAccountInactive,
                CounterpartyProblemCodesV1.EDocumentTaxIdentityRequired)
            .ProducesErpAuthenticatedProblems();

        group.MapPut(EDocumentPath + "/scenario", async (
            Guid partyId, SetCounterpartyEInvoiceScenarioOverride command, ICounterpartyEDocumentProfileService service,
            ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, HttpContext context,
            CancellationToken cancellationToken) =>
            await ExecuteEDocumentCommandAsync(context, user, company, correlation, metadata =>
                service.SetAccountEInvoiceScenarioOverrideAsync(partyId, command, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(CounterpartyEDocumentPermissionsV1.Update)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("SetCounterpartyAccountEInvoiceScenarioV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity)
            .Accepts<SetCounterpartyEInvoiceScenarioOverride>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(EDocumentCommon409Codes)
            .ProducesErpUnprocessableEntity(
                CounterpartyProblemCodesV1.EInvoiceScenarioCodeListMissing,
                CounterpartyProblemCodesV1.EInvoiceScenarioNotListed,
                CounterpartyProblemCodesV1.EDocumentAccountInactive,
                CounterpartyProblemCodesV1.EDocumentTaxIdentityRequired)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static async Task<IResult> ExecuteEDocumentCommandAsync(
        HttpContext context,
        ICurrentUser user,
        ICompanyContext company,
        ICorrelationContext correlation,
        Func<CounterpartyCommandMetadata, Task<CounterpartyCommandExecution>> execute)
    {
        if (context.Request.Query.Count != 0)
        {
            return ValidationProblem("Cari e-belge isteği geçersiz.",
                "Sorgu parametresi kabul edilmez; şirket kapsamı sunucu tarafından belirlenir.");
        }

        if (!TryReadCommandMetadata(context, user, company, correlation, out var metadata))
        {
            return InvalidCommandMetadata();
        }

        CounterpartyCommandExecution execution;
        try
        {
            execution = await execute(metadata);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem("Cari e-belge isteği geçersiz.", exception.Message);
        }
        catch (CounterpartySettingsUnavailableException)
        {
            // Nothing was stored: the platform's 503 service_unavailable, retried with the same key.
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        ApplyReplayHeaders(context, execution);
        return ToEDocumentHttpResult(execution.Response);
    }

    /// <summary>The stored answer as HTTP; the version conflict carries no Int64 <c>conflict</c> member (the GET has it as text).</summary>
    private static IResult ToEDocumentHttpResult(CounterpartyCommandResponse response)
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
            CounterpartyCommandResult.EInvoiceScenarioCodeListMissing
                or CounterpartyCommandResult.EInvoiceScenarioNotListed
                or CounterpartyCommandResult.EDocumentTaxIdentityRequired
                or CounterpartyCommandResult.EDocumentAccountInactive => StatusCodes.Status422UnprocessableEntity,
            CounterpartyCommandResult.VersionConflict
                or CounterpartyCommandResult.IdempotencyConflict => StatusCodes.Status409Conflict,
            _ => throw new InvalidOperationException($"e-Belge komutu bu sonucu üretmez: {response.Result}."),
        };
        if (response.StatusCode != expectedStatus)
        {
            throw new InvalidOperationException("e-Belge terminal sonucu HTTP durumuyla eşleşmiyor.");
        }

        return CounterpartyProblem(response.StatusCode, code, response.Title, response.Detail);
    }
}

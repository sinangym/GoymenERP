using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class FinanceEndpoints
{
    public const string DraftContextPath = "/api/v1/finance/manual-receipts/draft-context";
    public const string ManualReceiptsPath = "/api/v1/finance/manual-receipts";
    public const string DraftsPath = "/api/v1/finance/manual-receipts/drafts";
    public const string BankAccountsPath = "/api/v1/finance/bank-accounts";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;
    private static readonly JsonSerializerOptions ContractJson = FinanceJson.CreateOptions();

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var handler = endpoints.MapGet(DraftContextPath, async (
            [FromQuery] Guid treasuryAccountId,
            [FromQuery] Guid partyId,
            [FromQuery] Guid companyAccountId,
            [FromQuery] DateOnly occurredOn,
            IFinanceDraftContextEndpointService service,
            ICorrelationContext correlationContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "private, no-store";
            httpContext.Response.Headers.Pragma = "no-cache";
            // The binder above already refused a missing, repeated or impossible date. It also
            // accepts lenient forms (month-first, unpadded, a dropped time part); only the exact
            // ISO text may reach the reader, as the contract's format: date says.
            var rawOccurredOn = httpContext.Request.Query["occurredOn"];
            if (rawOccurredOn.Count != 1 || !FinanceIsoDate.TryParse(rawOccurredOn[0], out var isoOccurredOn)
                || isoOccurredOn != occurredOn)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.GetAsync(
                new(treasuryAccountId, partyId, companyAccountId, occurredOn),
                correlationContext.CorrelationId,
                cancellationToken);
            return result.Outcome switch
            {
                DraftReadOutcome.Found when result.Resource is not null => Results.Ok(result.Resource),
                DraftReadOutcome.InvalidRequest => Results.Problem(statusCode: StatusCodes.Status400BadRequest),
                DraftReadOutcome.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
                DraftReadOutcome.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound),
                DraftReadOutcome.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => throw new InvalidOperationException(
                    "Finance draft context returned an invalid outcome/resource combination."),
            };
        });

        handler.WithName("GetFinanceManualReceiptDraftContextV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<DraftReadContextView>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        MapManualReceiptEndpoints(endpoints);
        return endpoints;
    }

    /// <summary>
    /// FIN-T04b: the manual-receipt draft over HTTP, faithful to the #258 bodies. The company is only
    /// the server context (<c>X-Company-Id</c> + access); the actor and correlation are the host's.
    /// <list type="bullet">
    /// <item><b>Save</b> returns the #258 <c>CommandResult</c> for the outcomes a client acts on: Succeeded
    /// 200, ValidationFailed 422 (its issues), VersionConflict / IdempotencyConflict 409. Denied and
    /// Unavailable are the platform problems 403 <c>forbidden</c> and 503 <c>service_unavailable</c>. A replay
    /// (ADR-011) carries <c>Idempotency-Replayed</c> and <c>Idempotency-Original-Correlation-Id</c>; only a stored
    /// success replays until the terminal replay of §7 (FIN-UI-1).</item>
    /// <item><b>Reads</b> return the resource on 200; NotFound 404, Denied 403 and Unavailable 503 are
    /// platform problems. A malformed or foreign list cursor is 400.</item>
    /// </list>
    /// Permission is not a route policy: the services ask the Finance permission port afresh at every
    /// fence (#276), so a revocation in flight is seen. No Record or Reverse route exists (T05, OPEN-009).
    /// </summary>
    private static void MapManualReceiptEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(DraftsPath, async (
                HttpContext httpContext,
                IManualReceiptService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                if (!httpContext.Request.HasJsonContentType())
                {
                    return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                DraftSaveRequest? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<DraftSaveRequest>(httpContext.Request.Body, ContractJson,
                        cancellationToken);
                }
                catch (JsonException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                if (request is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
                }

                var result = await service.DraftSaveAsync(Context(companyContext, currentUser, correlation),
                    new CommandMetadata(key), request, cancellationToken);
                if (result.IsReplay)
                {
                    httpContext.Response.Headers["Idempotency-Replayed"] = "true";
                    httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
                }

                // Denied and Unavailable are the platform's problems (403 forbidden, 503 service_unavailable),
                // as for every other route; the #258 CommandResult carries the outcomes a client acts on.
                return result.Outcome switch
                {
                    CommandOutcome.Succeeded => Results.Json(result, ContractJson, statusCode: StatusCodes.Status200OK),
                    CommandOutcome.ValidationFailed =>
                        Results.Json(result, ContractJson, statusCode: StatusCodes.Status422UnprocessableEntity),
                    CommandOutcome.VersionConflict or CommandOutcome.IdempotencyConflict =>
                        Results.Json(result, ContractJson, statusCode: StatusCodes.Status409Conflict),
                    CommandOutcome.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
                    CommandOutcome.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
                    _ => throw new InvalidOperationException("Finance draft save returned an undefined outcome."),
                };
            })
            .WithName("SaveFinanceManualReceiptDraftV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<DraftSaveRequest>("application/json")
            .Produces<CommandResult>(StatusCodes.Status200OK)
            .Produces<CommandResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces<CommandResult>(StatusCodes.Status409Conflict)
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(ManualReceiptsPath, async (
                [FromQuery] string? cursor,
                HttpContext httpContext,
                IManualReceiptService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                var result = await service.ListAuthorizedAsync(Context(companyContext, currentUser, correlation),
                    new SourceListRequest(cursor), cancellationToken);
                // The list has no "not found": the service answers NotFound only for a malformed or foreign cursor.
                return result.Outcome == ReadOutcome.NotFound
                    ? Results.Problem(statusCode: StatusCodes.Status400BadRequest)
                    : Read(result);
            })
            .WithName("GetFinanceManualReceiptsV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<SourcePage>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(ManualReceiptsPath + "/{sourceId:guid}", async (
                Guid sourceId,
                HttpContext httpContext,
                IManualReceiptService service,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await service.DetailAuthorizedAsync(Context(companyContext, currentUser, correlation),
                    new SourceDetailRequest(sourceId), cancellationToken));
            })
            .WithName("GetFinanceManualReceiptV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<SourceDetail>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();

        endpoints.MapGet(BankAccountsPath, async (
                HttpContext httpContext,
                IFinanceBankAccountReader banks,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext);
                return Read(await banks.ListAuthorizedAsync(Context(companyContext, currentUser, correlation),
                    cancellationToken));
            })
            .WithName("GetFinanceBankAccountsV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<BankAccountPage>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpServiceUnavailable();
    }

    /// <summary>The request's server context: company, actor and correlation are never request data.</summary>
    private static FinanceContext Context(ICompanyContext companyContext, ICurrentUser currentUser,
        ICorrelationContext correlation) =>
        new FinanceContext(companyContext.CompanyId, currentUser.UserId ?? string.Empty, correlation.CorrelationId,
            correlation.CausationId);

    private static IResult Read<T>(ReadResult<T> result) where T : class => result.Outcome switch
    {
        ReadOutcome.Found when result.Resource is not null => Results.Json(result.Resource, ContractJson),
        ReadOutcome.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound),
        ReadOutcome.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        ReadOutcome.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => throw new InvalidOperationException("Finance read returned an invalid outcome/resource combination."),
    };

    private static void SetPrivateNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }

    /// <summary>Exactly one Idempotency-Key: non-blank, canonical (no surrounding whitespace), at most 128 characters.</summary>
    private static bool TryReadIdempotencyKey(IHeaderDictionary headers, out string idempotencyKey)
    {
        idempotencyKey = string.Empty;
        if (!headers.TryGetValue(IdempotencyHeaderName, out var values) || values.Count != 1 || values[0] is not { } value
            || string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Length > MaximumIdempotencyKeyLength || value.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        idempotencyKey = value;
        return true;
    }
}

public interface IFinanceDraftReadBinding
{
    string? MoneyOperation { get; }
}

public sealed class ManualReceiptDraftReadBinding : IFinanceDraftReadBinding
{
    public string? MoneyOperation => FinanceMoneyOperations.ManualBankReceiptV1;
}

public interface IFinanceDraftContextEndpointService
{
    Task<DraftReadContextResult> GetAsync(DraftReadContextRequest request, string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class FinanceDraftContextEndpointService(DraftReadContextReader reader,
    IFinanceDraftReadBinding binding) : IFinanceDraftContextEndpointService
{
    public Task<DraftReadContextResult> GetAsync(DraftReadContextRequest request, string correlationId,
        CancellationToken cancellationToken = default) =>
        reader.GetAsync(request, binding.MoneyOperation, correlationId, cancellationToken);
}

public sealed class CounterpartyFinancePartyAccountPort(ICompanyContext companyContext,
    ICurrentUser currentUser, ICounterpartyReadService counterparties) : IFinancePartyAccountPort
{
    public async Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context,
        Guid partyId, Guid companyAccountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent(context))
        {
            return new(ReadOutcome.Denied, null);
        }

        CounterpartyDetailView? party;
        try
        {
            party = await counterparties.GetAuthorizedPartyAsync(partyId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return new(ReadOutcome.Denied, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!IsCurrent(context)) return new(ReadOutcome.Denied, null);
            throw;
        }

        // The owner answered for the context captured before the await. If company or actor
        // changed meanwhile, neither its data nor its NotFound may be released.
        if (!IsCurrent(context))
        {
            return new(ReadOutcome.Denied, null);
        }

        if (party is null || party.Id != partyId)
        {
            return new(ReadOutcome.NotFound, null);
        }

        var accounts = party.CompanyAccounts.Where(account => account.Id == companyAccountId
            && account.CompanyId == context.CompanyId).ToArray();
        if (accounts.Length != 1)
        {
            return new(ReadOutcome.NotFound, null);
        }

        var account = accounts[0];
        return new(ReadOutcome.Found, new(
            party.Id,
            account.Id,
            account.CompanyId,
            account.IsActive,
            new(party.Id, account.Id, party.Code, party.LegalName, party.TradeName,
                party.Version, account.Version),
            party.IsActive));
    }

    private bool IsCurrent(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && currentUser.UserId == context.ActorId && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && companyContext.CompanyId == context.CompanyId
        && currentUser.CanAccessCompany(context.CompanyId);
}

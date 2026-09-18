using System.Globalization;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class SalesEndpoints
{
    public const string AccountsPath = "/api/v1/sales/references/accounts";
    public const string CurrencyPath = "/api/v1/sales/references/currency";
    public const string ProductPath = "/api/v1/sales/references/product";
    public const string DraftsPath = SalesDraftApiV1.DraftPath;
    private const string IdempotencyHeaderName = SalesDraftApiV1.IdempotencyHeader;
    private const int MaximumIdempotencyKeyLength = 128;
    private const int MaximumCursorLength = 8192;
    private static readonly HashSet<string> DraftListQueryNames = new(["pageSize", "cursor"], StringComparer.Ordinal);

    private static readonly HashSet<string> AccountQueryNames =
        new(["q", "pageSize", "cursor"], StringComparer.Ordinal);
    private static readonly HashSet<string> CurrencyQueryNames =
        new(["currencyId", "effectiveOn"], StringComparer.Ordinal);
    private static readonly HashSet<string> ProductQueryNames =
        new(["productId", "expectedProductVersion", "orderUnitId", "effectiveOn"], StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/sales/references")
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext();

        group.MapGet("/accounts", async (
            [FromQuery] string? q,
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            HttpContext httpContext,
            ISalesReferenceReadService service,
            IActorSessionBindingResolver sessionBindingResolver,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            var effectivePageSize = pageSize ?? 50;
            if (!HasOnlySingleQuery(httpContext.Request.Query, AccountQueryNames, Array.Empty<string>())
                || effectivePageSize is < 1 or > 100 || cursor?.Length > 8192
                || !TryNormalizeAccountSearch(q, out var normalizedQuery))
            {
                return Problem(StatusCodes.Status400BadRequest, "validation_failed");
            }

            var sessionBinding = await sessionBindingResolver.ResolveAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(sessionBinding))
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return ToHttpResult(await service.GetAccountsAsync(
                new(normalizedQuery, effectivePageSize, cursor), sessionBinding, cancellationToken),
                [400, 401, 403, 503],
                ["validation_failed", "invalid_cursor"]);
        })
            .WithName("GetSalesAccountReferencesV1")
            .Produces<SalesAccountOptionsV1>()
            .ProducesErpBadRequest("validation_failed", "invalid_cursor")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/currency", async (
            [FromQuery] Guid currencyId,
            [FromQuery] DateOnly effectiveOn,
            HttpContext httpContext,
            ISalesReferenceReadService service,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, CurrencyQueryNames, CurrencyQueryNames)
                || !HasExactDate(httpContext.Request.Query, "effectiveOn")
                || currencyId == Guid.Empty)
            {
                return Problem(StatusCodes.Status400BadRequest, "validation_failed");
            }

            return ToHttpResult(await service.GetCurrencyAsync(
                new(currencyId, effectiveOn), cancellationToken),
                [400, 401, 403, 422, 503],
                ["validation_failed", "unsupported_currency"]);
        })
            .WithName("GetSalesCurrencyReferenceV1")
            .Produces<SalesCurrencyReferenceV1>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnprocessableEntity("unsupported_currency")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/product", async (
            [FromQuery] Guid productId,
            [FromQuery] string expectedProductVersion,
            [FromQuery] Guid orderUnitId,
            [FromQuery] DateOnly effectiveOn,
            HttpContext httpContext,
            ISalesReferenceReadService service,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, ProductQueryNames, ProductQueryNames)
                || !HasExactDate(httpContext.Request.Query, "effectiveOn")
                || productId == Guid.Empty || orderUnitId == Guid.Empty
                || string.IsNullOrWhiteSpace(expectedProductVersion)
                || expectedProductVersion.Length > 128)
            {
                return Problem(StatusCodes.Status400BadRequest, "validation_failed");
            }

            return ToHttpResult(await service.GetProductAsync(
                new(productId, expectedProductVersion, orderUnitId, effectiveOn), cancellationToken),
                [400, 401, 403, 404, 409, 422, 503],
                ["validation_failed", "product_selection_changed", "unsupported_product", "unsupported_unit"]);
        })
            .WithName("GetSalesProductReferenceV1")
            .Produces<SalesProductReferenceV1>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict("product_selection_changed")
            .ProducesErpUnprocessableEntity("unsupported_product", "unsupported_unit")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        MapDraftEndpoints(endpoints);
        return endpoints;
    }

    /// <summary>
    /// The Sales API slice (#257 create / list / detail; card #66 with decisions 1–6). The route
    /// reads nothing but the server context and the wire body; <see cref="ISalesDraftService"/>
    /// decides, with a fresh central measurement of the Sales permission at every fence (#276) — the
    /// route's own policy is only "authenticated + company context", because the Sales names join
    /// the central catalogue with the central writer's step and an unregistered name is answered
    /// as a 503 setup problem, never as a grant.
    /// <list type="bullet">
    /// <item><b>POST</b>: exactly one canonical <c>Idempotency-Key</c> (≤128) → 400
    /// <c>command_metadata_invalid</c>; JSON only → 415; the strict #257 body → 400
    /// <c>validation_failed</c>; 201 + <c>Location</c> on the first commit, 200 + <c>Idempotency-Replayed</c>
    /// / <c>Idempotency-Original-Correlation-Id</c> on the same attempt (ADR-011 §4); a recorded
    /// terminal refusal replays with the same headers (§7).</item>
    /// <item><b>GET list</b>: <c>pageSize</c> 1–100 (default 50), opaque company-bound <c>cursor</c>
    /// → 400 <c>invalid_cursor</c>. <b>GET detail</b>: another company's draft is 404.</item>
    /// <item>Problems carry status/title, <c>code</c>, <c>correlationId</c> and field <c>errors</c>
    /// (#257); platform 401/403 stay code-less.</item>
    /// </list>
    /// </summary>
    private static void MapDraftEndpoints(IEndpointRouteBuilder endpoints)
    {
        var drafts = endpoints.MapGroup(DraftsPath)
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext();

        drafts.MapPost("/", async (
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey) || !correlation.IsResolved)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, SalesDraftProblemCodesV1.CommandMetadataInvalid, correlation);
            }

            if (!httpContext.Request.HasJsonContentType())
            {
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            }

            CreateSalesOrderDraftRequest request;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                request = SalesDraftJson.ReadCreate(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var response = await service.CreateDraftAsync(request,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (response.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, response.OriginalCorrelationId);
                    return Results.Json(response, SalesDraftJson.Options, statusCode: SalesDraftApiV1.ReplayStatus);
                }

                return Results.Json(response, SalesDraftJson.Options, statusCode: SalesDraftApiV1.CreatedStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                if (exception.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, exception.OriginalCorrelationId ?? correlation.CorrelationId);
                }

                return DraftProblem(exception, correlation);
            }
        })
            .WithName("CreateSalesOrderDraftV1")
            .WithIdempotencyContract(SalesDraftApiV1.CreatedStatus)
            .Accepts<CreateSalesOrderDraftRequest>("application/json")
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.CreatedStatus)
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.ReplayStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(SalesDraftProblemCodesV1.AccountState, SalesDraftProblemCodesV1.VersionConflict,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", "unsupported_product", "unsupported_unit",
                SalesDraftProblemCodesV1.UnsupportedCurrency, SalesDraftProblemCodesV1.CommercialNotAvailable,
                SalesDraftProblemCodesV1.OwnerReferenceUnavailable)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        drafts.MapGet("/", async (
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, DraftListQueryNames, Array.Empty<string>())
                || pageSize is < 1 or > 100 || cursor?.Length > MaximumCursorLength)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetDraftListAsync(new SalesOrderDraftListQuery(pageSize ?? 50, cursor), cancellationToken),
                    SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftsV1")
            .Produces<SalesOrderDraftListResponse>()
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.InvalidCursor)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        drafts.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetDraftDetailAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftV1")
            .Produces<SalesOrderDraftDetailView>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>Exactly one Idempotency-Key: non-blank, canonical (no surrounding whitespace), at most 128 characters, no comma.</summary>
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

    private static void ApplyReplayHeaders(HttpContext httpContext, string originalCorrelationId)
    {
        httpContext.Response.Headers["Idempotency-Replayed"] = "true";
        httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] = originalCorrelationId;
    }

    /// <summary>The service's refusal as the #257 problem: platform 401/403/404 stay code-less, every other status carries its code and field errors.</summary>
    private static IResult DraftProblem(SalesDraftServiceException exception, ICorrelationContext correlation)
    {
        if (exception.Status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound)
        {
            return Results.Problem(statusCode: exception.Status);
        }

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = exception.Code,
            ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null,
        };
        if (exception.Errors.Count > 0)
        {
            extensions["errors"] = exception.Errors;
        }

        return Results.Problem(statusCode: exception.Status, extensions: extensions);
    }

    private static IResult DraftProblem(int statusCode, string code, ICorrelationContext correlation) => Results.Problem(
        statusCode: statusCode,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = code,
            ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null,
        });

    /// <summary>
    /// The accounts search text, normalized exactly like the Cari list's own search
    /// (<see cref="CounterpartyPageQueryParser"/>): trimmed, Unicode FormC, empty or
    /// whitespace-only means no search, and the 200 limit counts Unicode scalar values
    /// (runes), not UTF-16 code units. Without it, <c>q="ACME "</c> or a decomposed
    /// <c>ş</c> returned an empty page where the Cari screen finds the same record, and a
    /// 200-character search made of astral characters was refused on its UTF-16 length.
    /// </summary>
    internal static bool TryNormalizeAccountSearch(string? raw, out string? normalized)
    {
        normalized = null;
        if (raw is null)
        {
            return true;
        }

        var candidate = raw.Trim().Normalize(NormalizationForm.FormC);
        if (candidate.Length == 0)
        {
            return true;
        }

        if (candidate.EnumerateRunes().Count() > 200)
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    private static bool HasOnlySingleQuery(IQueryCollection query, IReadOnlySet<string> allowed,
        IReadOnlyCollection<string> required) =>
        query.All(pair => allowed.Contains(pair.Key) && pair.Value.Count == 1)
        && required.All(query.ContainsKey);

    private static bool HasExactDate(IQueryCollection query, string name) =>
        query.TryGetValue(name, out var value) && value.Count == 1
        && DateOnly.TryParseExact(value[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _);

    private static IResult ToHttpResult<T>(SalesReferenceReadResult<T> result,
        int[] allowedStatuses, string[] allowedCodes) where T : class
    {
        if (result.StatusCode == StatusCodes.Status200OK && result.Value is not null && result.Code is null)
        {
            return Results.Ok(result.Value);
        }

        if (result.StatusCode == StatusCodes.Status200OK || result.Value is not null
            || !allowedStatuses.Contains(result.StatusCode))
        {
            throw new InvalidOperationException("Sales reference read returned an invalid result boundary.");
        }

        var code = result.Code is not null && allowedCodes.Contains(result.Code) ? result.Code : null;
        return code is null ? Results.Problem(statusCode: result.StatusCode)
            : Problem(result.StatusCode, code);
    }

    private static IResult Problem(int statusCode, string code) => Results.Problem(
        statusCode: statusCode,
        extensions: new Dictionary<string, object?> { ["code"] = code });

    private static void SetPrivateNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }
}

public sealed class CounterpartySalesAccountSelectionSourceV1(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICounterpartyReadService counterparties) : ISalesAccountSelectionSourceV1
{
    public async Task<SalesReferenceReadResult<SalesAccountSelectionPageV1>> GetPageAsync(
        SalesAccountSelectionRequestV1 request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Matches(request)) return new(403, null, null);

        CounterpartyPageExecution execution;
        try
        {
            execution = await counterparties.GetAuthorizedPageAsync(new(
                new(request.Q, null, PartyRole.None, CounterpartyRoleMatch.Any,
                    CounterpartyRecordState.Active, CounterpartyRecordState.Active,
                    CounterpartySort.Code, CounterpartySortDirection.Ascending,
                    request.PageSize, request.Cursor, false),
                request.ActorId, request.SessionBinding), cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return new(403, null, null);
        }

        if (!Matches(request)) return new(403, null, null);
        if (execution.Result == CounterpartyPageResult.Forbidden) return new(403, null, null);
        if (execution.Result == CounterpartyPageResult.InvalidCursor) return new(400, null, "invalid_cursor");
        if (execution.Result == CounterpartyPageResult.ServiceUnavailable) return new(503, null, null);
        if (execution.Result != CounterpartyPageResult.Succeeded || execution.Page is null)
            return new(503, null, null);

        return new(200, new(execution.Page.Items.Select(party => new SalesAccountSelectionPartyV1(
                party.Id, party.Code, (int)party.Kind, party.LegalName, party.TradeName,
                party.IsActive, party.Version.ToString(CultureInfo.InvariantCulture),
                party.CompanyAccounts.Select(account => new SalesAccountSelectionAccountV1(
                    account.Id, account.CompanyId, (int)account.Roles, account.IsActive,
                    account.Version.ToString(CultureInfo.InvariantCulture))).ToArray())).ToArray(),
            execution.Page.NextCursor), null);
    }

    private bool Matches(SalesAccountSelectionRequestV1 request) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && currentUser.UserId == request.ActorId && !string.IsNullOrWhiteSpace(request.SessionBinding)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == request.CompanyId
        && currentUser.CanAccessCompany(request.CompanyId);
}

/// <summary>
/// The Cari owner's answer for the draft's customer (<see cref="ISalesPartyAccountPortV1"/>), over the
/// authorized Counterparty party read, as the Finance party port does: the party, the account of that
/// party in the server company, and both active flags with the owner's versions. Denied when the
/// server context is not the request's; NotFound when the party or the account is not the owner's.
/// </summary>
public sealed class CounterpartySalesPartyAccountPortV1(ICompanyContext companyContext, ICurrentUser currentUser,
    ICounterpartyReadService counterparties) : ISalesPartyAccountPortV1
{
    public async Task<SalesPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent() || partyId == Guid.Empty || companyAccountId == Guid.Empty)
        {
            return new(SalesPartyAccountOutcomeV1.Denied, null);
        }

        var companyId = companyContext.CompanyId;
        CounterpartyDetailView? party;
        try
        {
            party = await counterparties.GetAuthorizedPartyAsync(partyId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return new(SalesPartyAccountOutcomeV1.Denied, null);
        }

        // The owner answered for the context captured before the await; a changed context releases nothing.
        if (!IsCurrent() || companyContext.CompanyId != companyId)
        {
            return new(SalesPartyAccountOutcomeV1.Denied, null);
        }

        if (party is null || party.Id != partyId)
        {
            return new(SalesPartyAccountOutcomeV1.NotFound, null);
        }

        var accounts = party.CompanyAccounts.Where(account => account.Id == companyAccountId && account.CompanyId == companyId).ToArray();
        if (accounts.Length != 1)
        {
            return new(SalesPartyAccountOutcomeV1.NotFound, null);
        }

        var account = accounts[0];
        return new(SalesPartyAccountOutcomeV1.Found, new SalesPartyAccountV1(party.Id, account.Id, account.CompanyId,
            party.IsActive, account.IsActive, party.Version, account.Version));
    }

    private bool IsCurrent() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId) && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && currentUser.CanAccessCompany(companyContext.CompanyId);
}

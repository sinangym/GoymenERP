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
    // SALES-A2a: the two list reads of the create form's selects.
    public const string ContactInformationPath = "/api/v1/sales/references/contact-information";
    public const string CurrenciesPath = "/api/v1/sales/references/currencies";
    public const string DraftsPath = SalesDraftApiV1.DraftPath;
    private const string IdempotencyHeaderName = SalesDraftApiV1.IdempotencyHeader;
    private const int MaximumIdempotencyKeyLength = 128;
    private const int MaximumCursorLength = 8192;
    private static readonly HashSet<string> DraftListQueryNames = new(["pageSize", "cursor", "approvalState"], StringComparer.Ordinal);
    private const int MaximumApprovalStateLength = 32;

    private static readonly HashSet<string> AccountQueryNames =
        new(["q", "pageSize", "cursor"], StringComparer.Ordinal);
    private static readonly HashSet<string> CurrencyQueryNames =
        new(["currencyId", "effectiveOn"], StringComparer.Ordinal);
    private static readonly HashSet<string> ProductQueryNames =
        new(["productId", "expectedProductVersion", "orderUnitId", "effectiveOn"], StringComparer.Ordinal);
    // SALES-A2a list reads: every query name is required and single.
    private static readonly HashSet<string> ContactInformationQueryNames =
        new(["partyId", "companyAccountId"], StringComparer.Ordinal);
    private static readonly HashSet<string> CurrenciesQueryNames =
        new(["effectiveOn"], StringComparer.Ordinal);

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

        // SALES-A2a: the Cari owner's ACTIVE delivery addresses and contacts of one party account, for the
        // create form's selects. A LIST read like the accounts route (same permission binding); no default,
        // no price. 404 code-less when the pair is not the owner's in this company.
        group.MapGet("/contact-information", async (
            [FromQuery] Guid partyId,
            [FromQuery] Guid companyAccountId,
            HttpContext httpContext,
            ISalesReferenceReadService service,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, ContactInformationQueryNames, ContactInformationQueryNames)
                || partyId == Guid.Empty || companyAccountId == Guid.Empty)
            {
                return Problem(StatusCodes.Status400BadRequest, "validation_failed");
            }

            return ToHttpResult(await service.GetContactInformationAsync(
                new(partyId, companyAccountId), cancellationToken),
                [400, 401, 403, 404, 503],
                ["validation_failed"]);
        })
            .WithName("GetSalesContactInformationReferencesV1")
            .Produces<SalesContactInformationOptionsV1>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // SALES-A2a: the company's currencies a draft may be opened in on the business date — the Settings
        // owner's candidates, each revalidated through the known-id money read for the Sales operation. An
        // empty list is a 200 answer; a missing or failing owner is never an empty list.
        group.MapGet("/currencies", async (
            [FromQuery] DateOnly effectiveOn,
            HttpContext httpContext,
            ISalesReferenceReadService service,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, CurrenciesQueryNames, CurrenciesQueryNames)
                || !HasExactDate(httpContext.Request.Query, "effectiveOn"))
            {
                return Problem(StatusCodes.Status400BadRequest, "validation_failed");
            }

            return ToHttpResult(await service.GetCurrenciesAsync(
                new(effectiveOn), cancellationToken),
                [400, 401, 403, 503],
                ["validation_failed"]);
        })
            .WithName("GetSalesCurrencyReferencesV1")
            .Produces<SalesCurrencyOptionsV1>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
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
    /// <c>validation_failed</c>; 201 + <c>Location</c> on the first commit and again on the same
    /// attempt, then with <c>Idempotency-Replayed</c> / <c>Idempotency-Original-Correlation-Id</c>
    /// (ADR-011 §4: the persisted result is replayed — a #257 delta, the contract said 200); a
    /// recorded terminal refusal (409) replays with the same headers (§7).</item>
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
                }

                httpContext.Response.Headers.Location = DraftLocation(httpContext.Request, response.Detail.Draft.Id);
                return Results.Json(response, SalesDraftJson.Options, statusCode: SalesDraftApiV1.CreatedStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                if (exception.IsReplay)
                {
                    // A replayed refusal always carries its first correlation (the attempt record requires
                    // one); labelling this request as the original would be a lie, so it is a server fault.
                    ApplyReplayHeaders(httpContext, exception.OriginalCorrelationId
                        ?? throw new InvalidOperationException("Replayed refusal without its original correlation."));
                }

                return DraftProblem(exception, correlation);
            }
        })
            .WithName("CreateSalesOrderDraftV1")
            // The replayable results: the stored draft (201) and the recorded terminal refusal (409, §7).
            .WithIdempotencyContract(SalesDraftApiV1.CreatedStatus, StatusCodes.Status409Conflict)
            .Accepts<CreateSalesOrderDraftRequest>("application/json")
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.CreatedStatus)
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
            [FromQuery] string? approvalState,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, DraftListQueryNames, Array.Empty<string>())
                || pageSize is < 1 or > 100 || cursor?.Length > MaximumCursorLength
                || approvalState is { Length: 0 or > MaximumApprovalStateLength })
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                // SALES-A1: `approvalState` narrows to one exact internal approval state (the service validates the name).
                return Results.Json(await service.GetDraftListAsync(new SalesOrderDraftListQuery(pageSize ?? 50, cursor, approvalState), cancellationToken),
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

        // SALES-A1: the internal-approval commands (DEC-SAL-001/002; Business 5737989917). One canonical
        // Idempotency-Key each; the strict body `{expectedVersion}` (+ `reason` on the rejection only);
        // 200 with the draft after the transition, and 200 + replay headers for the same key again
        // (ADR-011 §4); a stale version or a wrong state is the key's recorded 409 (§7). Submit follows
        // the create permission, approve and reject the `sales.orders.approve` name — both measured by
        // the service; another company's draft is 404.
        MapDraftDetail(drafts);
        MapStateCommand(drafts, "/{id:guid}/submission", "SubmitSalesOrderDraftV1", allowReason: false,
            (service, id, body, metadata, cancellationToken) => service.SubmitDraftAsync(id, body, metadata, cancellationToken),
            unprocessableCodes: ["validation_failed"]);
        MapStateCommand(drafts, "/{id:guid}/approval", "ApproveSalesOrderDraftV1", allowReason: false,
            (service, id, body, metadata, cancellationToken) => service.ApproveDraftAsync(id, body, metadata, cancellationToken),
            unprocessableCodes: []);
        MapStateCommand(drafts, "/{id:guid}/rejection", "RejectSalesOrderDraftV1", allowReason: true,
            (service, id, body, metadata, cancellationToken) => service.RejectDraftAsync(id, body, metadata, cancellationToken),
            unprocessableCodes: [SalesDraftProblemCodesV1.ApprovalReasonTooLong]);
        MapStateCommand(drafts, "/{id:guid}/withdrawal", "WithdrawSalesOrderDraftV1", allowReason: true,
            (service, id, body, metadata, cancellationToken) => service.WithdrawDraftAsync(id, body, metadata, cancellationToken),
            unprocessableCodes: [SalesDraftProblemCodesV1.ApprovalReasonTooLong]);
    }

    private static void MapStateCommand(RouteGroupBuilder drafts, string pattern, string name, bool allowReason,
        Func<ISalesDraftService, Guid, SalesDraftStateCommandRequest, SalesCommandMetadata, CancellationToken, Task<SalesOrderDraftResponse>> command,
        string[] unprocessableCodes)
    {
        var route = drafts.MapPost(pattern, async (
            Guid id,
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

            SalesDraftStateCommandRequest body;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                body = SalesDraftJson.ReadStateCommand(await reader.ReadToEndAsync(cancellationToken), allowReason);
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var response = await command(service, id, body,
                    new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow),
                    cancellationToken);
                if (response.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, response.OriginalCorrelationId);
                }

                return Results.Json(response, SalesDraftJson.Options, statusCode: SalesDraftApiV1.StateCommandStatus);
            }
            catch (SalesDraftServiceException exception)
            {
                if (exception.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, exception.OriginalCorrelationId
                        ?? throw new InvalidOperationException("Replayed refusal without its original correlation."));
                }

                return DraftProblem(exception, correlation);
            }
        })
            .WithName(name)
            // The replayable results: the draft after the transition (200) and the recorded terminal refusals (409, §7).
            .WithIdempotencyContract(SalesDraftApiV1.StateCommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftStateCommandRequest>("application/json")
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.StateCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
        if (unprocessableCodes.Length > 0)
        {
            route.ProducesErpUnprocessableEntity(unprocessableCodes);
        }
    }

    /// <summary>GET detail, mapped exactly once (the endpoint name is unique per host).</summary>
    private static void MapDraftDetail(RouteGroupBuilder drafts)
    {
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

    /// <summary>The created draft's absolute location on this host (the detail route).</summary>
    private static string DraftLocation(HttpRequest request, Guid draftId) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}{DraftsPath}/{draftId:D}";

    /// <summary>
    /// The service's refusal as the #257 problem. Platform 401/403/404 and every 503 stay code-less:
    /// the platform writer answers them with its own public code (<c>forbidden</c>,
    /// <c>service_unavailable</c>), so the service's setup and provider codes
    /// (<c>permission_unregistered</c>, <c>counterparty_provider_unavailable</c>,
    /// <c>settings_provider_unavailable</c>, <c>reference_provider_invalid</c>,
    /// <c>product_eligibility_unavailable</c>) never reach the wire — the same boundary as the
    /// reference routes' <see cref="ToHttpResult{T}"/>; they stay in the logs and the service tests.
    /// Every other status carries its declared code and field errors.
    /// </summary>
    private static IResult DraftProblem(SalesDraftServiceException exception, ICorrelationContext correlation)
    {
        if (exception.Status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
            or StatusCodes.Status404NotFound or StatusCodes.Status503ServiceUnavailable)
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
/// <para>
/// SALES-A2a: the same adapter answers <see cref="ISalesPartyContactInformationPortV1"/> over the
/// authorized Counterparty contact-information read (the owner measures its own read permission for the
/// actor): the account's version and its operational addresses and contacts, ids and versions as the
/// owner holds them, no channel. NotFound when the owner has no account of that party in the server
/// company, or the account is not the one asked for. PROPOSAL of the Sales lane; the central writer
/// adopts it and registers the interface in Program.cs.
/// </para>
/// </summary>
public sealed class CounterpartySalesPartyAccountPortV1(ICompanyContext companyContext, ICurrentUser currentUser,
    ICounterpartyReadService counterparties, ICounterpartyContactInformationService contactInformation)
    : ISalesPartyAccountPortV1, ISalesPartyContactInformationPortV1
{
    public async Task<SalesPartyContactInformationAnswerV1> GetContactInformationAsync(Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent() || partyId == Guid.Empty || companyAccountId == Guid.Empty)
        {
            return new(SalesPartyContactInformationOutcomeV1.Denied, null);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        CounterpartyContactInformationView? view;
        try
        {
            view = await contactInformation.GetContactInformationAsync(partyId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return new(SalesPartyContactInformationOutcomeV1.Denied, null);
        }

        // The owner answered for the context captured before the await — the company AND the actor; a changed context releases nothing.
        if (!IsCurrent() || companyContext.CompanyId != companyId || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
        {
            return new(SalesPartyContactInformationOutcomeV1.Denied, null);
        }

        if (view is null || view.PartyId != partyId || view.AccountId != companyAccountId || view.CompanyId != companyId)
        {
            return new(SalesPartyContactInformationOutcomeV1.NotFound, null);
        }

        var addresses = view.Information.Addresses
            .Select(address => new SalesPartyAddressV1(address.Id, address.Version, address.IsActive, address.Purpose,
                address.CountryCode, address.Line1, address.Line2, address.City, address.District, address.PostalCode))
            .ToArray();
        var contacts = view.Information.Contacts
            .Select(contact => new SalesPartyContactV1(contact.Id, contact.Version, contact.IsActive, contact.DisplayName,
                contact.JobTitle, contact.Purpose))
            .ToArray();
        return new(SalesPartyContactInformationOutcomeV1.Found, new SalesPartyContactInformationV1(view.PartyId, view.AccountId,
            view.CompanyId, view.Version, addresses, contacts));
    }

    public async Task<SalesPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent() || partyId == Guid.Empty || companyAccountId == Guid.Empty)
        {
            return new(SalesPartyAccountOutcomeV1.Denied, null);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        CounterpartyDetailView? party;
        try
        {
            party = await counterparties.GetAuthorizedPartyAsync(partyId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return new(SalesPartyAccountOutcomeV1.Denied, null);
        }

        // The owner answered for the context captured before the await — the company AND the actor; a changed context releases nothing.
        if (!IsCurrent() || companyContext.CompanyId != companyId || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
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

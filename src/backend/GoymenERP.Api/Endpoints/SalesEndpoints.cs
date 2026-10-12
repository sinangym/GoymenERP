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
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

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
    public const string DraftReadContractHeader = "X-ERP-Sales-Read-Contract";
    public const string DraftReadContractVersion = "record-party-display-v1";
    public const string DraftReadContractVersionPattern = "^record-party-display-v1$";
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
            [FromServices] ISalesDraftService legacyService,
            [FromServices] ISalesDraftReadServiceV1 enrichedService,
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
            if (!TryReadDraftContract(httpContext.Request.Headers, out var contract))
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                // SALES-A1: `approvalState` narrows to one exact internal approval state (the service validates the name).
                var query = new SalesOrderDraftListQuery(pageSize ?? 50, cursor, approvalState);
                if (contract == SalesDraftReadContract.RecordPartyDisplay)
                {
                    var result = await enrichedService.GetDraftListAsync(query, cancellationToken);
                    AcknowledgeDraftReadContract(httpContext.Response);
                    return Results.Json(result, SalesDraftJson.Options);
                }

                return Results.Json(await legacyService.GetDraftListAsync(query, cancellationToken), SalesDraftJson.Options);
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
        MapDraftRevisions(drafts);
        MapDraftLinePricing(drafts);
        MapPriceFloorExceptionApproval(drafts);
        MapCommercialRevisionCommand(drafts);
        MapQuantityRevisionCommand(drafts);
        MapDeliveryScheduleCommand(drafts);
        MapDeliverySchedules(drafts);
        MapDeliveryProgress(drafts);
        MapLoadingProgress(drafts);
        MapCancellationImpact(drafts);
        MapCancelCommand(drafts);
        MapCancellation(drafts);
        MapRemainderChanges(drafts);
        MapRemainderChangeCommand(drafts);
        MapRemainderDecisionCommand(drafts, "approval", "ApproveSalesOrderDraftRemainderChangeV1", allowReason: false,
            (service, id, changeNo, body, metadata, cancellationToken) =>
                service.ApproveRemainderChangeAsync(id, changeNo, body, metadata, cancellationToken));
        MapRemainderDecisionCommand(drafts, "rejection", "RejectSalesOrderDraftRemainderChangeV1", allowReason: true,
            (service, id, changeNo, body, metadata, cancellationToken) =>
                service.RejectRemainderChangeAsync(id, changeNo, body, metadata, cancellationToken));
        MapStateCommand(drafts, "/{id:guid}/submission", "SubmitSalesOrderDraftV1", allowReason: false,
            (service, id, body, metadata, cancellationToken) => service.SubmitDraftAsync(id, body, metadata, cancellationToken),
            unprocessableCodes: ["validation_failed"]);
        MapStateCommand(drafts, "/{id:guid}/approval", "ApproveSalesOrderDraftV1", allowReason: false,
            (service, id, body, metadata, cancellationToken) => service.ApproveDraftAsync(id, body, metadata, cancellationToken),
            unprocessableCodes: []);
        MapStateCommand(drafts, "/{id:guid}/customer-acceptance", "RecordSalesOrderDraftCustomerAcceptanceV1", allowReason: false,
            (service, id, body, metadata, cancellationToken) => service.RecordCustomerAcceptanceAsync(id, body, metadata, cancellationToken),
            unprocessableCodes: []);
        MapStateCommand(drafts, "/{id:guid}/rejection", "RejectSalesOrderDraftV1", allowReason: true,
            (service, id, body, metadata, cancellationToken) => service.RejectDraftAsync(id, body, metadata, cancellationToken),
            unprocessableCodes: [SalesDraftProblemCodesV1.ApprovalReasonTooLong]);
        MapStateCommand(drafts, "/{id:guid}/withdrawal", "WithdrawSalesOrderDraftV1", allowReason: true,
            (service, id, body, metadata, cancellationToken) => service.WithdrawDraftAsync(id, body, metadata, cancellationToken),
            unprocessableCodes: [SalesDraftProblemCodesV1.ApprovalReasonTooLong]);
    }

    private static void MapRemainderChanges(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/remainder-changes", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetRemainderChangesAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftRemainderChangesV1")
            .Produces<SalesOrderDraftRemainderChangesResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static void MapRemainderChangeCommand(RouteGroupBuilder drafts)
    {
        drafts.MapPost("/{id:guid}/remainder-changes", async (
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

            SalesDraftRemainderChangeRequest body;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                body = SalesDraftRemainderJson.ReadChange(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            return await ExecuteRemainderCommand(httpContext, correlation,
                metadata => service.RequestRemainderChangeAsync(id, body, metadata, cancellationToken), idempotencyKey);
        })
            .WithName("RequestSalesOrderDraftRemainderChangeV1")
            .WithIdempotencyContract(SalesDraftApiV1.StateCommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftRemainderChangeRequest>("application/json")
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.StateCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.RemainderBlocked, SalesDraftProblemCodesV1.SameKeyPayloadMismatch,
                SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static void MapRemainderDecisionCommand(RouteGroupBuilder drafts, string segment, string name, bool allowReason,
        Func<ISalesDraftService, Guid, int, SalesDraftRemainderDecisionRequest, SalesCommandMetadata, CancellationToken,
            Task<SalesOrderDraftResponse>> command)
    {
        drafts.MapPost($"/{{id:guid}}/remainder-changes/{{changeNo:int:min(1)}}/{segment}", async (
            Guid id,
            int changeNo,
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

            SalesDraftRemainderDecisionRequest body;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                body = SalesDraftRemainderJson.ReadDecision(await reader.ReadToEndAsync(cancellationToken), allowReason);
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            return await ExecuteRemainderCommand(httpContext, correlation,
                metadata => command(service, id, changeNo, body, metadata, cancellationToken), idempotencyKey);
        })
            .WithName(name)
            .WithIdempotencyContract(SalesDraftApiV1.StateCommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftRemainderDecisionRequest>("application/json")
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.StateCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.RemainderBlocked, SalesDraftProblemCodesV1.SameKeyPayloadMismatch,
                SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", SalesDraftProblemCodesV1.ApprovalReasonTooLong)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static async Task<IResult> ExecuteRemainderCommand(HttpContext httpContext, ICorrelationContext correlation,
        Func<SalesCommandMetadata, Task<SalesOrderDraftResponse>> command, string idempotencyKey)
    {
        try
        {
            var response = await command(new SalesCommandMetadata(idempotencyKey, correlation.CorrelationId,
                correlation.CausationId, DateTimeOffset.UtcNow));
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
    }

    /// <summary>
    /// K3 commercial revision: the source-owned service enforces <c>sales.orders.price.write</c>,
    /// version/state/idempotency and the full line-set invariant. The route owns only the strict
    /// HTTP envelope and never derives a price, discount, threshold or role.
    /// </summary>
    private static void MapCommercialRevisionCommand(RouteGroupBuilder drafts)
    {
        drafts.MapPost("/{id:guid}/commercial-revision", async (
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

            SalesDraftCommercialRevisionRequest body;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                body = SalesDraftJson.ReadCommercialRevision(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var response = await service.ReviseCommercialAsync(id, body,
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
            .WithName("ReviseSalesOrderDraftCommercialV1")
            .WithIdempotencyContract(SalesDraftApiV1.StateCommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftCommercialRevisionRequest>("application/json")
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.StateCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// DEC-SAL-002 / AT-023 quantity revision: the source-owned service enforces <c>sales.orders.quantity.write</c>,
    /// version/state/idempotency, the reason, every line exactly once, Ö2 in the line's own unit and the owners' links
    /// (fail-closed). The route owns only the strict HTTP envelope (the commercial revision's) and never derives a quantity,
    /// role or re-approval.
    /// </summary>
    private static void MapQuantityRevisionCommand(RouteGroupBuilder drafts)
    {
        drafts.MapPost("/{id:guid}/quantity-revision", async (
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

            SalesDraftQuantityRevisionRequest body;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                body = SalesDraftJson.ReadQuantityRevision(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var response = await service.ReviseQuantityAsync(id, body,
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
            .WithName("ReviseSalesOrderDraftQuantityV1")
            .WithIdempotencyContract(SalesDraftApiV1.StateCommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftQuantityRevisionRequest>("application/json")
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.StateCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.RevisionBlocked, SalesDraftProblemCodesV1.SameKeyPayloadMismatch,
                SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// DEC-SAL-003 delivery-schedule update: the source-owned service enforces <c>sales.orders.delivery-schedule.write</c>,
    /// version/state/idempotency, every line exactly once and each line's current slices exactly (dates only). The route
    /// owns only the strict HTTP envelope (the commercial revision's) and never derives a date, role or re-approval.
    /// </summary>
    private static void MapDeliveryScheduleCommand(RouteGroupBuilder drafts)
    {
        drafts.MapPost("/{id:guid}/delivery-schedule", async (
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

            SalesDraftDeliveryScheduleRequest body;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                body = SalesDraftJson.ReadDeliverySchedule(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var response = await service.RescheduleDeliveryAsync(id, body,
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
            .WithName("RescheduleSalesOrderDraftDeliveryV1")
            .WithIdempotencyContract(SalesDraftApiV1.StateCommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftDeliveryScheduleRequest>("application/json")
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.StateCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>DEC-SAL-003 ("auditlidir"): schedule 0 and every recorded change, under the draft read; another company's draft is 404.</summary>
    private static void MapDeliverySchedules(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/delivery-schedules", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetDraftDeliverySchedulesAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftDeliverySchedulesV1")
            .Produces<SalesOrderDraftDeliverySchedulesResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// AT-024: per line the delivered and remaining quantity, from the delivery owner's recorded deliveries (Logistics,
    /// read through the Sales-declared port), under the draft read; another company's draft is 404. Read-only.
    /// </summary>
    private static void MapDeliveryProgress(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/delivery-progress", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetDraftDeliveryProgressAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftDeliveryProgressV1")
            .Produces<SalesOrderDraftDeliveryProgressResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// DEC-SAL-005: per line the current loading-instruction facts from Logistics, read through the Sales-declared owner
    /// port under <c>sales.drafts.read</c>. Another company's draft is 404; the read writes nothing in either module.
    /// </summary>
    private static void MapLoadingProgress(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/loading-progress", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetDraftLoadingProgressAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftLoadingProgressV1")
            .Produces<SalesOrderDraftLoadingProgressResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// DEC-SAL-005: the impact preview of cancelling an approved order, under <c>sales.orders.cancel</c> (the service measures
    /// it): every DEC-SAL-005 category read afresh from its owner's port, a category without an owner on main as "no source".
    /// Read-only; another company's draft is 404.
    /// </summary>
    private static void MapCancellationImpact(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/cancellation-impact", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetCancellationImpactAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftCancellationImpactV1")
            .Produces<SalesOrderDraftCancellationImpactResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// DEC-SAL-005 / AT-007: cancel an approved order. The source-owned service enforces <c>sales.orders.cancel</c>, the
    /// version, the state, the required reason, the preview token and the fail-closed impact (an active allocation or a loading
    /// line refuses; nothing of another module is released). The route owns only the strict HTTP envelope (the delivery
    /// schedule's) and never derives an impact, a role or an approval.
    /// </summary>
    private static void MapCancelCommand(RouteGroupBuilder drafts)
    {
        drafts.MapPost("/{id:guid}/cancellation", async (
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

            SalesDraftCancelRequest body;
            try
            {
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                body = SalesDraftJson.ReadCancel(await reader.ReadToEndAsync(cancellationToken));
            }
            catch (JsonException)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var response = await service.CancelDraftAsync(id, body,
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
            .WithName("CancelSalesOrderDraftV1")
            .WithIdempotencyContract(SalesDraftApiV1.StateCommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftCancelRequest>("application/json")
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.StateCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.CancellationBlocked, SalesDraftProblemCodesV1.CancellationImpactChanged,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>AT-007: the recorded cancellation (or null) under the draft read; another company's draft is 404.</summary>
    private static void MapCancellation(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/cancellation", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetDraftCancellationAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftCancellationV1")
            .Produces<SalesOrderDraftCancellationResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// The approved commercial history under the draft read. Price disclosure remains the service's
    /// independent <c>sales.orders.price.read</c> decision; this route never fills withheld values.
    /// </summary>
    private static void MapDraftRevisions(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/revisions", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetDraftRevisionsAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftRevisionsV1")
            .Produces<SalesOrderDraftRevisionsResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// AT-018 / AT-021: every line's list-price reference, adjustments and net (the service measures the draft read AND the
    /// price read), and one line's Product list-price candidates (the price-write name; 503 until the Product owner is composed).
    /// </summary>
    private static void MapDraftLinePricing(RouteGroupBuilder drafts)
    {
        drafts.MapGet("/{id:guid}/line-pricing", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetDraftLinePricingAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftLinePricingV1")
            .Produces<SalesOrderDraftLinePricingResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        drafts.MapGet("/{id:guid}/lines/{lineId:guid}/price-references", async (
            Guid id,
            Guid lineId,
            HttpContext httpContext,
            [FromServices] ISalesDraftService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetLinePriceReferencesAsync(id, lineId, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftLinePriceReferencesV1")
            .Produces<SalesOrderDraftPriceReferencesResponse>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// AT-021: reasoned maker-checker approval of every current below-floor line. The owner service performs
    /// company/authority fences, version/idempotency, actor separation and the atomic write; HTTP only binds the strict
    /// request and preserves the existing unknown-result/replay contract.
    /// </summary>
    private static void MapPriceFloorExceptionApproval(RouteGroupBuilder drafts)
    {
        drafts.MapPost("/{id:guid}/below-floor-exception-approval", async (
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

            SalesDraftPriceFloorExceptionApprovalRequest? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync<SalesDraftPriceFloorExceptionApprovalRequest>(
                    httpContext.Request.Body, SalesDraftJson.Options, cancellationToken);
            }
            catch (JsonException)
            {
                body = null;
            }

            if (body is null)
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var response = await service.ApprovePriceFloorExceptionsAsync(id, body,
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
            .WithName("ApproveSalesOrderDraftPriceFloorExceptionsV1")
            .WithIdempotencyContract(SalesDraftApiV1.StateCommandStatus, StatusCodes.Status409Conflict)
            .Accepts<SalesDraftPriceFloorExceptionApprovalRequest>("application/json")
            .Produces<SalesOrderDraftResponse>(SalesDraftApiV1.StateCommandStatus)
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(SalesDraftProblemCodesV1.VersionConflict, SalesDraftProblemCodesV1.StateConflict,
                SalesDraftProblemCodesV1.SameKeyPayloadMismatch, SalesDraftProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
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
            [FromServices] ISalesDraftService legacyService,
            [FromServices] ISalesDraftReadServiceV1 enrichedService,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!TryReadDraftContract(httpContext.Request.Headers, out var contract))
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }
            try
            {
                if (contract == SalesDraftReadContract.RecordPartyDisplay)
                {
                    var result = await enrichedService.GetDraftDetailAsync(id, cancellationToken);
                    AcknowledgeDraftReadContract(httpContext.Response);
                    return Results.Json(result, SalesDraftJson.Options);
                }

                return Results.Json(await legacyService.GetDraftDetailAsync(id, cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithName("GetSalesOrderDraftV1")
            .Produces<SalesOrderDraftDetailView>()
            .ProducesErpBadRequest("validation_failed")
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static bool TryReadDraftContract(
        IHeaderDictionary headers,
        out SalesDraftReadContract contract)
    {
        contract = SalesDraftReadContract.Legacy;
        if (!headers.TryGetValue(DraftReadContractHeader, out var values))
        {
            return true;
        }

        if (values.Count != 1
            || !string.Equals(values[0], DraftReadContractVersion, StringComparison.Ordinal))
        {
            contract = SalesDraftReadContract.Invalid;
            return false;
        }

        contract = SalesDraftReadContract.RecordPartyDisplay;
        return true;
    }

    private static void AcknowledgeDraftReadContract(HttpResponse response) =>
        response.Headers[DraftReadContractHeader] = DraftReadContractVersion;

    public static async Task TransformDraftReadContractOpenApiAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.OperationId is not "GetSalesOrderDraftsV1" and not "GetSalesOrderDraftV1")
        {
            return;
        }

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = DraftReadContractHeader,
            In = ParameterLocation.Header,
            Required = false,
            Description = "Optional Sales read contract selector. Omit for the byte/schema-compatible legacy response; use the exact version to receive the record-bound Cari display.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Pattern = DraftReadContractVersionPattern,
            },
        });

        var legacyType = operation.OperationId == "GetSalesOrderDraftsV1"
            ? typeof(SalesOrderDraftListResponse)
            : typeof(SalesOrderDraftDetailView);
        var enrichedType = operation.OperationId == "GetSalesOrderDraftsV1"
            ? typeof(SalesOrderDraftReadListResponseV1)
            : typeof(SalesOrderDraftReadDetailViewV1);
        var document = context.Document
            ?? throw new InvalidOperationException("Sales read OpenAPI document is missing.");
        var enrichedSchema = await context.GetOrCreateSchemaAsync(
            enrichedType, cancellationToken: cancellationToken);
        var displaySchema = await context.GetOrCreateSchemaAsync(
            typeof(SalesPartyDisplayV1), cancellationToken: cancellationToken);
        var summarySchema = await context.GetOrCreateSchemaAsync(
            typeof(SalesOrderDraftReadSummaryViewV1), cancellationToken: cancellationToken);
        var productSnapshotSchema = await context.GetOrCreateSchemaAsync(
            typeof(SalesOrderDraftProductSnapshotViewV1), cancellationToken: cancellationToken);
        var components = document.Components ??= new OpenApiComponents();
        components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        components.Schemas[enrichedType.Name] = enrichedSchema;
        components.Schemas[nameof(SalesPartyDisplayV1)] = displaySchema;
        components.Schemas[nameof(SalesOrderDraftReadSummaryViewV1)] = summarySchema;
        components.Schemas[nameof(SalesOrderDraftProductSnapshotViewV1)] = productSnapshotSchema;
        var legacy = new OpenApiSchemaReference(legacyType.Name, document, externalResource: null);
        var enriched = new OpenApiSchemaReference(enrichedType.Name, document, externalResource: null);
        var display = new OpenApiSchemaReference(nameof(SalesPartyDisplayV1), document, externalResource: null);
        var summary = new OpenApiSchemaReference(nameof(SalesOrderDraftReadSummaryViewV1), document, externalResource: null);
        var stage = new OpenApiSchemaReference(nameof(SalesCommercialStageV1), document, externalResource: null);
        var productSnapshot = new OpenApiSchemaReference(nameof(SalesOrderDraftProductSnapshotViewV1), document, externalResource: null);
        var concreteSummarySchema = (OpenApiSchema)summarySchema;
        concreteSummarySchema.Properties!["partyDisplay"] = display;
        concreteSummarySchema.Properties["commercialStage"] = new OpenApiSchema
        {
            OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, stage],
        };
        concreteSummarySchema.Properties["productSnapshots"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Array | JsonSchemaType.Null,
            Items = productSnapshot,
        };
        if (enrichedType == typeof(SalesOrderDraftReadListResponseV1))
        {
            ((OpenApiSchema)enrichedSchema).Properties!["items"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = summary,
            };
        }

        if (enrichedType == typeof(SalesOrderDraftReadDetailViewV1))
        {
            var enrichedDetailSchema = (OpenApiSchema)enrichedSchema;
            enrichedDetailSchema.Properties!["draft"] = new OpenApiSchemaReference(
                nameof(SalesOrderDraftSummaryView), document, externalResource: null);
            enrichedDetailSchema.Properties["lines"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = new OpenApiSchemaReference(
                    nameof(SalesOrderDraftLineDetail), document, externalResource: null),
            };
            foreach (var propertyName in new[]
                     {
                         "deliveryAddress", "contact", "responsible", "paymentTerms", "sourceStrategy", "package",
                     })
            {
                enrichedDetailSchema.Properties[propertyName] = new OpenApiSchema
                {
                    OneOf =
                    [
                        new OpenApiSchema { Type = JsonSchemaType.Null },
                        new OpenApiSchemaReference(
                            nameof(SalesDraftOwnerReferenceV1), document, externalResource: null),
                    ],
                };
            }
            enrichedDetailSchema.Properties["approval"] = new OpenApiSchemaReference(
                nameof(SalesDraftApprovalV1), document, externalResource: null);
            enrichedDetailSchema.Properties["customerAcceptance"] = new OpenApiSchemaReference(
                nameof(SalesDraftCustomerAcceptanceV1), document, externalResource: null);
            enrichedDetailSchema.Properties!["partyDisplay"] = display;
        }
        var response = (OpenApiResponse)operation.Responses!["200"];
        var content = (OpenApiMediaType)response.Content!["application/json"];
        content.Schema = new OpenApiSchema { AnyOf = [legacy, enriched] };
        response.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
        response.Headers[DraftReadContractHeader] = new OpenApiHeader
        {
            Required = false,
            Description = "Present only on a successful enriched Sales read that accepted the exact request selector.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Pattern = DraftReadContractVersionPattern,
            },
        };
    }

    private enum SalesDraftReadContract
    {
        Invalid,
        Legacy,
        RecordPartyDisplay,
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
                new(request.Q, null, null, null, null, PartyRole.None, CounterpartyRoleMatch.Any,
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

using System.Text;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// The purchasing routes (PUR-4a; the Sales draft / Logistics routes' shape): purchase requests
/// (POST create → 201 + Location, GET list, GET detail, POST submission → 200) and purchase orders (POST
/// create → 201 + Location, GET list, GET detail, POST approval → 200). The route reads nothing but the
/// server context and the wire body; <see cref="IPurchasingService"/> decides, with a fresh central
/// measurement of the Purchasing permission at every fence (#276). The same attempt again answers the same
/// status with the replay headers (ADR-011 §4); a recorded terminal refusal (409) replays with the same
/// headers (§7). Problems carry <c>code</c>, <c>correlationId</c> and field <c>errors</c>; platform
/// 401/403/404 and every 503 stay code-less. No receipt, kantar or stock route (PUR-4b + Inventory T09).
/// </summary>
public static class PurchasingEndpoints
{
    public const string RequestsPath = PurchasingApiV1.RequestsPath;
    public const string OrdersPath = PurchasingApiV1.OrdersPath;
    private const string IdempotencyHeaderName = PurchasingApiV1.IdempotencyHeader;
    private const int MaximumIdempotencyKeyLength = 128;
    private const int MaximumCursorLength = 8192;
    private const int MaximumStatusLength = 32;
    private static readonly HashSet<string> ListQueryNames = new(["pageSize", "cursor", "status"], StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapPurchasingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapRequests(endpoints);
        MapOrders(endpoints);
        return endpoints;
    }

    private static void MapRequests(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(RequestsPath)
            .WithTags("Purchasing")
            .RequireAuthorization()
            .RequireCompanyContext();

        group.MapPost("/", (HttpContext httpContext, [FromServices] IPurchasingService service, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            CreateAsync(httpContext, correlation, PurchasingJson.ReadCreateRequest,
                (body, metadata) => service.CreateRequestAsync(body, metadata, cancellationToken),
                response => $"{RequestsPath}/{response.Detail.Request.Id:D}", cancellationToken))
            .WithName("CreatePurchaseRequestV1")
            .WithIdempotencyContract(PurchasingApiV1.CreatedStatus, StatusCodes.Status409Conflict)
            .Accepts<CreatePurchaseRequestRequest>("application/json")
            .Produces<PurchaseRequestResponse>(PurchasingApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(PurchasingProblemCodesV1.VersionConflict, PurchasingProblemCodesV1.SameKeyPayloadMismatch,
                PurchasingProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", PurchasingProblemCodesV1.UnsupportedUnit, PurchasingProblemCodesV1.UnsupportedProduct)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/", async ([FromQuery] int? pageSize, [FromQuery] string? cursor, [FromQuery] string? status, HttpContext httpContext,
            [FromServices] IPurchasingService service, ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!IsListQueryValid(httpContext, pageSize, cursor, status))
            {
                return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetRequestListAsync(new PurchaseRequestListQuery(pageSize ?? 50, cursor, status), cancellationToken),
                    PurchasingJson.Options);
            }
            catch (PurchasingServiceException exception)
            {
                return Problem(httpContext, exception, correlation);
            }
        })
            .WithName("GetPurchaseRequestsV1")
            .Produces<PurchaseRequestListResponse>()
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.InvalidCursor)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{id:guid}", async (Guid id, HttpContext httpContext, [FromServices] IPurchasingService service,
            ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetRequestDetailAsync(id, cancellationToken), PurchasingJson.Options);
            }
            catch (PurchasingServiceException exception)
            {
                return Problem(httpContext, exception, correlation);
            }
        })
            .WithName("GetPurchaseRequestV1")
            .Produces<PurchaseRequestDetailView>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/{id:guid}/submission", (Guid id, HttpContext httpContext, [FromServices] IPurchasingService service,
                ICorrelationContext correlation, CancellationToken cancellationToken) =>
            MutateAsync(httpContext, correlation, PurchasingJson.ReadSubmit,
                (body, metadata) => service.SubmitRequestAsync(id, body, metadata, cancellationToken), cancellationToken))
            .WithName("SubmitPurchaseRequestV1")
            .WithIdempotencyContract(PurchasingApiV1.MutationStatus, StatusCodes.Status409Conflict)
            .Accepts<SubmitPurchaseRequestRequest>("application/json")
            .Produces<PurchaseRequestResponse>(PurchasingApiV1.MutationStatus)
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(PurchasingProblemCodesV1.VersionConflict, PurchasingProblemCodesV1.RequestNotDraft,
                PurchasingProblemCodesV1.SameKeyPayloadMismatch, PurchasingProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static void MapOrders(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(OrdersPath)
            .WithTags("Purchasing")
            .RequireAuthorization()
            .RequireCompanyContext();

        group.MapPost("/", (HttpContext httpContext, [FromServices] IPurchasingService service, ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            CreateAsync(httpContext, correlation, PurchasingJson.ReadCreateOrder,
                (body, metadata) => service.CreateOrderAsync(body, metadata, cancellationToken),
                response => $"{OrdersPath}/{response.Detail.Order.Id:D}", cancellationToken))
            .WithName("CreatePurchaseOrderV1")
            .WithIdempotencyContract(PurchasingApiV1.CreatedStatus, StatusCodes.Status409Conflict)
            .Accepts<CreatePurchaseOrderRequest>("application/json")
            .Produces<PurchaseOrderResponse>(PurchasingApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(PurchasingProblemCodesV1.AccountState, PurchasingProblemCodesV1.RequestState, PurchasingProblemCodesV1.VersionConflict,
                PurchasingProblemCodesV1.SameKeyPayloadMismatch, PurchasingProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", PurchasingProblemCodesV1.UnsupportedUnit, PurchasingProblemCodesV1.UnsupportedProduct)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/", async ([FromQuery] int? pageSize, [FromQuery] string? cursor, [FromQuery] string? status, HttpContext httpContext,
            [FromServices] IPurchasingService service, ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!IsListQueryValid(httpContext, pageSize, cursor, status))
            {
                return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetOrderListAsync(new PurchaseOrderListQuery(pageSize ?? 50, cursor, status), cancellationToken),
                    PurchasingJson.Options);
            }
            catch (PurchasingServiceException exception)
            {
                return Problem(httpContext, exception, correlation);
            }
        })
            .WithName("GetPurchaseOrdersV1")
            .Produces<PurchaseOrderListResponse>()
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.InvalidCursor)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{id:guid}", async (Guid id, HttpContext httpContext, [FromServices] IPurchasingService service,
            ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetOrderDetailAsync(id, cancellationToken), PurchasingJson.Options);
            }
            catch (PurchasingServiceException exception)
            {
                return Problem(httpContext, exception, correlation);
            }
        })
            .WithName("GetPurchaseOrderV1")
            .Produces<PurchaseOrderDetailView>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/{id:guid}/approval", (Guid id, HttpContext httpContext, [FromServices] IPurchasingService service,
                ICorrelationContext correlation, CancellationToken cancellationToken) =>
            MutateAsync(httpContext, correlation, PurchasingJson.ReadApprove,
                (body, metadata) => service.ApproveOrderAsync(id, body, metadata, cancellationToken), cancellationToken))
            .WithName("ApprovePurchaseOrderV1")
            .WithIdempotencyContract(PurchasingApiV1.MutationStatus, StatusCodes.Status409Conflict)
            .Accepts<ApprovePurchaseOrderRequest>("application/json")
            .Produces<PurchaseOrderResponse>(PurchasingApiV1.MutationStatus)
            .ProducesErpBadRequest("validation_failed", PurchasingProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(PurchasingProblemCodesV1.VersionConflict, PurchasingProblemCodesV1.OrderState,
                PurchasingProblemCodesV1.RequestLineOverSourced, PurchasingProblemCodesV1.SameKeyPayloadMismatch, PurchasingProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static bool IsListQueryValid(HttpContext httpContext, int? pageSize, string? cursor, string? status) =>
        httpContext.Request.Query.All(pair => ListQueryNames.Contains(pair.Key) && pair.Value.Count == 1)
        && pageSize is null or (>= 1 and <= 100) && !(cursor?.Length > MaximumCursorLength) && status is not { Length: 0 or > MaximumStatusLength };

    private static async Task<IResult> CreateAsync<TBody, TResponse>(HttpContext httpContext, ICorrelationContext correlation, Func<string, TBody> read,
        Func<TBody, PurchasingCommandMetadata, Task<TResponse>> command, Func<TResponse, string> locationPath, CancellationToken cancellationToken)
        where TBody : class
        where TResponse : class
    {
        SetPrivateNoStore(httpContext.Response);
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey) || !correlation.IsResolved)
        {
            return Problem(StatusCodes.Status400BadRequest, PurchasingProblemCodesV1.CommandMetadataInvalid, correlation);
        }

        if (!httpContext.Request.HasJsonContentType())
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        TBody body;
        try
        {
            body = read(await ReadBodyAsync(httpContext, cancellationToken));
        }
        catch (JsonException)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
        }

        try
        {
            var response = await command(body, Metadata(idempotencyKey, correlation));
            if (IsReplay(response))
            {
                ApplyReplayHeaders(httpContext, OriginalCorrelationOf(response));
            }

            httpContext.Response.Headers.Location =
                $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}{locationPath(response)}";
            return Results.Json(response, PurchasingJson.Options, statusCode: PurchasingApiV1.CreatedStatus);
        }
        catch (PurchasingServiceException exception)
        {
            return Problem(httpContext, exception, correlation);
        }
    }

    private static async Task<IResult> MutateAsync<TBody, TResponse>(HttpContext httpContext, ICorrelationContext correlation, Func<string, TBody> read,
        Func<TBody, PurchasingCommandMetadata, Task<TResponse>> command, CancellationToken cancellationToken)
        where TBody : class
        where TResponse : class
    {
        SetPrivateNoStore(httpContext.Response);
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey) || !correlation.IsResolved)
        {
            return Problem(StatusCodes.Status400BadRequest, PurchasingProblemCodesV1.CommandMetadataInvalid, correlation);
        }

        if (!httpContext.Request.HasJsonContentType())
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        TBody body;
        try
        {
            body = read(await ReadBodyAsync(httpContext, cancellationToken));
        }
        catch (JsonException)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
        }

        try
        {
            var response = await command(body, Metadata(idempotencyKey, correlation));
            if (IsReplay(response))
            {
                ApplyReplayHeaders(httpContext, OriginalCorrelationOf(response));
            }

            return Results.Json(response, PurchasingJson.Options, statusCode: PurchasingApiV1.MutationStatus);
        }
        catch (PurchasingServiceException exception)
        {
            return Problem(httpContext, exception, correlation);
        }
    }

    private static bool IsReplay(object response) => response switch
    {
        PurchaseRequestResponse request => request.IsReplay,
        PurchaseOrderResponse order => order.IsReplay,
        _ => false,
    };

    private static string OriginalCorrelationOf(object response) => response switch
    {
        PurchaseRequestResponse request => request.OriginalCorrelationId,
        PurchaseOrderResponse order => order.OriginalCorrelationId,
        _ => throw new InvalidOperationException("Replay without its original correlation."),
    };

    private static async Task<string> ReadBodyAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static PurchasingCommandMetadata Metadata(string idempotencyKey, ICorrelationContext correlation) =>
        new(idempotencyKey, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow);

    /// <summary>Exactly one Idempotency-Key: non-blank, canonical, at most 128 characters, no comma.</summary>
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

    /// <summary>Platform 401/403/404 and every 503 stay code-less (the platform writer's public code); every other status carries its code and errors.</summary>
    private static IResult Problem(HttpContext httpContext, PurchasingServiceException exception, ICorrelationContext correlation)
    {
        if (exception.IsReplay)
        {
            ApplyReplayHeaders(httpContext, exception.OriginalCorrelationId
                ?? throw new InvalidOperationException("Replayed refusal without its original correlation."));
        }

        if (exception.Status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound
            or StatusCodes.Status503ServiceUnavailable)
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

    private static IResult Problem(int statusCode, string code, ICorrelationContext correlation) => Results.Problem(statusCode: statusCode,
        extensions: new Dictionary<string, object?> { ["code"] = code, ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null });

    private static void SetPrivateNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }
}

/// <summary>
/// The Cari owner's answer for the order's supplier (<see cref="IPurchasingPartyAccountPortV1"/>), over the
/// authorized Counterparty party read, exactly as the Sales and Logistics party ports: Denied when the server
/// context is not the request's, NotFound when the party or the account is not the owner's in this company;
/// the account's Supplier role flag is the owner's stored value. PROPOSAL for the central writer (CARİ), who
/// adopts or rewrites it and is its single writer from the central step on.
/// </summary>
public sealed class CounterpartyPurchasingPartyAccountPortV1(ICompanyContext companyContext, ICurrentUser currentUser,
    ICounterpartyReadService counterparties) : IPurchasingPartyAccountPortV1
{
    public async Task<PurchasingPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent() || partyId == Guid.Empty || companyAccountId == Guid.Empty)
        {
            return new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.Denied, null);
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
            return new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.Denied, null);
        }

        // The owner answered for the context captured before the await — the company AND the actor;
        // a changed context releases nothing. IsCurrent() alone is not enough: it re-checks the
        // CURRENT actor, so a swap to another actor with access to the same company would pass it.
        if (!IsCurrent() || companyContext.CompanyId != companyId
            || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
        {
            return new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.Denied, null);
        }

        if (party is null || party.Id != partyId)
        {
            return new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.NotFound, null);
        }

        var accounts = party.CompanyAccounts.Where(account => account.Id == companyAccountId && account.CompanyId == companyId).ToArray();
        if (accounts.Length != 1)
        {
            return new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.NotFound, null);
        }

        var account = accounts[0];
        return new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.Found, new PurchasingPartyAccountV1(party.Id, account.Id, account.CompanyId,
            party.IsActive, account.IsActive, account.Roles.HasFlag(PartyRole.Supplier), party.Version, account.Version));
    }

    private bool IsCurrent() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId) && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && currentUser.CanAccessCompany(companyContext.CompanyId);
}

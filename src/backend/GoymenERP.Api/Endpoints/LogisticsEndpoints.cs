using System.Text;
using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// The loading-instruction routes (LOG-4a; the Sales draft routes' shape). The route reads nothing but
/// the server context and the wire body; <see cref="ILogisticsInstructionService"/> decides, with a
/// fresh central measurement of the Logistics permission at every fence (#276). POST create → 201 +
/// Location (and again on the same attempt, with the replay headers, ADR-011 §4); POST issue and POST
/// load → 200 with the instruction after the transition; a recorded terminal refusal (409) replays with
/// the same headers (§7). Problems carry <c>code</c>, <c>correlationId</c> and field <c>errors</c>;
/// platform 401/403/404 and every 503 stay code-less. Definite shipment and delivery have no route (D3, LOG-5).
/// </summary>
public static class LogisticsEndpoints
{
    public const string InstructionsPath = LogisticsApiV1.InstructionsPath;
    private const string IdempotencyHeaderName = LogisticsApiV1.IdempotencyHeader;
    private const int MaximumIdempotencyKeyLength = 128;
    private const int MaximumCursorLength = 8192;
    private const int MaximumStatusLength = 32;
    private static readonly HashSet<string> ListQueryNames = new(["pageSize", "cursor", "status"], StringComparer.Ordinal);
    private static readonly HashSet<string> ApprovedLinesQueryNames = new(["pageSize", "cursor"], StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapLogisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(InstructionsPath)
            .WithTags("Logistics")
            .RequireAuthorization()
            .RequireCompanyContext();

        group.MapPost("/", async (HttpContext httpContext, [FromServices] ILogisticsInstructionService service, ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey) || !correlation.IsResolved)
            {
                return Problem(StatusCodes.Status400BadRequest, LogisticsProblemCodesV1.CommandMetadataInvalid, correlation);
            }

            if (!httpContext.Request.HasJsonContentType())
            {
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            }

            CreateLoadingInstructionRequest request;
            try
            {
                request = LogisticsJson.ReadCreate(await ReadBodyAsync(httpContext, cancellationToken));
            }
            catch (JsonException)
            {
                return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                var response = await service.CreateInstructionAsync(request, Metadata(idempotencyKey, correlation), cancellationToken);
                if (response.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, response.OriginalCorrelationId);
                }

                httpContext.Response.Headers.Location =
                    $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}{InstructionsPath}/{response.Detail.Instruction.Id:D}";
                return Results.Json(response, LogisticsJson.Options, statusCode: LogisticsApiV1.CreatedStatus);
            }
            catch (LogisticsInstructionServiceException exception)
            {
                return Problem(httpContext, exception, correlation);
            }
        })
            .WithName("CreateLoadingInstructionV1")
            .WithIdempotencyContract(LogisticsApiV1.CreatedStatus, StatusCodes.Status409Conflict)
            .Accepts<CreateLoadingInstructionRequest>("application/json")
            .Produces<LoadingInstructionResponse>(LogisticsApiV1.CreatedStatus)
            .ProducesErpBadRequest("validation_failed", LogisticsProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(LogisticsProblemCodesV1.AccountState, LogisticsProblemCodesV1.OrderLineState,
                LogisticsProblemCodesV1.SameKeyPayloadMismatch, LogisticsProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", LogisticsProblemCodesV1.UnsupportedUnit)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/", async ([FromQuery] int? pageSize, [FromQuery] string? cursor, [FromQuery] string? status, HttpContext httpContext,
            [FromServices] ILogisticsInstructionService service, ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!httpContext.Request.Query.All(pair => ListQueryNames.Contains(pair.Key) && pair.Value.Count == 1)
                || pageSize is < 1 or > 100 || cursor?.Length > MaximumCursorLength || status is { Length: 0 or > MaximumStatusLength })
            {
                return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetInstructionListAsync(new LoadingInstructionListQuery(pageSize ?? 50, cursor, status), cancellationToken),
                    LogisticsJson.Options);
            }
            catch (LogisticsInstructionServiceException exception)
            {
                return Problem(httpContext, exception, correlation);
            }
        })
            .WithName("GetLoadingInstructionsV1")
            .Produces<LoadingInstructionListResponse>()
            .ProducesErpBadRequest("validation_failed", LogisticsProblemCodesV1.InvalidCursor)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // LOG-4b: the price-free picker read (Sinan §4 #66 5738113623 — the Sevkiyatçı sees orders, never prices): the APPROVED
        // sales order lines as the Sales owner projects them for loading, under the create name. No price, discount, currency or
        // payment member exists on the wire type (pinned by a contract test); a static segment, so it never reads as an instruction id.
        group.MapGet("/approved-order-lines", async ([FromQuery] int? pageSize, [FromQuery] string? cursor, HttpContext httpContext,
            [FromServices] ILogisticsInstructionService service, ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!httpContext.Request.Query.All(pair => ApprovedLinesQueryNames.Contains(pair.Key) && pair.Value.Count == 1)
                || pageSize is < 1 or > 100 || cursor?.Length > MaximumCursorLength)
            {
                return Problem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetApprovedOrderLinesAsync(new LogisticsApprovedOrderLinesQuery(pageSize ?? 50, cursor), cancellationToken),
                    LogisticsJson.Options);
            }
            catch (LogisticsInstructionServiceException exception)
            {
                return Problem(httpContext, exception, correlation);
            }
        })
            .WithName("GetLogisticsApprovedOrderLinesV1")
            .Produces<LogisticsApprovedOrderLinePage>()
            .ProducesErpBadRequest("validation_failed", LogisticsProblemCodesV1.InvalidCursor)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{id:guid}", async (Guid id, HttpContext httpContext, [FromServices] ILogisticsInstructionService service,
            ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            try
            {
                return Results.Json(await service.GetInstructionDetailAsync(id, cancellationToken), LogisticsJson.Options);
            }
            catch (LogisticsInstructionServiceException exception)
            {
                return Problem(httpContext, exception, correlation);
            }
        })
            .WithName("GetLoadingInstructionV1")
            .Produces<LoadingInstructionDetailView>()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // The mutations: the issue (Draft → Issued) and the load share; one canonical Idempotency-Key each, the strict
        // body, 200 with the instruction after the transition, 200 + replay headers for the same key again.
        group.MapPost("/{id:guid}/issue", (Guid id, HttpContext httpContext, [FromServices] ILogisticsInstructionService service,
                ICorrelationContext correlation, CancellationToken cancellationToken) =>
            MutateAsync(httpContext, service, correlation, LogisticsJson.ReadIssue,
                (body, metadata) => service.IssueInstructionAsync(id, body, metadata, cancellationToken), cancellationToken))
            .WithName("IssueLoadingInstructionV1")
            .WithIdempotencyContract(LogisticsApiV1.MutationStatus, StatusCodes.Status409Conflict)
            .Accepts<IssueLoadingInstructionRequest>("application/json")
            .Produces<LoadingInstructionResponse>(LogisticsApiV1.MutationStatus)
            .ProducesErpBadRequest("validation_failed", LogisticsProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(LogisticsProblemCodesV1.VersionConflict, LogisticsProblemCodesV1.InstructionState,
                LogisticsProblemCodesV1.SameKeyPayloadMismatch, LogisticsProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed")
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/{id:guid}/lines/{lineId:guid}/loads", (Guid id, Guid lineId, HttpContext httpContext,
                [FromServices] ILogisticsInstructionService service, ICorrelationContext correlation, CancellationToken cancellationToken) =>
            MutateAsync(httpContext, service, correlation, LogisticsJson.ReadRecordLoad,
                (body, metadata) => service.RecordLoadAsync(id, lineId, body, metadata, cancellationToken), cancellationToken))
            .WithName("RecordLoadingInstructionLoadV1")
            .WithIdempotencyContract(LogisticsApiV1.MutationStatus, StatusCodes.Status409Conflict)
            .Accepts<RecordLoadRequest>("application/json")
            .Produces<LoadingInstructionResponse>(LogisticsApiV1.MutationStatus)
            .ProducesErpBadRequest("validation_failed", LogisticsProblemCodesV1.CommandMetadataInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(LogisticsProblemCodesV1.VersionConflict, LogisticsProblemCodesV1.InstructionState,
                LogisticsProblemCodesV1.LoadExceedsInstructed, LogisticsProblemCodesV1.LineBlocked, LogisticsProblemCodesV1.DuplicateShare,
                LogisticsProblemCodesV1.SameKeyPayloadMismatch, LogisticsProblemCodesV1.ConcurrentChange)
            .ProducesErpUnprocessableEntity("validation_failed", LogisticsProblemCodesV1.UnsupportedUnit)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static async Task<IResult> MutateAsync<TBody>(HttpContext httpContext, ILogisticsInstructionService service, ICorrelationContext correlation,
        Func<string, TBody> read, Func<TBody, LogisticsCommandMetadata, Task<LoadingInstructionResponse>> command, CancellationToken cancellationToken)
        where TBody : class
    {
        _ = service;
        SetPrivateNoStore(httpContext.Response);
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey) || !correlation.IsResolved)
        {
            return Problem(StatusCodes.Status400BadRequest, LogisticsProblemCodesV1.CommandMetadataInvalid, correlation);
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
            if (response.IsReplay)
            {
                ApplyReplayHeaders(httpContext, response.OriginalCorrelationId);
            }

            return Results.Json(response, LogisticsJson.Options, statusCode: LogisticsApiV1.MutationStatus);
        }
        catch (LogisticsInstructionServiceException exception)
        {
            return Problem(httpContext, exception, correlation);
        }
    }

    private static async Task<string> ReadBodyAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static LogisticsCommandMetadata Metadata(string idempotencyKey, ICorrelationContext correlation) =>
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
    private static IResult Problem(HttpContext httpContext, LogisticsInstructionServiceException exception, ICorrelationContext correlation)
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
/// The Cari owner's answer for the instruction's customer (<see cref="ILogisticsPartyAccountPortV1"/>), over
/// the authorized Counterparty party read, exactly as the Sales party port: Denied when the server context
/// is not the request's, NotFound when the party or the account is not the owner's in this company.
/// </summary>
public sealed class CounterpartyLogisticsPartyAccountPortV1(ICompanyContext companyContext, ICurrentUser currentUser,
    ICounterpartyReadService counterparties) : ILogisticsPartyAccountPortV1
{
    public async Task<LogisticsPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent() || partyId == Guid.Empty || companyAccountId == Guid.Empty)
        {
            return new(LogisticsPartyAccountOutcomeV1.Denied, null);
        }

        var companyId = companyContext.CompanyId;
        CounterpartyDetailView? party;
        try
        {
            party = await counterparties.GetAuthorizedPartyAsync(partyId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return new(LogisticsPartyAccountOutcomeV1.Denied, null);
        }

        if (!IsCurrent() || companyContext.CompanyId != companyId)
        {
            return new(LogisticsPartyAccountOutcomeV1.Denied, null);
        }

        if (party is null || party.Id != partyId)
        {
            return new(LogisticsPartyAccountOutcomeV1.NotFound, null);
        }

        var accounts = party.CompanyAccounts.Where(account => account.Id == companyAccountId && account.CompanyId == companyId).ToArray();
        if (accounts.Length != 1)
        {
            return new(LogisticsPartyAccountOutcomeV1.NotFound, null);
        }

        var account = accounts[0];
        return new(LogisticsPartyAccountOutcomeV1.Found, new LogisticsPartyAccountV1(party.Id, account.Id, account.CompanyId,
            party.IsActive, account.IsActive, party.Version, account.Version));
    }

    private bool IsCurrent() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId) && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && currentUser.CanAccessCompany(companyContext.CompanyId);
}

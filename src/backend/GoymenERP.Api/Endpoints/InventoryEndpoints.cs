using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// T07(a): Inventory's read surface, company-scoped. Each read needs the server-resolved company
/// (<c>X-Company-Id</c>) and its own permission, checked by the host's policy against the actor's
/// current grants. A label of another company, or one that does not exist, is the same 404: the
/// tenant filter never shows it. The one write is the opening count (INV-OPEN-1 part 2), below; the
/// receipt is a later slice.
/// </summary>
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/inventory")
            .RequireAuthorization()
            .WithTags("Inventory");

        group.MapGet("/labels/{labelId:guid}", async (
                Guid labelId,
                HttpContext httpContext,
                IInventoryLabelStore labels,
                IInventoryLotStore lots,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                var label = await labels.FindLabelAsync(labelId, cancellationToken);
                if (label is null)
                {
                    return NotFound();
                }

                // The (company, lot) foreign key keeps every stored label's lot in its own company, so a
                // missing lot here is a broken store, not an answer.
                var lot = await lots.FindLotAsync(label.LotId, cancellationToken)
                    ?? throw new InvalidOperationException("Kayıtlı etiketin lotu bulunamadı.");
                return Results.Ok(InventoryReadModelsV1.LabelView(label, lot));
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelsRead)
            .WithName("GetInventoryLabelV1")
            .Produces<InventoryLabelViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/labels/{labelId:guid}/balances", async (
                Guid labelId,
                HttpContext httpContext,
                IInventoryLabelStore labels,
                IInventoryMovementStore movements,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                // The label must be this company's; its facts are not returned here, only its stock.
                if (await labels.FindLabelAsync(labelId, cancellationToken) is null)
                {
                    return NotFound();
                }

                var balances = await movements.ReadBalancesAsync(labelId, cancellationToken);
                return Results.Ok(InventoryReadModelsV1.Balances(labelId, balances));
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryBalancesRead)
            .WithName("GetInventoryLabelBalancesV1")
            .Produces<InventoryLabelBalancesV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpAuthenticatedProblems();

        MapParentRollCandidateEndpoint(endpoints);
        MapOpeningCountEndpoints(group);
        return endpoints;
    }

    private static void MapParentRollCandidateEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(InventoryParentRollApiV1.CandidatesPath, async (
                HttpContext httpContext,
                IInventoryParentRollCandidatesV1 candidates,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                if (!TryReadParentRollQuery(httpContext.Request.Query, out var query))
                {
                    return ParentRollProblem(StatusCodes.Status400BadRequest,
                        InventoryParentRollCodesV1.InvalidQuery);
                }

                try
                {
                    return Results.Ok(await candidates.SearchAsync(query, cancellationToken));
                }
                catch (InventoryParentRollException exception)
                {
                    return exception.StatusCode switch
                    {
                        StatusCodes.Status400BadRequest
                            when exception.Code == InventoryParentRollCodesV1.InvalidQuery =>
                            ParentRollProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status409Conflict
                            when exception.Code == InventoryParentRollCodesV1.ReadConflict =>
                            ParentRollProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status503ServiceUnavailable
                            when exception.Code == InventoryParentRollCodesV1.Unavailable =>
                            ParentRollProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden =>
                            ParentRollProblem(exception.StatusCode),
                        _ => ParentRollProblem(StatusCodes.Status503ServiceUnavailable),
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return ParentRollProblem(StatusCodes.Status503ServiceUnavailable);
                }
            })
            .WithName(InventoryParentRollApiV1.GetCandidatesOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead)
            .Produces<InventoryParentRollCandidatesV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(InventoryParentRollCodesV1.InvalidQuery)
            .ProducesErpConflict(InventoryParentRollCodesV1.ReadConflict)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static bool TryReadParentRollQuery(
        IQueryCollection query,
        out InventoryParentRollQueryV1 parsed)
    {
        parsed = new();
        if (query.Keys.Any(name => name is not "q" and not "limit")
            || query.Any(parameter => parameter.Value.Count != 1))
        {
            return false;
        }

        string? q = null;
        if (query.TryGetValue("q", out var qValues))
        {
            q = qValues[0];
        }

        var limit = 25;
        if (query.TryGetValue("limit", out var limitValues)
            && (!int.TryParse(limitValues[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                || limit is < 1 or > 100))
        {
            return false;
        }

        parsed = new InventoryParentRollQueryV1(q, limit);
        return true;
    }

    private static IResult ParentRollProblem(int statusCode, string? code = null) => code is null
        ? Results.Problem(statusCode: statusCode)
        : Results.Problem(statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>
    /// INV-OPEN-1 part 2 (T07 = A, #66 5738113623 §2): record an opening count and read one back. The
    /// permission (<c>inventory.opening-stock.record</c>), the company, the location and every line's product
    /// are measured by the owner's service at each fence, not by a route policy, so a revocation in flight is
    /// seen. ADR-011: one canonical <c>Idempotency-Key</c>; 201 with the count on the first recording, 200 for
    /// the same key again; a replay — success or a recorded terminal refusal (§7) — carries
    /// <c>Idempotency-Replayed</c> and <c>Idempotency-Original-Correlation-Id</c>. The problem boundary is the
    /// Sales one: 401/403/404/503 are platform problems without the owner's code; 400/409/422 carry the
    /// code, the per-field errors and the correlation id. Every advertised code is the owner's closed set.
    /// </summary>
    private static void MapOpeningCountEndpoints(RouteGroupBuilder group)
    {
        group.MapPost(OpeningCountsPath, async (
                HttpContext httpContext,
                IInventoryOpeningCommandService service,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key) || !correlation.IsResolved)
                {
                    return OpeningProblem(StatusCodes.Status400BadRequest, InventoryOpeningCodesV1.ValidationFailed, correlation);
                }

                if (!httpContext.Request.HasJsonContentType())
                {
                    return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                OpeningCountRequestV1? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<OpeningCountRequestV1>(httpContext.Request.Body,
                        OpeningJson, cancellationToken);
                }
                catch (JsonException)
                {
                    return OpeningProblem(StatusCodes.Status400BadRequest, InventoryOpeningCodesV1.ValidationFailed, correlation);
                }

                if (request is null)
                {
                    return OpeningProblem(StatusCodes.Status400BadRequest, InventoryOpeningCodesV1.ValidationFailed, correlation);
                }

                // The actor, role codes and scope are the service's own fresh measurement; the host passes only
                // the request's identity and transport facts.
                var metadata = new InventoryCommandMetadata(
                    currentUser.UserId ?? string.Empty,
                    ErpPermissions.InventoryOpeningStockRecord,
                    [],
                    correlation.CorrelationId,
                    correlation.CausationId,
                    key,
                    correlation.SourceChannel,
                    correlation.ClientIpAddress,
                    correlation.UserAgent);
                try
                {
                    var result = await service.RecordAsync(request, metadata, cancellationToken);
                    if (result.IsReplay)
                    {
                        ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                    }

                    return Results.Json(result.Count, OpeningJson,
                        statusCode: result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
                }
                catch (InventoryOpeningServiceException exception)
                {
                    if (exception.IsReplay)
                    {
                        ApplyReplayHeaders(httpContext, exception.OriginalCorrelationId
                            ?? throw new InvalidOperationException("Replayed refusal without its original correlation."));
                    }

                    return OpeningProblem(exception, correlation);
                }
            })
            .RequireCompanyContext()
            .WithName("RecordInventoryOpeningCountV1")
            // The replayable results: the count (200 on a replay) and the recorded terminal refusals (409, 422).
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<OpeningCountRequestV1>("application/json")
            .Produces<OpeningCountViewV1>(StatusCodes.Status201Created)
            .Produces<OpeningCountViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryOpeningCodesV1.Status400Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict([.. InventoryOpeningCodesV1.Status409Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnprocessableEntity([.. InventoryOpeningCodesV1.Status422Codes.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet(OpeningCountsPath + "/{countId:guid}", async (
                Guid countId,
                HttpContext httpContext,
                IInventoryOpeningCommandService service,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                try
                {
                    return Results.Json(await service.ReadAsync(countId, cancellationToken), OpeningJson);
                }
                catch (InventoryOpeningServiceException exception)
                {
                    return OpeningProblem(exception, correlation);
                }
            })
            .RequireCompanyContext()
            .WithName("GetInventoryOpeningCountV1")
            .Produces<OpeningCountViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// The service's refusal as a problem: platform 401/403/404/503 stay code-less (the platform writer
    /// answers them with its own public code), every other status carries the owner's code and field errors.
    /// </summary>
    private static IResult OpeningProblem(InventoryOpeningServiceException exception, ICorrelationContext correlation)
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
        if (exception.Issues.Count > 0)
        {
            extensions["errors"] = exception.Issues;
        }

        return Results.Problem(statusCode: exception.Status, extensions: extensions);
    }

    private static IResult OpeningProblem(int statusCode, string code, ICorrelationContext correlation) => Results.Problem(
        statusCode: statusCode,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = code,
            ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null,
        });

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

    private static void SetPrivateNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }

    private const string OpeningCountsPath = "/opening-counts";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    // The contract DTOs refuse unknown and missing required members themselves (JsonUnmappedMemberHandling,
    // JsonRequired); web defaults give camelCase names and ISO dates.
    private static readonly JsonSerializerOptions OpeningJson = new(JsonSerializerDefaults.Web);

    private static IResult NotFound() =>
        Results.Problem(
            title: "Kayıt bulunamadı.",
            detail: "İstenen kayıt bulunamadı veya bu bağlamda kullanılamıyor.",
            statusCode: StatusCodes.Status404NotFound);
}

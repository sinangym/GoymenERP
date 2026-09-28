using System.Text.Json;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class InventoryEndpoints
{
    private const string StockScrapDecisionRoute = "/api/v1/inventory/stock-scraps/{scrapId:guid}/decision";
    private const string StockScrapLabelRoute = "/api/v1/inventory/labels/{labelId:guid}/stock-scraps";

    /// <summary>
    /// ST-02 scrap write-off: record a scrap of one stocked label, approve or reject it, and read a label's scraps.
    /// Mapped by <see cref="MapInventoryEndpoints"/>; the route policies name the owner's
    /// <see cref="InventoryPermissionManifestV1.StockScrapRecord"/> and
    /// <see cref="InventoryPermissionManifestV1.StockScrapApprove"/> (registered by the central catalogue) and the host
    /// wires <c>AddInventoryStockScraps()</c>. The stock-count (AT-037) shape: one canonical <c>Idempotency-Key</c>;
    /// 201 with the scrap view, 200 with the same view plus <c>Idempotency-Replayed</c> and
    /// <c>Idempotency-Original-Correlation-Id</c> for the same key and content; 401/403 are platform problems;
    /// 400/404/409/503 carry the owner's closed code (<see cref="InventoryStockScrapHttpCodesV1"/>) and the correlation
    /// id; any other owner answer is a code-less 503. The read reuses the ST-02 label-detail fence (labels.read +
    /// balances.read); a foreign or unknown label is the same 404.
    /// </summary>
    public static IEndpointRouteBuilder MapInventoryStockScrapEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost(InventoryStockScrapApiV1.Path, async (
                HttpContext httpContext,
                IInventoryStockScrapCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadStockScrapBodyAsync<RecordInventoryStockScrapV1>(httpContext, correlation, cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.RecordAsync(read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key, InventoryPermissionManifestV1.StockScrapRecord),
                        cancellationToken);
                    return StockScrapResult(httpContext, result);
                }
                catch (InventoryStockScrapException exception)
                {
                    return StockScrapProblem(exception, correlation, InventoryStockScrapHttpCodesV1.RecordStatus404Codes,
                        InventoryStockScrapHttpCodesV1.RecordStatus409Codes);
                }
            })
            .WithName(InventoryStockScrapApiV1.RecordOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(InventoryPermissionManifestV1.StockScrapRecord)
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<RecordInventoryStockScrapV1>("application/json")
            .Produces<InventoryStockScrapViewV1>(StatusCodes.Status201Created)
            .Produces<InventoryStockScrapViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryStockScrapHttpCodesV1.Status400Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound([.. InventoryStockScrapHttpCodesV1.RecordStatus404Codes.Order(StringComparer.Ordinal)])
            .ProducesErpConflict([.. InventoryStockScrapHttpCodesV1.RecordStatus409Codes.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable([.. InventoryStockScrapHttpCodesV1.Status503Codes.Order(StringComparer.Ordinal)])
            .ProducesErpAuthenticatedProblems();

        endpoints.MapPost(StockScrapDecisionRoute, async (
                Guid scrapId,
                HttpContext httpContext,
                IInventoryStockScrapCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadStockScrapBodyAsync<DecideInventoryStockScrapV1>(httpContext, correlation, cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.DecideAsync(scrapId, read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key, InventoryPermissionManifestV1.StockScrapApprove),
                        cancellationToken);
                    return StockScrapResult(httpContext, result);
                }
                catch (InventoryStockScrapException exception)
                {
                    return StockScrapProblem(exception, correlation, InventoryStockScrapHttpCodesV1.DecideStatus404Codes,
                        InventoryStockScrapHttpCodesV1.DecideStatus409Codes);
                }
            })
            .WithName(InventoryStockScrapApiV1.DecideOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(InventoryPermissionManifestV1.StockScrapApprove)
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<DecideInventoryStockScrapV1>("application/json")
            .Produces<InventoryStockScrapViewV1>(StatusCodes.Status201Created)
            .Produces<InventoryStockScrapViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryStockScrapHttpCodesV1.Status400Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound([.. InventoryStockScrapHttpCodesV1.DecideStatus404Codes.Order(StringComparer.Ordinal)])
            .ProducesErpConflict([.. InventoryStockScrapHttpCodesV1.DecideStatus409Codes.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable([.. InventoryStockScrapHttpCodesV1.Status503Codes.Order(StringComparer.Ordinal)])
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(StockScrapLabelRoute, async (
                Guid labelId,
                HttpContext httpContext,
                IInventoryStockScrapQueryV1 scraps,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                try
                {
                    return Results.Json(await scraps.GetAsync(labelId, cancellationToken), StockCountJson);
                }
                catch (InventoryStockScrapException exception)
                {
                    return exception.Status switch
                    {
                        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden =>
                            Results.Problem(statusCode: exception.Status),
                        StatusCodes.Status404NotFound => LabelDetailProblem(StatusCodes.Status404NotFound),
                        _ => LabelDetailProblem(StatusCodes.Status503ServiceUnavailable, InventoryStockScrapCodesV1.Unavailable),
                    };
                }
            })
            .WithName(InventoryStockScrapApiV1.LabelScrapsOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead)
            .Produces<InventoryLabelStockScrapsV1>(StatusCodes.Status200OK)
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(InventoryStockScrapCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static async Task<(T? Request, string Key, IResult? Problem)> ReadStockScrapBodyAsync<T>(
        HttpContext httpContext,
        ICorrelationContext correlation,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key) || !correlation.IsResolved)
        {
            return (null, string.Empty, LabelTransformationProblem(StatusCodes.Status400BadRequest,
                InventoryStockScrapCodesV1.ValidationFailed, correlation));
        }

        if (!httpContext.Request.HasJsonContentType())
        {
            return (null, key, Results.StatusCode(StatusCodes.Status415UnsupportedMediaType));
        }

        try
        {
            var request = await JsonSerializer.DeserializeAsync<T>(httpContext.Request.Body, StockCountJson, cancellationToken);
            return request is null
                ? (null, key, LabelTransformationProblem(StatusCodes.Status400BadRequest,
                    InventoryStockScrapCodesV1.ValidationFailed, correlation))
                : (request, key, null);
        }
        catch (JsonException)
        {
            return (null, key, LabelTransformationProblem(StatusCodes.Status400BadRequest,
                InventoryStockScrapCodesV1.ValidationFailed, correlation));
        }
    }

    private static IResult StockScrapResult(HttpContext httpContext, InventoryStockScrapResultV1 result)
    {
        if (result.IsReplay)
        {
            ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
        }

        return Results.Json(result.Scrap, StockCountJson,
            statusCode: result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }

    /// <summary>The owner's refusal as a problem: 401/403 code-less, a closed-set code with its correlation otherwise.</summary>
    private static IResult StockScrapProblem(
        InventoryStockScrapException exception,
        ICorrelationContext correlation,
        IReadOnlySet<string> status404Codes,
        IReadOnlySet<string> status409Codes)
    {
        var accepted = exception.Status switch
        {
            StatusCodes.Status400BadRequest => InventoryStockScrapHttpCodesV1.Status400Codes.Contains(exception.Code),
            StatusCodes.Status401Unauthorized => exception.Code == InventoryStockScrapCodesV1.Unauthenticated,
            StatusCodes.Status403Forbidden => exception.Code == InventoryStockScrapCodesV1.Forbidden,
            StatusCodes.Status404NotFound => status404Codes.Contains(exception.Code),
            StatusCodes.Status409Conflict => status409Codes.Contains(exception.Code),
            StatusCodes.Status503ServiceUnavailable => InventoryStockScrapHttpCodesV1.Status503Codes.Contains(exception.Code),
            _ => false,
        };
        if (!accepted)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return exception.Status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
            ? Results.Problem(statusCode: exception.Status)
            : LabelTransformationProblem(exception.Status, exception.Code, correlation);
    }
}

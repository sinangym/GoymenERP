using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class InventoryEndpoints
{
    private const string OpeningCostDecisionRoute = "/api/v1/inventory/opening-costs/{openingCostId:guid}/decision";

    /// <summary>
    /// SAT-D304 / SAT-D372 opening-stock cost: submit the manually entered unit cost of one location's uncosted stock of
    /// one product, and approve or reject it by another person. Mapped by <see cref="MapInventoryEndpoints"/>; the route
    /// policies name <see cref="ErpPermissions.InventoryOpeningCostSubmit"/> and
    /// <see cref="ErpPermissions.InventoryOpeningCostApprove"/> (registered by the central catalogue; nobody holds them
    /// until a human grants them) and the host wires <c>AddInventoryOpeningCosts()</c>. The stock-scrap shape: one
    /// canonical <c>Idempotency-Key</c>; 201 with the submission view, 200 with the same view plus
    /// <c>Idempotency-Replayed</c> and <c>Idempotency-Original-Correlation-Id</c> for the same key and content; 401/403
    /// are platform problems; 400/404/409/503 carry the owner's closed code
    /// (<see cref="InventoryOpeningCostHttpCodesV1"/>) and the correlation id; any other owner answer is a code-less 503.
    /// </summary>
    public static IEndpointRouteBuilder MapInventoryOpeningCostEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost(InventoryOpeningCostApiV1.Path, async (
                HttpContext httpContext,
                IInventoryOpeningCostCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadStockScrapBodyAsync<SubmitInventoryOpeningCostV1>(httpContext, correlation,
                    cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.SubmitAsync(read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key, ErpPermissions.InventoryOpeningCostSubmit),
                        cancellationToken);
                    return OpeningCostResult(httpContext, result);
                }
                catch (InventoryOpeningCostException exception)
                {
                    return OpeningCostProblem(exception, correlation, EmptyOpeningCostCodes,
                        InventoryOpeningCostHttpCodesV1.SubmitStatus409Codes);
                }
            })
            .WithName(InventoryOpeningCostApiV1.SubmitOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryOpeningCostSubmit)
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<SubmitInventoryOpeningCostV1>("application/json")
            .Produces<InventoryOpeningCostViewV1>(StatusCodes.Status201Created)
            .Produces<InventoryOpeningCostViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryOpeningCostHttpCodesV1.Status400Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict([.. InventoryOpeningCostHttpCodesV1.SubmitStatus409Codes.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable([.. InventoryOpeningCostHttpCodesV1.Status503Codes.Order(StringComparer.Ordinal)])
            .ProducesErpAuthenticatedProblems();

        endpoints.MapPost(OpeningCostDecisionRoute, async (
                Guid openingCostId,
                HttpContext httpContext,
                IInventoryOpeningCostCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadStockScrapBodyAsync<DecideInventoryOpeningCostV1>(httpContext, correlation,
                    cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.DecideAsync(openingCostId, read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key, ErpPermissions.InventoryOpeningCostApprove),
                        cancellationToken);
                    return OpeningCostResult(httpContext, result);
                }
                catch (InventoryOpeningCostException exception)
                {
                    return OpeningCostProblem(exception, correlation, InventoryOpeningCostHttpCodesV1.DecideStatus404Codes,
                        InventoryOpeningCostHttpCodesV1.DecideStatus409Codes);
                }
            })
            .WithName(InventoryOpeningCostApiV1.DecideOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryOpeningCostApprove)
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<DecideInventoryOpeningCostV1>("application/json")
            .Produces<InventoryOpeningCostViewV1>(StatusCodes.Status201Created)
            .Produces<InventoryOpeningCostViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryOpeningCostHttpCodesV1.Status400Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound([.. InventoryOpeningCostHttpCodesV1.DecideStatus404Codes.Order(StringComparer.Ordinal)])
            .ProducesErpConflict([.. InventoryOpeningCostHttpCodesV1.DecideStatus409Codes.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable([.. InventoryOpeningCostHttpCodesV1.Status503Codes.Order(StringComparer.Ordinal)])
            .ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static readonly IReadOnlySet<string> EmptyOpeningCostCodes = new HashSet<string>(StringComparer.Ordinal);

    private static IResult OpeningCostResult(HttpContext httpContext, InventoryOpeningCostResultV1 result)
    {
        if (result.IsReplay)
        {
            ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
        }

        return Results.Json(result.OpeningCost, StockCountJson,
            statusCode: result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }

    /// <summary>The owner's refusal as a problem: 401/403 code-less, a closed-set code with its correlation otherwise.</summary>
    private static IResult OpeningCostProblem(
        InventoryOpeningCostException exception,
        ICorrelationContext correlation,
        IReadOnlySet<string> status404Codes,
        IReadOnlySet<string> status409Codes)
    {
        var accepted = exception.Status switch
        {
            StatusCodes.Status400BadRequest => InventoryOpeningCostHttpCodesV1.Status400Codes.Contains(exception.Code),
            StatusCodes.Status401Unauthorized => exception.Code == InventoryOpeningCostCodesV1.Unauthenticated,
            StatusCodes.Status403Forbidden => exception.Code == InventoryOpeningCostCodesV1.Forbidden,
            StatusCodes.Status404NotFound => status404Codes.Contains(exception.Code),
            StatusCodes.Status409Conflict => status409Codes.Contains(exception.Code),
            StatusCodes.Status503ServiceUnavailable => InventoryOpeningCostHttpCodesV1.Status503Codes.Contains(exception.Code),
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

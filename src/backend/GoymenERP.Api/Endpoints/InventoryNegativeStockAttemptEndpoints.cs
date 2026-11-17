using System.Globalization;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.Endpoints;

public static class InventoryNegativeStockAttemptEndpoints
{
    public static IEndpointRouteBuilder MapInventoryNegativeStockAttemptEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(InventoryNegativeStockAttemptApiV1.Path, async (HttpContext context,
            IInventoryNegativeStockAttemptsV1 attempts, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";
            if (!TryQuery(context.Request.Query, out var query))
                return Problem(400, InventoryNegativeStockAttemptCodesV1.InvalidQuery);
            try { return Results.Ok(await attempts.SearchAsync(query, cancellationToken)); }
            catch (InventoryNegativeStockAttemptException exception)
            {
                return Problem(exception.StatusCode is 400 or 401 or 403 or 503 ? exception.StatusCode : 503,
                    exception.StatusCode is 400 or 503 ? exception.Code : null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return Problem(503, InventoryNegativeStockAttemptCodesV1.Unavailable); }
        })
        .WithName(InventoryNegativeStockAttemptApiV1.GetOperationId).WithTags("Inventory")
        .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata()).RequireCompanyContext()
        .RequireAuthorization(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead)
        .Produces<InventoryNegativeStockAttemptPageV1>(200)
        .ProducesErpBadRequest(InventoryNegativeStockAttemptCodesV1.InvalidQuery)
        .ProducesErpServiceUnavailable(InventoryNegativeStockAttemptCodesV1.Unavailable)
        .ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    internal static bool TryQuery(IQueryCollection values, out InventoryNegativeStockAttemptQueryV1 query)
    {
        query = new();
        if (values.Keys.Any(key => key is not ("productId" or "locationId" or "openOnly" or "limit"))
            || values.Any(pair => pair.Value.Count != 1)) return false;
        Guid? product = null, location = null;
        if (values.TryGetValue("productId", out var productText))
        {
            if (!Guid.TryParseExact(productText[0], "D", out var id) || id == Guid.Empty) return false;
            product = id;
        }
        if (values.TryGetValue("locationId", out var locationText))
        {
            if (!Guid.TryParseExact(locationText[0], "D", out var id) || id == Guid.Empty) return false;
            location = id;
        }
        var open = false;
        if (values.TryGetValue("openOnly", out var openText))
        {
            if (openText[0] is not ("true" or "false")) return false;
            open = openText[0] == "true";
        }
        var limit = 50;
        if (values.TryGetValue("limit", out var limitText)
            && (!int.TryParse(limitText[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                || limit is < 1 or > 100 || limitText[0] != limit.ToString(CultureInfo.InvariantCulture))) return false;
        query = new(product, location, open, limit);
        return true;
    }
    private static IResult Problem(int status, string? code) => Results.Problem(statusCode: status,
        extensions: code is null ? null : new Dictionary<string, object?> { ["code"] = code });
}

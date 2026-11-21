using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class InventoryEndpoints
{
    /// <summary>
    /// KLN-D42 (STK-S31): the dated shortage warning read (it replaces minimum stock). The answer comes from the one backend
    /// calculator behind <see cref="IInventoryShortageWarningsV1"/>; the host only parses the query and maps the owner's closed
    /// codes. Optional query: <c>productId</c> (a non-empty id) and <c>onlyWarnings</c> (<c>true</c>/<c>false</c>, default
    /// true); an unknown or repeated key is 400. Reading creates nothing — no purchase request, no allocation.
    /// </summary>
    private static void MapShortageWarningEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(InventoryShortageWarningApiV1.Path, async (
                HttpContext httpContext,
                IInventoryShortageWarningsV1 warnings,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                if (!TryReadShortageWarningQuery(httpContext.Request.Query, out var query))
                {
                    return ShortageWarningProblem(
                        StatusCodes.Status400BadRequest,
                        InventoryShortageWarningCodesV1.InvalidQuery);
                }

                try
                {
                    return Results.Ok(await warnings.ReadAsync(query, cancellationToken));
                }
                catch (InventoryShortageWarningException exception)
                {
                    return exception.StatusCode switch
                    {
                        StatusCodes.Status400BadRequest
                            when exception.Code == InventoryShortageWarningCodesV1.InvalidQuery =>
                            ShortageWarningProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status503ServiceUnavailable
                            when exception.Code == InventoryShortageWarningCodesV1.Unavailable =>
                            ShortageWarningProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden =>
                            ShortageWarningProblem(exception.StatusCode),
                        _ => ShortageWarningProblem(StatusCodes.Status503ServiceUnavailable),
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return ShortageWarningProblem(StatusCodes.Status503ServiceUnavailable);
                }
            })
            .WithName(InventoryShortageWarningApiV1.GetOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead)
            .Produces<InventoryShortageWarningsV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(InventoryShortageWarningCodesV1.InvalidQuery)
            .ProducesErpServiceUnavailable(InventoryShortageWarningCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();
    }

    internal static bool TryReadShortageWarningQuery(IQueryCollection query, out InventoryShortageWarningQueryV1 parsed)
    {
        parsed = new();
        if (query.Keys.Any(name => name is not "productId" and not "onlyWarnings")
            || query.Any(parameter => parameter.Value.Count != 1))
        {
            return false;
        }

        Guid? productId = null;
        if (query.TryGetValue("productId", out var productValues))
        {
            if (!Guid.TryParseExact(productValues[0], "D", out var product) || product == Guid.Empty)
            {
                return false;
            }

            productId = product;
        }

        var onlyWarnings = true;
        if (query.TryGetValue("onlyWarnings", out var onlyValues))
        {
            switch (onlyValues[0])
            {
                case "true":
                    onlyWarnings = true;
                    break;
                case "false":
                    onlyWarnings = false;
                    break;
                default:
                    return false;
            }
        }

        parsed = new InventoryShortageWarningQueryV1(productId, onlyWarnings);
        return true;
    }

    private static IResult ShortageWarningProblem(int statusCode, string? code = null) => code is null
        ? Results.Problem(statusCode: statusCode)
        : Results.Problem(statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

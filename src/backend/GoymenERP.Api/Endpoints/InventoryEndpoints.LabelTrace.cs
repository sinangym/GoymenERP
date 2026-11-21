using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class InventoryEndpoints
{
    /// <summary>
    /// STK-S33 (KLN-D44, KLN-D41): the cross-module label trace and its label search. Both ride the label-detail permissions
    /// (labels read + balances read); no new permission exists. The trace takes exactly one <c>direction</c>
    /// (<c>forward</c>/<c>backward</c>), the search exactly one <c>text</c>; an unknown or repeated key is 400. A missing or
    /// another company's label is a code-less 404. Reading creates nothing.
    /// </summary>
    private static void MapLabelTraceEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(InventoryLabelTraceApiV1.Path, async (
                Guid labelId,
                HttpContext httpContext,
                IInventoryLabelTraceV1 trace,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                if (!TryReadLabelTraceDirection(httpContext.Request.Query, out var direction))
                {
                    return LabelTraceProblem(StatusCodes.Status400BadRequest, InventoryLabelTraceCodesV1.InvalidQuery);
                }

                return await RunLabelTraceAsync(() => trace.ReadAsync(labelId, direction, cancellationToken), cancellationToken);
            })
            .WithName(InventoryLabelTraceApiV1.GetOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead)
            .Produces<InventoryLabelTraceV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(InventoryLabelTraceCodesV1.InvalidQuery)
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(InventoryLabelTraceCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(InventoryLabelTraceApiV1.SearchPath, async (
                HttpContext httpContext,
                IInventoryLabelTraceV1 trace,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                if (!TryReadLabelTraceSearchText(httpContext.Request.Query, out var text))
                {
                    return LabelTraceProblem(StatusCodes.Status400BadRequest, InventoryLabelTraceCodesV1.InvalidQuery);
                }

                return await RunLabelTraceAsync(() => trace.SearchAsync(text, cancellationToken), cancellationToken);
            })
            .WithName(InventoryLabelTraceApiV1.SearchOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead)
            .Produces<InventoryLabelTraceSearchV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(InventoryLabelTraceCodesV1.InvalidQuery)
            .ProducesErpServiceUnavailable(InventoryLabelTraceCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();
    }

    private static async Task<IResult> RunLabelTraceAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await read());
        }
        catch (InventoryLabelTraceException exception)
        {
            return exception.StatusCode switch
            {
                StatusCodes.Status400BadRequest when exception.Code == InventoryLabelTraceCodesV1.InvalidQuery =>
                    LabelTraceProblem(exception.StatusCode, exception.Code),
                StatusCodes.Status503ServiceUnavailable when exception.Code == InventoryLabelTraceCodesV1.Unavailable =>
                    LabelTraceProblem(exception.StatusCode, exception.Code),
                StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound =>
                    LabelTraceProblem(exception.StatusCode),
                _ => LabelTraceProblem(StatusCodes.Status503ServiceUnavailable),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return LabelTraceProblem(StatusCodes.Status503ServiceUnavailable);
        }
    }

    internal static bool TryReadLabelTraceDirection(IQueryCollection query, out InventoryLabelTraceDirectionV1 direction)
    {
        direction = default;
        if (query.Count != 1 || !query.TryGetValue(InventoryLabelTraceApiV1.DirectionQueryKey, out var values)
            || values.Count != 1)
        {
            return false;
        }

        switch (values[0])
        {
            case InventoryLabelTraceApiV1.Forward:
                direction = InventoryLabelTraceDirectionV1.Forward;
                return true;
            case InventoryLabelTraceApiV1.Backward:
                direction = InventoryLabelTraceDirectionV1.Backward;
                return true;
            default:
                return false;
        }
    }

    internal static bool TryReadLabelTraceSearchText(IQueryCollection query, out string text)
    {
        text = string.Empty;
        if (query.Count != 1 || !query.TryGetValue(InventoryLabelTraceApiV1.TextQueryKey, out var values)
            || values.Count != 1 || values[0] is not { } value)
        {
            return false;
        }

        text = value;
        return true;
    }

    private static IResult LabelTraceProblem(int statusCode, string? code = null) => code is null
        ? Results.Problem(statusCode: statusCode)
        : Results.Problem(statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

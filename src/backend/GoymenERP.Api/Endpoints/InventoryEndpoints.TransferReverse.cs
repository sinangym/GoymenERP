using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class InventoryEndpoints
{
    private const string TransferReverseRoute = InventoryTransferApiV1.Path + "/{transferId:guid}/reverse";
    private const string TransferReversalFactsRoute = InventoryTransferApiV1.Path + "/{transferId:guid}/reversal-facts";

    /// <summary>
    /// KLN-D22 reverse transfer. An accepted transfer is not cancelled; <c>reverse</c> plans a NEW transfer from the
    /// original destination back to the original source, linked to the original transfer and to the accepted original
    /// lines, with a mandatory reason. It is a plan under <see cref="ErpPermissions.InventoryStockTransferWrite"/>; the
    /// reverse transfer then moves stock only through the ordinary dispatch and receipt commands. The boundary is the
    /// transfer boundary: one canonical <c>Idempotency-Key</c>, strict JSON, 201 with the new transfer and its link, 200
    /// with the same body and the replay headers. <c>reversal-facts</c> is read under
    /// <see cref="ErpPermissions.InventoryBalancesRead"/>. Registration grants nothing.
    /// </summary>
    private static void MapInventoryTransferReverseEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(TransferReverseRoute, async (
                Guid transferId,
                HttpContext httpContext,
                IInventoryTransferReverseCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadTransferBodyAsync<ReverseInventoryTransferV1>(httpContext, correlation,
                    cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.ReverseAsync(transferId, read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key, ErpPermissions.InventoryStockTransferWrite),
                        cancellationToken);
                    if (result.IsReplay)
                    {
                        ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                    }

                    return Results.Json(new InventoryTransferReverseResponseV1(result.Transfer, result.Reversal),
                        TransferJson,
                        statusCode: result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
                }
                catch (InventoryTransferException exception)
                {
                    return TransferProblem(httpContext, exception, correlation, InventoryTransferReverseHttpCodes.Reverse);
                }
            })
            .RequireAuthorization(ErpPermissions.InventoryStockTransferWrite)
            .RequireCompanyContext()
            .WithName("ReverseInventoryTransferV1")
            .WithTags("Inventory")
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<ReverseInventoryTransferV1>("application/json")
            .Produces<InventoryTransferReverseResponseV1>(StatusCodes.Status201Created)
            .Produces<InventoryTransferReverseResponseV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryTransferReverseHttpCodes.Reverse.Status400.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict([.. InventoryTransferReverseHttpCodes.Reverse.Status409.Order(StringComparer.Ordinal)])
            .ProducesErpUnprocessableEntity(
                [.. InventoryTransferReverseHttpCodes.Reverse.Status422.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(TransferReversalFactsRoute, async (
                Guid transferId,
                HttpContext httpContext,
                IInventoryTransferReverseQueryV1 reversals,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                try
                {
                    return Results.Json(await reversals.GetReversalFactsAsync(transferId, cancellationToken),
                        TransferJson);
                }
                catch (InventoryTransferException exception)
                {
                    return exception.Status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
                        or StatusCodes.Status404NotFound
                        ? Results.Problem(statusCode: exception.Status)
                        : Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .RequireAuthorization(ErpPermissions.InventoryBalancesRead)
            .RequireCompanyContext()
            .WithName("GetInventoryTransferReversalFactsV1")
            .WithTags("Inventory")
            .Produces<InventoryTransferReversalFactsViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>The closed problem codes the KLN-D22 reverse command can answer with a code.</summary>
    private static class InventoryTransferReverseHttpCodes
    {
        internal static readonly InventoryTransferHttpCodes.Operation Reverse = new(
            new HashSet<string>(StringComparer.Ordinal) { InventoryTransferCodesV1.ValidationFailed },
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.IdempotencyConflict,
                InventoryTransferCodesV1.ConcurrentChange,
                InventoryTransferCodesV1.SourcePositionConflict,
                InventoryTransferReverseCodesV1.OriginalNotAccepted,
                InventoryTransferReverseCodesV1.LineNotAccepted,
                InventoryTransferReverseCodesV1.LineAlreadyReversed,
                InventoryTransferReverseCodesV1.OveragePending,
            },
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.DestinationNotFound,
                InventoryTransferCodesV1.DestinationInactive,
                InventoryTransferCodesV1.DestinationContextMismatch,
                InventoryTransferReverseCodesV1.ReasonRequired,
            });
    }
}

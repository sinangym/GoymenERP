using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class InventoryEndpoints
{
    // The owner's templates with the route constraint the host always applies to identities.
    private const string TransferDispatchRoute = InventoryTransferApiV1.Path + "/{transferId:guid}/dispatch";
    private const string TransferCancelDispatchRoute = InventoryTransferApiV1.Path + "/{transferId:guid}/cancel-dispatch";
    private const string TransferByIdRoute = InventoryTransferApiV1.Path + "/{transferId:guid}";
    private const string TransferReceiveRoute = InventoryTransferApiV1.Path + "/{transferId:guid}/receive-lines/{lineId:guid}";
    private const string TransferOverageDecisionRoute = TransferReceiveRoute + "/overage-decision";
    private const string TransferLossRequestRoute =
        InventoryTransferApiV1.Path + "/{transferId:guid}/lines/{lineId:guid}/loss-request";
    private const string TransferLossDecisionRoute =
        InventoryTransferApiV1.Path + "/{transferId:guid}/lines/{lineId:guid}/loss-decision";
    private const string TransferReceiptFactsRoute = InventoryTransferApiV1.Path + "/{transferId:guid}/receipt-facts";

    /// <summary>
    /// B-22a stock transfer: plan a same-company label transfer, dispatch it against the Logistics outgoing document,
    /// and read it back. Mapped by <see cref="MapInventoryEndpoints"/>; the host wires <c>AddInventoryTransfers()</c>
    /// with the Organization destination adapter and the Logistics proof adapter. The plain whole-label acceptance is not
    /// published here; the B-22c receipt (<see cref="MapInventoryTransferReceiptEndpoints"/>) is. The commands are
    /// fenced by the service on the centrally registered
    /// <see cref="ErpPermissions.InventoryStockTransferWrite"/> and the read on
    /// <see cref="ErpPermissions.InventoryBalancesRead"/>; registration grants nothing. The opening-count boundary:
    /// one canonical <c>Idempotency-Key</c>, strict JSON, 201 with the transfer view, 200 with the same view plus
    /// <c>Idempotency-Replayed</c> and <c>Idempotency-Original-Correlation-Id</c> for the same key and content; a
    /// recorded terminal dispatch refusal (409, outgoing document not final/cancelled/reversed/stale) replays with the
    /// same headers. 401/403/404/503 are code-less platform problems; 400/409/422 carry the owner's closed code and the
    /// correlation id; any code outside the operation's closed set is a code-less 503. B-22b dispatch cancellation
    /// (KLN-D20/D25/D27) is fenced by the service on <see cref="ErpPermissions.InventoryStockTransferCancel"/>; a closed
    /// window is a recorded terminal 409 that replays with the same headers, while an unreadable window or a missing
    /// owner confirmation is a 409 that is not recorded.
    /// </summary>
    private static void MapInventoryTransferEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(InventoryTransferApiV1.Path, async (
                HttpContext httpContext,
                IInventoryTransferCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadTransferBodyAsync<PlanInventoryTransferV1>(httpContext, correlation, cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.PlanAsync(read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key, ErpPermissions.InventoryStockTransferWrite),
                        cancellationToken);
                    return TransferResult(httpContext, result);
                }
                catch (InventoryTransferException exception)
                {
                    return TransferProblem(httpContext, exception, correlation, InventoryTransferHttpCodes.Plan);
                }
            })
            .RequireAuthorization(ErpPermissions.InventoryStockTransferWrite)
            .RequireCompanyContext()
            .WithName("PlanInventoryTransferV1")
            .WithTags("Inventory")
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<PlanInventoryTransferV1>("application/json")
            .Produces<InventoryTransferViewV1>(StatusCodes.Status201Created)
            .Produces<InventoryTransferViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryTransferHttpCodes.Plan.Status400.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict([.. InventoryTransferHttpCodes.Plan.Status409.Order(StringComparer.Ordinal)])
            .ProducesErpUnprocessableEntity([.. InventoryTransferHttpCodes.Plan.Status422.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapPost(TransferDispatchRoute, async (
                Guid transferId,
                HttpContext httpContext,
                IInventoryTransferCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadTransferBodyAsync<DispatchInventoryTransferV1>(httpContext, correlation, cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.DispatchAsync(transferId, read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key, ErpPermissions.InventoryStockTransferWrite),
                        cancellationToken);
                    return TransferResult(httpContext, result);
                }
                catch (InventoryTransferException exception)
                {
                    return TransferProblem(httpContext, exception, correlation, InventoryTransferHttpCodes.Dispatch);
                }
            })
            .RequireAuthorization(ErpPermissions.InventoryStockTransferWrite)
            .RequireCompanyContext()
            .WithName("DispatchInventoryTransferV1")
            .WithTags("Inventory")
            // The replayable results: the dispatched transfer (200 on a replay) and the recorded terminal refusals (409).
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict)
            .Accepts<DispatchInventoryTransferV1>("application/json")
            .Produces<InventoryTransferViewV1>(StatusCodes.Status201Created)
            .Produces<InventoryTransferViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryTransferHttpCodes.Dispatch.Status400.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict([.. InventoryTransferHttpCodes.Dispatch.Status409.Order(StringComparer.Ordinal)])
            .ProducesErpUnprocessableEntity([.. InventoryTransferHttpCodes.Dispatch.Status422.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapPost(TransferCancelDispatchRoute, async (
                Guid transferId,
                HttpContext httpContext,
                IInventoryTransferCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadTransferBodyAsync<CancelInventoryTransferDispatchV1>(
                    httpContext, correlation, cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.CancelDispatchAsync(transferId, read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key, ErpPermissions.InventoryStockTransferCancel),
                        cancellationToken);
                    return TransferResult(httpContext, result);
                }
                catch (InventoryTransferException exception)
                {
                    return TransferProblem(httpContext, exception, correlation, InventoryTransferHttpCodes.CancelDispatch);
                }
            })
            .RequireAuthorization(ErpPermissions.InventoryStockTransferCancel)
            .RequireCompanyContext()
            .WithName("CancelInventoryTransferDispatchV1")
            .WithTags("Inventory")
            // The replayable results: the cancelled transfer (200 on a replay) and the recorded closed window (409).
            .WithIdempotencyContract(StatusCodes.Status200OK, StatusCodes.Status409Conflict)
            .Accepts<CancelInventoryTransferDispatchV1>("application/json")
            .Produces<InventoryTransferViewV1>(StatusCodes.Status201Created)
            .Produces<InventoryTransferViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryTransferHttpCodes.CancelDispatch.Status400.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict([.. InventoryTransferHttpCodes.CancelDispatch.Status409.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(TransferByIdRoute, async (
                Guid transferId,
                HttpContext httpContext,
                IInventoryTransferQueryV1 transfers,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                try
                {
                    return Results.Json(await transfers.GetAsync(transferId, cancellationToken), TransferJson);
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
            .WithName("GetInventoryTransferV1")
            .WithTags("Inventory")
            .Produces<InventoryTransferViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        MapInventoryTransferReceiptEndpoints(endpoints);
    }

    /// <summary>
    /// B-22c receipt (KLN-D21/KLN-D23/KLN-D24). <c>receive-lines</c> records the receiver's weighed kilograms under
    /// <see cref="ErpPermissions.InventoryStockTransferWrite"/>: the whole dispatched label is accepted at the
    /// destination and a shortage is the separate automatic fire movement; an excess needs its reason and stays out of
    /// stock until <c>overage-decision</c> (<see cref="ErpPermissions.InventoryStockTransferOverageApprove"/>) by a person
    /// other than the receiver approves it. A pending line is closed as a transit loss only through <c>loss-request</c>
    /// (<see cref="ErpPermissions.InventoryStockTransferLossRecord"/>) and a <c>loss-decision</c>
    /// (<see cref="ErpPermissions.InventoryStockTransferLossApprove"/>) by a person other than the requester. Every
    /// command keeps the transfer boundary: one canonical <c>Idempotency-Key</c>, strict JSON, 201 with the transfer and
    /// receipt facts, 200 with the same body and the replay headers. The facts are read under
    /// <see cref="ErpPermissions.InventoryBalancesRead"/>. Registration grants nothing.
    /// </summary>
    private static void MapInventoryTransferReceiptEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(TransferReceiveRoute, async (
                Guid transferId,
                Guid lineId,
                HttpContext httpContext,
                IInventoryTransferReceiptCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadTransferBodyAsync<ReceiveInventoryTransferLineV1>(
                    httpContext, correlation, cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.ReceiveLineAsync(transferId, lineId, read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key, ErpPermissions.InventoryStockTransferWrite),
                        cancellationToken);
                    return TransferReceiptResult(httpContext, result);
                }
                catch (InventoryTransferException exception)
                {
                    return TransferProblem(httpContext, exception, correlation, InventoryTransferHttpCodes.ReceiveLine);
                }
            })
            .RequireAuthorization(ErpPermissions.InventoryStockTransferWrite)
            .RequireCompanyContext()
            .WithName("ReceiveInventoryTransferLineV1")
            .WithTags("Inventory")
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<ReceiveInventoryTransferLineV1>("application/json")
            .Produces<InventoryTransferReceiptResponseV1>(StatusCodes.Status201Created)
            .Produces<InventoryTransferReceiptResponseV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryTransferHttpCodes.ReceiveLine.Status400.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict([.. InventoryTransferHttpCodes.ReceiveLine.Status409.Order(StringComparer.Ordinal)])
            .ProducesErpUnprocessableEntity(
                [.. InventoryTransferHttpCodes.ReceiveLine.Status422.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapPost(TransferOverageDecisionRoute, async (
                Guid transferId,
                Guid lineId,
                HttpContext httpContext,
                IInventoryTransferReceiptCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadTransferBodyAsync<DecideInventoryTransferOverageV1>(
                    httpContext, correlation, cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.DecideOverageAsync(transferId, lineId, read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key,
                            ErpPermissions.InventoryStockTransferOverageApprove),
                        cancellationToken);
                    return TransferReceiptResult(httpContext, result);
                }
                catch (InventoryTransferException exception)
                {
                    return TransferProblem(httpContext, exception, correlation, InventoryTransferHttpCodes.DecideOverage);
                }
            })
            .RequireAuthorization(ErpPermissions.InventoryStockTransferOverageApprove)
            .RequireCompanyContext()
            .WithName("DecideInventoryTransferOverageV1")
            .WithTags("Inventory")
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<DecideInventoryTransferOverageV1>("application/json")
            .Produces<InventoryTransferReceiptResponseV1>(StatusCodes.Status201Created)
            .Produces<InventoryTransferReceiptResponseV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryTransferHttpCodes.DecideOverage.Status400.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict([.. InventoryTransferHttpCodes.DecideOverage.Status409.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapPost(TransferLossRequestRoute, async (
                Guid transferId,
                Guid lineId,
                HttpContext httpContext,
                IInventoryTransferReceiptCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadTransferBodyAsync<RequestInventoryTransferLossV1>(
                    httpContext, correlation, cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.RequestLossAsync(transferId, lineId, read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key,
                            ErpPermissions.InventoryStockTransferLossRecord),
                        cancellationToken);
                    return TransferReceiptResult(httpContext, result);
                }
                catch (InventoryTransferException exception)
                {
                    return TransferProblem(httpContext, exception, correlation, InventoryTransferHttpCodes.RequestLoss);
                }
            })
            .RequireAuthorization(ErpPermissions.InventoryStockTransferLossRecord)
            .RequireCompanyContext()
            .WithName("RequestInventoryTransferLossV1")
            .WithTags("Inventory")
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<RequestInventoryTransferLossV1>("application/json")
            .Produces<InventoryTransferReceiptResponseV1>(StatusCodes.Status201Created)
            .Produces<InventoryTransferReceiptResponseV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryTransferHttpCodes.RequestLoss.Status400.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict([.. InventoryTransferHttpCodes.RequestLoss.Status409.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapPost(TransferLossDecisionRoute, async (
                Guid transferId,
                Guid lineId,
                HttpContext httpContext,
                IInventoryTransferReceiptCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadTransferBodyAsync<DecideInventoryTransferLossV1>(
                    httpContext, correlation, cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.DecideLossAsync(transferId, lineId, read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key,
                            ErpPermissions.InventoryStockTransferLossApprove),
                        cancellationToken);
                    return TransferReceiptResult(httpContext, result);
                }
                catch (InventoryTransferException exception)
                {
                    return TransferProblem(httpContext, exception, correlation, InventoryTransferHttpCodes.DecideLoss);
                }
            })
            .RequireAuthorization(ErpPermissions.InventoryStockTransferLossApprove)
            .RequireCompanyContext()
            .WithName("DecideInventoryTransferLossV1")
            .WithTags("Inventory")
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<DecideInventoryTransferLossV1>("application/json")
            .Produces<InventoryTransferReceiptResponseV1>(StatusCodes.Status201Created)
            .Produces<InventoryTransferReceiptResponseV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryTransferHttpCodes.DecideLoss.Status400.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict([.. InventoryTransferHttpCodes.DecideLoss.Status409.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(TransferReceiptFactsRoute, async (
                Guid transferId,
                HttpContext httpContext,
                IInventoryTransferReceiptQueryV1 receipts,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                try
                {
                    return Results.Json(await receipts.GetReceiptFactsAsync(transferId, cancellationToken), TransferJson);
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
            .WithName("GetInventoryTransferReceiptFactsV1")
            .WithTags("Inventory")
            .Produces<InventoryTransferReceiptFactsViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static async Task<(T? Request, string Key, IResult? Problem)> ReadTransferBodyAsync<T>(
        HttpContext httpContext,
        ICorrelationContext correlation,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key) || !correlation.IsResolved)
        {
            return (null, string.Empty, OpeningProblem(StatusCodes.Status400BadRequest,
                InventoryTransferCodesV1.ValidationFailed, correlation));
        }

        if (!httpContext.Request.HasJsonContentType())
        {
            return (null, key, Results.StatusCode(StatusCodes.Status415UnsupportedMediaType));
        }

        try
        {
            var request = await JsonSerializer.DeserializeAsync<T>(httpContext.Request.Body, TransferJson, cancellationToken);
            return request is null
                ? (null, key, OpeningProblem(StatusCodes.Status400BadRequest, InventoryTransferCodesV1.ValidationFailed,
                    correlation))
                : (request, key, null);
        }
        catch (JsonException)
        {
            return (null, key, OpeningProblem(StatusCodes.Status400BadRequest, InventoryTransferCodesV1.ValidationFailed,
                correlation));
        }
    }

    private static IResult TransferResult(HttpContext httpContext, InventoryTransferResultV1 result)
    {
        if (result.IsReplay)
        {
            ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
        }

        return Results.Json(result.Transfer, TransferJson,
            statusCode: result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }

    private static IResult TransferReceiptResult(HttpContext httpContext, InventoryTransferReceiptResultV1 result)
    {
        if (result.IsReplay)
        {
            ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
        }

        return Results.Json(new InventoryTransferReceiptResponseV1(result.Transfer, result.Facts), TransferJson,
            statusCode: result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }

    /// <summary>
    /// The owner's refusal as a problem: 401/403/404/503 code-less; 400/409/422 only with a code of the operation's
    /// closed set (and the replay headers for a recorded refusal); anything else is a code-less 503.
    /// </summary>
    private static IResult TransferProblem(
        HttpContext httpContext,
        InventoryTransferException exception,
        ICorrelationContext correlation,
        InventoryTransferHttpCodes.Operation codes)
    {
        switch (exception.Status)
        {
            case StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound:
                return Results.Problem(statusCode: exception.Status);
            case StatusCodes.Status400BadRequest when codes.Status400.Contains(exception.Code):
            case StatusCodes.Status409Conflict when codes.Status409.Contains(exception.Code):
            case StatusCodes.Status422UnprocessableEntity when codes.Status422.Contains(exception.Code):
                if (exception.IsReplay)
                {
                    ApplyReplayHeaders(httpContext, exception.OriginalCorrelationId
                        ?? throw new InvalidOperationException("Replayed refusal without its original correlation."));
                }

                return OpeningProblem(exception.Status, exception.Code, correlation);
            default:
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static readonly JsonSerializerOptions TransferJson = new(JsonSerializerDefaults.Web);

    /// <summary>The closed problem codes each published transfer operation can answer with a code.</summary>
    private static class InventoryTransferHttpCodes
    {
        internal sealed record Operation(
            IReadOnlySet<string> Status400,
            IReadOnlySet<string> Status409,
            IReadOnlySet<string> Status422);

        internal static readonly Operation Plan = new(
            new HashSet<string>(StringComparer.Ordinal) { InventoryTransferCodesV1.ValidationFailed },
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.IdempotencyConflict,
                InventoryTransferCodesV1.ConcurrentChange,
                InventoryTransferCodesV1.LabelVersionConflict,
                InventoryTransferCodesV1.SourcePositionConflict,
                InventoryTransferCodesV1.ActiveAllocationConflict,
            },
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.DestinationNotFound,
                InventoryTransferCodesV1.DestinationInactive,
                InventoryTransferCodesV1.DestinationContextMismatch,
            });

        internal static readonly Operation Dispatch = new(
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.ValidationFailed,
                InventoryScaleCodes.ScaleExceeded,
            },
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.IdempotencyConflict,
                InventoryTransferCodesV1.ConcurrentChange,
                InventoryTransferCodesV1.TransferVersionConflict,
                InventoryTransferCodesV1.TransferStateConflict,
                InventoryTransferCodesV1.LabelVersionConflict,
                InventoryTransferCodesV1.SourcePositionConflict,
                InventoryTransferCodesV1.ActiveAllocationConflict,
                InventoryTransferCodesV1.OutgoingDocumentNotFinal,
                InventoryTransferCodesV1.OutgoingDocumentCancelled,
                InventoryTransferCodesV1.OutgoingDocumentReversed,
                InventoryTransferCodesV1.OutgoingDocumentVersionConflict,
            },
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.OutgoingDocumentNotFound,
                InventoryTransferCodesV1.OutgoingDocumentBindingMismatch,
            });

        internal static readonly Operation CancelDispatch = new(
            new HashSet<string>(StringComparer.Ordinal) { InventoryTransferCodesV1.ValidationFailed },
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.IdempotencyConflict,
                InventoryTransferCodesV1.ConcurrentChange,
                InventoryTransferCodesV1.TransferVersionConflict,
                InventoryTransferCodesV1.TransferStateConflict,
                InventoryTransferCodesV1.LabelVersionConflict,
                InventoryTransferCodesV1.CancelWindowClosed,
                InventoryTransferCodesV1.CancelWindowUnreadable,
                InventoryTransferCodesV1.CancelConfirmationMissing,
            },
            new HashSet<string>(StringComparer.Ordinal));

        internal static readonly Operation ReceiveLine = new(
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.ValidationFailed,
                InventoryScaleCodes.ScaleExceeded,
            },
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.IdempotencyConflict,
                InventoryTransferCodesV1.ConcurrentChange,
                InventoryTransferCodesV1.TransferVersionConflict,
                InventoryTransferCodesV1.TransferStateConflict,
                InventoryTransferCodesV1.LabelVersionConflict,
                InventoryTransferCodesV1.SourcePositionConflict,
                InventoryTransferReceiptCodesV1.SplitUndecided,
                InventoryTransferReceiptCodesV1.LossPending,
            },
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.DestinationNotFound,
                InventoryTransferCodesV1.DestinationInactive,
                InventoryTransferCodesV1.DestinationContextMismatch,
                InventoryTransferReceiptCodesV1.OverageReasonRequired,
                InventoryTransferReceiptCodesV1.OverageReasonNotAllowed,
            });

        internal static readonly Operation DecideOverage = new(
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.ValidationFailed,
                InventoryScaleCodes.ScaleExceeded,
            },
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.IdempotencyConflict,
                InventoryTransferCodesV1.ConcurrentChange,
                InventoryTransferCodesV1.TransferStateConflict,
                InventoryTransferCodesV1.LabelVersionConflict,
                InventoryTransferCodesV1.SourcePositionConflict,
                InventoryTransferReceiptCodesV1.OverageNotPending,
                InventoryTransferReceiptCodesV1.AlreadyDecided,
                InventoryTransferReceiptCodesV1.SelfApproval,
            },
            new HashSet<string>(StringComparer.Ordinal));

        internal static readonly Operation RequestLoss = new(
            new HashSet<string>(StringComparer.Ordinal) { InventoryTransferCodesV1.ValidationFailed },
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.IdempotencyConflict,
                InventoryTransferCodesV1.ConcurrentChange,
                InventoryTransferCodesV1.TransferVersionConflict,
                InventoryTransferCodesV1.TransferStateConflict,
                InventoryTransferReceiptCodesV1.LossAlreadyRequested,
            },
            new HashSet<string>(StringComparer.Ordinal));

        internal static readonly Operation DecideLoss = new(
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.ValidationFailed,
                InventoryScaleCodes.ScaleExceeded,
            },
            new HashSet<string>(StringComparer.Ordinal)
            {
                InventoryTransferCodesV1.IdempotencyConflict,
                InventoryTransferCodesV1.ConcurrentChange,
                InventoryTransferCodesV1.TransferVersionConflict,
                InventoryTransferCodesV1.TransferStateConflict,
                InventoryTransferCodesV1.LabelVersionConflict,
                InventoryTransferCodesV1.SourcePositionConflict,
                InventoryTransferReceiptCodesV1.AlreadyDecided,
                InventoryTransferReceiptCodesV1.SelfApproval,
            },
            new HashSet<string>(StringComparer.Ordinal));
    }
}

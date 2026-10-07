using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// T07(a): Inventory's read surface, company-scoped. Each read needs the server-resolved company
/// (<c>X-Company-Id</c>) and its own permission, checked by the host's policy against the actor's
/// current grants. A label of another company, or one that does not exist, is the same 404: the
/// tenant filter never shows it. The one write is the opening count (INV-OPEN-1 part 2), below; the
/// receipt is a later slice.
/// </summary>
public static partial class InventoryEndpoints
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

        MapLabelDetailEndpoint(endpoints);
        MapParentRollCandidateEndpoint(endpoints);
        MapStockPositionEndpoint(endpoints);
        MapMovementTimelineEndpoint(endpoints);
        endpoints.MapInventoryReferenceEndpoints();
        MapPhysicalAllocationEndpoints(group);
        MapOpeningCountEndpoints(group);
        MapLabelTransformationEndpoint(endpoints);
        MapLabelClassificationEndpoint(endpoints);
        endpoints.MapInventoryStockCountEndpoints();
        endpoints.MapInventoryStockScrapEndpoints();
        return endpoints;
    }

    /// <summary>
    /// R19 / AT-038: record one same-company label split or merge through the owner's fenced command
    /// (<see cref="IInventoryLabelTransformationCommandV1"/>). The route policy and the command's own two fresh
    /// company-wide measurements both demand <c>inventory.label-transformation.record</c>; actor, permission and
    /// role codes are derived by the command, the host passes only the transport facts. ADR-011: one canonical
    /// <c>Idempotency-Key</c>; 201 with the transformation view, 200 with the same view plus
    /// <c>Idempotency-Replayed</c> and <c>Idempotency-Original-Correlation-Id</c> for the same key and content.
    /// 401/403 are platform problems; 400/404/409/503 carry the owner's closed code
    /// (<see cref="InventoryLabelTransformationHttpCodesV1"/>) and the correlation id; an owner answer outside
    /// those sets is a code-less 503, and any other fault is the platform's 500.
    /// </summary>
    private static void MapLabelTransformationEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(InventoryLabelTransformationApiV1.Path, async (
                HttpContext httpContext,
                IInventoryLabelTransformationCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key) || !correlation.IsResolved)
                {
                    return LabelTransformationProblem(StatusCodes.Status400BadRequest,
                        InventoryLabelGenealogyCodesV1.ValidationFailed, correlation);
                }

                if (!httpContext.Request.HasJsonContentType())
                {
                    return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                TransformInventoryLabelsV1? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<TransformInventoryLabelsV1>(
                        httpContext.Request.Body, LabelTransformationJson, cancellationToken);
                }
                catch (JsonException)
                {
                    return LabelTransformationProblem(StatusCodes.Status400BadRequest,
                        InventoryLabelGenealogyCodesV1.ValidationFailed, correlation);
                }

                if (request is null)
                {
                    return LabelTransformationProblem(StatusCodes.Status400BadRequest,
                        InventoryLabelGenealogyCodesV1.ValidationFailed, correlation);
                }

                var transport = new InventoryCommandMetadata(
                    currentUser.UserId ?? string.Empty,
                    InventoryPermissionManifestV1.LabelTransformationRecord,
                    [],
                    correlation.CorrelationId,
                    correlation.CausationId,
                    key,
                    correlation.SourceChannel,
                    correlation.ClientIpAddress,
                    correlation.UserAgent);
                try
                {
                    var result = await command.RecordAsync(request, transport, cancellationToken);
                    if (result.IsReplay)
                    {
                        ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                    }

                    return Results.Json(result.Transformation, LabelTransformationJson,
                        statusCode: result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
                }
                catch (InventoryLabelGenealogyException exception)
                {
                    return LabelTransformationProblem(exception, correlation);
                }
            })
            .WithName(InventoryLabelTransformationApiV1.RecordOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelTransformationRecord)
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<TransformInventoryLabelsV1>("application/json")
            .Produces<InventoryLabelTransformationViewV1>(StatusCodes.Status201Created)
            .Produces<InventoryLabelTransformationViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryLabelTransformationHttpCodesV1.Status400Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound([.. InventoryLabelTransformationHttpCodesV1.Status404Codes.Order(StringComparer.Ordinal)])
            .ProducesErpConflict([.. InventoryLabelTransformationHttpCodesV1.Status409Codes.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable([.. InventoryLabelTransformationHttpCodesV1.Status503Codes.Order(StringComparer.Ordinal)])
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// Classifies one label through Inventory's versioned, company-scoped and replayable owner command.
    /// Publishing and registering the permission grants nothing; the command performs fresh authority fences.
    /// </summary>
    private static void MapLabelClassificationEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/inventory/labels/{labelId:guid}/classification", async (
                Guid labelId,
                HttpContext httpContext,
                IInventoryLabelClassificationCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key) || !correlation.IsResolved)
                {
                    return LabelTransformationProblem(StatusCodes.Status400BadRequest,
                        InventoryLabelClassificationCodesV1.ValidationFailed, correlation);
                }

                if (!httpContext.Request.HasJsonContentType())
                {
                    return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                ClassifyInventoryLabelV1? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<ClassifyInventoryLabelV1>(
                        httpContext.Request.Body, LabelClassificationJson, cancellationToken);
                }
                catch (JsonException)
                {
                    return LabelTransformationProblem(StatusCodes.Status400BadRequest,
                        InventoryLabelClassificationCodesV1.ValidationFailed, correlation);
                }

                if (request is null)
                {
                    return LabelTransformationProblem(StatusCodes.Status400BadRequest,
                        InventoryLabelClassificationCodesV1.ValidationFailed, correlation);
                }

                try
                {
                    var result = await command.ClassifyAsync(
                        labelId,
                        request,
                        TransportMetadata(currentUser, correlation, key),
                        cancellationToken);
                    if (result.IsReplay)
                    {
                        ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                    }

                    return Results.Json(result.Classification, LabelClassificationJson,
                        statusCode: result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
                }
                catch (InventoryLabelClassificationException exception)
                {
                    return LabelClassificationProblem(exception, correlation);
                }
            })
            .WithName("ClassifyInventoryLabelV1")
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelClassificationWrite)
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<ClassifyInventoryLabelV1>("application/json")
            .Produces<InventoryLabelClassificationViewV1>(StatusCodes.Status201Created)
            .Produces<InventoryLabelClassificationViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([InventoryLabelClassificationCodesV1.ValidationFailed])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound([InventoryLabelClassificationCodesV1.LabelNotFound])
            .ProducesErpConflict(
            [
                InventoryLabelClassificationCodesV1.IdempotencyConflict,
                InventoryLabelClassificationCodesV1.LabelVersionConflict,
                InventoryLabelClassificationCodesV1.ClassificationUnchanged,
                InventoryLabelClassificationCodesV1.ConcurrentChange,
            ])
            .ProducesErpServiceUnavailable([InventoryLabelClassificationCodesV1.PermissionUnregistered])
            .ProducesErpAuthenticatedProblems();
    }

    private static IResult LabelClassificationProblem(
        InventoryLabelClassificationException exception,
        ICorrelationContext correlation)
    {
        var accepted = exception.Status switch
        {
            StatusCodes.Status400BadRequest =>
                exception.Code == InventoryLabelClassificationCodesV1.ValidationFailed,
            StatusCodes.Status401Unauthorized =>
                exception.Code == InventoryLabelClassificationCodesV1.Unauthenticated,
            StatusCodes.Status403Forbidden =>
                exception.Code == InventoryLabelClassificationCodesV1.Forbidden,
            StatusCodes.Status404NotFound =>
                exception.Code == InventoryLabelClassificationCodesV1.LabelNotFound,
            StatusCodes.Status409Conflict => exception.Code is
                InventoryLabelClassificationCodesV1.IdempotencyConflict
                or InventoryLabelClassificationCodesV1.LabelVersionConflict
                or InventoryLabelClassificationCodesV1.ClassificationUnchanged
                or InventoryLabelClassificationCodesV1.ConcurrentChange,
            StatusCodes.Status503ServiceUnavailable =>
                exception.Code == InventoryLabelClassificationCodesV1.PermissionUnregistered,
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

    /// <summary>The owner's refusal as a problem: 401/403 code-less, a closed-set code with its correlation otherwise.</summary>
    private static IResult LabelTransformationProblem(InventoryLabelGenealogyException exception, ICorrelationContext correlation)
    {
        var accepted = exception.Status switch
        {
            StatusCodes.Status400BadRequest => InventoryLabelTransformationHttpCodesV1.Status400Codes.Contains(exception.Code),
            StatusCodes.Status401Unauthorized => exception.Code == InventoryLabelTransformationCommandCodesV1.Unauthenticated,
            StatusCodes.Status403Forbidden => exception.Code == InventoryLabelTransformationCommandCodesV1.Forbidden,
            StatusCodes.Status404NotFound => InventoryLabelTransformationHttpCodesV1.Status404Codes.Contains(exception.Code),
            StatusCodes.Status409Conflict => InventoryLabelTransformationHttpCodesV1.Status409Codes.Contains(exception.Code),
            StatusCodes.Status503ServiceUnavailable => InventoryLabelTransformationHttpCodesV1.Status503Codes.Contains(exception.Code),
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

    private static IResult LabelTransformationProblem(int status, string code, ICorrelationContext correlation) =>
        Results.Problem(statusCode: status,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null,
            });

    /// <summary>
    /// ST-05 / AT-037 (DEC-STK-007): record a count of one stocked label, approve or reject its difference, and read
    /// a label's counts. Mapped by <see cref="MapInventoryEndpoints"/>; the route policies name the registered
    /// <see cref="ErpPermissions.InventoryStockCountRecord"/> and <see cref="ErpPermissions.InventoryStockCountApprove"/>
    /// and the host wires <c>AddInventoryStockCounts()</c>. The two commands follow the R19
    /// shape: one canonical <c>Idempotency-Key</c>; 201 with the count view, 200 with the same view plus
    /// <c>Idempotency-Replayed</c> and <c>Idempotency-Original-Correlation-Id</c> for the same key and content;
    /// 401/403 are platform problems; 400/404/409/503 carry the owner's closed code
    /// (<see cref="InventoryStockCountHttpCodesV1"/>) and the correlation id; any other owner answer is a code-less
    /// 503. The read reuses the ST-02 label-detail fence (labels.read + balances.read); a foreign or unknown label
    /// is the same 404.
    /// </summary>
    public static IEndpointRouteBuilder MapInventoryStockCountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost(InventoryStockCountApiV1.Path, async (
                HttpContext httpContext,
                IInventoryStockCountCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadStockCountBodyAsync<RecordInventoryStockCountV1>(httpContext, correlation, cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.RecordAsync(read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key, InventoryPermissionManifestV1.StockCountRecord),
                        cancellationToken);
                    return StockCountResult(httpContext, result);
                }
                catch (InventoryStockCountException exception)
                {
                    return StockCountProblem(exception, correlation, InventoryStockCountHttpCodesV1.RecordStatus404Codes,
                        InventoryStockCountHttpCodesV1.RecordStatus409Codes);
                }
            })
            .WithName(InventoryStockCountApiV1.RecordOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryStockCountRecord)
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<RecordInventoryStockCountV1>("application/json")
            .Produces<InventoryStockCountViewV1>(StatusCodes.Status201Created)
            .Produces<InventoryStockCountViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryStockCountHttpCodesV1.Status400Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound([.. InventoryStockCountHttpCodesV1.RecordStatus404Codes.Order(StringComparer.Ordinal)])
            .ProducesErpConflict([.. InventoryStockCountHttpCodesV1.RecordStatus409Codes.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable([.. InventoryStockCountHttpCodesV1.Status503Codes.Order(StringComparer.Ordinal)])
            .ProducesErpAuthenticatedProblems();

        endpoints.MapPost(StockCountDecisionRoute, async (
                Guid countId,
                HttpContext httpContext,
                IInventoryStockCountCommandV1 command,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                var read = await ReadStockCountBodyAsync<DecideInventoryStockCountV1>(httpContext, correlation, cancellationToken);
                if (read.Problem is not null)
                {
                    return read.Problem;
                }

                try
                {
                    var result = await command.DecideAsync(countId, read.Request!,
                        StockCountTransport(currentUser, correlation, read.Key, InventoryPermissionManifestV1.StockCountApprove),
                        cancellationToken);
                    return StockCountResult(httpContext, result);
                }
                catch (InventoryStockCountException exception)
                {
                    return StockCountProblem(exception, correlation, InventoryStockCountHttpCodesV1.DecideStatus404Codes,
                        InventoryStockCountHttpCodesV1.DecideStatus409Codes);
                }
            })
            .WithName(InventoryStockCountApiV1.DecideOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryStockCountApprove)
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<DecideInventoryStockCountV1>("application/json")
            .Produces<InventoryStockCountViewV1>(StatusCodes.Status201Created)
            .Produces<InventoryStockCountViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryStockCountHttpCodesV1.Status400Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound([.. InventoryStockCountHttpCodesV1.DecideStatus404Codes.Order(StringComparer.Ordinal)])
            .ProducesErpConflict([.. InventoryStockCountHttpCodesV1.DecideStatus409Codes.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable([.. InventoryStockCountHttpCodesV1.Status503Codes.Order(StringComparer.Ordinal)])
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(StockCountLabelRoute, async (
                Guid labelId,
                HttpContext httpContext,
                IInventoryStockCountQueryV1 counts,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                try
                {
                    return Results.Json(await counts.GetAsync(labelId, cancellationToken), StockCountJson);
                }
                catch (InventoryStockCountException exception)
                {
                    return exception.Status switch
                    {
                        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden =>
                            Results.Problem(statusCode: exception.Status),
                        StatusCodes.Status404NotFound => LabelDetailProblem(StatusCodes.Status404NotFound),
                        _ => LabelDetailProblem(StatusCodes.Status503ServiceUnavailable, InventoryStockCountCodesV1.Unavailable),
                    };
                }
            })
            .WithName(InventoryStockCountApiV1.LabelCountsOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead)
            .Produces<InventoryLabelStockCountsV1>(StatusCodes.Status200OK)
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(InventoryStockCountCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static async Task<(T? Request, string Key, IResult? Problem)> ReadStockCountBodyAsync<T>(
        HttpContext httpContext,
        ICorrelationContext correlation,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key) || !correlation.IsResolved)
        {
            return (null, string.Empty, LabelTransformationProblem(StatusCodes.Status400BadRequest,
                InventoryStockCountCodesV1.ValidationFailed, correlation));
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
                    InventoryStockCountCodesV1.ValidationFailed, correlation))
                : (request, key, null);
        }
        catch (JsonException)
        {
            return (null, key, LabelTransformationProblem(StatusCodes.Status400BadRequest,
                InventoryStockCountCodesV1.ValidationFailed, correlation));
        }
    }

    private static InventoryCommandMetadata StockCountTransport(
        ICurrentUser currentUser,
        ICorrelationContext correlation,
        string idempotencyKey,
        string permission) => new(
        currentUser.UserId ?? string.Empty,
        permission,
        [],
        correlation.CorrelationId,
        correlation.CausationId,
        idempotencyKey,
        correlation.SourceChannel,
        correlation.ClientIpAddress,
        correlation.UserAgent);

    private static IResult StockCountResult(HttpContext httpContext, InventoryStockCountResultV1 result)
    {
        if (result.IsReplay)
        {
            ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
        }

        return Results.Json(result.Count, StockCountJson,
            statusCode: result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }

    /// <summary>The owner's refusal as a problem: 401/403 code-less, a closed-set code with its correlation otherwise.</summary>
    private static IResult StockCountProblem(
        InventoryStockCountException exception,
        ICorrelationContext correlation,
        IReadOnlySet<string> status404Codes,
        IReadOnlySet<string> status409Codes)
    {
        var accepted = exception.Status switch
        {
            StatusCodes.Status400BadRequest => InventoryStockCountHttpCodesV1.Status400Codes.Contains(exception.Code),
            StatusCodes.Status401Unauthorized => exception.Code == InventoryStockCountCodesV1.Unauthenticated,
            StatusCodes.Status403Forbidden => exception.Code == InventoryStockCountCodesV1.Forbidden,
            StatusCodes.Status404NotFound => status404Codes.Contains(exception.Code),
            StatusCodes.Status409Conflict => status409Codes.Contains(exception.Code),
            StatusCodes.Status503ServiceUnavailable => InventoryStockCountHttpCodesV1.Status503Codes.Contains(exception.Code),
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

    private static void MapLabelDetailEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(InventoryLabelDetailApiV1.DetailPath, async (
                Guid labelId,
                HttpContext httpContext,
                IInventoryLabelDetailsV1 details,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                try
                {
                    return Results.Ok(await details.GetAsync(labelId, cancellationToken));
                }
                catch (InventoryLabelDetailException exception)
                {
                    return exception.StatusCode switch
                    {
                        StatusCodes.Status404NotFound =>
                            LabelDetailProblem(StatusCodes.Status404NotFound),
                        StatusCodes.Status409Conflict
                            when exception.Code == InventoryLabelDetailCodesV1.ReadConflict =>
                            LabelDetailProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status503ServiceUnavailable
                            when exception.Code == InventoryLabelDetailCodesV1.Unavailable =>
                            LabelDetailProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden =>
                            LabelDetailProblem(exception.StatusCode),
                        _ => LabelDetailProblem(
                            StatusCodes.Status503ServiceUnavailable,
                            InventoryLabelDetailCodesV1.Unavailable),
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return LabelDetailProblem(
                        StatusCodes.Status503ServiceUnavailable,
                        InventoryLabelDetailCodesV1.Unavailable);
                }
            })
            .WithName(InventoryLabelDetailApiV1.GetDetailOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead)
            .Produces<InventoryLabelDetailV1>(StatusCodes.Status200OK)
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(InventoryLabelDetailCodesV1.ReadConflict)
            .ProducesErpServiceUnavailable(InventoryLabelDetailCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();
    }

    private static IResult LabelDetailProblem(int statusCode, string? code = null) => code is null
        ? Results.Problem(statusCode: statusCode)
        : Results.Problem(statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });

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

    private static void MapStockPositionEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(InventoryStockPositionApiV1.PositionsPath, async (
                HttpContext httpContext,
                IInventoryStockPositionsV1 positions,
                IProductStockDisplayReferencesV1 productDisplays,
                ICompanyContext companyContext,
                ICurrentUser currentUser,
                IErpAuthorityRevalidationService authority,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                if (!TryReadStockPositionQuery(httpContext.Request.Query, out var query))
                {
                    return StockPositionProblem(StatusCodes.Status400BadRequest,
                        InventoryStockPositionCodesV1.InvalidQuery);
                }
                if (!TryReadStockPositionDisplayContract(httpContext.Request.Headers, out var includeProductDisplay))
                {
                    return StockPositionProblem(StatusCodes.Status400BadRequest,
                        InventoryStockPositionCodesV1.InvalidQuery);
                }

                try
                {
                    var scope = includeProductDisplay
                        ? InventoryStockPositionDisplayHost.CaptureScope(companyContext, currentUser)
                        : null;
                    var page = await positions.SearchAsync(query, cancellationToken);
                    if (!includeProductDisplay)
                    {
                        return Results.Ok(InventoryStockPositionDisplayHost.Legacy(page));
                    }

                    return Results.Ok(await InventoryStockPositionDisplayHost.ComposeAuthorizedAsync(
                        scope!, page, productDisplays, companyContext, currentUser, authority, cancellationToken));
                }
                catch (InventoryStockPositionException exception)
                {
                    return exception.StatusCode switch
                    {
                        StatusCodes.Status400BadRequest
                            when exception.Code == InventoryStockPositionCodesV1.InvalidQuery =>
                            StockPositionProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status409Conflict
                            when exception.Code == InventoryStockPositionCodesV1.ReadConflict =>
                            StockPositionProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status503ServiceUnavailable
                            when exception.Code == InventoryStockPositionCodesV1.Unavailable =>
                            StockPositionProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden =>
                            StockPositionProblem(exception.StatusCode),
                        _ => StockPositionProblem(StatusCodes.Status503ServiceUnavailable),
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (ProductOperationException exception)
                {
                    return exception.StatusCode == StatusCodes.Status403Forbidden
                        ? StockPositionProblem(StatusCodes.Status403Forbidden)
                        : StockPositionProblem(StatusCodes.Status503ServiceUnavailable);
                }
                catch (Exception)
                {
                    return StockPositionProblem(StatusCodes.Status503ServiceUnavailable);
                }
            })
            .WithName(InventoryStockPositionApiV1.GetPositionsOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead)
            .Produces<InventoryStockPositionPageV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(InventoryStockPositionCodesV1.InvalidQuery)
            .ProducesErpConflict(InventoryStockPositionCodesV1.ReadConflict)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static bool TryReadStockPositionDisplayContract(IHeaderDictionary headers, out bool enabled)
    {
        enabled = false;
        if (!headers.TryGetValue(InventoryStockPositionApiV1.ProductDisplayContractHeader, out var values))
        {
            return true;
        }

        if (values.Count != 1
            || !string.Equals(values[0], InventoryStockPositionApiV1.ProductDisplayContractVersion,
                StringComparison.Ordinal))
        {
            return false;
        }

        enabled = true;
        return true;
    }

    private static bool TryReadStockPositionQuery(
        IQueryCollection query,
        out InventoryStockPositionQueryV1 parsed)
    {
        parsed = new();
        if (query.Keys.Any(name => name is not "q" and not "limit" and not "cursor"
                and not "productId" and not "locationId" and not "classification"
                and not "ownerKind" and not "ownerId")
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
            && (!int.TryParse(limitValues[0], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out limit)
                || limit is < 1 or > 100))
        {
            return false;
        }

        string? cursor = null;
        if (query.TryGetValue("cursor", out var cursorValues)) cursor = cursorValues[0];

        if (!TryReadOptionalGuid(query, "productId", out var productId)
            || !TryReadOptionalGuid(query, "locationId", out var locationId)
            || !TryReadOptionalGuid(query, "ownerId", out var ownerId))
        {
            return false;
        }

        InventoryLabelClassificationV1? classification = null;
        if (query.TryGetValue("classification", out var classificationValues))
        {
            var raw = classificationValues[0];
            if (!Enum.TryParse<InventoryLabelClassificationV1>(raw, ignoreCase: false, out var value)
                || !Enum.IsDefined(value)
                || !string.Equals(value.ToString(), raw, StringComparison.Ordinal))
            {
                return false;
            }
            classification = value;
        }

        string? ownerKind = null;
        if (query.TryGetValue("ownerKind", out var ownerKindValues))
        {
            ownerKind = ownerKindValues[0];
            if (ownerKind is not "Company" and not "Counterparty") return false;
        }

        parsed = new InventoryStockPositionQueryV1(
            q, limit, cursor, productId, locationId, classification, ownerKind, ownerId);
        return true;
    }

    private static bool TryReadOptionalGuid(IQueryCollection query, string name, out Guid? value)
    {
        value = null;
        if (!query.TryGetValue(name, out var values)) return true;
        if (!Guid.TryParseExact(values[0], "D", out var parsed) || parsed == Guid.Empty) return false;
        value = parsed;
        return true;
    }

    private static IResult StockPositionProblem(int statusCode, string? code = null) => code is null
        ? Results.Problem(statusCode: statusCode)
        : Results.Problem(statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static void MapMovementTimelineEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(InventoryMovementTimelineApiV1.Path, async (
                HttpContext httpContext,
                IInventoryMovementTimelineV1 timeline,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                if (!TryReadMovementTimelineQuery(httpContext.Request.Query, out var query))
                {
                    return MovementTimelineProblem(
                        StatusCodes.Status400BadRequest,
                        InventoryMovementTimelineCodesV1.InvalidQuery);
                }

                try
                {
                    return Results.Ok(await timeline.SearchAsync(query, cancellationToken));
                }
                catch (InventoryMovementTimelineException exception)
                {
                    return exception.StatusCode switch
                    {
                        StatusCodes.Status400BadRequest
                            when exception.Code == InventoryMovementTimelineCodesV1.InvalidQuery =>
                            MovementTimelineProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status503ServiceUnavailable
                            when exception.Code == InventoryMovementTimelineCodesV1.Unavailable =>
                            MovementTimelineProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden =>
                            MovementTimelineProblem(exception.StatusCode),
                        _ => MovementTimelineProblem(StatusCodes.Status503ServiceUnavailable),
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return MovementTimelineProblem(StatusCodes.Status503ServiceUnavailable);
                }
            })
            .WithName(InventoryMovementTimelineApiV1.GetOperationId)
            .WithTags("Inventory")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead)
            .Produces<InventoryMovementTimelinePageV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(InventoryMovementTimelineCodesV1.InvalidQuery)
            .ProducesErpServiceUnavailable(InventoryMovementTimelineCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();
    }

    private static bool TryReadMovementTimelineQuery(
        IQueryCollection query,
        out InventoryMovementTimelineQueryV1 parsed)
    {
        parsed = new();
        if (query.Keys.Any(name => name is not "limit" and not "cursor")
            || query.Any(parameter => parameter.Value.Count != 1))
        {
            return false;
        }

        var limit = 50;
        if (query.TryGetValue("limit", out var limitValues)
            && (!int.TryParse(limitValues[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                || limit is < 1 or > 100))
        {
            return false;
        }

        string? cursor = null;
        if (query.TryGetValue("cursor", out var cursorValues))
        {
            cursor = cursorValues[0];
            if (string.IsNullOrWhiteSpace(cursor))
            {
                return false;
            }
        }

        parsed = new InventoryMovementTimelineQueryV1(limit, cursor);
        return true;
    }

    private static IResult MovementTimelineProblem(int statusCode, string? code = null) => code is null
        ? Results.Problem(statusCode: statusCode)
        : Results.Problem(statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static void MapPhysicalAllocationEndpoints(RouteGroupBuilder group)
    {
        group.MapGet(PhysicalAllocationsPath, async (
                HttpContext httpContext,
                IInventoryPhysicalAllocationReadsV1 reads,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                if (!TryReadPhysicalAllocationQuery(httpContext.Request.Query, out var query))
                {
                    return PhysicalAllocationReadProblem(StatusCodes.Status400BadRequest,
                        InventoryPhysicalAllocationReadCodesV1.InvalidQuery);
                }

                try
                {
                    return Results.Ok(await reads.SearchAsync(query, cancellationToken));
                }
                catch (InventoryPhysicalAllocationReadException exception)
                {
                    return exception.StatusCode switch
                    {
                        StatusCodes.Status400BadRequest
                            when exception.Code == InventoryPhysicalAllocationReadCodesV1.InvalidQuery =>
                            PhysicalAllocationReadProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status409Conflict
                            when exception.Code == InventoryPhysicalAllocationReadCodesV1.ReadConflict =>
                            PhysicalAllocationReadProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status503ServiceUnavailable
                            when exception.Code == InventoryPhysicalAllocationReadCodesV1.Unavailable =>
                            PhysicalAllocationReadProblem(exception.StatusCode),
                        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden =>
                            PhysicalAllocationReadProblem(exception.StatusCode),
                        _ => PhysicalAllocationReadProblem(StatusCodes.Status503ServiceUnavailable),
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return PhysicalAllocationReadProblem(StatusCodes.Status503ServiceUnavailable);
                }
            })
            .WithName("GetInventoryPhysicalAllocationsV1")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryPhysicalAllocationWrite)
            .Produces<InventoryPhysicalAllocationReadsV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(InventoryPhysicalAllocationReadCodesV1.InvalidQuery)
            .ProducesErpConflict(InventoryPhysicalAllocationReadCodesV1.ReadConflict)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost(PhysicalAllocationsPath, async (
                HttpContext httpContext,
                InventoryPhysicalAllocationHostService service,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key) || !correlation.IsResolved)
                {
                    return AllocationProblem(StatusCodes.Status400BadRequest,
                        InventoryAllocationCodesV1.ValidationFailed, correlation);
                }

                if (!httpContext.Request.HasJsonContentType())
                {
                    return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                CreateInventoryPhysicalAllocationRequestV1? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<CreateInventoryPhysicalAllocationRequestV1>(
                        httpContext.Request.Body, AllocationJson, cancellationToken);
                }
                catch (JsonException)
                {
                    return AllocationProblem(StatusCodes.Status400BadRequest,
                        InventoryAllocationCodesV1.ValidationFailed, correlation);
                }

                if (request is null)
                {
                    return AllocationProblem(StatusCodes.Status400BadRequest,
                        InventoryAllocationCodesV1.ValidationFailed, correlation);
                }

                try
                {
                    var result = await service.CreateAsync(request,
                        TransportMetadata(currentUser, correlation, key), cancellationToken);
                    if (result.IsReplay)
                    {
                        ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                    }

                    return Results.Json(result.Allocation, AllocationJson,
                        statusCode: result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
                }
                catch (InventoryPhysicalAllocationHostException exception)
                {
                    return AllocationProblem(exception.Status, exception.Code, correlation);
                }
                catch (InventoryAllocationException exception)
                {
                    return AllocationProblem(exception, correlation, create: true);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryPhysicalAllocationWrite)
            .WithName("CreateInventoryPhysicalAllocationV1")
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<CreateInventoryPhysicalAllocationRequestV1>("application/json")
            .Produces<InventoryPhysicalAllocationViewV1>(StatusCodes.Status201Created)
            .Produces<InventoryPhysicalAllocationViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryPhysicalAllocationHttpCodesV1.Status400Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict([.. InventoryPhysicalAllocationHttpCodesV1.CreateStatus409Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnprocessableEntity([.. InventoryPhysicalAllocationHttpCodesV1.CreateStatus422Codes.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapPost(PhysicalAllocationsPath + "/{allocationId:guid}/release", async (
                Guid allocationId,
                HttpContext httpContext,
                InventoryPhysicalAllocationHostService service,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key) || !correlation.IsResolved)
                {
                    return AllocationProblem(StatusCodes.Status400BadRequest,
                        InventoryAllocationCodesV1.ValidationFailed, correlation);
                }

                if (!httpContext.Request.HasJsonContentType())
                {
                    return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                ReleasePhysicalAllocationV1? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<ReleasePhysicalAllocationV1>(
                        httpContext.Request.Body, AllocationJson, cancellationToken);
                }
                catch (JsonException)
                {
                    return AllocationProblem(StatusCodes.Status400BadRequest,
                        InventoryAllocationCodesV1.ValidationFailed, correlation);
                }

                if (request is null)
                {
                    return AllocationProblem(StatusCodes.Status400BadRequest,
                        InventoryAllocationCodesV1.ValidationFailed, correlation);
                }

                try
                {
                    var result = await service.ReleaseAsync(allocationId, request,
                        TransportMetadata(currentUser, correlation, key), cancellationToken);
                    if (result.IsReplay)
                    {
                        ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                    }

                    return Results.Json(result.Allocation, AllocationJson);
                }
                catch (InventoryPhysicalAllocationHostException exception)
                {
                    return AllocationProblem(exception.Status, exception.Code, correlation);
                }
                catch (InventoryAllocationException exception)
                {
                    return AllocationProblem(exception, correlation, create: false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryPhysicalAllocationWrite)
            .WithName("ReleaseInventoryPhysicalAllocationV1")
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<ReleasePhysicalAllocationV1>("application/json")
            .Produces<InventoryPhysicalAllocationViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryPhysicalAllocationHttpCodesV1.Status400Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict([.. InventoryPhysicalAllocationHttpCodesV1.ReleaseStatus409Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnprocessableEntity([.. InventoryPhysicalAllocationHttpCodesV1.ReleaseStatus422Codes.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // INV-AL-1 change (Master v3 §25.4 "revize et", AT-063): resize in place or move to another label of the same
        // product, one owner save. 200 for the first change and for the same key again (the replay carries
        // Idempotency-Replayed and the original correlation), as release does.
        group.MapPost(PhysicalAllocationsPath + "/{allocationId:guid}/change", async (
                Guid allocationId,
                HttpContext httpContext,
                InventoryPhysicalAllocationHostService service,
                ICurrentUser currentUser,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key) || !correlation.IsResolved)
                {
                    return AllocationProblem(StatusCodes.Status400BadRequest,
                        InventoryAllocationCodesV1.ValidationFailed, correlation);
                }

                if (!httpContext.Request.HasJsonContentType())
                {
                    return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                ChangeInventoryPhysicalAllocationRequestV1? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<ChangeInventoryPhysicalAllocationRequestV1>(
                        httpContext.Request.Body, AllocationJson, cancellationToken);
                }
                catch (JsonException)
                {
                    return AllocationProblem(StatusCodes.Status400BadRequest,
                        InventoryAllocationCodesV1.ValidationFailed, correlation);
                }

                if (request is null)
                {
                    return AllocationProblem(StatusCodes.Status400BadRequest,
                        InventoryAllocationCodesV1.ValidationFailed, correlation);
                }

                try
                {
                    var result = await service.ChangeAsync(allocationId, request,
                        TransportMetadata(currentUser, correlation, key), cancellationToken);
                    if (result.IsReplay)
                    {
                        ApplyReplayHeaders(httpContext, result.OriginalCorrelationId);
                    }

                    return Results.Json(result.Change, AllocationJson);
                }
                catch (InventoryPhysicalAllocationHostException exception)
                {
                    return AllocationProblem(exception.Status, exception.Code, correlation);
                }
                catch (InventoryAllocationException exception)
                {
                    return ChangeAllocationProblem(exception, correlation);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryPhysicalAllocationWrite)
            .WithName("ChangeInventoryPhysicalAllocationV1")
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<ChangeInventoryPhysicalAllocationRequestV1>("application/json")
            .Produces<InventoryPhysicalAllocationChangeViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest([.. InventoryPhysicalAllocationHttpCodesV1.Status400Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict([.. InventoryPhysicalAllocationHttpCodesV1.ChangeStatus409Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnprocessableEntity([.. InventoryPhysicalAllocationHttpCodesV1.ChangeStatus422Codes.Order(StringComparer.Ordinal)])
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        // INV-AL-1 change: the label's allocations with their history (ST-02 label detail), same authority as the line read.
        group.MapGet(LabelPhysicalAllocationsPath, async (
                Guid labelId,
                HttpContext httpContext,
                IInventoryPhysicalAllocationReadsV1 reads,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                try
                {
                    return Results.Ok(await reads.ReadLabelAsync(labelId, cancellationToken));
                }
                catch (InventoryPhysicalAllocationReadException exception)
                {
                    return exception.StatusCode switch
                    {
                        StatusCodes.Status400BadRequest
                            when exception.Code == InventoryPhysicalAllocationReadCodesV1.InvalidQuery =>
                            PhysicalAllocationReadProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status404NotFound => PhysicalAllocationReadProblem(exception.StatusCode),
                        StatusCodes.Status409Conflict
                            when exception.Code == InventoryPhysicalAllocationReadCodesV1.ReadConflict =>
                            PhysicalAllocationReadProblem(exception.StatusCode, exception.Code),
                        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden =>
                            PhysicalAllocationReadProblem(exception.StatusCode),
                        _ => PhysicalAllocationReadProblem(StatusCodes.Status503ServiceUnavailable),
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return PhysicalAllocationReadProblem(StatusCodes.Status503ServiceUnavailable);
                }
            })
            .WithName("GetInventoryLabelPhysicalAllocationsV1")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryPhysicalAllocationWrite)
            .Produces<InventoryLabelPhysicalAllocationsV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(InventoryPhysicalAllocationReadCodesV1.InvalidQuery)
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(InventoryPhysicalAllocationReadCodesV1.ReadConflict)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    private static bool TryReadPhysicalAllocationQuery(
        IQueryCollection query,
        out InventoryPhysicalAllocationReadQueryV1 parsed)
    {
        parsed = new(Guid.Empty, Guid.Empty);
        if (query.Keys.Any(name => name is not "salesOrderId" and not "salesOrderLineId" and not "limit")
            || query.Any(parameter => parameter.Value.Count != 1)
            || !query.TryGetValue("salesOrderId", out var salesOrderValues)
            || !Guid.TryParseExact(salesOrderValues[0], "D", out var salesOrderId)
            || salesOrderId == Guid.Empty
            || !query.TryGetValue("salesOrderLineId", out var salesOrderLineValues)
            || !Guid.TryParseExact(salesOrderLineValues[0], "D", out var salesOrderLineId)
            || salesOrderLineId == Guid.Empty)
        {
            return false;
        }

        var limit = 100;
        if (query.TryGetValue("limit", out var limitValues)
            && (!int.TryParse(limitValues[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                || limit is < 1 or > 100))
        {
            return false;
        }

        parsed = new InventoryPhysicalAllocationReadQueryV1(salesOrderId, salesOrderLineId, limit);
        return true;
    }

    private static IResult PhysicalAllocationReadProblem(int statusCode, string? code = null) => code is null
        ? Results.Problem(statusCode: statusCode)
        : Results.Problem(statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static InventoryCommandMetadata TransportMetadata(
        ICurrentUser currentUser,
        ICorrelationContext correlation,
        string idempotencyKey) => new(
        currentUser.UserId ?? string.Empty,
        InventoryPermissionManifestV1.PhysicalAllocationWrite,
        [],
        correlation.CorrelationId,
        correlation.CausationId,
        idempotencyKey,
        correlation.SourceChannel,
        correlation.ClientIpAddress,
        correlation.UserAgent);

    private static IResult AllocationProblem(
        InventoryAllocationException exception,
        ICorrelationContext correlation,
        bool create)
    {
        var accepted = exception.Status switch
        {
            StatusCodes.Status400BadRequest =>
                InventoryPhysicalAllocationHttpCodesV1.Status400Codes.Contains(exception.Code),
            StatusCodes.Status404NotFound => exception.Code == InventoryAllocationCodesV1.InvalidReference,
            StatusCodes.Status409Conflict => (create
                ? InventoryPhysicalAllocationHttpCodesV1.CreateStatus409Codes
                : InventoryPhysicalAllocationHttpCodesV1.ReleaseStatus409Codes).Contains(exception.Code),
            StatusCodes.Status422UnprocessableEntity => (create
                ? InventoryPhysicalAllocationHttpCodesV1.CreateStatus422Codes
                : InventoryPhysicalAllocationHttpCodesV1.ReleaseStatus422Codes).Contains(exception.Code),
            StatusCodes.Status503ServiceUnavailable => exception.Code == InventoryAllocationCodesV1.InvalidReference,
            _ => false,
        };
        return accepted
            ? AllocationProblem(exception.Status, exception.Code, correlation)
            : Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult ChangeAllocationProblem(InventoryAllocationException exception, ICorrelationContext correlation)
    {
        var accepted = exception.Status switch
        {
            StatusCodes.Status400BadRequest =>
                InventoryPhysicalAllocationHttpCodesV1.Status400Codes.Contains(exception.Code),
            StatusCodes.Status404NotFound => exception.Code == InventoryAllocationCodesV1.InvalidReference,
            StatusCodes.Status409Conflict => InventoryPhysicalAllocationHttpCodesV1.ChangeStatus409Codes.Contains(exception.Code),
            StatusCodes.Status422UnprocessableEntity =>
                InventoryPhysicalAllocationHttpCodesV1.ChangeStatus422Codes.Contains(exception.Code),
            StatusCodes.Status503ServiceUnavailable => exception.Code == InventoryAllocationCodesV1.InvalidReference,
            _ => false,
        };
        return accepted
            ? AllocationProblem(exception.Status, exception.Code, correlation)
            : Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult AllocationProblem(int status, string? code, ICorrelationContext correlation)
    {
        if (status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
            or StatusCodes.Status404NotFound or StatusCodes.Status503ServiceUnavailable || code is null)
        {
            return Results.Problem(statusCode: status);
        }

        return Results.Problem(statusCode: status,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null,
            });
    }

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
    private const string PhysicalAllocationsPath = "/physical-allocations";
    private const string LabelPhysicalAllocationsPath = "/labels/{labelId:guid}/physical-allocations";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    // The contract DTOs refuse unknown and missing required members themselves (JsonUnmappedMemberHandling,
    // JsonRequired); web defaults give camelCase names and ISO dates.
    private static readonly JsonSerializerOptions OpeningJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions AllocationJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions LabelTransformationJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions LabelClassificationJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions StockCountJson = new(JsonSerializerDefaults.Web);

    // The owner's templates with the route constraint the host always applies to identities.
    private const string StockCountDecisionRoute = "/api/v1/inventory/stock-counts/{countId:guid}/decision";
    private const string StockCountLabelRoute = "/api/v1/inventory/labels/{labelId:guid}/stock-counts";

    private static IResult NotFound() =>
        Results.Problem(
            title: "Kayıt bulunamadı.",
            detail: "İstenen kayıt bulunamadı veya bu bağlamda kullanılamıyor.",
            statusCode: StatusCodes.Status404NotFound);
}

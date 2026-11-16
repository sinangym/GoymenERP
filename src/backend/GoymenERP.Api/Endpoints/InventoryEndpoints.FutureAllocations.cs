using System.Text.Json;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.Endpoints;

public static partial class InventoryEndpoints
{
    private static void MapInventoryFutureAllocationEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(InventoryFutureAllocationApiV1.Path, async (HttpContext http, IInventoryFutureAllocationCommandV1 command,
            ICurrentUser user, ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(http.Response);
            var read = await ReadFutureAllocationBodyAsync<CreateFutureSupplyAllocationV1>(http, correlation, cancellationToken);
            if (read.Problem is not null) return read.Problem;
            try
            {
                var result = await command.CreateAsync(read.Request!, StockCountTransport(user, correlation, read.Key,
                    ErpPermissions.InventoryFutureAllocationWrite), cancellationToken);
                return FutureAllocationResult(http, result);
            }
            catch (InventoryAllocationException exception) { return FutureAllocationProblem(http, exception, correlation); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return Results.Problem(statusCode: 503); }
        }).RequireCompanyContext().RequireAuthorization(ErpPermissions.InventoryFutureAllocationWrite)
            .WithName("CreateInventoryFutureAllocationV1").WithTags("Inventory")
            .WithIdempotencyContract(200, 409, 422).Accepts<CreateFutureSupplyAllocationV1>("application/json")
            .Produces<InventoryFutureAllocationViewV1>(201).Produces<InventoryFutureAllocationViewV1>(200)
            .ProducesErpBadRequest(InventoryFutureAllocationCodesV1.ValidationFailed).ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound().ProducesErpConflict(FutureAllocationConflictCodes)
            .ProducesErpUnprocessableEntity(FutureAllocationRuleCodes).ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        endpoints.MapPost(InventoryFutureAllocationApiV1.ReleasePath, async (Guid allocationId, HttpContext http,
            IInventoryFutureAllocationCommandV1 command, ICurrentUser user, ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(http.Response);
            var read = await ReadFutureAllocationBodyAsync<ReleaseFutureSupplyAllocationV1>(http, correlation, cancellationToken);
            if (read.Problem is not null) return read.Problem;
            try
            {
                var result = await command.ReleaseAsync(allocationId, read.Request!, StockCountTransport(user, correlation, read.Key,
                    ErpPermissions.InventoryFutureAllocationWrite), cancellationToken);
                return FutureAllocationResult(http, result);
            }
            catch (InventoryAllocationException exception) { return FutureAllocationProblem(http, exception, correlation); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return Results.Problem(statusCode: 503); }
        }).RequireCompanyContext().RequireAuthorization(ErpPermissions.InventoryFutureAllocationWrite)
            .WithName("ReleaseInventoryFutureAllocationV1").WithTags("Inventory")
            .WithIdempotencyContract(200, 409, 422).Accepts<ReleaseFutureSupplyAllocationV1>("application/json")
            .Produces<InventoryFutureAllocationViewV1>(201).Produces<InventoryFutureAllocationViewV1>(200)
            .ProducesErpBadRequest(InventoryFutureAllocationCodesV1.ValidationFailed).ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound().ProducesErpConflict(FutureAllocationConflictCodes)
            .ProducesErpUnprocessableEntity(FutureAllocationRuleCodes).ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        endpoints.MapGet(InventoryFutureAllocationApiV1.DemandPath, async (Guid salesOrderId, Guid salesOrderLineId, HttpContext http,
            IInventoryFutureAllocationQueryV1 query, ICorrelationContext correlation, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(http.Response);
            if (http.Request.Query.Count != 0)
                return OpeningProblem(400, InventoryFutureAllocationCodesV1.ValidationFailed, correlation);
            try { return Results.Ok(await query.ListByDemandAsync(salesOrderId, salesOrderLineId, cancellationToken)); }
            catch (InventoryAllocationException exception) { return FutureAllocationProblem(http, exception, correlation); }
        }).RequireCompanyContext().RequireAuthorization(ErpPermissions.InventoryFutureAllocationRead)
            .WithName("ListInventoryFutureAllocationsByDemandV1").WithTags("Inventory")
            .Produces<InventoryFutureAllocationListV1>().ProducesErpBadRequest(InventoryFutureAllocationCodesV1.ValidationFailed)
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
    }

    private static readonly string[] FutureAllocationConflictCodes =
    [ InventoryFutureAllocationCodesV1.IdempotencyConflict, InventoryFutureAllocationCodesV1.ConcurrentChange,
      InventoryFutureAllocationCodesV1.VersionConflict, InventoryFutureAllocationCodesV1.StateConflict,
      InventoryFutureAllocationCodesV1.InsufficientFutureSupply, InventoryFutureAllocationCodesV1.ExceedsDemandOpenQuantity,
      PurchaseLineSerializationLockContractV1.BusyCode, SalesOrderLineSerializationLockContractV1.BusyCode ];
    private static readonly string[] FutureAllocationRuleCodes =
    [ InventoryFutureAllocationCodesV1.InvalidReference, InventoryFutureAllocationCodesV1.UnitConversionUnavailable,
      InventoryFutureAllocationCodesV1.ReleaseSelectionInvalid, InventoryFutureAllocationCodesV1.ValidationFailed ];

    private static async Task<(T? Request, string Key, IResult? Problem)> ReadFutureAllocationBodyAsync<T>(HttpContext http,
        ICorrelationContext correlation, CancellationToken cancellationToken) where T : class
    {
        if (!TryReadIdempotencyKey(http.Request.Headers, out var key) || !correlation.IsResolved)
            return (null, string.Empty, OpeningProblem(400, InventoryFutureAllocationCodesV1.ValidationFailed, correlation));
        if (!http.Request.HasJsonContentType()) return (null, key, Results.StatusCode(415));
        try
        {
            var request = await JsonSerializer.DeserializeAsync<T>(http.Request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
            return request is null ? (null, key, OpeningProblem(400, InventoryFutureAllocationCodesV1.ValidationFailed, correlation)) : (request, key, null);
        }
        catch (JsonException) { return (null, key, OpeningProblem(400, InventoryFutureAllocationCodesV1.ValidationFailed, correlation)); }
    }

    private static IResult FutureAllocationResult(HttpContext http, InventoryFutureAllocationResultV1 result)
    {
        if (result.IsReplay)
        {
            if (string.IsNullOrWhiteSpace(result.OriginalCorrelationId)) return Results.Problem(statusCode: 503);
            ApplyReplayHeaders(http, result.OriginalCorrelationId);
        }
        return Results.Json(result.Allocation, statusCode: result.IsReplay ? 200 : 201);
    }

    private static IResult FutureAllocationProblem(HttpContext http, InventoryAllocationException exception, ICorrelationContext correlation)
    {
        if (exception.Status is 401 or 403 or 404) return Results.Problem(statusCode: exception.Status);
        if (!(exception.Status == 400 && exception.Code == InventoryFutureAllocationCodesV1.ValidationFailed
            || exception.Status == 409 && FutureAllocationConflictCodes.Contains(exception.Code, StringComparer.Ordinal)
            || exception.Status == 422 && FutureAllocationRuleCodes.Contains(exception.Code, StringComparer.Ordinal)))
            return Results.Problem(statusCode: 503);
        if (exception.IsReplay)
        {
            if (string.IsNullOrWhiteSpace(exception.OriginalCorrelationId)) return Results.Problem(statusCode: 503);
            ApplyReplayHeaders(http, exception.OriginalCorrelationId);
        }
        return OpeningProblem(exception.Status, exception.Code, correlation);
    }
}

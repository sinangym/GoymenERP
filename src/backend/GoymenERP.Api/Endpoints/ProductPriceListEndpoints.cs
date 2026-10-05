using System.Text.Json;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// AT-018 price lists (U-05), Product-owned and company-scoped. Mapped by the host with
/// <c>app.MapProductPriceListEndpoints()</c> once the three names of <see cref="ProductPriceListPermissionsV1"/> are
/// registered in the central catalogue and <c>AddProductPriceLists</c> is composed. The stock-count shape: one canonical
/// <c>Idempotency-Key</c> per command (≤128 visible characters, no comma); 201 for a create, 200 for the other commands,
/// and 200 with <c>Idempotency-Replayed</c> + <c>Idempotency-Original-Correlation-Id</c> for the same key and content;
/// 401/403 are platform problems; 400/404/409/422/503 carry the owner's closed code, the correlation id and the field
/// errors; any other owner answer is a code-less 503. Responses are <c>private, no-store</c>.
/// </summary>
public static class ProductPriceListEndpoints
{
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;
    private static readonly JsonSerializerOptions Json = ProductPriceListWire.Json;

    public static IEndpointRouteBuilder MapProductPriceListEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(ProductPriceListApiV1.ListPath, async (
                HttpContext httpContext,
                IProductPriceListServiceV1 service,
                ICorrelationContext correlation,
                int? pageSize,
                string? cursor,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                try
                {
                    return Results.Json(await service.ListAsync(pageSize, cursor, cancellationToken), Json);
                }
                catch (ProductPriceListException exception)
                {
                    return Problem(exception, correlation);
                }
            })
            .WithName(ProductPriceListApiV1.ListOperationId)
            .WithTags("Product")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ProductPriceListPermissionsV1.Read)
            .Produces<PriceListPageV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ProductPriceListCodesV1.InvalidCursor, ProductPriceListCodesV1.ValidationFailed)
            .ProducesErpServiceUnavailable(ProductPriceListCodesV1.PermissionUnregistered, ProductPriceListCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(ProductPriceListApiV1.ProductOptionsPath, async (
                HttpContext httpContext,
                IProductPriceListServiceV1 service,
                ICorrelationContext correlation,
                string? q,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                try
                {
                    return Results.Json(await service.GetProductOptionsAsync(q, cancellationToken), Json);
                }
                catch (ProductPriceListException exception)
                {
                    return Problem(exception, correlation);
                }
            })
            .WithName(ProductPriceListApiV1.ProductOptionsOperationId)
            .WithTags("Product")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ProductPriceListPermissionsV1.Write)
            .Produces<PriceListProductOptionsV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ProductPriceListCodesV1.ValidationFailed)
            .ProducesErpServiceUnavailable(ProductPriceListCodesV1.PermissionUnregistered, ProductPriceListCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(ProductPriceListApiV1.DetailPath, async (
                Guid priceListId,
                HttpContext httpContext,
                IProductPriceListServiceV1 service,
                ICorrelationContext correlation,
                CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(httpContext.Response);
                try
                {
                    return Results.Json(await service.GetAsync(priceListId, cancellationToken), Json);
                }
                catch (ProductPriceListException exception)
                {
                    return Problem(exception, correlation);
                }
            })
            .WithName(ProductPriceListApiV1.DetailOperationId)
            .WithTags("Product")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(ProductPriceListPermissionsV1.Read)
            .Produces<PriceListViewV1>(StatusCodes.Status200OK)
            .ProducesErpNotFound(ProductPriceListCodesV1.NotFound)
            .ProducesErpServiceUnavailable(ProductPriceListCodesV1.PermissionUnregistered, ProductPriceListCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();

        MapCommand<CreatePriceListRequestV1>(endpoints, ProductPriceListApiV1.ListPath, ProductPriceListApiV1.CreateOperationId,
            ProductPriceListPermissionsV1.Write, created: true,
            (service, _, request, metadata, cancellationToken) => service.CreateAsync(request, metadata, cancellationToken));
        MapCommand<RevisePriceListDraftRequestV1>(endpoints, ProductPriceListApiV1.DraftRevisionPath, ProductPriceListApiV1.ReviseDraftOperationId,
            ProductPriceListPermissionsV1.Write, created: false,
            (service, id, request, metadata, cancellationToken) => service.ReviseDraftAsync(id, request, metadata, cancellationToken));
        MapCommand<StartPriceListVersionRequestV1>(endpoints, ProductPriceListApiV1.VersionsPath, ProductPriceListApiV1.StartVersionOperationId,
            ProductPriceListPermissionsV1.Write, created: false,
            (service, id, request, metadata, cancellationToken) => service.StartVersionAsync(id, request, metadata, cancellationToken));
        MapCommand<PublishPriceListVersionRequestV1>(endpoints, ProductPriceListApiV1.PublicationPath, ProductPriceListApiV1.PublishOperationId,
            ProductPriceListPermissionsV1.Publish, created: false,
            (service, id, request, metadata, cancellationToken) => service.PublishAsync(id, request, metadata, cancellationToken));
        return endpoints;
    }

    private static void MapCommand<TRequest>(IEndpointRouteBuilder endpoints, string path, string operationId, string permission, bool created,
        Func<IProductPriceListServiceV1, Guid, TRequest, ProductPriceListCommandMetadata, CancellationToken, Task<PriceListResultV1>> call)
        where TRequest : class
    {
        RouteHandlerBuilder builder;
        if (path.Contains("{priceListId", StringComparison.Ordinal))
        {
            builder = endpoints.MapPost(path, async (
                    Guid priceListId,
                    HttpContext httpContext,
                    IProductPriceListServiceV1 service,
                    ICurrentUser currentUser,
                    ICorrelationContext correlation,
                    CancellationToken cancellationToken) =>
                await HandleCommandAsync(priceListId, httpContext, service, currentUser, correlation, cancellationToken,
                    permission, created, call));
        }
        else
        {
            builder = endpoints.MapPost(path, async (
                    HttpContext httpContext,
                    IProductPriceListServiceV1 service,
                    ICurrentUser currentUser,
                    ICorrelationContext correlation,
                    CancellationToken cancellationToken) =>
                await HandleCommandAsync(Guid.Empty, httpContext, service, currentUser, correlation, cancellationToken,
                    permission, created, call));
        }

        builder
            .WithName(operationId)
            .WithTags("Product")
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .RequireCompanyContext()
            .RequireAuthorization(permission)
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<TRequest>("application/json")
            .Produces<PriceListResultV1>(created ? StatusCodes.Status201Created : StatusCodes.Status200OK)
            .Produces<PriceListResultV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ProductPriceListCodesV1.ValidationFailed)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(ProductPriceListCodesV1.NotFound)
            .ProducesErpConflict([.. ProductPriceListHttpCodesV1.Status409Codes.Order(StringComparer.Ordinal)])
            .ProducesErpUnprocessableEntity(ProductPriceListCodesV1.ValidationFailed)
            .ProducesErpServiceUnavailable(ProductPriceListCodesV1.PermissionUnregistered, ProductPriceListCodesV1.Unavailable)
            .ProducesErpAuthenticatedProblems();
    }

    private static async Task<IResult> HandleCommandAsync<TRequest>(
        Guid id,
        HttpContext httpContext,
        IProductPriceListServiceV1 service,
        ICurrentUser currentUser,
        ICorrelationContext correlation,
        CancellationToken cancellationToken,
        string permission,
        bool created,
        Func<IProductPriceListServiceV1, Guid, TRequest, ProductPriceListCommandMetadata, CancellationToken, Task<PriceListResultV1>> call)
        where TRequest : class
    {
        SetPrivateNoStore(httpContext.Response);
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var key) || !correlation.IsResolved)
        {
            return Problem(StatusCodes.Status400BadRequest, ProductPriceListCodesV1.ValidationFailed, [], correlation);
        }

        if (!httpContext.Request.HasJsonContentType())
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        TRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<TRequest>(httpContext.Request.Body, Json, cancellationToken);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null)
        {
            return Problem(StatusCodes.Status400BadRequest, ProductPriceListCodesV1.ValidationFailed, [], correlation);
        }

        try
        {
            var metadata = new ProductPriceListCommandMetadata(currentUser.UserId ?? string.Empty, permission, [],
                correlation.CorrelationId, correlation.CausationId, key, correlation.SourceChannel, correlation.ClientIpAddress,
                correlation.UserAgent);
            var result = await call(service, id, request, metadata, cancellationToken);
            if (result.IsReplay)
            {
                httpContext.Response.Headers["Idempotency-Replayed"] = "true";
                httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
            }

            if (created && !result.IsReplay)
            {
                httpContext.Response.Headers.Location = $"/api/v1/price-lists/{result.PriceList.Id:D}";
            }

            return Results.Json(result, Json,
                statusCode: created && !result.IsReplay ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        }
        catch (ProductPriceListException exception)
        {
            return Problem(exception, correlation);
        }
    }

    /// <summary>The owner's refusal as a problem: 401/403 code-less, a closed-set code with its correlation otherwise.</summary>
    private static IResult Problem(ProductPriceListException exception, ICorrelationContext correlation)
    {
        var accepted = exception.Status switch
        {
            StatusCodes.Status400BadRequest => ProductPriceListHttpCodesV1.Status400Codes.Contains(exception.Code),
            StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => true,
            StatusCodes.Status404NotFound => ProductPriceListHttpCodesV1.Status404Codes.Contains(exception.Code),
            StatusCodes.Status409Conflict => ProductPriceListHttpCodesV1.Status409Codes.Contains(exception.Code),
            StatusCodes.Status422UnprocessableEntity => ProductPriceListHttpCodesV1.Status422Codes.Contains(exception.Code),
            StatusCodes.Status503ServiceUnavailable => ProductPriceListHttpCodesV1.Status503Codes.Contains(exception.Code),
            _ => false,
        };
        if (!accepted)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return exception.Status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
            ? Results.Problem(statusCode: exception.Status)
            : Problem(exception.Status, exception.Code, exception.Errors, correlation);
    }

    private static IResult Problem(int status, string code, IReadOnlyList<ProductPriceListFieldErrorV1> errors, ICorrelationContext correlation) =>
        Results.Problem(statusCode: status,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["correlationId"] = correlation.IsResolved ? correlation.CorrelationId : null,
                ["errors"] = errors.Select(error => new { field = error.Field, code = error.Code }).ToArray(),
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

    private static void SetPrivateNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }
}

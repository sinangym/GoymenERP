using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// SAT-D134 (EK19) / SAT-D149 (EK20): the item type (Steel / Scrap / Service) of a product on the SHARED product card (the same
/// value in every company), read and set by an actor of the server-resolved company (X-Company-Id) under
/// <see cref="ProductItemTypePermissionsV1.Manage"/> in that company (company-wide, granted to no role here); each change is
/// audited in that company.
/// <c>GET /api/v1/product-item-types/{productId}</c> answers it (version "0" while none is stored); <c>PUT</c> sets it (204) with
/// Idempotency-Key, the expected version and a mandatory reason. Private, no-store.
/// </summary>
public static class ProductItemTypeEndpoints
{
    public const string BasePath = "/api/v1/product-item-types";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    public static IEndpointRouteBuilder MapProductItemTypeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath)
            .RequireAuthorization()
            .WithTags("Product");

        group.MapGet("/{productId:guid}", async (
                Guid productId,
                IProductItemTypeServiceV1 service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                if (httpContext.Request.Query.Count != 0)
                {
                    return Problem(StatusCodes.Status400BadRequest, ProductItemTypeProblemCodesV1.RequestInvalid,
                        "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
                }

                try
                {
                    return Results.Ok(await service.GetAsync(productId, cancellationToken));
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
                }
                catch (ProductItemTypeException exception)
                {
                    return FromException(exception);
                }
            })
            .RequireCompanyContext()
            .RequireAuthorization(ProductItemTypePermissionsV1.Manage)
            .RequiresNonEmptyGuidPathParameter("productId")
            .WithName("GetProductItemTypeV1")
            .Produces<ProductItemTypeViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ProductItemTypeProblemCodesV1.RequestInvalid)
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(ProductItemTypeProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/{productId:guid}", async (
                Guid productId,
                SetProductItemTypeRequestV1 request,
                IProductItemTypeServiceV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                if (httpContext.Request.Query.Count != 0)
                {
                    return Problem(StatusCodes.Status400BadRequest, ProductItemTypeProblemCodesV1.RequestInvalid,
                        "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
                }

                if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
                {
                    return Problem(StatusCodes.Status400BadRequest, ProductItemTypeProblemCodesV1.CommandMetadataInvalid,
                        "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
                }

                ProductItemTypeResultV1 result;
                try
                {
                    result = await service.SetAsync(productId, request, metadata, cancellationToken);
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
                }
                catch (ProductItemTypeException exception)
                {
                    return FromException(exception);
                }

                if (result.IsReplay)
                {
                    httpContext.Response.Headers["Idempotency-Replayed"] = "true";
                    httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
                }

                var answer = result.Answer;
                return answer.StatusCode switch
                {
                    StatusCodes.Status204NoContent => Results.NoContent(),
                    StatusCodes.Status404NotFound when answer.Code is null => Results.Problem(title: answer.Title,
                        detail: answer.Detail, statusCode: StatusCodes.Status404NotFound),
                    _ when answer.Code is { } code => Problem(answer.StatusCode, code, answer.Title, answer.Detail),
                    _ => throw new InvalidOperationException("Ürün kalem tipi komut sonucu HTTP durumuyla eşleşmiyor."),
                };
            })
            .RequireCompanyContext()
            .RequireAuthorization(ProductItemTypePermissionsV1.Manage)
            .RequiresNonEmptyGuidPathParameter("productId")
            .WithName("SetProductItemTypeV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<SetProductItemTypeRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(
                ProductItemTypeProblemCodesV1.RequestInvalid,
                ProductItemTypeProblemCodesV1.CommandMetadataInvalid,
                ProductItemTypeProblemCodesV1.ReasonRequired,
                ProductItemTypeProblemCodesV1.ReasonTooLong)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                ProductItemTypeProblemCodesV1.IdempotencyConflict,
                ProductItemTypeProblemCodesV1.VersionConflict)
            .ProducesErpServiceUnavailable(ProductItemTypeProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static IResult FromException(ProductItemTypeException exception) => exception.StatusCode switch
    {
        StatusCodes.Status403Forbidden => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        StatusCodes.Status404NotFound => Results.Problem(title: "Ürün bulunamadı.", detail: "İstenen ürün kartı yok.",
            statusCode: StatusCodes.Status404NotFound),
        StatusCodes.Status400BadRequest => Problem(StatusCodes.Status400BadRequest,
            ProductItemTypeProblemCodesV1.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, ProductItemTypeProblemCodesV1.ProviderUnavailable,
            "Kalem tipi kaynağı kullanılamıyor.", "Ürünün kalem tipi şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
    };

    private static IResult Problem(int status, string code, string title, string detail) =>
        Results.Problem(title: title, detail: detail, statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static bool TryReadMetadata(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        out ProductItemTypeCommandMetadataV1 metadata)
    {
        metadata = null!;
        if (!httpContext.Request.Headers.TryGetValue(IdempotencyHeaderName, out var values)
            || values.Count != 1
            || values[0] is not { } raw
            || raw.Trim() is not { Length: > 0 and <= MaximumIdempotencyKeyLength } key
            || key.Contains(',', StringComparison.Ordinal)
            || !correlationContext.IsResolved
            || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved)
        {
            return false;
        }

        metadata = new ProductItemTypeCommandMetadataV1(
            companyContext.CompanyId,
            currentUser.UserId,
            correlationContext.CorrelationId,
            correlationContext.CausationId,
            correlationContext.SourceChannel,
            correlationContext.ClientIpAddress,
            correlationContext.UserAgent,
            key);
        return true;
    }
}

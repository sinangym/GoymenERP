using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// MOD-03 (SAT-D73/D74/D82): the product's VAT class and withholding class in the server-resolved company (X-Company-Id),
/// under <see cref="ProductTaxClassificationPermissionsV1.Manage"/> (company-wide, granted to no role here).
/// <c>GET /api/v1/product-tax-classifications/{productId}</c> answers the classification (version "0" while none is
/// stored); <c>PUT</c> sets it (204) with Idempotency-Key, the expected version and a mandatory reason. Private, no-store.
/// </summary>
public static class ProductTaxClassificationEndpoints
{
    public const string BasePath = "/api/v1/product-tax-classifications";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    public static IEndpointRouteBuilder MapProductTaxClassificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath)
            .RequireAuthorization()
            .WithTags("Product");

        group.MapGet("/{productId:guid}", async (
                Guid productId,
                IProductTaxClassificationServiceV1 service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                if (httpContext.Request.Query.Count != 0)
                {
                    return Problem(StatusCodes.Status400BadRequest, ProductTaxClassificationProblemCodesV1.RequestInvalid,
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
                catch (ProductTaxClassificationException exception)
                {
                    return FromException(exception);
                }
            })
            .RequireCompanyContext()
            .RequireAuthorization(ProductTaxClassificationPermissionsV1.Manage)
            .RequiresNonEmptyGuidPathParameter("productId")
            .WithName("GetProductTaxClassificationV1")
            .Produces<ProductTaxClassificationViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ProductTaxClassificationProblemCodesV1.RequestInvalid)
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(ProductTaxClassificationProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/{productId:guid}", async (
                Guid productId,
                SetProductTaxClassificationRequestV1 request,
                IProductTaxClassificationServiceV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                if (httpContext.Request.Query.Count != 0)
                {
                    return Problem(StatusCodes.Status400BadRequest, ProductTaxClassificationProblemCodesV1.RequestInvalid,
                        "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
                }

                if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
                {
                    return Problem(StatusCodes.Status400BadRequest, ProductTaxClassificationProblemCodesV1.CommandMetadataInvalid,
                        "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
                }

                ProductTaxClassificationResultV1 result;
                try
                {
                    result = await service.SetAsync(productId, request, metadata, cancellationToken);
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
                }
                catch (ProductTaxClassificationException exception)
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
                    _ => throw new InvalidOperationException("Ürün vergi sınıfı komut sonucu HTTP durumuyla eşleşmiyor."),
                };
            })
            .RequireCompanyContext()
            .RequireAuthorization(ProductTaxClassificationPermissionsV1.Manage)
            .RequiresNonEmptyGuidPathParameter("productId")
            .WithName("SetProductTaxClassificationV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity)
            .Accepts<SetProductTaxClassificationRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(
                ProductTaxClassificationProblemCodesV1.RequestInvalid,
                ProductTaxClassificationProblemCodesV1.CommandMetadataInvalid,
                ProductTaxClassificationProblemCodesV1.ReasonRequired,
                ProductTaxClassificationProblemCodesV1.ReasonTooLong)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                ProductTaxClassificationProblemCodesV1.IdempotencyConflict,
                ProductTaxClassificationProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(
                ProductTaxClassificationProblemCodesV1.VatClassInvalid,
                ProductTaxClassificationProblemCodesV1.WithholdingClassInvalid)
            .ProducesErpServiceUnavailable(ProductTaxClassificationProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static IResult FromException(ProductTaxClassificationException exception) => exception.StatusCode switch
    {
        StatusCodes.Status403Forbidden => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        StatusCodes.Status404NotFound => Results.Problem(title: "Ürün bulunamadı.", detail: "İstenen ürün kartı yok.",
            statusCode: StatusCodes.Status404NotFound),
        StatusCodes.Status400BadRequest => Problem(StatusCodes.Status400BadRequest,
            ProductTaxClassificationProblemCodesV1.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, ProductTaxClassificationProblemCodesV1.ProviderUnavailable,
            "Vergi sınıfı kaynağı kullanılamıyor.", "Ürünün vergi sınıfı şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
    };

    private static IResult Problem(int status, string code, string title, string detail) =>
        Results.Problem(title: title, detail: detail, statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static bool TryReadMetadata(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        out ProductTaxClassificationCommandMetadataV1 metadata)
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

        metadata = new ProductTaxClassificationCommandMetadataV1(
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

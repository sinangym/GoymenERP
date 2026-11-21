using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// URN-D13…D16 (STK-S37R): the company's kg/m table of profiles and round tubes, read and imported by an actor of the
/// server-resolved company (X-Company-Id) under <see cref="ProductProfileWeightPermissionsV1.Import"/> in that company
/// (company-wide, granted to no role here). <c>GET /api/v1/product-profile-weight-tables/current</c> answers the current table
/// (version "0" and no rows while none was imported); <c>POST /api/v1/product-profile-weight-tables</c> imports a new version
/// from the file (204) with Idempotency-Key, the expected version, a mandatory source label and reason. The file is kept in the
/// document vault (URN-D13): while no vault or scanner can take it, the import answers 503 and nothing changes. Private, no-store.
/// </summary>
public static class ProductProfileWeightTableEndpoints
{
    public const string BasePath = "/api/v1/product-profile-weight-tables";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    public static IEndpointRouteBuilder MapProductProfileWeightTableEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath)
            .RequireAuthorization()
            .WithTags("Product");

        group.MapGet("/current", async (
                IProductProfileWeightTableServiceV1 service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                if (httpContext.Request.Query.Count != 0)
                {
                    return Problem(StatusCodes.Status400BadRequest, ProductProfileWeightProblemCodesV1.RequestInvalid,
                        "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
                }

                try
                {
                    return Results.Ok(await service.GetCurrentAsync(cancellationToken));
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
                }
                catch (ProductProfileWeightException exception)
                {
                    return FromException(exception);
                }
            })
            .RequireCompanyContext()
            .RequireAuthorization(ProductProfileWeightPermissionsV1.Import)
            .WithName("GetCurrentProductProfileWeightTableV1")
            .Produces<ProductProfileWeightTableViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ProductProfileWeightProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(ProductProfileWeightProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/", async (
                ImportProductProfileWeightTableRequestV1 request,
                IProductProfileWeightTableServiceV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                if (httpContext.Request.Query.Count != 0)
                {
                    return Problem(StatusCodes.Status400BadRequest, ProductProfileWeightProblemCodesV1.RequestInvalid,
                        "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
                }

                if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
                {
                    return Problem(StatusCodes.Status400BadRequest, ProductProfileWeightProblemCodesV1.CommandMetadataInvalid,
                        "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
                }

                ProductProfileWeightResultV1 result;
                try
                {
                    result = await service.ImportAsync(request, metadata, cancellationToken);
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
                }
                catch (ProductProfileWeightException exception)
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
                    _ when answer.Code is { } code => Problem(answer.StatusCode, code, answer.Title, answer.Detail),
                    _ => throw new InvalidOperationException("Metre ağırlığı tablosu komut sonucu HTTP durumuyla eşleşmiyor."),
                };
            })
            .RequireCompanyContext()
            .RequireAuthorization(ProductProfileWeightPermissionsV1.Import)
            .WithName("ImportProductProfileWeightTableV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status400BadRequest,
                StatusCodes.Status409Conflict)
            .Accepts<ImportProductProfileWeightTableRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(
                ProductProfileWeightProblemCodesV1.RequestInvalid,
                ProductProfileWeightProblemCodesV1.CommandMetadataInvalid,
                ProductProfileWeightProblemCodesV1.FileInvalid,
                ProductProfileWeightProblemCodesV1.FileRejected,
                ProductProfileWeightProblemCodesV1.SourceLabelRequired,
                ProductProfileWeightProblemCodesV1.SourceLabelTooLong,
                ProductProfileWeightProblemCodesV1.ReasonRequired,
                ProductProfileWeightProblemCodesV1.ReasonTooLong)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                ProductProfileWeightProblemCodesV1.IdempotencyConflict,
                ProductProfileWeightProblemCodesV1.VersionConflict)
            .ProducesErpServiceUnavailable(
                ProductProfileWeightProblemCodesV1.FileVaultUnavailable,
                ProductProfileWeightProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static IResult FromException(ProductProfileWeightException exception) => exception.StatusCode switch
    {
        StatusCodes.Status403Forbidden => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        StatusCodes.Status400BadRequest => Problem(StatusCodes.Status400BadRequest,
            ProductProfileWeightProblemCodesV1.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, ProductProfileWeightProblemCodesV1.ProviderUnavailable,
            "Metre ağırlığı tablosu kullanılamıyor.",
            "Şirketin metre ağırlığı tablosu şu anda okunamıyor veya içe aktarılamıyor; daha sonra yeniden deneyin."),
    };

    private static IResult Problem(int status, string code, string title, string detail) =>
        Results.Problem(title: title, detail: detail, statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static bool TryReadMetadata(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        out ProductProfileWeightCommandMetadataV1 metadata)
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

        metadata = new ProductProfileWeightCommandMetadataV1(
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

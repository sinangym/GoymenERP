using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>SAT-D422: explicit global Product card class; op7 authorizes writes, never company permissions. No account/stock/invoice write.</summary>
public static class ProductCogsClassificationEndpoints
{
    public const string BasePath = "/api/v1/product-cogs-classifications";
    public static IEndpointRouteBuilder MapProductCogsClassificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).RequireAuthorization().WithTags("Product");
        Common(group.MapGet("/{productId:guid}",GetAsync).WithName("GetProductCogsClassificationV1"))
            .Produces<ProductCogsClassificationViewV1>(200);
        Common(group.MapPut("/{productId:guid}",SetAsync).WithName("SetProductCogsClassificationV1"))
            .Accepts<SetProductCogsClassificationV1>("application/json").Produces<ProductCogsClassificationMutationV1>(200)
            .AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(ProductCogsClassificationCodesV1.VersionConflict,ProductCogsClassificationCodesV1.IdempotencyConflict)
            .WithIdempotencyContract(200,404,409);
        return endpoints;
    }
    private static RouteHandlerBuilder Common(RouteHandlerBuilder builder) => builder.RequireCompanyContext()
        .RequiresNonEmptyGuidPathParameter("productId").ProducesErpBadRequest(ProductCogsClassificationCodesV1.InvalidRequest)
        .ProducesErpNotFound(ProductCogsClassificationCodesV1.NotFound)
        .ProducesErpServiceUnavailable(ProductCogsClassificationCodesV1.Unavailable).ProducesErpAuthenticatedProblems();
    private static Task<IResult> GetAsync(Guid productId,IProductCogsClassificationServiceV1 service,HttpContext http,CancellationToken ct) =>
        RespondAsync(http,() => service.GetAsync(productId,ct));
    private static Task<IResult> SetAsync(Guid productId,SetProductCogsClassificationV1 request,IProductCogsClassificationServiceV1 service,
        ICurrentUser user,ICompanyContext company,ICorrelationContext correlation,HttpContext http,CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        if (!company.IsResolved || !user.IsAuthenticated || user.UserId is null || !correlation.IsResolved
            || !http.Request.Headers.TryGetValue("Idempotency-Key",out var values) || values.Count != 1 || values[0] is not { } key)
            return Task.FromResult<IResult>(Problem(400,ProductCogsClassificationCodesV1.InvalidRequest));
        return RespondAsync(http,() => service.SetAsync(productId,request,new(company.CompanyId,user.UserId,correlation.CorrelationId,
            correlation.CausationId,key,correlation.SourceChannel,correlation.ClientIpAddress,correlation.UserAgent),ct));
    }
    private static async Task<IResult> RespondAsync<T>(HttpContext http,Func<Task<T>> action)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        if (http.Request.Query.Count != 0) return Problem(400,ProductCogsClassificationCodesV1.InvalidRequest);
        try
        {
            var result = await action();
            if (result is ProductCogsClassificationMutationV1 mutation) ReplayHeaders(http,mutation.IsReplay,mutation.OriginalCorrelationId);
            return Results.Ok(result);
        }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode:403); }
        catch (ProductCogsClassificationException exception)
        { ReplayHeaders(http,exception.IsReplay,exception.OriginalCorrelationId); return Problem(exception.StatusCode,exception.Code); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return Problem(503,ProductCogsClassificationCodesV1.Unavailable); }
    }
    private static void ReplayHeaders(HttpContext http,bool replay,string? correlation)
    {
        if (!replay) return;
        http.Response.Headers[ErpOpenApiContract.IdempotencyReplayedHeaderName] = "true";
        http.Response.Headers[ErpOpenApiContract.OriginalCorrelationHeaderName] = correlation;
    }
    private static IResult Problem(int status,string code) => Results.Problem(statusCode:status,
        title:status == 503 ? "Ürün maliyet sınıfı şu anda kullanılamıyor." : "Ürün maliyet sınıfı işlemi tamamlanamadı.",
        extensions:new Dictionary<string,object?> { ["code"] = code });
}

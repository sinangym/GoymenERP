using System.Globalization;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>BS-10 / URT-D25: real Product code validation, explicit apply and versioned company membership maintenance.
/// The permission is registered only. A disabled Product runtime answers 503; no source is created by a read.</summary>
public static class ProductPaintMembershipEndpoints
{
    public const string BasePath = "/api/v1/product-paint-memberships";
    public static IEndpointRouteBuilder MapProductPaintMembershipEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).RequireAuthorization().WithTags("Product");
        Common(group.MapGet("/current", GetCurrentAsync).WithName("GetProductPaintMembershipsV1"))
            .Produces<ProductPaintMembershipCatalogV1>(200);
        Common(group.MapGet("/candidates", GetCandidatesAsync).WithName("GetProductPaintMembershipCandidatesV1"))
            .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
            .Produces<ProductPaintMembershipCandidatesV1>(200)
            .ProducesErpConflict(ProductPaintMembershipCodesV1.VersionConflict);
        Common(group.MapPost("/imports/validate", ValidateAsync).WithName("ValidateProductPaintMembershipImportV1"))
            .Accepts<ValidateProductPaintMembershipImportV1>("application/json")
            .Produces<ProductPaintMembershipImportPreviewV1>(200)
            .AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(ProductPaintMembershipCodesV1.PreviewMismatch)
            .ProducesErpUnprocessableEntity(ProductPaintMembershipCodesV1.InvalidFile);
        Command(Common(group.MapPost("/imports", ApplyAsync).WithName("ApplyProductPaintMembershipImportV1")))
            .Accepts<ApplyProductPaintMembershipImportV1>("application/json");
        Command(Common(group.MapPut("/products/{productId:guid}", SetAsync).WithName("SetProductPaintMembershipV1")))
            .Accepts<SetProductPaintMembershipV1>("application/json");
        return endpoints;
    }
    private static RouteHandlerBuilder Common(RouteHandlerBuilder builder) => builder.RequireCompanyContext()
        .RequireAuthorization(ProductPaintMembershipPermissionsV1.Manage)
        .ProducesErpBadRequest(ProductPaintMembershipCodesV1.InvalidRequest)
        .ProducesErpServiceUnavailable(ProductPaintMembershipCodesV1.Unavailable).ProducesErpAuthenticatedProblems();
    private static RouteHandlerBuilder Command(RouteHandlerBuilder builder) => builder
        .Produces<ProductPaintMembershipMutationV1>(200).AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
        .ProducesErpNotFound(ProductPaintMembershipCodesV1.NotFound)
        .ProducesErpConflict(ProductPaintMembershipCodesV1.VersionConflict, ProductPaintMembershipCodesV1.IdempotencyConflict,
            ProductPaintMembershipCodesV1.PreviewMismatch, ProductPaintMembershipCodesV1.NoSource)
        .ProducesErpUnprocessableEntity(ProductPaintMembershipCodesV1.InvalidFile, ProductPaintMembershipCodesV1.Inactive)
        .WithIdempotencyContract(200, 400, 404, 409, 422);

    private static Task<IResult> GetCurrentAsync(IProductPaintMembershipServiceV1 service, HttpContext context, CancellationToken ct) =>
        RespondAsync(context, () => service.GetCurrentAsync(ct), ct);
    private static Task<IResult> GetCandidatesAsync(IProductPaintMembershipServiceV1 service, HttpContext context, CancellationToken ct) =>
        TryCandidateQuery(context.Request.Query, out var query)
            ? RespondAsync(context, () => service.SearchCandidatesAsync(query, ct), ct, allowCandidateQuery: true)
            : InvalidCandidateQuery(context);
    private static Task<IResult> InvalidCandidateQuery(HttpContext context)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        return Task.FromResult(Problem(400, ProductPaintMembershipCodesV1.InvalidRequest));
    }
    internal static bool TryCandidateQuery(IQueryCollection values, out ProductPaintMembershipCandidateQueryV1 query)
    {
        query = new(null);
        if (values.Any(x => x.Key is not ("q" or "limit") || x.Value.Count != 1)) return false;
        var limit = 25;
        if (values.TryGetValue("limit", out var raw) && (!int.TryParse(raw[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
            || raw[0] != limit.ToString(CultureInfo.InvariantCulture))) return false;
        return ProductPaintMembershipCandidateQueryV1.TryCreate(values.TryGetValue("q", out var q) ? q[0] : null, limit, out query);
    }
    private static Task<IResult> ValidateAsync(ValidateProductPaintMembershipImportV1 request,
        IProductPaintMembershipServiceV1 service, HttpContext context, CancellationToken ct) =>
        RespondAsync(context, () => service.ValidateImportAsync(request, ct), ct);
    private static Task<IResult> ApplyAsync(ApplyProductPaintMembershipImportV1 request, IProductPaintMembershipServiceV1 service,
        ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, HttpContext context, CancellationToken ct) =>
        Metadata(context, user, company, correlation) is { } metadata
            ? RespondAsync(context, () => service.ApplyImportAsync(request, metadata, ct), ct)
            : Task.FromResult<IResult>(Problem(400, ProductPaintMembershipCodesV1.InvalidRequest));
    private static Task<IResult> SetAsync(Guid productId, SetProductPaintMembershipV1 request, IProductPaintMembershipServiceV1 service,
        ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, HttpContext context, CancellationToken ct) =>
        Metadata(context, user, company, correlation) is { } metadata
            ? RespondAsync(context, () => service.SetAsync(productId, request, metadata, ct), ct)
            : Task.FromResult<IResult>(Problem(400, ProductPaintMembershipCodesV1.InvalidRequest));
    private static ProductPaintMembershipCommandMetadataV1? Metadata(HttpContext context, ICurrentUser user,
        ICompanyContext company, ICorrelationContext correlation)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)
            || !correlation.IsResolved || !context.Request.Headers.TryGetValue("Idempotency-Key", out var values)
            || values.Count != 1 || values[0]?.Trim() is not { Length: > 0 and <= 128 } key
            || key.Contains(',') || key.Any(char.IsControl)) return null;
        return new(company.CompanyId, user.UserId, correlation.CorrelationId, correlation.CausationId, key,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);
    }
    private static async Task<IResult> RespondAsync<T>(HttpContext context, Func<Task<T>> action, CancellationToken ct, bool allowCandidateQuery = false)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!allowCandidateQuery && context.Request.Query.Count != 0) return Problem(400, ProductPaintMembershipCodesV1.InvalidRequest);
        try
        {
            var result = await action();
            if (result is ProductPaintMembershipMutationV1 mutation) ReplayHeaders(context, mutation.IsReplay, mutation.OriginalCorrelationId);
            return Results.Ok(result);
        }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
        catch (ProductPaintMembershipException exception)
        { ReplayHeaders(context, exception.IsReplay, exception.OriginalCorrelationId); return Problem(exception.StatusCode, exception.Code); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return Problem(503, ProductPaintMembershipCodesV1.Unavailable); }
    }
    private static void ReplayHeaders(HttpContext context, bool replay, string? correlation)
    {
        if (!replay) return;
        context.Response.Headers[ErpOpenApiContract.IdempotencyReplayedHeaderName] = "true";
        context.Response.Headers[ErpOpenApiContract.OriginalCorrelationHeaderName] = correlation;
    }
    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status,
        title: status == 503 ? "Boya ürünleri şu anda kullanılamıyor." : "Boya ürünü işlemi tamamlanamadı.",
        extensions: new Dictionary<string, object?> { ["code"] = code });
}

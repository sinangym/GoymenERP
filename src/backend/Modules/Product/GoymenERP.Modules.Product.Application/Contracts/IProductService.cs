namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Every operation requires current server-resolved global authorization, including
/// reference reads and replay. No company-scoped grant satisfies this contract.
/// Product infrastructure implements this port using the accepted platform boundary;
/// no permissive default exists.
/// </summary>
public interface IProductService
{
    Task<ProductPage> GetPageAsync(ProductListQuery query, CancellationToken cancellationToken);
    Task<ProductCardView?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductCodeGroup>> GetCodeGroupsAsync(CancellationToken cancellationToken);
    Task<ProductMutationResponse> CreateDraftAsync(CreateProductDraftRequest request,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<ProductMutationResponse> EditDraftAsync(Guid id, EditProductDraftRequest request,
        string idempotencyKey, CancellationToken cancellationToken);
}

public sealed record ProductPage(IReadOnlyList<ProductDraftView> Items, string? NextCursor);
public sealed record ProductListQuery(string? Q = null, string? Lifecycle = null,
    Guid? GroupId = null, Guid? ClassId = null, Guid? FamilyId = null,
    string? Role = null, int PageSize = 50, string? Cursor = null);

public sealed record ProductMutationResponse(ProductCardView Card, bool IsReplay);

public static class ProductApiV1
{
    public const string BasePath = "/api/v1/products";
    public const string CodeGroupsPath = "/api/v1/products/code-groups";
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string ValidationFailed = "validation_failed";
    public const string InvalidCursor = "invalid_cursor";
    public const string NotFound = "not_found";
    public const string VersionConflict = "version_conflict";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string LifecycleConflict = "lifecycle_conflict";
}

public sealed class ProductOperationException(string code, int statusCode)
    : Exception("Ürün işlemi tamamlanamadı.")
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

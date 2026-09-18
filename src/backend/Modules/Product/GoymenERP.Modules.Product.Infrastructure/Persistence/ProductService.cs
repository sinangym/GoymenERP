using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// Implemented by the shared host: fresh authorization/replay, one transaction,
/// Product participant, canonical audit and global terminal; return only after
/// commit. Unique-key races roll back before a fresh resolver lookup. There is no
/// default runner and this port alone registers no runtime authority.
/// </summary>
public interface IProductDraftCommandRunner
{
    Task<ProductMutationResponse> RunAsync(ProductPreparedCommand prepared, CancellationToken cancellationToken);
}

public sealed class ProductService(ProductReader reader, ProductCommandGateway gateway,
    IProductDraftCommandRunner commands) : IProductService
{
    public Task<ProductPage> GetPageAsync(ProductListQuery query, CancellationToken cancellationToken) =>
        reader.GetPageAsync(query, cancellationToken);

    public Task<ProductCardView?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        reader.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<ProductCodeGroup>> GetCodeGroupsAsync(CancellationToken cancellationToken) =>
        reader.GetCodeGroupsAsync(cancellationToken);

    public Task<ProductReferenceOptions> GetReferenceOptionsAsync(ProductReferenceQuery query, CancellationToken cancellationToken) =>
        reader.GetReferenceOptionsAsync(query, cancellationToken);

    public async Task<ProductMutationResponse> CreateDraftAsync(CreateProductDraftRequest request,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var prepared = await gateway.PrepareCreateAsync(idempotencyKey, request, cancellationToken);
        // Even a prepared replay goes through the host so current authority and
        // original-correlation response metadata cannot be skipped here.
        return await commands.RunAsync(prepared, cancellationToken);
    }

    public async Task<ProductMutationResponse> EditDraftAsync(Guid id, EditProductDraftRequest request,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var prepared = await gateway.PrepareEditAsync(idempotencyKey, id, request, cancellationToken);
        return await commands.RunAsync(prepared, cancellationToken);
    }

    public async Task<ProductMutationResponse> ActivateAsync(Guid id, ActivateProductRequest request,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var prepared = await gateway.PrepareActivateAsync(idempotencyKey, id, request, cancellationToken);
        return await commands.RunAsync(prepared, cancellationToken);
    }
}

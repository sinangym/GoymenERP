using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Product.Application.Contracts;
namespace GoymenERP.Api.ProductRuntime;

internal sealed class UnavailableProductPaintMembershipService(ICompanyContext companyContext) : IProductPaintMembershipServiceV1, IProductPaintMembershipReaderV1
{
    public Task<ProductPaintMembershipCatalogV1> GetCurrentAsync(CancellationToken cancellationToken = default) => Fail<ProductPaintMembershipCatalogV1>(cancellationToken);
    public Task<ProductPaintMembershipImportPreviewV1> ValidateImportAsync(ValidateProductPaintMembershipImportV1 request, CancellationToken cancellationToken = default) => Fail<ProductPaintMembershipImportPreviewV1>(cancellationToken);
    public Task<ProductPaintMembershipMutationV1> ApplyImportAsync(ApplyProductPaintMembershipImportV1 request, ProductPaintMembershipCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => Fail<ProductPaintMembershipMutationV1>(cancellationToken);
    public Task<ProductPaintMembershipMutationV1> SetAsync(Guid productId, SetProductPaintMembershipV1 request, ProductPaintMembershipCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => Fail<ProductPaintMembershipMutationV1>(cancellationToken);
    public Task<ProductPaintMembershipReadV1> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new ProductPaintMembershipReadV1(ProductPaintMembershipReadOutcomeV1.Unavailable, Company(), productId, null, "product.paint-memberships.v1")); }
    public Task<ProductPaintMembershipCatalogV1> ListAsync(CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new ProductPaintMembershipCatalogV1(ProductPaintMembershipCatalogOutcomeV1.Unavailable, Company(), "0", [], "product.paint-memberships.v1")); }
    private Guid Company() => companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty;
    private static Task<T> Fail<T>(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromException<T>(new ProductPaintMembershipException(503, ProductPaintMembershipCodesV1.Unavailable)); }
}

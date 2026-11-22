namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// WP13 (DALGA_PLANI_v2 1.14): price-free identity (code and name) of a product already named by an authorized Purchasing order, for
/// the purchase order PDF. Measured under the producing Purchasing order read (<c>purchasing.orders.read</c>), never a global Product
/// read: it does not list products, select eligibility or grant Product read/write; the consumer repeats its own record read.
/// </summary>
public interface IProductPurchasingDocumentDisplayV1
{
    Task<ProductPurchasingDocumentDisplayV1> ReadAsync(Guid companyId, Guid productId, CancellationToken cancellationToken = default);
}

public enum ProductPurchasingDocumentDisplayOutcomeV1 { Available = 1, NotFound = 2, Unavailable = 3, Forbidden = 4 }

public sealed record ProductPurchasingDocumentDisplayV1(ProductPurchasingDocumentDisplayOutcomeV1 Outcome, Guid CompanyId,
    Guid ProductId, string? ProductVersion, string? Code, string? Name);

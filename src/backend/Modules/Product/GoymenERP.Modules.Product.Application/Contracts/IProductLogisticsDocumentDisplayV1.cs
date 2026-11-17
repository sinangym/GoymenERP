namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>Price-free identity of a product already named by an authorized Logistics record. This port does not
/// list products, select eligibility or grant Product read/write; the consumer repeats its actual record-scope read.</summary>
public interface IProductLogisticsDocumentDisplayV1
{
    Task<ProductLogisticsDocumentDisplayV1> ReadAsync(Guid companyId, Guid productId, CancellationToken cancellationToken = default);
}
public enum ProductLogisticsDocumentDisplayOutcomeV1 { Available = 1, NotFound = 2, Unavailable = 3, Forbidden = 4 }
public sealed record ProductLogisticsDocumentDisplayV1(ProductLogisticsDocumentDisplayOutcomeV1 Outcome, Guid CompanyId,
    Guid ProductId, string? ProductVersion, string? Code, string? Name);

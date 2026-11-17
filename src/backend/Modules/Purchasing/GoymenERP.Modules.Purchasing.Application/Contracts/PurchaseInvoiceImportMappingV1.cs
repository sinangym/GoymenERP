namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public sealed record PurchaseInvoiceImportReferenceV1(Guid Id, string Version, string Code, string Name);
public sealed record PurchaseInvoiceImportUnitV1(Guid UnitId, string Code, Guid ProfileId, string ProfileVersion, int Scale);
public sealed record PurchaseInvoiceImportCurrencyV1(Guid CurrencyId, string Code, Guid ProfileId, string ProfileVersion, int Scale);
public sealed record PurchaseInvoiceImportReferencesV1(Guid CompanyId, Guid ImportId, Guid DocumentId, int DocumentVersion,
    string DocumentSha256, DateOnly InvoiceDate, IReadOnlyList<PurchaseInvoiceImportUnitV1> Units,
    IReadOnlyList<PurchaseInvoiceImportCurrencyV1> Currencies, string Payable, string? FunctionalPayable, IReadOnlyList<PurchaseInvoiceImportReferenceV1>? ServiceTypes,
    IReadOnlyList<PurchaseInvoiceImportReferenceV1>? ExpenseTypes);
public sealed record PurchaseInvoiceImportSupplierRequestV1(PurchaseInvoiceImportPreviewRequestV1 Source, Guid SupplierPartyId, Guid CompanyAccountId);
public sealed record PurchaseInvoiceImportSupplierV1(Guid CompanyId, Guid ImportId, Guid SupplierPartyId, Guid CompanyAccountId,
    string PartyVersion, string AccountVersion);
public sealed record PurchaseInvoiceImportProductsRequestV1(PurchaseInvoiceImportPreviewRequestV1 Source, string? Q);
public sealed record PurchaseInvoiceImportProductV1(Guid ProductId, string ProductVersion, string Code, string Name,
    Guid UnitId, string UnitCode, Guid ProfileId, string ProfileVersion);
public sealed record PurchaseInvoiceImportProductsV1(Guid CompanyId, Guid ImportId,
    IReadOnlyList<PurchaseInvoiceImportProductV1> Items, bool SearchWindowFull);
public interface IPurchaseInvoiceImportProductSearchV1
{
    Task<(IReadOnlyList<PurchaseInvoiceImportProductV1> Items, bool SearchWindowFull)> SearchAsync(string? q, DateOnly on, CancellationToken token);
}
public interface IPurchaseInvoiceImportMappingV1
{
    Task<PurchaseInvoiceImportReferencesV1> ReferencesAsync(Guid importId, PurchaseInvoiceImportPreviewRequestV1 source, CancellationToken token = default);
    Task<PurchaseInvoiceImportSupplierV1> SupplierAsync(Guid importId, PurchaseInvoiceImportSupplierRequestV1 request, CancellationToken token = default);
    Task<PurchaseInvoiceImportProductsV1> ProductsAsync(Guid importId, PurchaseInvoiceImportProductsRequestV1 request, CancellationToken token = default);
}

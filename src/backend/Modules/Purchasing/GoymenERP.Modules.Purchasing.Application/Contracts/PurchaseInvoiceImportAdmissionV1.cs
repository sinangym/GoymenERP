namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>An explicit source-line mapping, not a product/code guess. IDs and versions are re-read from their owners.</summary>
public sealed record PurchaseInvoiceImportLineMappingV1(string SourceLineId, Guid LineId, Guid ProductId,
    string ExpectedProductVersion, Guid UnitId, string ItemType, string LinePurpose, string VatTaxCode,
    Guid? ServiceTypeId = null, string? ExpectedServiceTypeVersion = null,
    Guid? ExpenseTypeId = null, string? ExpectedExpenseTypeVersion = null);
public sealed record PurchaseInvoiceImportInstallmentV1(Guid Id, Guid OpenItemId, int Sequence, DateOnly DueOn,
    string Amount, string? FunctionalAmount);
public sealed record PurchaseInvoiceImportAdmissionRequestV1(PurchaseInvoiceImportPreviewRequestV1 Source,
    string ExpectedInvoiceVersion, Guid SupplierPartyId, Guid CompanyAccountId,
    string ExpectedPartyVersion, string ExpectedAccountVersion, Guid CurrencyId,
    IReadOnlyList<PurchaseInvoiceImportLineMappingV1> Lines,
    IReadOnlyList<PurchaseInvoiceImportInstallmentV1> Installments);

/// <summary>Records immutable, owner-validated source facts. Matching, variance approval, finalization and Finance remain separate commands.</summary>
public interface IPurchaseInvoiceImportAdmissionV1
{
    Task<PurchaseIncomingInvoiceOperationResultV1> RecordAsync(Guid importId,
        PurchaseInvoiceImportAdmissionRequestV1 request, PurchasingCommandMetadata metadata, CancellationToken token = default);
}

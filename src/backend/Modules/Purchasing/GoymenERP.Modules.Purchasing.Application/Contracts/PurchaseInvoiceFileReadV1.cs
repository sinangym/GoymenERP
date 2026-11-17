namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>ALS-D7: file facts, before owner mapping, acceptance matching or any financial admission.
/// XML is a UBL invoice; tabular exports use an explicit column map, never a guessed native vendor layout.</summary>
public enum PurchaseInvoiceFileFormatV1 { UblXml = 1, Csv = 2, Xlsx = 3 }
public sealed record PurchaseInvoiceFileReadOptionsV1(PurchaseInvoiceFileFormatV1 Format,
    IReadOnlyDictionary<string, string>? Columns = null, string? Worksheet = null,
    char Delimiter = ';', string DecimalSeparator = ".", string DateFormat = "yyyy-MM-dd");
public sealed record PurchaseInvoiceFileIssueV1(string Field, string Code);
public sealed record PurchaseInvoiceFileReadV1(string Sha256, PurchaseInvoiceFileFactsV1? Invoice,
    IReadOnlyList<PurchaseInvoiceFileIssueV1> Issues);
public sealed record PurchaseInvoiceFileIdentityV1(string Scheme, string Value);
public sealed record PurchaseInvoiceFileAmountV1(string Currency, string Value);
public sealed record PurchaseInvoiceFileTaxV1(string? Code, string? Percent, PurchaseInvoiceFileAmountV1 Amount,
    PurchaseInvoiceFileAmountV1? TaxableAmount, bool IsWithholding);
public sealed record PurchaseInvoiceFileAdjustmentV1(bool IsCharge, PurchaseInvoiceFileAmountV1 Amount, string? Reason);
public sealed record PurchaseInvoiceFileRateV1(string Purpose, string SourceCurrency, string TargetCurrency,
    string Rate, string? SourceBaseRate, string? TargetBaseRate, string? Operator, DateOnly? Date);
public sealed record PurchaseInvoiceFilePaymentV1(DateOnly? DueOn, PurchaseInvoiceFileAmountV1? Amount,
    DateOnly? InstallmentDueOn = null, string Source = "PaymentTerms");
public sealed record PurchaseInvoiceFileFunctionalAmountsV1(string Net, string Vat, string Withholding);
public sealed record PurchaseInvoiceFileLineV1(string Id, string? SellerCode, string? BuyerCode, string Description,
    string UnitCode, string Quantity, PurchaseInvoiceFileAmountV1 Net,
    IReadOnlyList<PurchaseInvoiceFileTaxV1> Taxes, IReadOnlyList<PurchaseInvoiceFileAdjustmentV1> Adjustments,
    PurchaseInvoiceFileFunctionalAmountsV1? FunctionalAmounts = null);
public sealed record PurchaseInvoiceFileFactsV1(string Number, Guid Ettn, DateOnly InvoiceDate, string InvoiceType,
    string Currency, IReadOnlyList<PurchaseInvoiceFileIdentityV1> SupplierIdentities,
    IReadOnlyList<PurchaseInvoiceFileIdentityV1> BuyerIdentities,
    IReadOnlyDictionary<string, PurchaseInvoiceFileAmountV1> Totals,
    IReadOnlyList<PurchaseInvoiceFileTaxV1> Taxes, IReadOnlyList<PurchaseInvoiceFileAdjustmentV1> Adjustments,
    IReadOnlyList<PurchaseInvoiceFileRateV1> Rates, IReadOnlyList<PurchaseInvoiceFilePaymentV1> Payments,
    IReadOnlyList<PurchaseInvoiceFileLineV1> Lines, PurchaseInvoiceFileFunctionalAmountsV1? FunctionalTotals = null);

public interface IPurchaseInvoiceFileReaderV1
{
    PurchaseInvoiceFileReadV1 Read(ReadOnlyMemory<byte> content, PurchaseInvoiceFileReadOptionsV1 options);
}

/// <summary>Technical parser bounds only; no business tolerance, monetary precision or tax rule.</summary>
public static class PurchaseInvoiceFileBoundsV1
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    public const int MaximumExpandedBytes = 32 * 1024 * 1024;
    public const int MaximumRows = 2000;
    public const int MaximumColumns = 128;
    public const int MaximumCellCharacters = 4000;
    public const int MaximumArchiveEntries = 512;
}

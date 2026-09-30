namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// Versioned application CSV carrier for FIN-F03 Option A. It mirrors the existing canonical
/// approved-source row exactly; company and document kind are server-owned and therefore absent.
/// This is not a Mikro export claim and does not approve a file or a party mapping.
/// </summary>
public static class FinanceOfficialInvoiceImportTemplateV1
{
    public const string Version = "finance-official-invoice-installment-csv.v1";
    public const string Header =
        "openItemId,sourceDocumentId,sourceInstallmentId,sourceVersion,partyId,companyAccountId,currencyId,amount,dueOn";
    public const int MaximumBytes = 1_048_576;
    public const int MaximumRows = 10_000;
}

public static class FinanceOfficialInvoiceImportCodesV1
{
    public const string FileEmpty = "finance_official_invoice_import_file_empty";
    public const string FileTooLarge = "finance_official_invoice_import_file_too_large";
    public const string Utf8Invalid = "finance_official_invoice_import_utf8_invalid";
    public const string HeaderInvalid = "finance_official_invoice_import_header_invalid";
    public const string RowShapeInvalid = "finance_official_invoice_import_row_shape_invalid";
    public const string RowLimitExceeded = "finance_official_invoice_import_row_limit_exceeded";
    public const string DateInvalid = "finance_official_invoice_import_date_invalid";
}

public sealed record FinanceOfficialInvoiceImportPreviewRequestV1(
    Guid BatchId,
    string BatchVersion,
    ReadOnlyMemory<byte> Utf8Csv);

public sealed record FinanceOfficialInvoiceImportIssueV1(
    int RowNumber,
    string Field,
    string Code);

/// <summary>
/// Source-only preview. A non-null batch means syntax/canonical shape passed; it does not mean the
/// real carrier, approval evidence or party/account mapping has been accepted. Admission performs
/// those owner checks again through the approved source port.
/// </summary>
public sealed record FinanceOfficialInvoiceImportPreviewV1(
    string TemplateVersion,
    string SourceSha256,
    ApprovedFinanceOpenItemSourceBatch? Batch,
    IReadOnlyList<FinanceOfficialInvoiceImportIssueV1> Issues)
{
    public bool IsValid => Batch is not null && Issues.Count == 0;
}

public interface IFinanceOfficialInvoiceImportParserV1
{
    FinanceOfficialInvoiceImportPreviewV1 Preview(
        FinanceContext context,
        FinanceOfficialInvoiceImportPreviewRequestV1 request);
}

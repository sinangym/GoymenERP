namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// FIN-S5 (BS-4, FNS-D28): the v2 controlled-file template for official sales invoice installments. It is the v1
/// template (<see cref="FinanceOfficialInvoiceImportTemplateV1"/>, unchanged) followed by the official document
/// columns the accounting export carries: official number, ETTN, and the functional (TL) tax base, VAT and
/// withholding. The document columns repeat on every installment row of the same <c>sourceDocumentId</c> and must
/// be identical there. The file reuses v1's byte and row limits, upload, maker≠checker approval and admission.
/// No formula check (tax base + VAT − withholding = payable) is applied: FNS-D17 did not choose one (NEEDS-SINAN).
/// </summary>
public static class FinanceOfficialSalesInvoiceImportTemplateV2
{
    public const string Version = "finance-official-invoice-installment-csv.v2";

    public const string DocumentColumns =
        "officialNo,ettn,taxBaseFunctionalAmount,vatFunctionalAmount,withholdingFunctionalAmount";

    public const string Header = FinanceOfficialInvoiceImportTemplateV1.Header + "," + DocumentColumns;
}

/// <summary>Stable issue codes of the v2 document columns and of FNS-D19 (D5) official identity admission.</summary>
public static class FinanceOfficialSalesInvoiceImportCodesV2
{
    public const string OfficialNoRequired = "official_no_required";
    public const string OfficialNoInvalid = "official_no_invalid";
    public const string EttnRequired = "ettn_required";
    public const string EttnInvalid = "ettn_invalid";
    public const string FunctionalAmountRequired = "functional_amount_required";
    public const string FunctionalAmountInvalid = "functional_amount_invalid";

    /// <summary>Two installment rows of the same source document carry different document columns.</summary>
    public const string DocumentInconsistent = "official_document_inconsistent";

    /// <summary>Two different source documents in one file carry the same official number.</summary>
    public const string OfficialNoDuplicateInFile = "official_no_duplicate_in_file";

    /// <summary>Two different source documents in one file carry the same ETTN.</summary>
    public const string EttnDuplicateInFile = "ettn_duplicate_in_file";

    /// <summary>FNS-D19: a not-cancelled receivable from another acceptance already carries this official number.</summary>
    public const string OfficialNoAlreadyOpen = "finance_official_no_already_open";

    /// <summary>FNS-D19: a not-cancelled receivable from another acceptance already carries this ETTN.</summary>
    public const string EttnAlreadyOpen = "finance_ettn_already_open";

    /// <summary>The stored v2 document facts of an approved batch cannot be read back consistently.</summary>
    public const string DocumentsUnavailable = "finance_official_documents_unavailable";
}

/// <summary>
/// One official sales invoice document of a v2 file. Amounts are canonical exact decimal text (DEC-FIN-014) in the
/// company's functional currency; the ETTN is canonical lowercase "D" GUID text.
/// </summary>
public sealed record FinanceOfficialSalesInvoiceImportDocumentV2(
    Guid SourceDocumentId,
    string OfficialNo,
    string Ettn,
    string TaxBaseFunctionalAmount,
    string VatFunctionalAmount,
    string WithholdingFunctionalAmount);

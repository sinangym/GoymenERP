using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>The parser's preview plus the v2 official documents of a valid file (empty for v1 and invalid files).</summary>
internal sealed record FinanceOfficialInvoiceImportParseResult(
    FinanceOfficialInvoiceImportPreviewV1 Preview,
    IReadOnlyList<FinanceOfficialSalesInvoiceImportDocumentV2> Documents);

/// <summary>
/// FIN-S5 v2 document columns: reads one row's columns with FNS-D17's field rules (official number required and at
/// most the D1 length, ETTN a canonical "D" GUID, the three functional amounts exact non-negative decimals) and
/// collects one document per source document. Rows of the same source document must repeat identical columns; two
/// different source documents of one file may not share an official number or an ETTN. The FNS-D64 tax formula is
/// only a preview warning (FinanceOfficialInvoiceImportParserV1), never a row issue, and a zero amount is accepted because no positivity rule was chosen.
/// </summary>
internal sealed class FinanceOfficialSalesInvoiceImportDocumentsV2
{
    private static readonly string[] Columns = FinanceOfficialSalesInvoiceImportTemplateV2.DocumentColumns.Split(',');
    private readonly List<FinanceOfficialSalesInvoiceImportDocumentV2> ordered = [];
    private readonly Dictionary<Guid, FinanceOfficialSalesInvoiceImportDocumentV2> byDocument = [];
    private readonly Dictionary<string, Guid> byOfficialNo = new Dictionary<string, Guid>(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> byEttn = new Dictionary<string, Guid>(StringComparer.Ordinal);

    public IReadOnlyList<FinanceOfficialSalesInvoiceImportDocumentV2> InOrder => ordered;

    /// <summary>Reads the document columns of one row; returns null when any column is invalid.</summary>
    public static FinanceOfficialSalesInvoiceImportDocumentV2? Read(
        Guid sourceDocumentId,
        ReadOnlySpan<string> fields,
        int rowNumber,
        ICollection<FinanceOfficialInvoiceImportIssueV1> issues,
        bool isV3 = false)
    {
        if (fields.Length != Columns.Length + (isV3 ? 2 : 0))
        {
            issues.Add(new FinanceOfficialInvoiceImportIssueV1(rowNumber, "row", FinanceOfficialInvoiceImportCodesV1.RowShapeInvalid));
            return null;
        }
        var before = issues.Count;
        var officialNo = fields[0];
        if (officialNo.Length == 0)
        {
            issues.Add(new FinanceOfficialInvoiceImportIssueV1(rowNumber, Columns[0], FinanceOfficialSalesInvoiceImportCodesV2.OfficialNoRequired));
        }
        else if (!string.Equals(officialNo, officialNo.Trim(), StringComparison.Ordinal)
            || officialNo.Length > FinanceDbContext.OfficialSalesInvoiceMaximumOfficialNoLength
            || officialNo.Any(char.IsControl))
        {
            issues.Add(new FinanceOfficialInvoiceImportIssueV1(rowNumber, Columns[0], FinanceOfficialSalesInvoiceImportCodesV2.OfficialNoInvalid));
        }

        var ettn = fields[1];
        var canonicalEttn = string.Empty;
        if (ettn.Length == 0)
        {
            issues.Add(new FinanceOfficialInvoiceImportIssueV1(rowNumber, Columns[1], FinanceOfficialSalesInvoiceImportCodesV2.EttnRequired));
        }
        else if (ettn.Length != FinanceDbContext.OfficialSalesInvoiceEttnLength
            || !Guid.TryParseExact(ettn, "D", out var parsedEttn) || parsedEttn == Guid.Empty)
        {
            issues.Add(new FinanceOfficialInvoiceImportIssueV1(rowNumber, Columns[1], FinanceOfficialSalesInvoiceImportCodesV2.EttnInvalid));
        }
        else
        {
            canonicalEttn = parsedEttn.ToString("D");
        }

        var taxBase = Amount(fields[2], rowNumber, Columns[2], issues);
        var vat = Amount(fields[3], rowNumber, Columns[3], issues);
        var withholding = Amount(fields[4], rowNumber, Columns[4], issues);
        DateOnly? invoiceDate = null;
        string? payableTl = null;
        if (isV3)
        {
            if (!DateOnly.TryParseExact(fields[5], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsedDate) || parsedDate == default)
                issues.Add(new FinanceOfficialInvoiceImportIssueV1(rowNumber, "invoiceDate", FinanceOfficialInvoiceImportCodesV1.DateInvalid));
            else invoiceDate = parsedDate;
            // FNS-D89: an absent TL amount stays unknown; an invalid supplied amount is refused.
            if (fields[6].Length > 0)
            {
                if (!FinanceExactDecimalText.TryParsePositive(fields[6], out var parsedPayable))
                    issues.Add(new FinanceOfficialInvoiceImportIssueV1(rowNumber, "payableFunctionalAmount", FinanceOfficialSalesInvoiceImportCodesV2.FunctionalAmountInvalid));
                else payableTl = parsedPayable.Text;
            }
        }
        return issues.Count == before
            ? new FinanceOfficialSalesInvoiceImportDocumentV2(
                sourceDocumentId, officialNo, canonicalEttn, taxBase, vat, withholding, invoiceDate, payableTl)
            : null;
    }

    /// <summary>Adds a valid row's document; reports a document that disagrees with the file's earlier facts.</summary>
    public void Add(
        FinanceOfficialSalesInvoiceImportDocumentV2 document,
        int rowNumber,
        ICollection<FinanceOfficialInvoiceImportIssueV1> issues)
    {
        if (byDocument.TryGetValue(document.SourceDocumentId, out var first))
        {
            if (!Same(first, document))
            {
                issues.Add(new FinanceOfficialInvoiceImportIssueV1(rowNumber, "sourceDocumentId", FinanceOfficialSalesInvoiceImportCodesV2.DocumentInconsistent));
            }
            return;
        }

        var refused = false;
        if (byOfficialNo.TryGetValue(document.OfficialNo, out var officialNoHolder)
            && officialNoHolder != document.SourceDocumentId)
        {
            issues.Add(new FinanceOfficialInvoiceImportIssueV1(rowNumber, Columns[0], FinanceOfficialSalesInvoiceImportCodesV2.OfficialNoDuplicateInFile));
            refused = true;
        }
        if (byEttn.TryGetValue(document.Ettn, out var ettnHolder) && ettnHolder != document.SourceDocumentId)
        {
            issues.Add(new FinanceOfficialInvoiceImportIssueV1(rowNumber, Columns[1], FinanceOfficialSalesInvoiceImportCodesV2.EttnDuplicateInFile));
            refused = true;
        }
        if (refused)
        {
            return;
        }

        byDocument.Add(document.SourceDocumentId, document);
        byOfficialNo.Add(document.OfficialNo, document.SourceDocumentId);
        byEttn.Add(document.Ettn, document.SourceDocumentId);
        ordered.Add(document);
    }

    private static bool Same(
        FinanceOfficialSalesInvoiceImportDocumentV2 left,
        FinanceOfficialSalesInvoiceImportDocumentV2 right) =>
        string.Equals(left.OfficialNo, right.OfficialNo, StringComparison.Ordinal)
        && string.Equals(left.Ettn, right.Ettn, StringComparison.Ordinal)
        && FinanceExactDecimalText.Equal(left.TaxBaseFunctionalAmount, right.TaxBaseFunctionalAmount)
        && FinanceExactDecimalText.Equal(left.VatFunctionalAmount, right.VatFunctionalAmount)
        && FinanceExactDecimalText.Equal(left.WithholdingFunctionalAmount, right.WithholdingFunctionalAmount)
        && left.InvoiceDate == right.InvoiceDate
        && (left.PayableFunctionalAmount is null && right.PayableFunctionalAmount is null
            || left.PayableFunctionalAmount is { } leftPayable && right.PayableFunctionalAmount is { } rightPayable
            && FinanceExactDecimalText.Equal(leftPayable, rightPayable));

    private static string Amount(
        string value,
        int rowNumber,
        string field,
        ICollection<FinanceOfficialInvoiceImportIssueV1> issues)
    {
        if (value.Length == 0)
        {
            issues.Add(new FinanceOfficialInvoiceImportIssueV1(rowNumber, field, FinanceOfficialSalesInvoiceImportCodesV2.FunctionalAmountRequired));
            return string.Empty;
        }
        if (!FinanceExactDecimalText.TryParseNonNegative(value, out var parsed))
        {
            issues.Add(new FinanceOfficialInvoiceImportIssueV1(rowNumber, field, FinanceOfficialSalesInvoiceImportCodesV2.FunctionalAmountInvalid));
            return string.Empty;
        }
        return parsed.Text;
    }
}

using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FNS-D76 / KLN-D104 pure comparison of the two official sales invoice sources: the approved manual (F-005 D1)
/// record and a CSV v2 import document (FIN-S5). The approved manual record wins for the same official number or
/// ETTN; this policy never changes either record, it only names the fields on which the two disagree so the admission
/// can write a warning to the audit trail. Amounts are compared as exact decimals (no tolerance); a value that cannot
/// be read exactly counts as a difference, never as equal. It performs no I/O.
/// </summary>
internal static class FinanceOfficialInvoiceSourceComparison
{
    public const string OfficialNoField = "officialNo";
    public const string EttnField = "ettn";
    public const string CustomerPartyField = "customerPartyId";
    public const string CurrencyField = "currencyId";
    public const string PayableAmountField = "payableAmount";
    public const string TaxBaseField = "taxBaseFunctionalAmount";
    public const string VatField = "vatFunctionalAmount";
    public const string WithholdingField = "withholdingFunctionalAmount";

    /// <summary>
    /// One imported document as the comparison sees it. The customer and currency are null when the document's
    /// installments disagree on them; the payable amount (the sum of the installments) is null when an installment
    /// amount cannot be read exactly.
    /// </summary>
    internal sealed record ImportedDocument(
        Guid BatchId,
        Guid SourceDocumentId,
        string OfficialNo,
        string Ettn,
        Guid? CustomerPartyId,
        Guid? CurrencyId,
        string? PayableAmount,
        string TaxBaseFunctionalAmount,
        string VatFunctionalAmount,
        string WithholdingFunctionalAmount);

    /// <summary>An approved manual record and the imported document it disagrees with, field names in a stable order.</summary>
    internal sealed record Difference(Guid ManualInvoiceId, ImportedDocument Imported, IReadOnlyList<string> Fields);

    /// <summary>The imported documents of an approved v2 batch, with the customer, currency and payable taken from its rows.</summary>
    public static IReadOnlyList<ImportedDocument> Imported(
        ApprovedFinanceOpenItemSourceBatch batch,
        IReadOnlyList<FinanceOfficialSalesInvoiceImportDocumentV2> documents)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(documents);
        var result = new List<ImportedDocument>(documents.Count);
        foreach (var document in documents)
        {
            var rows = batch.Rows.Where(row => row.SourceDocumentId == document.SourceDocumentId).ToArray();
            var parties = rows.Select(row => row.PartyId).Distinct().ToArray();
            var currencies = rows.Select(row => row.CurrencyId).Distinct().ToArray();
            result.Add(new ImportedDocument(
                batch.BatchId,
                document.SourceDocumentId,
                document.OfficialNo,
                document.Ettn,
                parties is [var party] ? party : null,
                currencies is [var currency] ? currency : null,
                Payable(rows),
                document.TaxBaseFunctionalAmount,
                document.VatFunctionalAmount,
                document.WithholdingFunctionalAmount));
        }
        return result;
    }

    /// <summary>
    /// Pairs every imported document with each approved manual record that carries the same official number or the
    /// same ETTN (exact, ordinal) and keeps only the pairs that disagree on at least one field.
    /// </summary>
    public static IReadOnlyList<Difference> Compare(
        IEnumerable<FinanceOfficialSalesInvoiceRow> approvedManual,
        IEnumerable<ImportedDocument> imported)
    {
        ArgumentNullException.ThrowIfNull(approvedManual);
        ArgumentNullException.ThrowIfNull(imported);
        var manual = approvedManual.ToArray();
        var differences = new List<Difference>();
        foreach (var document in imported)
        {
            foreach (var row in manual
                         .Where(row => string.Equals(row.OfficialNo, document.OfficialNo, StringComparison.Ordinal)
                             || string.Equals(row.Ettn, document.Ettn, StringComparison.Ordinal))
                         .OrderBy(row => row.Id))
            {
                var fields = Differences(row, document);
                if (fields.Count > 0)
                {
                    differences.Add(new Difference(row.Id, document, fields));
                }
            }
        }
        return differences;
    }

    /// <summary>The fields on which the approved manual record and the imported document disagree, in a stable order.</summary>
    public static IReadOnlyList<string> Differences(FinanceOfficialSalesInvoiceRow manual, ImportedDocument imported)
    {
        ArgumentNullException.ThrowIfNull(manual);
        ArgumentNullException.ThrowIfNull(imported);
        var fields = new List<string>();
        if (!string.Equals(manual.OfficialNo, imported.OfficialNo, StringComparison.Ordinal))
        {
            fields.Add(OfficialNoField);
        }
        if (!string.Equals(manual.Ettn, imported.Ettn, StringComparison.Ordinal))
        {
            fields.Add(EttnField);
        }
        if (imported.CustomerPartyId != manual.CustomerPartyId)
        {
            fields.Add(CustomerPartyField);
        }
        if (imported.CurrencyId != manual.CurrencyId)
        {
            fields.Add(CurrencyField);
        }
        if (!SameAmount(manual.PayableAmountText, imported.PayableAmount))
        {
            fields.Add(PayableAmountField);
        }
        if (!SameAmount(manual.TaxBaseFunctionalAmountText, imported.TaxBaseFunctionalAmount))
        {
            fields.Add(TaxBaseField);
        }
        if (!SameAmount(manual.VatFunctionalAmountText, imported.VatFunctionalAmount))
        {
            fields.Add(VatField);
        }
        if (!SameAmount(manual.WithholdingFunctionalAmountText, imported.WithholdingFunctionalAmount))
        {
            fields.Add(WithholdingField);
        }
        return fields;
    }

    private static string? Payable(IReadOnlyList<ApprovedFinanceOpenItemSourceRow> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }
        FinanceExactDecimalText? total = null;
        foreach (var row in rows)
        {
            if (!FinanceExactDecimalText.TryParsePositive(row.Amount, out var amount))
            {
                return null;
            }
            total = total is { } sum ? sum.Add(amount) : amount;
        }
        return total?.Text;
    }

    private static bool SameAmount(string? left, string? right) =>
        FinanceExactDecimalText.TryParseNonNegative(left, out var leftValue)
        && FinanceExactDecimalText.TryParseNonNegative(right, out var rightValue)
        && leftValue.CompareTo(rightValue) == 0;
}

using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FNS-D64: the one deterministic check (tax base + VAT − withholding = payable, functional TL amounts, exact decimal
/// text, no tolerance: "fark kuruş bile olsa"). The manual record, its reads, the CSV preview and the audit all call it.
/// It never blocks. A payable TL that is unknown (blank) or an amount that does not parse gives no verdict (null): the
/// field validators refuse unreadable amounts on their own, and an unknown payable is never treated as zero.
/// </summary>
internal static class FinanceOfficialInvoiceTaxConsistencyV1
{
    public static FinanceOfficialInvoiceTaxWarningV1? Check(string? taxBase, string? vat, string? withholding,
        string? payable, Guid? sourceDocumentId = null)
    {
        if (!FinanceExactDecimalText.TryParseNonNegative(taxBase, out var baseAmount)
            || !FinanceExactDecimalText.TryParseNonNegative(vat, out var vatAmount)
            || !FinanceExactDecimalText.TryParseNonNegative(withholding, out var withheld)
            || !FinanceExactDecimalText.TryParseNonNegative(payable, out var payableAmount))
            return null;
        var gross = baseAmount.Add(vatAmount);
        var left = payableAmount.Add(withheld);
        var comparison = left.CompareTo(gross);
        if (comparison == 0) return null;
        var difference = comparison > 0 ? left.Subtract(gross) : gross.Subtract(left);
        var expected = gross.CompareTo(withheld) >= 0 ? gross.Subtract(withheld).Text : "-" + withheld.Subtract(gross).Text;
        return new FinanceOfficialInvoiceTaxWarningV1(FinanceOfficialInvoiceTaxWarningV1.InconsistentCode, sourceDocumentId,
            expected, payableAmount.Text, difference.Text);
    }
}

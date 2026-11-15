using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FIN-S6 (F-005 D3): the Finance-owned answer to the Sales-declared <see cref="ISalesOfficialInvoiceReadPortV1"/> (API-35,
/// SAT-D89), over <see cref="IFinanceOfficialSalesInvoiceReadPortV1"/>. Only an approved record is the official fact
/// (F-005 D1): an approved record answers Found; a pending one, none, or a rejected / withdrawn one answers NotFound.
/// Finance's Unavailable, a fault, or an approved record whose amounts System.Decimal cannot hold exactly answers
/// Unavailable (the match then fails closed with 503) — never a rounded amount. The Sales facts carry no tax base: the
/// match does not compare it (FNS-D17, FNS-D21). The adapter carries no authority and creates nothing.
/// </summary>
internal sealed class FinanceSalesOfficialInvoiceReadPortV1(IFinanceOfficialSalesInvoiceReadPortV1 records)
    : ISalesOfficialInvoiceReadPortV1
{
    public async Task<SalesOfficialInvoiceAnswerV1> LookupAsync(Guid companyId, string officialNo, string ettn,
        CancellationToken cancellationToken = default)
    {
        FinanceOfficialSalesInvoiceReadAnswerV1? answer;
        try
        {
            answer = await records.LookupAsync(companyId, officialNo, ettn, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return SalesOfficialInvoiceAnswerV1.Unavailable;
        }

        if (answer is null)
        {
            return SalesOfficialInvoiceAnswerV1.Unavailable;
        }

        return answer.Outcome switch
        {
            FinanceOfficialSalesInvoiceReadOutcomeV1.NotFound or FinanceOfficialSalesInvoiceReadOutcomeV1.PendingApproval
                when answer.Invoice is null => SalesOfficialInvoiceAnswerV1.NotFound,
            FinanceOfficialSalesInvoiceReadOutcomeV1.Approved when answer.Invoice is { } invoice
                && invoice.CompanyId == companyId => Found(invoice),
            _ => SalesOfficialInvoiceAnswerV1.Unavailable,
        };
    }

    private static SalesOfficialInvoiceAnswerV1 Found(FinanceOfficialSalesInvoiceFactsV1 invoice) =>
        ExactDecimal(invoice.PayableAmount) is { } payable
        && ExactDecimal(invoice.PayableFunctionalAmount) is { } payableFunctional
        && ExactDecimal(invoice.VatFunctionalAmount) is { } vatFunctional
        && ExactDecimal(invoice.WithholdingFunctionalAmount) is { } withholdingFunctional
            ? new(SalesOfficialInvoiceOutcomeV1.Found, new SalesOfficialInvoiceFacts(
                invoice.OfficialNo,
                invoice.Ettn,
                invoice.CustomerPartyId,
                invoice.CurrencyId,
                payable,
                payableFunctional,
                vatFunctional,
                withholdingFunctional))
            : SalesOfficialInvoiceAnswerV1.Unavailable;

    /// <summary>
    /// The exact decimal text as a <see cref="decimal"/>, only when it is held without rounding (the record refused any other
    /// value, FIN-S6 re-checks on read); a negative or unreadable text is null.
    /// </summary>
    internal static decimal? ExactDecimal(string? text) =>
        FinanceExactDecimalText.TryParseNonNegative(text, out var exact)
        && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
        && FinanceExactDecimalText.Equal(value.ToString(CultureInfo.InvariantCulture), exact.Text)
            ? value
            : null;
}

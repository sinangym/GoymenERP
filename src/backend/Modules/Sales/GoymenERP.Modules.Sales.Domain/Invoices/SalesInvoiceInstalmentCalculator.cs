using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Sales.Domain.Invoices;

/// <summary>
/// The TL value of each instalment of a foreign-currency invoice (S5 API-36, SAT-D227). Every instalment but the last is its
/// invoice-currency amount converted at the invoice's own rate and rounded once, half away from zero, at the TL scale (SAT-D11);
/// the last instalment takes the remainder so that the instalments add up to the frozen TL payable total (SAT-D124, SAT-D226).
/// A TL invoice has no TL split (null). A last instalment whose TL remainder is negative (SAT-D233) or exactly zero (#66, 9 Nov 2026
/// extension of SAT-D233) is refused with <see cref="SalesRuleCodes.InvoiceInstalmentFunctionalInvalid"/>, never clamped.
/// </summary>
public static class SalesInvoiceInstalmentCalculator
{
    public static IReadOnlyList<decimal?> FunctionalAmounts(IReadOnlyList<SalesInvoiceInstalment> instalments, SalesInvoiceRateSnapshot? rate,
        Guid currencyId, Money? functionalPayable)
    {
        ArgumentNullException.ThrowIfNull(instalments);
        if (instalments.Count == 0)
        {
            throw new ArgumentException("Taksit listesi boş olamaz.", nameof(instalments));
        }

        if (rate is null)
        {
            return [.. instalments.Select(_ => (decimal?)null)];
        }

        if (functionalPayable is not { } payable)
        {
            throw new SalesRuleException(SalesRuleCodes.InvoiceInstalmentFunctionalInvalid, "Dövizli faturanın TL toplamı yok.");
        }

        var exchange = ExchangeRate.Of(currencyId, rate.FunctionalCurrencyId, rate.Rate);
        var result = new decimal?[instalments.Count];
        var running = Money.Zero(rate.FunctionalCurrencyId, rate.FunctionalScale);
        for (var index = 0; index < instalments.Count - 1; index++)
        {
            var converted = exchange.Convert(instalments[index].Amount, rate.FunctionalScale);
            result[index] = converted.Amount;
            running = running.Add(converted);
        }

        var last = payable.Subtract(running);
        if (last.Amount <= 0m)
        {
            throw new SalesRuleException(SalesRuleCodes.InvoiceInstalmentFunctionalInvalid, "Son taksidin TL tutarı sıfır veya negatif çıktı.");
        }

        result[^1] = last.Amount;
        return result;
    }
}

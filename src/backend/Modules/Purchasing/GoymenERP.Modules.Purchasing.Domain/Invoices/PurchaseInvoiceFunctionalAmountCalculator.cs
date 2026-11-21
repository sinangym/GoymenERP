using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Purchasing.Domain.Invoices;

/// <summary>ALS-D7: supplies missing functional amounts from the single official document rate.
/// Existing official functional amounts are supplied separately and never replaced here.
/// The source net determines the residual recipient; input order breaks equal-net ties.
/// No tax rate, exchange rate or tolerance is selected by this calculator.</summary>
public static class PurchaseInvoiceFunctionalAmountCalculator
{
    public sealed record Line(Guid Id, PurchaseInvoiceAmounts Amounts);
    public sealed record Result(IReadOnlyList<Line> Lines, PurchaseInvoiceAmounts Total);

    public static Result Calculate(IReadOnlyList<Line> source, decimal officialRate,
        Guid functionalCurrencyId, int functionalScale, PurchaseInvoiceAmounts? documentTotal = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count == 0 || source.Any(x => x is null || x.Id == Guid.Empty || x.Amounts is null)
            || source.Select(x => x.Id).Distinct().Count() != source.Count || officialRate <= 0
            || functionalScale is < 0 or > PurchaseIncomingInvoice.MaximumStoredScale)
            throw new ArgumentException("Resmî kur, para profili ve tekil kaynak satırları gerekir.");
        foreach (var line in source) line.Amounts.Validate(PurchaseIncomingInvoice.MaximumStoredScale);
        documentTotal?.Validate(functionalScale);
        decimal Round(decimal value) => Money.Round(checked(value * officialRate), functionalCurrencyId, functionalScale).Amount;
        var lines = source.Select(x => new Line(x.Id, new(Round(x.Amounts.Net), Round(x.Amounts.Vat), Round(x.Amounts.Withholding)))).ToArray();
        var total = Sum(lines);
        if (documentTotal is not null)
        {
            var recipient = Enumerable.Range(0, source.Count).OrderByDescending(i => source[i].Amounts.Net).First();
            var old = lines[recipient];
            var adjusted = new PurchaseInvoiceAmounts(checked(old.Amounts.Net + (documentTotal.Net - total.Net)),
                checked(old.Amounts.Vat + (documentTotal.Vat - total.Vat)),
                checked(old.Amounts.Withholding + (documentTotal.Withholding - total.Withholding)));
            // An inconsistent document cannot be repaired with negative money or hidden tax changes.
            adjusted.Validate(functionalScale);
            lines[recipient] = old with { Amounts = adjusted };
            total = documentTotal;
        }
        if (Sum(lines) != total) throw new InvalidOperationException("Resmî TL toplamı korunamadı.");
        return new(Array.AsReadOnly(lines), total);
    }

    private static PurchaseInvoiceAmounts Sum(IEnumerable<Line> lines) => new(lines.Sum(x => x.Amounts.Net),
        lines.Sum(x => x.Amounts.Vat), lines.Sum(x => x.Amounts.Withholding));
}

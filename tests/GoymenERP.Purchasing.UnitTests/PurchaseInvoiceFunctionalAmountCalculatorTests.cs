using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseInvoiceFunctionalAmountCalculatorTests
{
    private static readonly Guid Currency = Guid.Parse("357a3897-82cb-4d88-bcad-30f7cb7de10a");
    private static PurchaseInvoiceFunctionalAmountCalculator.Line Line(decimal net, decimal vat = 0m, decimal withholding = 0m)
        => new(Guid.NewGuid(), new(net, vat, withholding));

    [Fact]
    public void Missing_TL_total_uses_rounded_line_sums_and_preserves_official_foreign_facts()
    {
        var lines = new[] { Line(1.005m), Line(1.005m) };
        var before = lines.ToArray();
        var result = PurchaseInvoiceFunctionalAmountCalculator.Calculate(lines, 1m, Currency, 2);
        Assert.Equal(2.02m, result.Total.Net);
        Assert.All(result.Lines, x => Assert.Equal(1.01m, x.Amounts.Net));
        Assert.Equal(before, lines);
    }

    [Fact]
    public void Official_TL_total_is_exact_and_residual_goes_to_highest_net_first_on_a_tie()
    {
        var lines = new[] { Line(1m), Line(2m), Line(2m) };
        var total = new PurchaseInvoiceAmounts(5.01m, 0m, 0m);
        var result = PurchaseInvoiceFunctionalAmountCalculator.Calculate(lines, 1m, Currency, 2, total);
        Assert.Equal(new[] { 1m, 2.01m, 2m }, result.Lines.Select(x => x.Amounts.Net));
        Assert.Equal(total, result.Total);
        Assert.Equal(lines.Select(x => x.Id), result.Lines.Select(x => x.Id));
    }

    [Fact]
    public void Negative_residual_preserves_official_net_VAT_withholding_and_payable_totals()
    {
        var lines = new[] { Line(10m, 2m, 1m), Line(20m, 4m, 2m) };
        var total = new PurchaseInvoiceAmounts(59.99m, 11.99m, 5.99m);
        var result = PurchaseInvoiceFunctionalAmountCalculator.Calculate(lines, 2m, Currency, 2, total);
        Assert.Equal(new PurchaseInvoiceAmounts(39.99m, 7.99m, 3.99m), result.Lines[1].Amounts);
        Assert.Equal(total.Net, result.Lines.Sum(x => x.Amounts.Net));
        Assert.Equal(total.Vat, result.Lines.Sum(x => x.Amounts.Vat));
        Assert.Equal(total.Withholding, result.Lines.Sum(x => x.Amounts.Withholding));
        Assert.Equal(total.Payable, result.Lines.Sum(x => x.Amounts.Payable));
    }

    [Fact]
    public void Unrepresentable_document_or_invalid_official_rate_is_refused_without_mutating_source()
    {
        var lines = new[] { Line(1m) };
        Assert.Throws<ArgumentException>(() => PurchaseInvoiceFunctionalAmountCalculator.Calculate(lines, 0m, Currency, 2));
        Assert.Throws<ArgumentException>(() => PurchaseInvoiceFunctionalAmountCalculator.Calculate(lines, 1m, Currency, 2, new(0m, 1m, 2m)));
        Assert.Throws<ArgumentException>(() => PurchaseInvoiceFunctionalAmountCalculator.Calculate(lines, 1m, Currency, 2, new(1.001m, 0m, 0m)));
        Assert.Throws<OverflowException>(() => PurchaseInvoiceFunctionalAmountCalculator.Calculate([Line(2m)], decimal.MaxValue, Currency, 2));
        Assert.Equal(1m, lines[0].Amounts.Net);
    }
}

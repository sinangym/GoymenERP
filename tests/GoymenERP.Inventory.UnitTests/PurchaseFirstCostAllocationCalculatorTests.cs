using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Inventory.UnitTests;

public sealed class PurchaseFirstCostAllocationCalculatorTests
{
    [Fact]
    public void First_invoice_rounds_the_shipped_share_once_and_keeps_the_authoritative_residual_in_stock()
    {
        var result = Split(100.01m, 3m, 3m, 1m, new PurchaseFirstCostStockShare("stock", "A", 1, Q(2m)));
        Assert.Equal(33.34m, result.ConsumedTlAmount);
        Equal(66.67m, Assert.Single(result.Stock).TlValue);
        Conserves(100.01m, result);
    }

    [Fact]
    public void A_partial_invoice_without_explicit_physical_binding_preserves_the_stock_and_net_shipped_ratio()
    {
        var result = Split(1000m, 100m, 50m, 80m, new PurchaseFirstCostStockShare("stock", "A", 1, Q(20m)));
        Equal(40m, result.ConsumedQuantityKg);
        Assert.Equal(800m, result.ConsumedTlAmount);
        var stock = Assert.Single(result.Stock);
        Equal(10m, stock.InvoicedQuantityKg);
        Equal(200m, stock.TlValue);
        Equal(50m, result.InvoicedQuantityKg);
        Conserves(1000m, result);
    }

    [Fact]
    public void Stock_residual_goes_to_the_largest_kg_then_source_line_then_ordinal_location_code()
    {
        var result = Split(1m, 3m, 3m, 0m,
            new("later", "A", 2, Q(1m)), new("code-z", "Z", 1, Q(1m)), new("first", "A", 1, Q(1m)));
        Equal(.34m, result.Stock.Single(x => x.Key == "first").TlValue);
        Equal(.33m, result.Stock.Single(x => x.Key == "later").TlValue);
        Equal(.33m, result.Stock.Single(x => x.Key == "code-z").TlValue);
        Conserves(1m, result);
    }

    [Fact]
    public void Fully_shipped_non_minor_source_retains_the_exact_signed_rounding_residue_without_fictitious_stock()
    {
        var result = Split(33.3333m, 1m, 1m, 1m);
        Assert.Equal(33.33m, result.ConsumedTlAmount);
        Assert.Empty(result.Stock);
        Equal(.0033m, result.ConsumedRoundingResidue);
        Equal(33.3333m, Q(result.ConsumedTlAmount).Add(result.ConsumedRoundingResidue));
    }

    [Fact]
    public void Many_small_stock_shares_never_become_negative_while_the_total_is_conserved()
    {
        var shares = Enumerable.Range(0, 100).Select(i => new PurchaseFirstCostStockShare(
            i.ToString(), i.ToString("D3"), 1, Q(1m))).ToArray();
        var result = Split(.60m, 100m, 100m, 0m, shares);
        Assert.All(result.Stock, x => Assert.True(x.TlValue.Sign >= 0));
        Conserves(.60m, result);
    }

    [Fact]
    public void Unreconciled_or_duplicate_physical_sources_do_not_become_a_cost_allocation()
    {
        Assert.Throws<ArgumentException>(() => Split(100m, 100m, 50m, 80m, new PurchaseFirstCostStockShare("x", "A", 1, Q(10m))));
        Assert.Throws<ArgumentException>(() => Split(100m, 2m, 2m, 0m,
            new("x", "A", 1, Q(1m)), new("x", "B", 1, Q(1m))));
        Assert.Throws<ArgumentException>(() => Split(100m, 1m, 2m, 1m));
    }

    private static PurchaseFirstCostAllocation Split(decimal value, decimal accepted, decimal invoiced, decimal shipped,
        params PurchaseFirstCostStockShare[] stock) => PurchaseFirstCostAllocationCalculator.Allocate(
            Q(value), Q(accepted), Q(invoiced), Q(shipped), stock, 2);
    private static ExactCostRatio Q(decimal value) => ExactCostRatio.Of(value);
    private static void Equal(decimal expected, ExactCostRatio actual) => Assert.Equal(0, Q(expected).CompareTo(actual));
    private static void Conserves(decimal expected, PurchaseFirstCostAllocation actual) => Equal(expected,
        actual.Stock.Aggregate(Q(actual.ConsumedTlAmount), (sum, x) => sum.Add(x.TlValue)));
}

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

    [Fact]
    public void Two_half_cent_receipts_of_one_invoice_line_round_the_consumed_total_only_once()
    {
        var result = PurchaseFirstCostAllocationCalculator.AllocateLine([
            new(Q(.005m), Q(1m), Q(1m), Q(1m), []),
            new(Q(.005m), Q(1m), Q(1m), Q(1m), [])], 2);
        Assert.Equal(.01m, result.ConsumedTlAmount);
        Equal(.01m, result.ConsumedExactTlAmount);
        Equal(0m, result.ConsumedRoundingResidue);
        Assert.Empty(result.Stock);
    }

    [Fact]
    public void Invoice_line_aggregation_preserves_each_receipts_price_before_conserving_the_stock_residue()
    {
        var result = PurchaseFirstCostAllocationCalculator.AllocateLine([
            new(Q(10m), Q(2m), Q(2m), Q(1m), [new("A", "A", 1, Q(1m))]),
            new(Q(30m), Q(2m), Q(2m), Q(1m), [new("B", "B", 1, Q(1m))])], 2);
        Assert.Equal(20m, result.ConsumedTlAmount);
        Equal(5m, result.Stock.Single(x => x.Key == "A").TlValue);
        Equal(15m, result.Stock.Single(x => x.Key == "B").TlValue);
        Conserves(40m, result);
    }

    [Fact]
    public void Multiple_receipts_in_the_same_real_position_form_one_stock_pool_addition()
    {
        var result = PurchaseFirstCostAllocationCalculator.AllocateLine([
            new(Q(.005m), Q(1m), Q(1m), Q(0m), [new("A", "A", 1, Q(1m))]),
            new(Q(.005m), Q(1m), Q(1m), Q(0m), [new("A", "A", 1, Q(1m))])], 2);
        var position = Assert.Single(result.Stock);
        Equal(2m, position.InvoicedQuantityKg);
        Equal(.01m, position.TlValue);
        Conserves(.01m, result);
    }

    private static PurchaseFirstCostAllocation Split(decimal value, decimal accepted, decimal invoiced, decimal shipped,
        params PurchaseFirstCostStockShare[] stock) => PurchaseFirstCostAllocationCalculator.Allocate(
            Q(value), Q(accepted), Q(invoiced), Q(shipped), stock, 2);
    private static ExactCostRatio Q(decimal value) => ExactCostRatio.Of(value);
    private static void Equal(decimal expected, ExactCostRatio actual) => Assert.Equal(0, Q(expected).CompareTo(actual));
    private static void Conserves(decimal expected, PurchaseFirstCostAllocation actual) => Equal(expected,
        actual.Stock.Aggregate(Q(actual.ConsumedTlAmount), (sum, x) => sum.Add(x.TlValue)));
}

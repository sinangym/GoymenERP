using GoymenERP.Api.InventoryRuntime;
using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AK-3 (ALS-D119, ALS-D120): the weighed-kg receipt cost against the independent golden rows of
/// <c>docs/decisions/ADET_KG_FORMUL_GOLDEN_V1.md</c> §2 (A01–A14; row ids in each test). The golden travels with this train, so the
/// switch is on; the closed state (SAT-D298) stays pinned by parameter in <see cref="InventoryPurchaseReceiptCostHostTests"/>. The
/// calculator itself is exact and is checked with the existing 4-decimal average precision. A08/A13's pending TL cost is the
/// host adapter's (BS-1). SENTETIK.
/// </summary>
public sealed class PurchaseReceiptWeighedCostTests
{
    private static decimal Display(ExactCostRatio ratio) => ratio.Round(AverageCostPrecision.InternalScale);

    [Fact]
    public void The_weighed_kilogram_cost_rule_is_on_with_its_golden()
    {
        Assert.True(PurchaseReceiptWeighedCost.PurchaseCostGoldenAccepted);
    }

    [Fact]
    public void A09_a_partial_invoice_of_a_TL_receipt_prices_its_kilogram_share_at_the_receipts_own_cost()
    {
        // 40 of 100 pieces invoiced at the order price: 1 250,400 x 40 / 100 = 500,160 kg; 10 000,00 / 500,160 = 19,9936 — the
        // partial invoice does not change the receipt's cost.
        var share = GoymenERP.Modules.Purchasing.Domain.Orders.PurchasePieceKilograms.InvoicedKilogramShare(1250.4m, 40m, 100m);
        Assert.Equal(500.160m, share);
        var partial = PurchaseReceiptWeighedCost.PerWeighedKilogram(250m, 40m, share)!;
        Assert.Equal(0, partial.CompareTo(PurchaseReceiptWeighedCost.PerWeighedKilogram(250m, 100m, 1250.4m)!));
        Assert.Equal(19.9936m, Display(partial));
    }

    [Fact]
    public void ALS_D122_is_one_switch_Inventory_and_the_Purchasing_indicator_mirror_agree_and_are_on()
    {
        Assert.True(PurchaseInvoiceKilogramShare.PurchaseInvoiceRemainingShareGoldenAccepted);
        Assert.Equal(PurchaseInvoiceKilogramShare.PurchaseInvoiceRemainingShareGoldenAccepted,
            GoymenERP.Modules.Purchasing.Domain.Orders.PurchasePieceKilograms.RemainingShareGoldenAccepted);
    }

    [Fact]
    public void A01_the_cost_per_kg_is_the_paid_amount_over_the_weighed_kilograms_not_the_theoretical()
    {
        // R1: 100 pieces × 250,00 = 25 000,00 TL; weighed 1 250,400 kg → 19,9936 (theoretical 1 234,500 → 20,2511 OLMAZ).
        var ratio = PurchaseReceiptWeighedCost.PerWeighedKilogram(250m, 100m, 1250.4m)!;
        Assert.Equal(19.9936m, Display(ratio));
        Assert.NotEqual(20.2511m, Display(ratio));
    }

    [Fact]
    public void A02_a_half_at_the_fourth_decimal_rounds_once_away_from_zero()
    {
        // 246,89 TL (1 piece × 246,89) over 200,000 kg = 1.23445 → 1,2345 (1,2344 OLMAZ).
        Assert.Equal(1.2345m, Display(PurchaseReceiptWeighedCost.PerWeighedKilogram(246.89m, 1m, 200m)!));
    }

    [Theory]
    [InlineData("1234.5", "20.2511")] // A03: weighed = theoretical — the only case equal to the old rule.
    [InlineData("1250.4", "19.9936")] // A04: +1,288 % outside the 1 % tolerance — the cost still uses the weighed kg.
    [InlineData("1246.845", "20.0506")] // A05: exactly at the 1 % bound.
    public void A03_A04_A05_the_weighed_kilograms_decide_the_cost_whatever_the_tolerance_outcome(string weighed, string expected)
    {
        var ratio = PurchaseReceiptWeighedCost.PerWeighedKilogram(250m, 100m, decimal.Parse(weighed, System.Globalization.CultureInfo.InvariantCulture))!;
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Display(ratio));
    }

    [Fact]
    public void A06_the_exact_amount_enters_the_average_pool_and_only_the_result_is_rounded()
    {
        // Pool 500,000 kg @ 20,1234; entry 25 000,00 TL / 1 250,400 kg → (10061.7 + 25000) / 1750.4 = 20.0307.
        var entry = PurchaseReceiptWeighedCost.PerWeighedKilogram(250m, 100m, 1250.4m)!;
        var pool = new ExactAverageCostPool(ExactCostRatio.Of(500m), 20.1234m)
            .Add(ExactCostRatio.Of(1250.4m), entry.Multiply(1250.4m));
        Assert.Equal(20.0307m, pool.AverageCostPerKg);
    }

    [Fact]
    public void A07_a_foreign_currency_receipt_converts_the_exact_ratio_at_the_invoice_rate()
    {
        // 100 × 7,50 USD = 750,00 USD; invoice rate 34,5678 → 25 925,85 TL over 1 250,400 kg = 20,7340 TL/kg.
        var original = PurchaseReceiptWeighedCost.PerWeighedKilogram(7.5m, 100m, 1250.4m)!;
        Assert.Equal(20.7340m, Display(original.Multiply(34.5678m)));
    }

    [Fact]
    public void A10_a_supplier_return_leaves_at_the_receipts_exact_cost_and_the_rest_is_re_averaged()
    {
        // After A06: 1 750,400 kg @ 20,0307; return a 249,870 kg label of R1 → out 4 995,80 TL, remainder average 20,0369.
        var receipt = PurchaseReceiptWeighedCost.PerWeighedKilogram(250m, 100m, 1250.4m)!;
        var outValue = receipt.Multiply(249.87m);
        Assert.Equal(4995.80m, outValue.Round(2));
        var pool = new ExactAverageCostPool(ExactCostRatio.Of(1750.4m), 20.0307m).Remove(ExactCostRatio.Of(249.87m), outValue);
        Assert.Equal(20.0369m, pool.AverageCostPerKg);
    }

    [Fact]
    public void A13_a_partial_invoice_of_a_foreign_receipt_prices_its_kilogram_share_at_the_same_exact_rate()
    {
        // 40 of 100 pieces invoiced: 1 250,400 × 40 ÷ 100 = 500,160 kg; 40 × 7,50 × 34,5678 = 10 370,34 TL → 20,7340 TL/kg.
        var share = GoymenERP.Modules.Purchasing.Domain.Orders.PurchasePieceKilograms.InvoicedKilogramShare(1250.4m, 40m, 100m);
        Assert.Equal(500.160m, share);
        var partial = PurchaseReceiptWeighedCost.PerWeighedKilogram(7.5m, 40m, share)!;
        Assert.Equal(10370.34m, partial.Multiply(34.5678m).Multiply(share).Round(2));
        Assert.Equal(20.7340m, Display(partial.Multiply(34.5678m)));
    }

    [Fact]
    public void A14_the_entry_cost_is_never_rounded_before_it_enters_the_average()
    {
        // Pool 2,000 kg @ 10,0000; entry 1 000,01 TL / 7,000 kg → 113,3344 (a rounded 142,8586 entry would give 113,3345 OLMAZ).
        var entry = PurchaseReceiptWeighedCost.PerWeighedKilogram(1000.01m, 1m, 7m)!;
        var pool = new ExactAverageCostPool(ExactCostRatio.Of(2m), 10m).Add(ExactCostRatio.Of(7m), entry.Multiply(7m));
        Assert.Equal(113.3344m, pool.AverageCostPerKg);
    }

    [Theory]
    [InlineData(null)] // A11: no weighing
    [InlineData("0")] // A11: zero weighed kg
    public void A11_without_weighed_kilograms_there_is_no_cost_and_no_theoretical_fallback(string? weighed)
    {
        Assert.Null(PurchaseReceiptWeighedCost.PerWeighedKilogram(250m, 100m,
            weighed is null ? null : decimal.Parse(weighed, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void A12_a_zero_amount_receipt_and_a_fractional_piece_count_have_no_cost()
    {
        Assert.Null(PurchaseReceiptWeighedCost.PerWeighedKilogram(0m, 100m, 1250.4m));
        Assert.Null(PurchaseReceiptWeighedCost.PerWeighedKilogram(250m, 2.5m, 1250.4m));
    }
}

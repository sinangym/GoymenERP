using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class PurchaseFirstCostAttributionTests
{
    private static readonly Guid Currency = Guid.Parse("973751d9-e778-4bea-8386-b26fb524d910");
    private static readonly DateTimeOffset At = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private static AccountingInventoryCostRatioV1 R(decimal value) => AccountingInventoryCostAmountsV1.FromDecimal(value);
    private static PostedCostAttributionWeightV1 Weight(decimal kg, int seconds) => new(Guid.NewGuid(), Guid.NewGuid(), 1, R(kg), At.AddSeconds(seconds));

    [Fact]
    public void Purchase_preserves_unequal_actual_costs_and_uses_quantity_only_to_order_the_rounding_residue()
    {
        var lowCost = Weight(2, 0); var highCost = Weight(1, 1);
        Assert.True(PostedManufacturingCostAttributionCalculatorV1.TryCalculatePurchase(10m, Currency, 2, R(3),
            [new(lowCost, R(1.005m)), new(highCost, R(8.995m))], out var result, out var reason));
        Assert.Null(reason);
        Assert.Equal(1m, result!.Shares.Single(x => x.Source == lowCost).RoundedSignedAmount);
        Assert.Equal(9m, result.Shares.Single(x => x.Source == highCost).RoundedSignedAmount);
        Assert.Equal(R(1.005m), result.Shares.Single(x => x.Source == lowCost).ExactSignedAmount);
        Assert.Equal(-.01m, result.Shares.Single(x => x.Source == lowCost).SignedResidue);
    }

    [Fact]
    public void Two_half_cent_receipts_keep_one_cent_and_the_earlier_equal_weight_share_absorbs_only_the_negative_residue()
    {
        var early = Weight(1, 0); var late = Weight(1, 1);
        Assert.True(PostedManufacturingCostAttributionCalculatorV1.TryCalculatePurchase(.01m, Currency, 2, R(2),
            [new(late, R(.005m)), new(early, R(.005m))], out var result, out _));
        Assert.Equal(0m, result!.Shares.Single(x => x.Source == early).RoundedSignedAmount);
        Assert.Equal(.01m, result.Shares.Single(x => x.Source == late).RoundedSignedAmount);
        Assert.Equal(R(.01m), result.Shares.Aggregate(R(0), (sum, x) => AccountingInventoryCostAmountsV1.Add(sum, R(x.RoundedSignedAmount))));
        Assert.All(result.Shares, x => Assert.Equal(R(.005m), x.ExactSignedAmount));
    }

    [Fact]
    public void A_negative_remainder_cannot_make_a_positive_purchase_share_negative_and_continues_in_original_order()
    {
        var weights = Enumerable.Range(0, 100).Select(x => Weight(1, x)).ToArray();
        Assert.True(PostedManufacturingCostAttributionCalculatorV1.TryCalculatePurchase(.60m, Currency, 2, R(100),
            weights.Reverse().Select(x => new PostedCostAttributionExactSourceV1(x, R(.006m))).ToArray(), out var result, out _));
        foreach (var weight in weights.Take(40)) Assert.Equal(0m, result!.Shares.Single(x => x.Source == weight).RoundedSignedAmount);
        foreach (var weight in weights.Skip(40)) Assert.Equal(.01m, result!.Shares.Single(x => x.Source == weight).RoundedSignedAmount);
    }

    [Fact]
    public void A_separately_approved_rounded_zero_keeps_nonzero_exact_evidence_without_a_fabricated_money_share()
    {
        var first = Weight(1, 0); var second = Weight(1, 1);
        Assert.True(PostedManufacturingCostAttributionCalculatorV1.TryCalculatePurchase(0, Currency, 2, R(2),
            [new(first, R(.002m)), new(second, R(.002m))], out var value, out _));
        Assert.All(value!.Shares, x => { Assert.Equal(0m, x.RoundedSignedAmount); Assert.Equal(R(.002m), x.ExactSignedAmount); });
    }

    [Fact]
    public void A_purchase_candidate_keeps_the_actual_returned_quantity_invoice_fraction_and_exact_receipt_cost()
    {
        var source = Source(); var candidates = PurchaseFirstCostCandidatesV1.Build(source); var candidate = Assert.Single(candidates);
        Assert.Equal(R(.5m), candidate.InvoicedRootShare); Assert.Equal(R(3m), candidate.InvoicedNetQuantityKg);
        Assert.Equal(R(.004m), candidate.ExactTlAmount); Assert.Single(candidate.Shipment.Returns);
        Assert.Equal(source.Receipts[0].AcceptanceId, candidate.AcceptanceId);
        Assert.Equal(source.Receipts[0].RootLabelId, candidate.RootLabelId);
        Assert.Equal(candidate.Fingerprint, AccountingInventoryCostSourceValidationV1.Sha256(JsonSerializer.Serialize(
            candidate with { Fingerprint = "" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Assert.Throws<AccountingStoreException>(() => PurchaseFirstCostCandidatesV1.Build(source with {
            Receipts = [source.Receipts[0] with { ConsumedQuantityKg = R(5) }] }));
        Assert.Throws<AccountingStoreException>(() => PurchaseFirstCostCandidatesV1.Build(source with {
            Receipts = [source.Receipts[0] with { Shipments = [candidate.Shipment with { Returns = [] }] }] }));
    }

    private static PurchaseFirstCostLineageV1 Source()
    {
        var company = Guid.NewGuid(); var product = Guid.NewGuid(); var line = Guid.NewGuid(); var movement = Guid.NewGuid();
        Assert.True(AccountingInventoryCostRoundingV1Rules.TryRound(R(.004m), Currency, 2, out _, out var rounding));
        var shipment = new PurchaseFirstCostShipmentV1(Guid.NewGuid(), movement, 1, Guid.NewGuid(), product, Guid.NewGuid(), 10m,
            R(1m), R(6m), At, null, [new(Guid.NewGuid(), Guid.NewGuid(), 1, movement, 4m, At.AddSeconds(1))], "{\"synthetic\":true}");
        return new(company, Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), line, 1, product,
            AccountingInventoryCostTraceV1.PurchaseFirstLineComponent(line), new string('A', 64),
            new(Currency, Guid.NewGuid(), 1, 2, rounding!),
            [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), R(.5m), R(3m), R(.004m), new string('B', 64), [shipment])]);
    }
}

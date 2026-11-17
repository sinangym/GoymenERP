using GoymenERP.Modules.Purchasing.Domain.Expenses;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseAdditionalCostTests
{
    [Fact]
    public void The_lump_sum_expense_uses_shared_line_tax_rounding_and_preserves_the_official_currency_profile_scale()
    {
        var value = Cost();
        Assert.Equal((20m, 120.01m), PurchaseAdditionalCost.Amounts(value.CostId, 100.01m, 20m, 2));
        Assert.Equal(value, PurchaseAdditionalCost.Record(value));
        Assert.Throws<ArgumentException>(() => PurchaseAdditionalCost.Record(value with { VatAmount = 20.002m }));
        Assert.Throws<ArgumentException>(() => PurchaseAdditionalCost.Record(value with { NetAmount = 100.001m }));
    }
    [Fact]
    public void A_previously_untyped_expense_keeps_its_actual_description_and_does_not_invent_a_seed_type()
    {
        var value = Cost();
        Assert.Null(value.ExpenseType); Assert.Equal("SENTETIK nakliye", value.Description);
        Assert.Equal(value, PurchaseAdditionalCost.Record(value));
        Assert.Throws<ArgumentException>(() => PurchaseAdditionalCost.Record(value with { Description = " " }));
        Assert.Throws<ArgumentException>(() => PurchaseAdditionalCost.Record(value with { TypeSourceFingerprint = "empty" }));
    }
    [Fact]
    public void A_receipt_reference_requires_the_actual_source_line_and_cannot_be_a_standalone_artificial_receipt_identity()
    {
        var value = Cost();
        Assert.Throws<ArgumentException>(() => PurchaseAdditionalCost.Record(value with { AcceptanceId = Guid.NewGuid() }));
        Assert.Throws<ArgumentException>(() => PurchaseAdditionalCost.Record(value with { AcceptanceLineId = Guid.NewGuid() }));
        var bound = value with { AcceptanceId = Guid.NewGuid(), AcceptanceLineId = Guid.NewGuid() };
        Assert.Equal(bound, PurchaseAdditionalCost.Record(bound));
    }
    private static PurchaseAdditionalCostState Cost() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, "SENTETIK-SIP-1",
        PurchaseAdditionalCostSourceKind.GoodsOrder, null, null, Guid.NewGuid(), Guid.NewGuid(), "1", "1", new(2026, 11, 17),
        new(Guid.NewGuid(), "TRY", Guid.NewGuid(), 1, 2, 4), "SENTETIK nakliye", null, new string('a', 64),
        100.01m, new("SENTETIK-20", 1, new(2026, 11, 17), 20m), 20m, 120.01m, "SENTETIK-actor", "SENTETIK belge", new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
}

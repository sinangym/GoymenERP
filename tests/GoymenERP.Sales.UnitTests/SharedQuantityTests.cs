using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Sales.Domain;
using SalesQuantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Sales.Domain.Quantities.SalesQuantityRefusals>;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// TEK-02: one quantity rule for Sales, Purchasing and Logistics. Each module keeps its own type and its own
/// stable refusal codes; the arithmetic is the shared <see cref="Quantity{TRefusals}"/>. SENTETIK.
/// </summary>
public sealed class SharedQuantityTests
{
    private sealed class TestRefusals : IQuantityRuleRefusals
    {
        public static Exception UnitMismatch(string message) => new InvalidOperationException("unit:" + message);

        public static Exception BelowZero(string message) => new InvalidOperationException("below:" + message);
    }

    [Fact]
    public void Kilograms_carry_three_decimals_and_pieces_are_whole_never_rounded()
    {
        Assert.Equal(3, Quantity<TestRefusals>.KilogramScale);
        Assert.Equal((3, 0), (Quantity<TestRefusals>.AllowedScale(QuantityUnit.Kilogram), Quantity<TestRefusals>.AllowedScale(QuantityUnit.Piece)));
        Assert.Equal(12.5m, Quantity<TestRefusals>.Of(12.5000m, QuantityUnit.Kilogram).Value);
        Assert.Equal("12.5 Kilogram", Quantity<TestRefusals>.Of(12.5000m, QuantityUnit.Kilogram).ToString());
        Assert.Throws<ArgumentException>(() => Quantity<TestRefusals>.Of(12.5001m, QuantityUnit.Kilogram));
        Assert.Equal(100m, Quantity<TestRefusals>.Of(100.00m, QuantityUnit.Piece).Value);
        Assert.Throws<ArgumentException>(() => Quantity<TestRefusals>.Of(100.5m, QuantityUnit.Piece));
        Assert.Throws<ArgumentOutOfRangeException>(() => Quantity<TestRefusals>.Of(-0.001m, QuantityUnit.Kilogram));
        Assert.Throws<ArgumentOutOfRangeException>(() => Quantity<TestRefusals>.Positive(0m, QuantityUnit.Piece));
        Assert.Throws<ArgumentException>(() => Quantity<TestRefusals>.Of(1m, (QuantityUnit)99));
        Assert.Equal(4, Quantity<TestRefusals>.SignificantDecimals(12.50010m));
    }

    [Fact]
    public void The_owning_module_supplies_the_refusal_and_the_shared_message_passes_through()
    {
        var kg = Quantity<TestRefusals>.Of(1m, QuantityUnit.Kilogram);
        var piece = Quantity<TestRefusals>.Of(1m, QuantityUnit.Piece);
        Assert.Equal("unit:Miktar birimleri farklı; kg ile adet birlikte işlenemez.",
            Assert.Throws<InvalidOperationException>(() => kg.Add(piece)).Message);
        Assert.Equal("below:Çıkarılan miktar eldeki miktardan büyük olamaz.",
            Assert.Throws<InvalidOperationException>(() => kg.Subtract(Quantity<TestRefusals>.Of(2m, QuantityUnit.Kilogram))).Message);
    }

    [Fact]
    public void Sales_keeps_its_stable_codes_on_the_one_rule()
    {
        var salesKg = SalesQuantity.Of(1m, QuantityUnit.Kilogram);
        Assert.Equal(SalesRuleCodes.UnitMismatch,
            Assert.Throws<SalesRuleException>(() => salesKg.CompareTo(SalesQuantity.Of(1m, QuantityUnit.Piece))).Code);
        Assert.Equal(SalesRuleCodes.QuantityBelowZero,
            Assert.Throws<SalesRuleException>(() => salesKg.Subtract(SalesQuantity.Of(1.001m, QuantityUnit.Kilogram))).Code);

        // Purchasing and Logistics pin their own codes on the same rule in their QuantityTests.
    }
}

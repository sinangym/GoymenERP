using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryReceiptCostEvidenceAdmissionTests
{
    [Theory]
    [InlineData(0, "1", "1")]
    [InlineData(10, "20", "1")]
    [InlineData(-1, "-1", "1")]
    public void Contradictory_or_negative_receipt_cost_never_admits_a_known_stamp(decimal scalar, string numerator, string denominator)
        => Assert.Throws<InvalidOperationException>(() => InventoryStore.AdmittedReceiptUnit(scalar, new(numerator, denominator), null));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void An_unavailable_reason_cannot_be_combined_with_any_cost_value(bool scalar, bool ratio)
        => Assert.Throws<InvalidOperationException>(() => InventoryStore.AdmittedReceiptUnit(scalar ? 1m : null,
            ratio ? new("1", "1") : null, "source_missing"));

    [Fact]
    public void Exact_ratio_only_legacy_scalar_and_real_unavailable_remain_distinct()
    {
        Assert.Null(InventoryStore.AdmittedReceiptUnit(null, null, "source_missing"));
        Assert.Null(InventoryStore.AdmittedReceiptUnit(null, null, null));
        Assert.Equal("1", InventoryStore.AdmittedReceiptUnit(1m, null, null)!.Numerator);
        var third = InventoryStore.AdmittedReceiptUnit(null, new InventoryExactCostRatioV1("1", "3"), null)!;
        Assert.False(third.TryToDecimal(out _));
        Assert.Equal("3", third.Denominator);
        Assert.Equal(0, InventoryStore.AdmittedReceiptUnit(0m, new("0", "1"), null)!.Sign);
    }
}

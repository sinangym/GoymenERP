using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// KLN-D99 (SINAN-CEVAP 34): the label list for a reservation is ordered closest to the requested kilograms first, ties by
/// label code, and the requested quantity is read strictly from query text. Nothing here selects a label. SENTETIK.
/// </summary>
public sealed class InventoryLabelQuantityProximityTests
{
    [Fact]
    public void Labels_are_ordered_closest_to_the_requested_kilograms_first_with_ties_by_label_code()
    {
        var requested = Kilograms.Positive(55m);
        var labels = new (decimal UnreservedKg, string Code)[]
        {
            (100m, "SYN-A"),
            (60m, "SYN-C"),
            (50m, "SYN-B"),
            (55.001m, "SYN-D"),
            (10m, "SYN-E"),
        };

        var ordered = labels
            .OrderBy(label => label, Comparer<(decimal UnreservedKg, string Code)>.Create((left, right) =>
                InventoryLabelQuantityProximityV1.Compare(
                    left.UnreservedKg, left.Code, right.UnreservedKg, right.Code, requested)))
            .Select(label => label.Code)
            .ToArray();

        Assert.Equal(new[] { "SYN-D", "SYN-B", "SYN-C", "SYN-A", "SYN-E" }, ordered);
    }

    [Fact]
    public void A_label_below_and_a_label_above_at_the_same_distance_are_equally_close()
    {
        var requested = Kilograms.Positive(20m);

        Assert.True(InventoryLabelQuantityProximityV1.Compare(15m, "SYN-A", 25m, "SYN-B", requested) < 0);
        Assert.True(InventoryLabelQuantityProximityV1.Compare(25m, "SYN-A", 15m, "SYN-B", requested) < 0);
        Assert.True(InventoryLabelQuantityProximityV1.Compare(15m, "SYN-B", 25m, "SYN-A", requested) > 0);
        Assert.Equal(0, InventoryLabelQuantityProximityV1.Compare(15m, "SYN-A", 15m, "SYN-A", requested));
    }

    [Theory]
    [InlineData("12.5", "12.5")]
    [InlineData("12.500", "12.5")]
    [InlineData("007", "7")]
    [InlineData("0.001", "0.001")]
    [InlineData("1000000", "1000000")]
    public void A_positive_kilogram_text_is_read_exactly(string text, string expected)
    {
        Assert.True(InventoryLabelQuantityProximityV1.TryParseRequestedKg(text, out var requested));
        Assert.Equal(expected, requested.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0.000")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.2345")]
    [InlineData("1,5")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1e3")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1.2.3")]
    [InlineData("123456789012345678901234567890123")]
    public void A_malformed_or_non_positive_kilogram_text_is_refused_not_rounded(string? text)
    {
        Assert.False(InventoryLabelQuantityProximityV1.TryParseRequestedKg(text, out var requested));
        Assert.Equal(Kilograms.Zero, requested);
    }
}

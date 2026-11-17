using GoymenERP.Modules.Settings.Application;

namespace GoymenERP.Settings.UnitTests;

public sealed class PurchaseInvoiceVariancePolicyRulesV1Tests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData("0.000001", "0.000001")]
    [InlineData("1.230000", "1.23")]
    [InlineData("999999999999999999.999999", "999999999999999999.999999")]
    public void Explicit_nonnegative_values_preserve_exact_storage_precision(string text, string canonical)
    {
        Assert.True(PurchaseInvoiceVariancePolicyRulesV1.TryValue(text, out var value));
        Assert.Equal(canonical, PurchaseInvoiceVariancePolicyRulesV1.Text(value));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("-0.01")] [InlineData("+1")] [InlineData("01")]
    [InlineData("1,2")] [InlineData("1e2")] [InlineData("1.")] [InlineData(".1")]
    [InlineData(" 1")] [InlineData("0.0000001")] [InlineData("1000000000000000000")]
    public void Invalid_or_unrepresentable_values_are_refused_without_rounding(string? text) =>
        Assert.False(PurchaseInvoiceVariancePolicyRulesV1.TryValue(text, out _));

    [Theory]
    [InlineData("0", true)] [InlineData("1", true)] [InlineData("01", false)] [InlineData("-1", false)]
    [InlineData("9223372036854775807", false)] [InlineData("1.0", false)]
    public void Expected_version_is_canonical_and_leaves_room_for_the_next_version(string text, bool valid) =>
        Assert.Equal(valid, PurchaseInvoiceVariancePolicyRulesV1.TryVersion(text, out _));
}

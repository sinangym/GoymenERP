using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

public sealed class FinancePolicyRulesV1Tests
{
    [Theory]
    [InlineData("100000", "100000", true)]
    [InlineData("99999.999999999999999999999999", "100000", false)]
    [InlineData("100000.000000000000000000000001", "100000", true)]
    [InlineData("000100.0000", "100", true)]
    [InlineData("0", "0", true)]
    public void Matrix_uses_the_explicit_inclusive_threshold_without_rounding(string amount, string threshold, bool expected) =>
        Assert.Equal(expected, FinancePolicyRulesV1.MatrixThresholdReached(amount, threshold));

    [Theory]
    [InlineData("5", "5", false)]
    [InlineData("5.000000000000000000000000000001", "5", true)]
    [InlineData("0", "0", false)]
    [InlineData("0.000000000000000000000000000001", "0", true)]
    public void Cash_difference_preserves_API33_above_threshold_semantics(string difference, string threshold, bool expected) =>
        Assert.Equal(expected, FinancePolicyRulesV1.CashDifferenceThresholdExceeded(difference, threshold));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1e3")]
    [InlineData("1,5")]
    [InlineData("-1")]
    [InlineData(".5")]
    [InlineData("5.")]
    public void Missing_or_malformed_amount_is_not_a_zero_threshold(string? amount) =>
        Assert.False(FinancePolicyRulesV1.TryAmount(amount, out _));

    [Fact]
    public void Forward_windows_preserve_the_last_active_day_and_retirement_boundary()
    {
        var today = new DateOnly(2026, 11, 17);
        Assert.False(FinancePolicyRulesV1.IsWindow(today.AddDays(-1), null, today));
        Assert.True(FinancePolicyRulesV1.IsWindow(today, null, today));
        Assert.False(FinancePolicyRulesV1.IsEffective(SettingsFinancePolicyStateV1.Draft, today, null, null, today));
        Assert.True(FinancePolicyRulesV1.IsEffective(SettingsFinancePolicyStateV1.Published, today, today, null, today));
        Assert.False(FinancePolicyRulesV1.IsEffective(SettingsFinancePolicyStateV1.Retired, today, null, today, today));
        Assert.True(FinancePolicyRulesV1.IsEffective(SettingsFinancePolicyStateV1.Retired, today, null, today.AddDays(1), today));
        Assert.False(FinancePolicyRulesV1.Overlaps(today, null, today.AddDays(1), today.AddDays(1), null));
        Assert.True(FinancePolicyRulesV1.Overlaps(today, null, today.AddDays(1), today, today));
        Assert.False(FinancePolicyRulesV1.Overlaps(today, null, today, today, null));
    }
}

using GoymenERP.Modules.Manufacturing.Domain.Lineage;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-S21 (URT-D62): an Ayarlar output label format holds exactly one number placeholder and nothing the system could
/// fill with an input label number; a label code is bounded, never blank and never carries a control character.
/// </summary>
public sealed class ProductionOutputLabelCodesTests
{
    [Theory]
    [InlineData("{n}")]
    [InlineData("CK-{n}")]
    [InlineData("CK-{n}-A")]
    [InlineData("{n}-2026")]
    public void A_format_with_exactly_one_number_placeholder_is_valid(string format) =>
        Assert.True(ProductionOutputLabelCodes.IsValidFormat(format));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CK-")]
    [InlineData("CK-{N}")]
    [InlineData("CK-{n}-{n}")]
    [InlineData("CK-{input}")]
    [InlineData("CK-{n")]
    public void A_format_without_exactly_one_number_placeholder_is_not_valid(string? format) =>
        Assert.False(ProductionOutputLabelCodes.IsValidFormat(format));

    [Fact]
    public void A_format_longer_than_its_bound_or_with_a_control_character_is_not_valid()
    {
        var longest = new string('K', ProductionOutputLabelCodes.MaximumFormatLength
                                      - ProductionOutputLabelCodes.NumberPlaceholder.Length)
                      + ProductionOutputLabelCodes.NumberPlaceholder;

        Assert.True(ProductionOutputLabelCodes.IsValidFormat(longest));
        Assert.False(ProductionOutputLabelCodes.IsValidFormat("K" + longest));
        Assert.False(ProductionOutputLabelCodes.IsValidFormat("CK-\t{n}"));
    }

    [Fact]
    public void A_code_is_valid_only_when_not_blank_within_its_bound_and_without_a_control_character()
    {
        Assert.True(ProductionOutputLabelCodes.IsValidCode("SENTETIK-CIKTI-1"));
        Assert.True(ProductionOutputLabelCodes.IsValidCode(new string('K', ProductionOutputLabelCodes.MaximumCodeLength)));
        Assert.False(ProductionOutputLabelCodes.IsValidCode(null));
        Assert.False(ProductionOutputLabelCodes.IsValidCode(""));
        Assert.False(ProductionOutputLabelCodes.IsValidCode("  "));
        Assert.False(ProductionOutputLabelCodes.IsValidCode(
            new string('K', ProductionOutputLabelCodes.MaximumCodeLength + 1)));
        Assert.False(ProductionOutputLabelCodes.IsValidCode("SENTETIK\u0000KOD"));
    }

    [Theory]
    [InlineData("CK-{n}", 1L, "CK-1")]
    [InlineData("CK-{n}-A", 42L, "CK-42-A")]
    [InlineData("{n}", 1000000L, "1000000")]
    public void Format_fills_the_single_placeholder_with_the_invariant_number(string format, long number, string code) =>
        Assert.Equal(code, ProductionOutputLabelCodes.Format(format, number));

    [Fact]
    public void Format_refuses_an_invalid_format_or_a_number_that_is_not_positive()
    {
        Assert.Throws<ArgumentException>(() => ProductionOutputLabelCodes.Format("CK-", 1));
        Assert.Throws<ArgumentException>(() => ProductionOutputLabelCodes.Format("CK-{n}-{n}", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductionOutputLabelCodes.Format("CK-{n}", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductionOutputLabelCodes.Format("CK-{n}", -1));
    }
}

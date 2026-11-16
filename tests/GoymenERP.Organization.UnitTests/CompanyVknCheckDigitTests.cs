using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;

namespace GoymenERP.Organization.UnitTests;

/// <summary>
/// KIM-D40: the official last-digit check of the ten-digit VKN. Every golden value here is synthetic: built to satisfy
/// (or to break) the algorithm, never a real VKN.
/// </summary>
public sealed class CompanyVknCheckDigitTests
{
    [Theory]
    [InlineData("9000000009")]
    [InlineData("9000000017")]
    [InlineData("9000000025")]
    [InlineData("9000000033")]
    [InlineData("9000000040")]
    [InlineData("9000000058")]
    [InlineData("9000000066")]
    [InlineData("9000000074")]
    [InlineData("1111111114")]
    public void A_synthetic_vkn_whose_last_digit_is_the_computed_check_digit_passes(string vkn)
    {
        Assert.True(CompanyVknCheckDigit.IsSatisfiedBy(vkn));
        Assert.True(OrganizationCompanyTaxIdRules.HasValidCheckDigit($" {vkn} "));
    }

    [Theory]
    [InlineData("9000000001")]
    [InlineData("9000000002")]
    [InlineData("9000000018")]
    [InlineData("1111111111")]
    [InlineData("0000000000")]
    public void A_well_formed_vkn_with_another_last_digit_fails(string vkn)
    {
        Assert.False(CompanyVknCheckDigit.IsSatisfiedBy(vkn));
        Assert.False(OrganizationCompanyTaxIdRules.HasValidCheckDigit(vkn));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("900000001")]
    [InlineData("90000000170")]
    [InlineData("90000O0017")]
    [InlineData(" 9000000017")]
    public void A_value_that_is_not_ten_ascii_digits_never_passes_the_domain_check(string? vkn) =>
        Assert.False(CompanyVknCheckDigit.IsSatisfiedBy(vkn));

    [Fact]
    public void Exactly_one_last_digit_satisfies_the_check_for_any_first_nine_digits()
    {
        foreach (var prefix in new[] { "900000001", "123456789", "000000000", "999999999", "410004658" })
        {
            var passing = Enumerable.Range(0, 10).Count(digit => CompanyVknCheckDigit.IsSatisfiedBy($"{prefix}{digit}"));
            Assert.Equal(1, passing);
        }
    }
}

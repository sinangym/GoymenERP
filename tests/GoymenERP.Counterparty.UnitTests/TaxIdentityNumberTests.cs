using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Counterparty.UnitTests;

/// <summary>AT-009: VKN (10) / TCKN (11) are accepted only when the official check digits hold; nothing is cleaned.</summary>
public sealed class TaxIdentityNumberTests
{
    [Theory]
    [InlineData("1234567890")]
    [InlineData("9876543217")]
    [InlineData("4567890128")]
    [InlineData("1111111114")]
    [InlineData(" 1234567890 ")]
    public void A_valid_VKN_is_accepted_as_bare_digits(string text)
    {
        var number = TaxIdentityNumber.Parse(text);
        Assert.Equal(TaxIdentityKind.Vkn, number.Kind);
        Assert.Equal(text.Trim(), number.Value);
    }

    [Theory]
    [InlineData("10000000146")]
    [InlineData("12345678950")]
    [InlineData("98765432150")]
    public void A_valid_TCKN_is_accepted(string text)
    {
        var number = TaxIdentityNumber.Parse(text);
        Assert.Equal(TaxIdentityKind.Tckn, number.Kind);
        Assert.Equal(text, number.Value);
    }

    [Theory]
    [InlineData("1234567891")] // VKN, wrong check digit
    [InlineData("1234567899")]
    [InlineData("10000000145")] // TCKN, wrong 11th digit
    [InlineData("10000000156")] // TCKN, wrong 10th digit
    [InlineData("01234567890")] // TCKN starting with 0
    public void A_wrong_check_digit_is_refused(string text)
    {
        Assert.False(TaxIdentityNumber.TryParse(text, out _, out var problem));
        Assert.NotEmpty(problem);
        Assert.Throws<ArgumentException>(() => TaxIdentityNumber.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789")] // 9 digits
    [InlineData("123456789012")] // 12 digits
    [InlineData("123 4567890")] // inner space is not cleaned
    [InlineData("123-456-7890")]
    [InlineData("12345678A0")]
    [InlineData("١٢٣٤٥٦٧٨٩٠")] // non-ASCII digits
    [InlineData("１２３４５６７８９０")] // full-width digits
    public void A_malformed_number_is_refused(string? text)
    {
        Assert.False(TaxIdentityNumber.TryParse(text, out var number, out var problem));
        Assert.Equal(default, number);
        Assert.NotEmpty(problem);
    }

    [Fact]
    public void Every_VKN_prefix_has_exactly_one_valid_check_digit()
    {
        foreach (var prefix in new[] { "000000000", "123456789", "555555555", "999999999", "314159265" })
        {
            var valid = Enumerable.Range(0, 10).Count(digit => TaxIdentityNumber.IsValidVkn(prefix + digit));
            Assert.Equal(1, valid);
        }
    }

    [Fact]
    public void Every_TCKN_prefix_has_exactly_one_valid_pair_of_check_digits()
    {
        foreach (var prefix in new[] { "100000001", "123456789", "987654321", "555555555" })
        {
            var valid = Enumerable.Range(0, 100).Count(pair => TaxIdentityNumber.IsValidTckn(prefix + pair.ToString("D2")));
            Assert.Equal(1, valid);
        }
    }
}

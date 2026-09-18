using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceIsoDateTests
{
    [Theory]
    [InlineData("2026-09-13", 2026, 9, 13)]
    [InlineData("2026-01-02", 2026, 1, 2)]
    [InlineData("2024-02-29", 2024, 2, 29)]
    public void Only_the_exact_iso_text_is_a_date(string text, int year, int month, int day)
    {
        Assert.True(FinanceIsoDate.TryParse(text, out var value));
        Assert.Equal(new DateOnly(year, month, day), value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("01/02/2026")]
    [InlineData("09/13/2026")]
    [InlineData("2026-9-3")]
    [InlineData("13 Sep 2026")]
    [InlineData("2026-09-13 ")]
    [InlineData(" 2026-09-13")]
    [InlineData("2026-09-13T10:00:00")]
    [InlineData("2026-09-13T00:00:00Z")]
    [InlineData("20260913")]
    [InlineData("2026-02-30")]
    [InlineData("2023-02-29")]
    public void Lenient_or_impossible_forms_are_refused(string? text)
    {
        Assert.False(FinanceIsoDate.TryParse(text, out var value));
        Assert.Equal(default(DateOnly), value);
    }
}

using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// F-029 S4b: the deterministic timeline of a persistent Sales policy family key — derived windows equal to the compiled
/// catalog's, the same-date supersede rule, the latest version as the next expected version, and the lossless amount text.
/// </summary>
public sealed class SalesPolicyTimelineV1Tests
{
    private static readonly DateOnly Day = new(2026, 11, 14);

    [Fact]
    public void Each_version_holds_until_the_day_before_the_next_one_starts_and_the_last_one_is_open()
    {
        var timeline = Build((1, Day), (2, Day.AddDays(10)), (3, Day.AddDays(20)));

        var expected = new (long Version, DateOnly From, DateOnly? Through)[]
        {
            (1L, Day, Day.AddDays(9)), (2L, Day.AddDays(10), Day.AddDays(19)), (3L, Day.AddDays(20), null),
        };
        Assert.Equal(expected,
            timeline.Select(entry => (entry.Version, entry.EffectiveFrom, entry.EffectiveThrough)).ToArray());
        Assert.All(timeline, entry => Assert.False(entry.IsSuperseded));
        Assert.Equal(3L, SalesPolicyTimelineV1.LatestVersion(timeline));
    }

    [Fact]
    public void A_later_version_on_the_same_date_supersedes_the_earlier_one_which_never_applies()
    {
        var timeline = Build((1, Day), (2, Day.AddDays(5)), (3, Day.AddDays(5)));

        var superseded = Assert.Single(timeline, entry => entry.IsSuperseded);
        Assert.Equal(2L, superseded.Version);
        Assert.Equal(SalesPolicyVersionStatesV1.Superseded, SalesPolicyTimelineV1.State(superseded, Day));
        Assert.Equal(3L, SalesPolicyTimelineV1.For(timeline, Day.AddDays(5))!.Version);
        Assert.Equal(Day.AddDays(4), timeline.Single(entry => entry.Version == 1).EffectiveThrough);
    }

    [Fact]
    public void Before_every_start_the_first_version_answers_with_its_real_window_and_nothing_is_in_force()
    {
        var timeline = Build((1, Day), (2, Day.AddDays(10)));

        var answer = SalesPolicyTimelineV1.For(timeline, Day.AddDays(-1));

        Assert.Equal((1L, Day), (answer!.Version, answer.EffectiveFrom));
        Assert.Null(SalesPolicyTimelineV1.InForceOn(timeline, Day.AddDays(-1)));
        Assert.Equal(2L, SalesPolicyTimelineV1.InForceOn(timeline, Day.AddDays(15))!.Version);
    }

    [Fact]
    public void An_empty_key_has_no_answer_and_its_next_expected_version_is_zero()
    {
        var timeline = Build();

        Assert.Null(SalesPolicyTimelineV1.For(timeline, Day));
        Assert.Equal(0L, SalesPolicyTimelineV1.LatestVersion(timeline));
    }

    [Fact]
    public void States_follow_today()
    {
        var timeline = Build((1, Day.AddDays(-10)), (2, Day), (3, Day.AddDays(3)));

        Assert.Equal(
            new[] { SalesPolicyVersionStatesV1.Ended, SalesPolicyVersionStatesV1.InForce, SalesPolicyVersionStatesV1.Scheduled },
            timeline.OrderBy(entry => entry.Version).Select(entry => SalesPolicyTimelineV1.State(entry, Day)).ToArray());
    }

    [Theory]
    [InlineData(1L, 1L)]
    [InlineData(0L, 2L)]
    [InlineData(-1L, 2L)]
    public void Repeated_or_non_positive_versions_are_a_malformed_source(long first, long second)
    {
        var exception = Assert.Throws<SettingsPrerequisiteException>(() => Build((first, Day), (second, Day.AddDays(1))));

        Assert.Equal("settings_profile_invalid", exception.Code);
    }

    [Theory]
    [InlineData("250000", "250000", 0)]
    [InlineData("250000.00", "250000", 2)]
    [InlineData("0.5", "0.5", 1)]
    [InlineData("12345678901234567890.12345678", "12345678901234567890.12345678", 8)]
    public void An_amount_parses_losslessly_and_formats_back_to_the_same_text(string text, string amount, int scale)
    {
        Assert.True(SalesPolicyTimelineV1.TryParseAmount(text, out var parsed, out var parsedScale));
        Assert.Equal((decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), scale), (parsed, parsedScale));
        Assert.Equal(text, SalesPolicyTimelineV1.FormatAmount(parsed, parsedScale));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1,5")]
    [InlineData(" 1")]
    [InlineData("1e3")]
    [InlineData("1.123456789")]
    [InlineData("123456789012345678901")]
    public void Anything_else_is_not_an_amount(string? text)
    {
        Assert.False(SalesPolicyTimelineV1.TryParseAmount(text, out _, out _));
    }

    [Theory]
    [InlineData("5", true)]
    [InlineData("12.5", true)]
    [InlineData("12.50", false)]
    [InlineData("1.12345", false)]
    public void A_canonical_percent_has_no_trailing_zero_and_at_most_four_decimals(string text, bool canonical)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(canonical, SalesPolicyTimelineV1.IsCanonicalPercent(value));
    }

    [Fact]
    public void A_percent_read_back_from_a_four_decimal_column_canonicalizes_to_the_value_entered()
    {
        var canonical = SalesPolicyTimelineV1.Canonical(12.5000m);

        Assert.Equal(12.5m, canonical);
        Assert.Equal(1, canonical.Scale);
        Assert.True(SalesPolicyTimelineV1.IsCanonicalPercent(canonical));
    }

    private static IReadOnlyList<SalesPolicyTimelineEntryV1<(long Version, DateOnly From)>> Build(
        params (long Version, DateOnly From)[] versions) =>
        SalesPolicyTimelineV1.Build(versions, version => version.Version, version => version.From);
}

using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FNS-D75: a Finance "as of" date is the company business day — the Europe/Istanbul calendar date of the server clock —
/// never the UTC date. SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinanceBusinessDayTests
{
    [Fact]
    public void The_business_day_turns_at_Istanbul_midnight_not_at_UTC_midnight()
    {
        Assert.True(FinanceBusinessDay.TryDateOf(new DateTimeOffset(2026, 11, 15, 20, 59, 59, TimeSpan.Zero), out var before));
        Assert.True(FinanceBusinessDay.TryDateOf(new DateTimeOffset(2026, 11, 15, 21, 0, 0, TimeSpan.Zero), out var after));

        Assert.Equal((new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 16)), (before, after));
    }

    [Fact]
    public void Today_reads_the_server_clock_through_the_business_day()
    {
        Assert.True(FinanceBusinessDay.TryToday(new FixedClock(new DateTimeOffset(2026, 11, 15, 22, 30, 0, TimeSpan.Zero)),
            out var today));

        Assert.Equal(new DateOnly(2026, 11, 16), today);
        Assert.Throws<ArgumentNullException>(() => FinanceBusinessDay.TryToday(null!, out _));
    }

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}

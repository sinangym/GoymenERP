namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FNS-D75 (P7, the same business day as SAT-D10): a Finance "as of" date — a collateral's validity and the Settings rate
/// read date — is the company business day, the Europe/Istanbul calendar date of the server clock, never the UTC date and
/// never a date the client sends. Instants stay UTC in storage. When the host cannot resolve the zone nothing is guessed (no
/// UTC fallback): <see cref="TryToday"/> answers false and the caller fails closed (unavailable).
/// </summary>
internal static class FinanceBusinessDay
{
    /// <summary>The IANA time zone of the business day.</summary>
    public const string TimeZoneId = "Europe/Istanbul";

    /// <summary>The stable reason code of a figure that could not be dated.</summary>
    public const string BusinessDayUnavailable = "finance_business_day_unavailable";

    private static readonly Lazy<TimeZoneInfo?> Zone = new(() =>
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The business day of <paramref name="instant"/>, or false when the zone cannot be resolved.</summary>
    internal static bool TryDateOf(DateTimeOffset instant, out DateOnly day)
    {
        if (Zone.Value is not { } zone)
        {
            day = default;
            return false;
        }

        day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
        return true;
    }

    /// <summary>Today's business day on the server clock, or false when the zone cannot be resolved.</summary>
    internal static bool TryToday(TimeProvider timeProvider, out DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return TryDateOf(timeProvider.GetUtcNow(), out day);
    }
}

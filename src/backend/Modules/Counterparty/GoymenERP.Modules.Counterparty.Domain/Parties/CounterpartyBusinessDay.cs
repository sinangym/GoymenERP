namespace GoymenERP.Modules.Counterparty.Domain.Parties;

/// <summary>
/// KLN-D38 (Sinan, 2026-11-14) dates a party's identity by business day. As in Sales (SAT-D10 / P7) the business day is the
/// Europe/Istanbul calendar day: instants stay UTC in storage and a dated identity change takes effect on the Istanbul date of
/// the server's clock, never a date the client sends. If the host cannot resolve the zone nothing is guessed (no UTC
/// fallback): <see cref="DateOf"/> throws <see cref="CounterpartyBusinessDayUnavailableException"/>.
/// </summary>
public static class CounterpartyBusinessDay
{
    /// <summary>The IANA time zone of the business day.</summary>
    public const string TimeZoneId = "Europe/Istanbul";

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

    /// <summary>The Europe/Istanbul calendar date of <paramref name="instant"/>.</summary>
    public static DateOnly DateOf(DateTimeOffset instant)
    {
        var zone = Zone.Value ?? throw new CounterpartyBusinessDayUnavailableException();
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }
}

/// <summary>The host cannot resolve the business day's time zone; no other zone stands in for it.</summary>
public sealed class CounterpartyBusinessDayUnavailableException()
    : InvalidOperationException(
        $"İş günü saat dilimi ({CounterpartyBusinessDay.TimeZoneId}) bu sunucuda çözülemedi; UTC'ye düşülmez.");

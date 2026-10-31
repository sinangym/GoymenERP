namespace GoymenERP.Modules.Sales.Domain;

/// <summary>
/// SAT-D10 / P7 (Sinan, 2026-10-25 and 2026-10-21): the business day is the Europe/Istanbul calendar day. Instants stay UTC
/// in storage; a server-side "today" — e.g. whether a quote has expired (SAT-D24) — is the Istanbul date of the server's
/// clock, never a date the client sends. The zone is the IANA identifier resolved from the host's time zone data; if the host
/// cannot resolve it nothing is guessed (no UTC fallback): <see cref="DateOf"/> throws <see cref="SalesBusinessDayUnavailableException"/>
/// and the caller answers 503.
/// </summary>
public static class SalesBusinessDay
{
    /// <summary>The IANA time zone of the business day (SAT-D10).</summary>
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
        var zone = Zone.Value ?? throw new SalesBusinessDayUnavailableException();
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }
}

/// <summary>The host cannot resolve the business day's time zone; no other zone stands in for it.</summary>
public sealed class SalesBusinessDayUnavailableException()
    : InvalidOperationException($"İş günü saat dilimi ({SalesBusinessDay.TimeZoneId}) bu sunucuda çözülemedi; UTC'ye düşülmez.");

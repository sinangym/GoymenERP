namespace GoymenERP.Modules.Product.Domain.PriceLists;

/// <summary>
/// SAT-D10 (user decision 2026-10-25, replaces ASSUMPTION-PRL-6): the business day of a price is the Europe/Istanbul
/// calendar day. Instants stay UTC in storage; "today" for publication (no backdating), the derived version status and a
/// validity boundary is the Istanbul date of the instant. The zone is the IANA identifier resolved from the host's time
/// zone data; if the host cannot resolve it nothing is guessed (no UTC fallback) — the call fails.
/// </summary>
public static class PriceListBusinessDay
{
    /// <summary>The IANA time zone of the business day (SAT-D10).</summary>
    public const string TimeZoneId = "Europe/Istanbul";

    private static readonly Lazy<TimeZoneInfo> Zone = new(() =>
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"İş günü saat dilimi ({TimeZoneId}) bu sunucuda çözülemedi; fiyat geçerliliği UTC'ye düşürülmez.", exception);
        }
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The Europe/Istanbul calendar date of <paramref name="instant"/>.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone.Value).DateTime);
}

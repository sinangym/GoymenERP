namespace GoymenERP.Modules.Manufacturing.Domain.Common;

/// <summary>
/// URT-D66 (with SAT-D10 / P7): the company's business day is the Europe/Istanbul calendar day. A termin earlier than
/// that day is refused on the plan and on the production order. "Today" is always the Istanbul date of the server's clock,
/// never a date the client sends. The zone is the IANA identifier resolved from the host's time zone data; if the host
/// cannot resolve it nothing is guessed (no UTC fallback): <see cref="DateOf"/> throws
/// <see cref="ManufacturingBusinessDayUnavailableException"/> and the caller answers 503. Manufacturing keeps its own copy
/// of the rule so it reads no other module's domain.
/// </summary>
public static class ManufacturingBusinessDay
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
        var zone = Zone.Value ?? throw new ManufacturingBusinessDayUnavailableException();
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }
}

/// <summary>The host cannot resolve the business day's time zone; no other zone stands in for it.</summary>
public sealed class ManufacturingBusinessDayUnavailableException()
    : InvalidOperationException(
        $"İş günü saat dilimi ({ManufacturingBusinessDay.TimeZoneId}) bu sunucuda çözülemedi; UTC'ye düşülmez.");

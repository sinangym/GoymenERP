namespace GoymenERP.BuildingBlocks.Monetary;

/// <summary>The stable reasons a previous-business-day rate is unavailable (SAT-D54/D55). Nothing falls back.</summary>
public static class PreviousBusinessDayExchangeRateCodes
{
    /// <summary>
    /// The walk reached a day with no record (an import gap or failure): it is not known whether the source
    /// published, so the walk stops instead of sliding silently onto an older rate.
    /// </summary>
    public const string DayUnconfirmed = "exchange_rate_day_unconfirmed";

    /// <summary>Every day within the Settings lookback cap was a confirmed no-bulletin day.</summary>
    public const string LookbackExceeded = "exchange_rate_lookback_exceeded";

    /// <summary>The nearest published day carries no selling rate for the requested pair; no older day is tried.</summary>
    public const string CurrencyMissing = "exchange_rate_currency_missing";
}

/// <summary>
/// What the caller asks for. Every value is the caller's: <see cref="CheckDate"/> is the Europe/Istanbul
/// business date (SAT-D10) the caller derived; <see cref="Source"/> is the policy's source (DEC-FIN-010);
/// the currencies are Settings currency identities; <see cref="LookbackCapDays"/> is read from Settings
/// (SAT-D54 note) and has no default here.
/// </summary>
public readonly record struct PreviousBusinessDayExchangeRateRequest(
    DateOnly CheckDate,
    ExchangeRateBulletinSource Source,
    Guid SourceCurrencyId,
    Guid TargetCurrencyId,
    int LookbackCapDays);

/// <summary>
/// The resolver's answer. Resolved: <see cref="BulletinDate"/>, <see cref="Rate"/> (selling side) and
/// <see cref="Source"/> are set. Unavailable: <see cref="Code"/> is one of
/// <see cref="PreviousBusinessDayExchangeRateCodes"/> and <see cref="StoppedAt"/> is the day that stopped
/// the walk (for <see cref="PreviousBusinessDayExchangeRateCodes.LookbackExceeded"/>, the last day within the cap).
/// <see cref="SkippedDays"/> counts the confirmed no-bulletin days walked over in both cases.
/// </summary>
public sealed record PreviousBusinessDayExchangeRateResult
{
    private PreviousBusinessDayExchangeRateResult(
        DateOnly checkDate,
        ExchangeRateBulletinSource source,
        int skippedDays,
        DateOnly? bulletinDate,
        ExchangeRate? rate,
        string? code,
        DateOnly? stoppedAt)
    {
        CheckDate = checkDate;
        Source = source;
        SkippedDays = skippedDays;
        BulletinDate = bulletinDate;
        Rate = rate;
        Code = code;
        StoppedAt = stoppedAt;
    }

    public DateOnly CheckDate { get; }
    public ExchangeRateBulletinSource Source { get; }
    public int SkippedDays { get; }
    public DateOnly? BulletinDate { get; }
    public ExchangeRate? Rate { get; }
    public string? Code { get; }
    public DateOnly? StoppedAt { get; }

    public bool IsResolved => Code is null;

    internal static PreviousBusinessDayExchangeRateResult Resolved(
        PreviousBusinessDayExchangeRateRequest request, int skippedDays, DateOnly bulletinDate, ExchangeRate rate) =>
        new(request.CheckDate, request.Source, skippedDays, bulletinDate, rate, null, null);

    internal static PreviousBusinessDayExchangeRateResult Unavailable(
        PreviousBusinessDayExchangeRateRequest request, int skippedDays, string code, DateOnly? stoppedAt) =>
        new(request.CheckDate, request.Source, skippedDays, null, null, code, stoppedAt);
}

/// <summary>
/// SAT-D54/D55 (DEC-FIN-010): the rate for check day D is the source's forex selling rate of the nearest
/// day <b>strictly before</b> D on which the source published a bulletin. D's own bulletin is never used.
/// <para>
/// Pure and deterministic: no clock, no time zone, no I/O, no holiday or weekday knowledge. The walk starts
/// at D−1 and moves back one calendar day at a time:
/// <list type="bullet">
/// <item>published → that day's selling rate for the pair, or <see cref="PreviousBusinessDayExchangeRateCodes.CurrencyMissing"/>
/// when the day lacks the pair (never an older day);</item>
/// <item>confirmed no bulletin → skipped;</item>
/// <item>no record → <see cref="PreviousBusinessDayExchangeRateCodes.DayUnconfirmed"/>;</item>
/// <item>more than <see cref="PreviousBusinessDayExchangeRateRequest.LookbackCapDays"/> days back →
/// <see cref="PreviousBusinessDayExchangeRateCodes.LookbackExceeded"/>.</item>
/// </list>
/// Weekends are skipped only because their records say "no bulletin"; nothing here knows a Saturday.
/// </para>
/// </summary>
public static class PreviousBusinessDayExchangeRate
{
    public static PreviousBusinessDayExchangeRateResult Resolve(
        PreviousBusinessDayExchangeRateRequest request,
        IEnumerable<ExchangeRateBulletinDay> bulletinDays)
    {
        ExchangeRateBulletinDay.RequireSource(request.Source);
        if (request.SourceCurrencyId == Guid.Empty || request.TargetCurrencyId == Guid.Empty)
        {
            throw new ArgumentException("İstenen kurun kaynak ve hedef para birimi kimliği boş olamaz.", nameof(request));
        }

        if (request.SourceCurrencyId == request.TargetCurrencyId)
        {
            throw new ArgumentException("Kur iki farklı para birimi arasında istenir.", nameof(request));
        }

        if (request.LookbackCapDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Geriye bakış sınırı Ayarlar'dan gelen pozitif bir gün sayısıdır.");
        }

        var days = Index(request.Source, bulletinDays);
        var pair = (request.SourceCurrencyId, request.TargetCurrencyId);
        var skipped = 0;
        for (var step = 1; step <= request.LookbackCapDays; step++)
        {
            var dayNumber = request.CheckDate.DayNumber - step;
            if (dayNumber < DateOnly.MinValue.DayNumber)
            {
                return PreviousBusinessDayExchangeRateResult.Unavailable(
                    request, skipped, PreviousBusinessDayExchangeRateCodes.DayUnconfirmed, null);
            }

            var date = DateOnly.FromDayNumber(dayNumber);
            if (!days.TryGetValue(date, out var day))
            {
                return PreviousBusinessDayExchangeRateResult.Unavailable(
                    request, skipped, PreviousBusinessDayExchangeRateCodes.DayUnconfirmed, date);
            }

            if (day.Status == ExchangeRateBulletinDayStatus.ConfirmedNoBulletin)
            {
                skipped++;
                continue;
            }

            return day.SellingRates.TryGetValue(pair, out var rate)
                ? PreviousBusinessDayExchangeRateResult.Resolved(request, skipped, date, rate)
                : PreviousBusinessDayExchangeRateResult.Unavailable(
                    request, skipped, PreviousBusinessDayExchangeRateCodes.CurrencyMissing, date);
        }

        var lastWithinCap = DateOnly.FromDayNumber(request.CheckDate.DayNumber - request.LookbackCapDays);
        return PreviousBusinessDayExchangeRateResult.Unavailable(
            request, skipped, PreviousBusinessDayExchangeRateCodes.LookbackExceeded, lastWithinCap);
    }

    private static Dictionary<DateOnly, ExchangeRateBulletinDay> Index(
        ExchangeRateBulletinSource source, IEnumerable<ExchangeRateBulletinDay> bulletinDays)
    {
        ArgumentNullException.ThrowIfNull(bulletinDays);
        var days = new Dictionary<DateOnly, ExchangeRateBulletinDay>();
        foreach (var day in bulletinDays)
        {
            ArgumentNullException.ThrowIfNull(day, nameof(bulletinDays));
            if (day.Source != source)
            {
                throw new ArgumentException("Bülten günleri istenen kaynağa ait olmalıdır.", nameof(bulletinDays));
            }

            if (!days.TryAdd(day.BulletinDate, day))
            {
                throw new ArgumentException("Bir kaynağın bir günü için tek bülten kaydı olur.", nameof(bulletinDays));
            }
        }

        return days;
    }
}

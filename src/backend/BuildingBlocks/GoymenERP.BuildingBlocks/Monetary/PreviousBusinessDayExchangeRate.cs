namespace GoymenERP.BuildingBlocks.Monetary;

/// <summary>The stable reasons a previous-business-day rate is unavailable (SAT-D54/D55). Nothing falls back.</summary>
public static class PreviousBusinessDayExchangeRateCodes
{
    /// <summary>
    /// The walk reached a day with no record or an <see cref="ExchangeRateBulletinDayStatus.Unconfirmed"/> one
    /// (an import gap or failure): it is not known whether the source published, so the walk stops instead of
    /// sliding silently onto an older rate.
    /// </summary>
    public const string DayUnconfirmed = "exchange_rate_day_unconfirmed";

    /// <summary>The nearest published day carries no selling rate for the requested pair; no older day is tried.</summary>
    public const string CurrencyMissing = "exchange_rate_currency_missing";
}

/// <summary>
/// What the caller asks for. Every value is the caller's: <see cref="CheckDate"/> is the Europe/Istanbul
/// business date (SAT-D10) the caller derived; <see cref="Source"/> is the policy's source (DEC-FIN-010);
/// the currencies are Settings currency identities.
/// </summary>
public readonly record struct PreviousBusinessDayExchangeRateRequest(
    DateOnly CheckDate,
    ExchangeRateBulletinSource Source,
    Guid SourceCurrencyId,
    Guid TargetCurrencyId);

/// <summary>
/// The resolver's answer. Resolved: <see cref="BulletinDate"/>, <see cref="Rate"/> (selling side) and
/// <see cref="RateProvenance"/> are set. Unavailable: <see cref="Code"/> is one of
/// <see cref="PreviousBusinessDayExchangeRateCodes"/> and <see cref="StoppedAt"/> is the day that stopped
/// the walk. In both cases <see cref="SkippedDays"/> counts the "no bulletin" days walked over, and
/// <see cref="ManualNoBulletinDaysSkipped"/> how many of them were marked by hand (DEC-FIN-010 manual changes).
/// </summary>
public sealed record PreviousBusinessDayExchangeRateResult
{
    private PreviousBusinessDayExchangeRateResult(
        PreviousBusinessDayExchangeRateRequest request,
        WalkCount walked,
        DateOnly? bulletinDate,
        ExchangeRate? rate,
        ExchangeRateBulletinDayProvenance? rateProvenance,
        string? code,
        DateOnly? stoppedAt)
    {
        CheckDate = request.CheckDate;
        Source = request.Source;
        SkippedDays = walked.Skipped;
        ManualNoBulletinDaysSkipped = walked.ManualSkipped;
        BulletinDate = bulletinDate;
        Rate = rate;
        RateProvenance = rateProvenance;
        Code = code;
        StoppedAt = stoppedAt;
    }

    public DateOnly CheckDate { get; }
    public ExchangeRateBulletinSource Source { get; }
    public int SkippedDays { get; }
    public int ManualNoBulletinDaysSkipped { get; }
    public DateOnly? BulletinDate { get; }
    public ExchangeRate? Rate { get; }
    public ExchangeRateBulletinDayProvenance? RateProvenance { get; }
    public string? Code { get; }
    public DateOnly? StoppedAt { get; }

    public bool IsResolved => Code is null;

    internal static PreviousBusinessDayExchangeRateResult Resolved(
        PreviousBusinessDayExchangeRateRequest request, WalkCount walked, DateOnly bulletinDate, ExchangeRate rate,
        ExchangeRateBulletinDayProvenance provenance) =>
        new(request, walked, bulletinDate, rate, provenance, null, null);

    internal static PreviousBusinessDayExchangeRateResult Unavailable(
        PreviousBusinessDayExchangeRateRequest request, WalkCount walked, string code, DateOnly? stoppedAt) =>
        new(request, walked, null, null, null, code, stoppedAt);

    internal readonly record struct WalkCount(int Skipped, int ManualSkipped);
}

/// <summary>
/// SAT-D54/D55 (DEC-FIN-010): the rate for check day D is the source's forex selling rate of the nearest
/// day <b>strictly before</b> D on which the source published a bulletin. D's own bulletin is never used.
/// <para>
/// Pure and deterministic: no clock, no time zone, no I/O, no holiday or weekday knowledge, no lookback
/// setting. The walk starts at D−1 and moves back one calendar day at a time over the day's effective record
/// (a manual record, when present, decides the day's status; see below):
/// <list type="bullet">
/// <item>published → that day's selling rate for the pair, or <see cref="PreviousBusinessDayExchangeRateCodes.CurrencyMissing"/>
/// when the day lacks the pair (never an older day);</item>
/// <item>"no bulletin" → skipped;</item>
/// <item>no record, or an unconfirmed one → <see cref="PreviousBusinessDayExchangeRateCodes.DayUnconfirmed"/>.</item>
/// </list>
/// The walk is bounded by itself: it only passes days with a definite "no bulletin" record. Weekends are
/// skipped only because their records say "no bulletin"; nothing here knows a Saturday.
/// </para>
/// <para>
/// Manual records (DEC-FIN-010 manual change): a manual "no bulletin" skips the day whatever the official
/// record says. A manual published record gives its own rate for the pairs it carries; for any other pair
/// the day's official published rate applies, or the pair is missing.
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

        var days = Index(request.Source, bulletinDays);
        var pair = (request.SourceCurrencyId, request.TargetCurrencyId);
        var walked = new PreviousBusinessDayExchangeRateResult.WalkCount(0, 0);
        for (var dayNumber = request.CheckDate.DayNumber - 1; dayNumber >= DateOnly.MinValue.DayNumber; dayNumber--)
        {
            var date = DateOnly.FromDayNumber(dayNumber);
            days.TryGetValue((date, ExchangeRateBulletinDayProvenance.Official), out var official);
            days.TryGetValue((date, ExchangeRateBulletinDayProvenance.Manual), out var manual);
            var effective = manual ?? official;
            switch (effective?.Status)
            {
                case ExchangeRateBulletinDayStatus.ConfirmedNoBulletin:
                    walked = new(walked.Skipped + 1, walked.ManualSkipped + (manual is null ? 0 : 1));
                    continue;
                case ExchangeRateBulletinDayStatus.Published:
                    return Published(request, walked, date, pair, manual, official);
                default:
                    return PreviousBusinessDayExchangeRateResult.Unavailable(
                        request, walked, PreviousBusinessDayExchangeRateCodes.DayUnconfirmed, date);
            }
        }

        return PreviousBusinessDayExchangeRateResult.Unavailable(
            request, walked, PreviousBusinessDayExchangeRateCodes.DayUnconfirmed, null);
    }

    private static PreviousBusinessDayExchangeRateResult Published(
        PreviousBusinessDayExchangeRateRequest request,
        PreviousBusinessDayExchangeRateResult.WalkCount walked,
        DateOnly date,
        (Guid, Guid) pair,
        ExchangeRateBulletinDay? manual,
        ExchangeRateBulletinDay? official)
    {
        foreach (var day in new[] { manual, official })
        {
            if (day is { Status: ExchangeRateBulletinDayStatus.Published } && day.SellingRates.TryGetValue(pair, out var rate))
            {
                return PreviousBusinessDayExchangeRateResult.Resolved(request, walked, date, rate, day.Provenance);
            }
        }

        return PreviousBusinessDayExchangeRateResult.Unavailable(
            request, walked, PreviousBusinessDayExchangeRateCodes.CurrencyMissing, date);
    }

    private static Dictionary<(DateOnly, ExchangeRateBulletinDayProvenance), ExchangeRateBulletinDay> Index(
        ExchangeRateBulletinSource source, IEnumerable<ExchangeRateBulletinDay> bulletinDays)
    {
        ArgumentNullException.ThrowIfNull(bulletinDays);
        var days = new Dictionary<(DateOnly, ExchangeRateBulletinDayProvenance), ExchangeRateBulletinDay>();
        foreach (var day in bulletinDays)
        {
            ArgumentNullException.ThrowIfNull(day, nameof(bulletinDays));
            if (day.Source != source)
            {
                throw new ArgumentException("Bülten günleri istenen kaynağa ait olmalıdır.", nameof(bulletinDays));
            }

            if (!days.TryAdd((day.BulletinDate, day.Provenance), day))
            {
                throw new ArgumentException("Bir kaynağın bir günü için tek resmî ve tek elle kayıt olur.", nameof(bulletinDays));
            }
        }

        return days;
    }
}

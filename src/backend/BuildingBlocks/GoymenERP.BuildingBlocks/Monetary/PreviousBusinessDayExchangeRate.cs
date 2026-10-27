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

    /// <summary>
    /// The walk reached a day whose manual record would change whether the day is a business day: a manual rate
    /// next to an official record that is not published (or no official record), or any manual "no bulletin".
    /// SAT-D55 leaves that to the source's publication alone, so the walk stops.
    /// </summary>
    public const string ManualConflict = "exchange_rate_manual_conflict";
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
/// The resolver's answer, carrying the request (check day, source, pair) in both outcomes. Resolved:
/// <see cref="BulletinDate"/>, <see cref="Rate"/> (selling side) and <see cref="RateProvenance"/> are set.
/// Unavailable: <see cref="Code"/> is one of <see cref="PreviousBusinessDayExchangeRateCodes"/> and
/// <see cref="StoppedAt"/> is the day that stopped the walk. <see cref="SkippedDays"/> counts the official
/// "no bulletin" days walked over.
/// </summary>
public sealed record PreviousBusinessDayExchangeRateResult
{
    private PreviousBusinessDayExchangeRateResult(
        PreviousBusinessDayExchangeRateRequest request,
        int skippedDays,
        DateOnly? bulletinDate,
        ExchangeRate? rate,
        ExchangeRateBulletinDayProvenance? rateProvenance,
        string? code,
        DateOnly? stoppedAt)
    {
        CheckDate = request.CheckDate;
        Source = request.Source;
        SourceCurrencyId = request.SourceCurrencyId;
        TargetCurrencyId = request.TargetCurrencyId;
        SkippedDays = skippedDays;
        BulletinDate = bulletinDate;
        Rate = rate;
        RateProvenance = rateProvenance;
        Code = code;
        StoppedAt = stoppedAt;
    }

    public DateOnly CheckDate { get; }
    public ExchangeRateBulletinSource Source { get; }
    public Guid SourceCurrencyId { get; }
    public Guid TargetCurrencyId { get; }
    public int SkippedDays { get; }
    public DateOnly? BulletinDate { get; }
    public ExchangeRate? Rate { get; }
    public ExchangeRateBulletinDayProvenance? RateProvenance { get; }
    public string? Code { get; }
    public DateOnly? StoppedAt { get; }

    public bool IsResolved => Code is null;

    internal static PreviousBusinessDayExchangeRateResult Resolved(
        PreviousBusinessDayExchangeRateRequest request, int skippedDays, DateOnly bulletinDate, ExchangeRate rate,
        ExchangeRateBulletinDayProvenance provenance) =>
        new(request, skippedDays, bulletinDate, rate, provenance, null, null);

    internal static PreviousBusinessDayExchangeRateResult Unavailable(
        PreviousBusinessDayExchangeRateRequest request, int skippedDays, string code, DateOnly? stoppedAt) =>
        new(request, skippedDays, null, null, null, code, stoppedAt);
}

/// <summary>
/// SAT-D54/D55 (DEC-FIN-010): the rate for check day D is the source's forex selling rate of the nearest
/// day <b>strictly before</b> D on which the source published a bulletin. D's own bulletin is never used.
/// <para>
/// Pure and deterministic: no clock, no time zone, no I/O, no holiday or weekday knowledge, no lookback
/// setting. The walk starts at D−1 and moves back one calendar day at a time. Whether a day is a business day
/// is decided by its <b>official</b> record alone:
/// <list type="bullet">
/// <item>published → that day's selling rate for the pair, or <see cref="PreviousBusinessDayExchangeRateCodes.CurrencyMissing"/>
/// when the day lacks the pair (never an older day);</item>
/// <item>definite "no bulletin" → skipped;</item>
/// <item>no record, or an unconfirmed one → <see cref="PreviousBusinessDayExchangeRateCodes.DayUnconfirmed"/>.</item>
/// </list>
/// The walk is bounded by itself: it only passes days with a definite official "no bulletin" record. Weekends
/// are skipped only because their records say "no bulletin"; nothing here knows a Saturday.
/// </para>
/// <para>
/// Manual records (DEC-FIN-010 manual change) can only change rate values: on a day whose official record is
/// published, a manual rate replaces the official rate for the pairs it carries, and the official rate stays for
/// the others. Any other manual record on a walked day — a manual rate next to an official "no bulletin",
/// unconfirmed or missing record, or any manual "no bulletin" — stops the walk with
/// <see cref="PreviousBusinessDayExchangeRateCodes.ManualConflict"/>.
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
        var skipped = 0;
        for (var dayNumber = request.CheckDate.DayNumber - 1; dayNumber >= DateOnly.MinValue.DayNumber; dayNumber--)
        {
            var date = DateOnly.FromDayNumber(dayNumber);
            days.TryGetValue((date, ExchangeRateBulletinDayProvenance.Official), out var official);
            days.TryGetValue((date, ExchangeRateBulletinDayProvenance.Manual), out var manual);
            var officiallyPublished = official?.Status == ExchangeRateBulletinDayStatus.Published;
            if (manual is not null && (manual.Status != ExchangeRateBulletinDayStatus.Published || !officiallyPublished))
            {
                return PreviousBusinessDayExchangeRateResult.Unavailable(
                    request, skipped, PreviousBusinessDayExchangeRateCodes.ManualConflict, date);
            }

            switch (official?.Status)
            {
                case ExchangeRateBulletinDayStatus.ConfirmedNoBulletin:
                    skipped++;
                    continue;
                case ExchangeRateBulletinDayStatus.Published:
                    return Published(request, skipped, date, pair, manual, official);
                default:
                    return PreviousBusinessDayExchangeRateResult.Unavailable(
                        request, skipped, PreviousBusinessDayExchangeRateCodes.DayUnconfirmed, date);
            }
        }

        return PreviousBusinessDayExchangeRateResult.Unavailable(
            request, skipped, PreviousBusinessDayExchangeRateCodes.DayUnconfirmed, null);
    }

    private static PreviousBusinessDayExchangeRateResult Published(
        PreviousBusinessDayExchangeRateRequest request,
        int skipped,
        DateOnly date,
        (Guid, Guid) pair,
        ExchangeRateBulletinDay? manual,
        ExchangeRateBulletinDay official)
    {
        foreach (var day in new[] { manual, official })
        {
            if (day is not null && day.SellingRates.TryGetValue(pair, out var rate))
            {
                return PreviousBusinessDayExchangeRateResult.Resolved(request, skipped, date, rate, day.Provenance);
            }
        }

        return PreviousBusinessDayExchangeRateResult.Unavailable(
            request, skipped, PreviousBusinessDayExchangeRateCodes.CurrencyMissing, date);
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

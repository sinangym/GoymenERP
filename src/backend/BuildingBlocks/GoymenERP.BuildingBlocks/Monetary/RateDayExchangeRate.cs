namespace GoymenERP.BuildingBlocks.Monetary;

/// <summary>
/// What an automatic official source says about one calendar day (SAT-D170/D171): an ordinary day, an arife or half day
/// (yarım gün tatil), or not determined. A day without a mark is undetermined too; nothing is assumed.
/// </summary>
public enum ExchangeRateHalfDayStatus
{
    /// <summary>The source says the day is not an arife or half day.</summary>
    FullDay = 1,

    /// <summary>The source says the day is an arife or a half-day holiday.</summary>
    HalfDay = 2,

    /// <summary>The source was asked but gave no definite answer; the resolver stops exactly as for a day without a mark.</summary>
    Undetermined = 3,
}

/// <summary>The half-day answer for one calendar day (SAT-D171). At most one mark per day.</summary>
public sealed record ExchangeRateHalfDayMark(DateOnly Date, ExchangeRateHalfDayStatus Status);

/// <summary>The stable reasons of <see cref="RateDayExchangeRate"/> besides <see cref="PreviousBusinessDayExchangeRateCodes"/>.</summary>
public static class RateDayExchangeRateCodes
{
    /// <summary>
    /// SAT-D171: whether the rate day (or a published day walked over while looking for the previous FULL business day) is
    /// an arife or half day is not known; the system chooses no rate ("kur belirlenemedi").
    /// </summary>
    public const string HalfDayUndetermined = "exchange_rate_half_day_undetermined";
}

/// <summary>
/// The rate day D (kur günü) a document or check asks about, as the caller derived it: the shipment day (SAT-D90), the invoice
/// day in the invoice-first flow (SAT-D90/D170), the goods-receipt day (ALS-D6) or the risk check day (SAT-D47) — always the
/// Europe/Istanbul business date (SAT-D10). The currencies are Settings currency identities.
/// </summary>
public readonly record struct RateDayExchangeRateRequest(
    DateOnly RateDay,
    ExchangeRateBulletinSource Source,
    Guid SourceCurrencyId,
    Guid TargetCurrencyId);

/// <summary>
/// The answer of <see cref="RateDayExchangeRate.Resolve"/>. Resolved: <see cref="BulletinDate"/>, <see cref="Rate"/> and
/// <see cref="RateProvenance"/> are set. Unavailable: <see cref="Code"/> is one of
/// <see cref="PreviousBusinessDayExchangeRateCodes"/> or <see cref="RateDayExchangeRateCodes"/>, and <see cref="StoppedAt"/>
/// is the day that stopped it. <see cref="HalfDayRuleApplied"/> says the rate day was an arife/half day (SAT-D170), and then
/// <see cref="PreviousFullBusinessDay"/> is the full business day whose previous business day's bulletin was used.
/// </summary>
public sealed record RateDayExchangeRateResult
{
    private RateDayExchangeRateResult(RateDayExchangeRateRequest request)
    {
        RateDay = request.RateDay;
        Source = request.Source;
        SourceCurrencyId = request.SourceCurrencyId;
        TargetCurrencyId = request.TargetCurrencyId;
    }

    public DateOnly RateDay { get; }
    public ExchangeRateBulletinSource Source { get; }
    public Guid SourceCurrencyId { get; }
    public Guid TargetCurrencyId { get; }
    public bool HalfDayRuleApplied { get; private init; }
    public DateOnly? PreviousFullBusinessDay { get; private init; }
    public DateOnly? BulletinDate { get; private init; }
    public ExchangeRate? Rate { get; private init; }
    public ExchangeRateBulletinDayProvenance? RateProvenance { get; private init; }
    public string? Code { get; private init; }
    public DateOnly? StoppedAt { get; private init; }

    public bool IsResolved => Code is null;

    internal static RateDayExchangeRateResult From(RateDayExchangeRateRequest request, PreviousBusinessDayExchangeRateResult inner,
        DateOnly? previousFullBusinessDay) => new(request)
    {
        HalfDayRuleApplied = previousFullBusinessDay is not null,
        PreviousFullBusinessDay = previousFullBusinessDay,
        BulletinDate = inner.BulletinDate,
        Rate = inner.Rate,
        RateProvenance = inner.RateProvenance,
        Code = inner.Code,
        StoppedAt = inner.StoppedAt,
    };

    internal static RateDayExchangeRateResult Unavailable(RateDayExchangeRateRequest request, bool halfDayRuleApplied, string code,
        DateOnly? stoppedAt) => new(request)
    {
        HalfDayRuleApplied = halfDayRuleApplied,
        Code = code,
        StoppedAt = stoppedAt,
    };
}

/// <summary>
/// DEC-FIN-010 with SAT-D54/D55 and SAT-D170/D171: the one deterministic calculator for "which TCMB selling rate does rate day D
/// use". Pure: no clock, time zone, I/O, holiday or weekday knowledge.
/// <list type="number">
/// <item>D's half-day mark must be known (SAT-D171); a missing or <see cref="ExchangeRateHalfDayStatus.Undetermined"/> mark is
/// <see cref="RateDayExchangeRateCodes.HalfDayUndetermined"/>.</item>
/// <item>D is a full day → <see cref="PreviousBusinessDayExchangeRate.Resolve"/> for D (SAT-D54: the nearest published day
/// strictly before D; SAT-D55: only through definite official "no bulletin" days).</item>
/// <item>D is an arife or half day (SAT-D170) → walk back from D−1 to the previous FULL business day F: an official "no
/// bulletin" day is skipped, a published half day is skipped (consecutive half days are all skipped), a published day whose
/// mark is missing or undetermined stops with <see cref="RateDayExchangeRateCodes.HalfDayUndetermined"/>, a day without a
/// definite official answer stops with <see cref="PreviousBusinessDayExchangeRateCodes.DayUnconfirmed"/>, and a manual record
/// that would change whether a day is a business day stops with <see cref="PreviousBusinessDayExchangeRateCodes.ManualConflict"/>.
/// Then the rate is <see cref="PreviousBusinessDayExchangeRate.Resolve"/> for F: the bulletin TCMB announced on the business
/// day before F (the rate published in the Resmî Gazete on F).</item>
/// </list>
/// Nothing falls back: every stop is a code, never an older or invented rate.
/// </summary>
public static class RateDayExchangeRate
{
    public static RateDayExchangeRateResult Resolve(
        RateDayExchangeRateRequest request,
        IEnumerable<ExchangeRateBulletinDay> bulletinDays,
        IEnumerable<ExchangeRateHalfDayMark> halfDayMarks)
    {
        ExchangeRateBulletinDay.RequireSource(request.Source);
        ArgumentNullException.ThrowIfNull(halfDayMarks);
        var days = bulletinDays as IReadOnlyCollection<ExchangeRateBulletinDay> ?? bulletinDays?.ToArray()
            ?? throw new ArgumentNullException(nameof(bulletinDays));
        var marks = new Dictionary<DateOnly, ExchangeRateHalfDayStatus>();
        foreach (var mark in halfDayMarks)
        {
            ArgumentNullException.ThrowIfNull(mark, nameof(halfDayMarks));
            if (!Enum.IsDefined(mark.Status))
            {
                throw new ArgumentOutOfRangeException(nameof(halfDayMarks), "Yarım gün durumu tanımlı değil.");
            }

            if (!marks.TryAdd(mark.Date, mark.Status))
            {
                throw new ArgumentException("Bir gün için tek yarım gün işareti olur.", nameof(halfDayMarks));
            }
        }

        var inner = new PreviousBusinessDayExchangeRateRequest(request.RateDay, request.Source, request.SourceCurrencyId,
            request.TargetCurrencyId);
        switch (marks.GetValueOrDefault(request.RateDay, ExchangeRateHalfDayStatus.Undetermined))
        {
            case ExchangeRateHalfDayStatus.FullDay:
                return RateDayExchangeRateResult.From(request, PreviousBusinessDayExchangeRate.Resolve(inner, days), null);
            case ExchangeRateHalfDayStatus.HalfDay:
                break;
            default:
                return RateDayExchangeRateResult.Unavailable(request, false, RateDayExchangeRateCodes.HalfDayUndetermined,
                    request.RateDay);
        }

        // Validates the request and the day records exactly as the inner resolver does, before walking them.
        _ = PreviousBusinessDayExchangeRate.Resolve(inner, days);
        var index = PreviousBusinessDayExchangeRate.Index(request.Source, days);
        for (var dayNumber = request.RateDay.DayNumber - 1; dayNumber >= DateOnly.MinValue.DayNumber; dayNumber--)
        {
            var date = DateOnly.FromDayNumber(dayNumber);
            index.TryGetValue((date, ExchangeRateBulletinDayProvenance.Official), out var official);
            index.TryGetValue((date, ExchangeRateBulletinDayProvenance.Manual), out var manual);
            var officiallyPublished = official?.Status == ExchangeRateBulletinDayStatus.Published;
            if (manual is not null && (manual.Status != ExchangeRateBulletinDayStatus.Published || !officiallyPublished))
            {
                return RateDayExchangeRateResult.Unavailable(request, true, PreviousBusinessDayExchangeRateCodes.ManualConflict, date);
            }

            switch (official?.Status)
            {
                case ExchangeRateBulletinDayStatus.ConfirmedNoBulletin:
                    continue;
                case ExchangeRateBulletinDayStatus.Published:
                    switch (marks.GetValueOrDefault(date, ExchangeRateHalfDayStatus.Undetermined))
                    {
                        case ExchangeRateHalfDayStatus.FullDay:
                            return RateDayExchangeRateResult.From(request,
                                PreviousBusinessDayExchangeRate.Resolve(inner with { CheckDate = date }, days), date);
                        case ExchangeRateHalfDayStatus.HalfDay:
                            continue;
                        default:
                            return RateDayExchangeRateResult.Unavailable(request, true, RateDayExchangeRateCodes.HalfDayUndetermined,
                                date);
                    }

                default:
                    return RateDayExchangeRateResult.Unavailable(request, true, PreviousBusinessDayExchangeRateCodes.DayUnconfirmed,
                        date);
            }
        }

        return RateDayExchangeRateResult.Unavailable(request, true, PreviousBusinessDayExchangeRateCodes.DayUnconfirmed, null);
    }
}

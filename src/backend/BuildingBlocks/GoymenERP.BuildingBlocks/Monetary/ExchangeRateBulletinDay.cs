using System.Collections.Frozen;

namespace GoymenERP.BuildingBlocks.Monetary;

/// <summary>
/// The publisher of an exchange-rate bulletin. DEC-FIN-010 names the TCMB indicative selling rate;
/// no other source is admitted until a decision adds one. There is no default: the caller states it.
/// </summary>
public enum ExchangeRateBulletinSource
{
    /// <summary>Türkiye Cumhuriyet Merkez Bankası indicative rates (gösterge niteliğindeki kurlar).</summary>
    Tcmb = 1,
}

/// <summary>What the official source says about one calendar day (SAT-D55).</summary>
public enum ExchangeRateBulletinDayStatus
{
    /// <summary>The source published a bulletin for the day: the day is a business day.</summary>
    Published = 1,

    /// <summary>
    /// The source confirmed that no bulletin exists for the day (weekend, holiday, a non-published arife):
    /// the day is skipped. An import failure (network, timeout) is never this status; that day has no record.
    /// </summary>
    ConfirmedNoBulletin = 2,
}

/// <summary>
/// One calendar day of one source's bulletin history: the record contract the previous-business-day
/// resolver reads (SAT-D54/D55). There is no holiday calendar: the day's status is the only answer to
/// "was it a business day". A day without a record is unknown, never assumed to be either.
/// <para>
/// A published day carries the source's forex <b>selling</b> rates (DEC-FIN-010), each one an
/// <see cref="ExchangeRate"/> for one currency pair, per <b>one</b> source-currency unit (a bulletin line
/// quoted per 100 units is normalised by whoever records it). At most one rate per pair.
/// </para>
/// </summary>
public sealed class ExchangeRateBulletinDay
{
    private ExchangeRateBulletinDay(
        DateOnly bulletinDate,
        ExchangeRateBulletinSource source,
        ExchangeRateBulletinDayStatus status,
        FrozenDictionary<(Guid Source, Guid Target), ExchangeRate> sellingRates)
    {
        BulletinDate = bulletinDate;
        Source = source;
        Status = status;
        SellingRates = sellingRates;
    }

    /// <summary>The calendar date the bulletin is (or is confirmed not to be) published for.</summary>
    public DateOnly BulletinDate { get; }

    public ExchangeRateBulletinSource Source { get; }

    public ExchangeRateBulletinDayStatus Status { get; }

    /// <summary>The day's forex selling rates keyed by (source currency, target currency); empty unless published.</summary>
    public IReadOnlyDictionary<(Guid Source, Guid Target), ExchangeRate> SellingRates { get; }

    /// <summary>A day the source published, with its selling rates (at least one; one per currency pair).</summary>
    public static ExchangeRateBulletinDay Published(
        DateOnly bulletinDate,
        ExchangeRateBulletinSource source,
        IEnumerable<ExchangeRate> sellingRates)
    {
        RequireSource(source);
        ArgumentNullException.ThrowIfNull(sellingRates);
        var rates = new Dictionary<(Guid Source, Guid Target), ExchangeRate>();
        foreach (var rate in sellingRates)
        {
            if (!rates.TryAdd((rate.SourceCurrencyId, rate.TargetCurrencyId), rate))
            {
                throw new ArgumentException("Bir bülten gününde aynı para birimi çifti için tek satış kuru olur.", nameof(sellingRates));
            }
        }

        if (rates.Count == 0)
        {
            throw new ArgumentException("Yayımlanmış bülten günü en az bir satış kuru taşır.", nameof(sellingRates));
        }

        return new ExchangeRateBulletinDay(bulletinDate, source, ExchangeRateBulletinDayStatus.Published, rates.ToFrozenDictionary());
    }

    /// <summary>A day the source confirmed it did not publish (official "no bulletin" answer); it carries no rate.</summary>
    public static ExchangeRateBulletinDay ConfirmedNoBulletin(DateOnly bulletinDate, ExchangeRateBulletinSource source)
    {
        RequireSource(source);
        return new ExchangeRateBulletinDay(bulletinDate, source, ExchangeRateBulletinDayStatus.ConfirmedNoBulletin,
            FrozenDictionary<(Guid Source, Guid Target), ExchangeRate>.Empty);
    }

    internal static void RequireSource(ExchangeRateBulletinSource source)
    {
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source), "Kur bülteni kaynağı tanımlı değil.");
        }
    }
}

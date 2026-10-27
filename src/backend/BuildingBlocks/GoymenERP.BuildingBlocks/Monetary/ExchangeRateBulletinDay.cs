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

/// <summary>What is known about one calendar day of a source (SAT-D55).</summary>
public enum ExchangeRateBulletinDayStatus
{
    /// <summary>The source published a bulletin for the day: the day is a business day.</summary>
    Published = 1,

    /// <summary>
    /// A definite answer that no bulletin exists for the day (the official source reports the dated bulletin
    /// does not exist): the day is skipped. Weekends and holidays are recorded with this status.
    /// </summary>
    ConfirmedNoBulletin = 2,

    /// <summary>
    /// The day was attempted but the answer was not definite (network error, timeout, any other response).
    /// It stops the walk exactly like a day without a record.
    /// </summary>
    Unconfirmed = 3,
}

/// <summary>Where a day record came from.</summary>
public enum ExchangeRateBulletinDayProvenance
{
    /// <summary>Imported from the source's own bulletin (or its definite "no bulletin" answer).</summary>
    Official = 1,

    /// <summary>
    /// A DEC-FIN-010 manual rate change. It may only change rate values of a day whose official record is
    /// published (SAT-D55: only the source's publication decides a business day); any other manual record is a
    /// conflict the resolver fails closed on. It carries a reason and an actor; the owning store authorises and
    /// audits it, and the manual-exception limit is open under OPEN-012.
    /// </summary>
    Manual = 2,
}

/// <summary>Who made a manual change and why (DEC-FIN-010). Authority and audit are the owning store's; the limit is OPEN-012.</summary>
public sealed record ExchangeRateManualEntry
{
    /// <summary>The repository's actor identity column width (string actor ids, as every module stores them).</summary>
    public const int MaximumActorIdLength = 128;

    /// <summary>The repository's common reason column width.</summary>
    public const int MaximumReasonLength = 1000;

    public ExchangeRateManualEntry(string actorId, string reason)
    {
        var actor = actorId?.Trim();
        if (string.IsNullOrEmpty(actor) || actor.Length > MaximumActorIdLength)
        {
            throw new ArgumentException("Elle kur değişikliğini yapan kullanıcı belirtilmelidir.", nameof(actorId));
        }

        var trimmedReason = reason?.Trim();
        if (string.IsNullOrEmpty(trimmedReason) || trimmedReason.Length > MaximumReasonLength)
        {
            throw new ArgumentException(
                $"Elle kur değişikliği en fazla {MaximumReasonLength} karakterlik bir gerekçe taşır.", nameof(reason));
        }

        ActorId = actor;
        Reason = trimmedReason;
    }

    public string ActorId { get; }
    public string Reason { get; }
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
/// <para>
/// A day may have one <see cref="ExchangeRateBulletinDayProvenance.Official"/> record and, next to it, one
/// <see cref="ExchangeRateBulletinDayProvenance.Manual"/> record. Only official records can be
/// <see cref="ExchangeRateBulletinDayStatus.Unconfirmed"/>. A manual record never changes whether a day is a
/// business day: a manual rate is honoured only next to an official published record, and a manual
/// "no bulletin" is never honoured (only an official definite answer marks a day "no bulletin").
/// </para>
/// </summary>
public sealed class ExchangeRateBulletinDay
{
    private ExchangeRateBulletinDay(
        DateOnly bulletinDate,
        ExchangeRateBulletinSource source,
        ExchangeRateBulletinDayStatus status,
        FrozenDictionary<(Guid Source, Guid Target), ExchangeRate> sellingRates,
        ExchangeRateManualEntry? manualEntry)
    {
        BulletinDate = bulletinDate;
        Source = source;
        Status = status;
        SellingRates = sellingRates;
        ManualEntry = manualEntry;
    }

    /// <summary>The calendar date the bulletin is (or is confirmed not to be) published for.</summary>
    public DateOnly BulletinDate { get; }

    public ExchangeRateBulletinSource Source { get; }

    public ExchangeRateBulletinDayStatus Status { get; }

    /// <summary>The day's forex selling rates keyed by (source currency, target currency); empty unless published.</summary>
    public IReadOnlyDictionary<(Guid Source, Guid Target), ExchangeRate> SellingRates { get; }

    /// <summary>Set only on a manual record.</summary>
    public ExchangeRateManualEntry? ManualEntry { get; }

    public ExchangeRateBulletinDayProvenance Provenance =>
        ManualEntry is null ? ExchangeRateBulletinDayProvenance.Official : ExchangeRateBulletinDayProvenance.Manual;

    /// <summary>An official day the source published, with its selling rates (at least one; one per currency pair).</summary>
    public static ExchangeRateBulletinDay Published(
        DateOnly bulletinDate,
        ExchangeRateBulletinSource source,
        IEnumerable<ExchangeRate> sellingRates) =>
        PublishedDay(bulletinDate, source, sellingRates, null);

    /// <summary>An official definite answer that the dated bulletin does not exist; it carries no rate.</summary>
    public static ExchangeRateBulletinDay ConfirmedNoBulletin(DateOnly bulletinDate, ExchangeRateBulletinSource source) =>
        NoRateDay(bulletinDate, source, ExchangeRateBulletinDayStatus.ConfirmedNoBulletin, null);

    /// <summary>An official attempt without a definite answer; the day stays unknown to the resolver.</summary>
    public static ExchangeRateBulletinDay Unconfirmed(DateOnly bulletinDate, ExchangeRateBulletinSource source) =>
        NoRateDay(bulletinDate, source, ExchangeRateBulletinDayStatus.Unconfirmed, null);

    /// <summary>
    /// A DEC-FIN-010 manual rate change for the day (one or more pairs). It replaces only the pairs it carries and
    /// only on a day whose official record is published; anywhere else the resolver stops with a manual conflict.
    /// </summary>
    public static ExchangeRateBulletinDay ManualPublished(
        DateOnly bulletinDate,
        ExchangeRateBulletinSource source,
        IEnumerable<ExchangeRate> sellingRates,
        ExchangeRateManualEntry manualEntry)
    {
        ArgumentNullException.ThrowIfNull(manualEntry);
        return PublishedDay(bulletinDate, source, sellingRates, manualEntry);
    }

    /// <summary>
    /// A day marked "no bulletin" by hand. SAT-D55 leaves that answer to the source alone, so the resolver never
    /// skips such a day: it stops with a manual conflict. Modelled so a stored row fails closed instead of being ignored.
    /// </summary>
    public static ExchangeRateBulletinDay ManualNoBulletin(
        DateOnly bulletinDate,
        ExchangeRateBulletinSource source,
        ExchangeRateManualEntry manualEntry)
    {
        ArgumentNullException.ThrowIfNull(manualEntry);
        return NoRateDay(bulletinDate, source, ExchangeRateBulletinDayStatus.ConfirmedNoBulletin, manualEntry);
    }

    internal static void RequireSource(ExchangeRateBulletinSource source)
    {
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source), "Kur bülteni kaynağı tanımlı değil.");
        }
    }

    private static ExchangeRateBulletinDay PublishedDay(
        DateOnly bulletinDate,
        ExchangeRateBulletinSource source,
        IEnumerable<ExchangeRate> sellingRates,
        ExchangeRateManualEntry? manualEntry)
    {
        RequireSource(source);
        ArgumentNullException.ThrowIfNull(sellingRates);
        var rates = new Dictionary<(Guid Source, Guid Target), ExchangeRate>();
        foreach (var rate in sellingRates)
        {
            if (rate.SourceCurrencyId == Guid.Empty || rate.TargetCurrencyId == Guid.Empty || rate.Rate <= 0m)
            {
                throw new ArgumentException("Bülten kuru boş bir çift ya da sıfır kur olamaz.", nameof(sellingRates));
            }

            if (!rates.TryAdd((rate.SourceCurrencyId, rate.TargetCurrencyId), rate))
            {
                throw new ArgumentException("Bir bülten gününde aynı para birimi çifti için tek satış kuru olur.", nameof(sellingRates));
            }
        }

        if (rates.Count == 0)
        {
            throw new ArgumentException("Yayımlanmış bülten günü en az bir satış kuru taşır.", nameof(sellingRates));
        }

        return new ExchangeRateBulletinDay(bulletinDate, source, ExchangeRateBulletinDayStatus.Published, rates.ToFrozenDictionary(), manualEntry);
    }

    private static ExchangeRateBulletinDay NoRateDay(
        DateOnly bulletinDate,
        ExchangeRateBulletinSource source,
        ExchangeRateBulletinDayStatus status,
        ExchangeRateManualEntry? manualEntry)
    {
        RequireSource(source);
        return new ExchangeRateBulletinDay(bulletinDate, source, status,
            FrozenDictionary<(Guid Source, Guid Target), ExchangeRate>.Empty, manualEntry);
    }
}

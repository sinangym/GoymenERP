using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Domain.Costing;

/// <summary>
/// The calendar month a scrap price belongs to (URT-D47 (1): each scrap weighing is valued at the price of the month it
/// was weighed in). A month is a year and a month number only; it carries no day, time or time zone. Which business day a
/// weighing instant falls on is the caller's (the company's business date), never this type's.
/// </summary>
public readonly record struct ScrapPricePeriod
{
    public const int FirstMonth = 1;
    public const int LastMonth = 12;

    public ScrapPricePeriod(int year, int month)
    {
        if (year < DateOnly.MinValue.Year || year > DateOnly.MaxValue.Year)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Hurda fiyatı yılı geçerli bir takvim yılı değil.");
        }

        if (month is < FirstMonth or > LastMonth)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Hurda fiyatı ayı 1 ile 12 arasında olmalı.");
        }

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    /// <summary>The month a business day falls in: the month whose price values a scrap weighed on that day.</summary>
    public static ScrapPricePeriod Of(DateOnly businessDate) => new(businessDate.Year, businessDate.Month);

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}

/// <summary>
/// A monthly scrap price in TL per kg (SAT-D316 (3)-(4), URT-D47, URT-D65: one price per month for the company, no
/// per-material price). The value is a hand-entered figure, never computed here. Its stored column is decimal(18,4) as the
/// stamped average cost per kg; a fifth decimal is REFUSED, not rounded, and a value with more integer digits than the
/// column holds is refused as well.
/// <para>
/// A zero or negative price is refused (URT-D74 (1)); the cost calculator reads one as unreadable as well (URT-D74 (2)).
/// </para>
/// </summary>
public readonly record struct ScrapPriceTlPerKg
{
    public const int Scale = 4;

    // The exclusive bound of a decimal(18,4) column (14 integer digits). A storage bound, not a price policy.
    public const decimal ExclusiveUpperBound = 100_000_000_000_000m;

    public ScrapPriceTlPerKg(decimal value)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Hurda fiyatı sıfırdan büyük olmalı.");
        }

        if (value >= ExclusiveUpperBound)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Hurda fiyatı kayıt alanına sığmıyor.");
        }

        if (decimal.Round(value, Scale) != value)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.ScrapPricePrecisionExceeded,
                $"Hurda fiyatı en fazla {Scale} ondalık taşıyabilir; yuvarlanmaz, reddedilir.");
        }

        Value = value;
    }

    public decimal Value { get; }

    public override string ToString() => $"{Value} TL/kg";
}

/// <summary>
/// The company's scrap price of one month (URT-D47, URT-D65), kept as versions (URT-D72). The first entry of a month is
/// version 1 with no reason; a wrong price is corrected by a new version with a reason and the old version is kept, never
/// changed or deleted. The effective price of a month is its latest version (URT-D72 (2)); every version stays in the
/// audit trail. A second first entry for a recorded month is refused with
/// <see cref="ManufacturingRuleCodes.ScrapPriceAlreadyRecorded"/> (the month is corrected instead). URT-D73: a price is
/// entered for the current or a past month only; a month after the Europe/Istanbul business day of the entry is refused
/// with <see cref="ManufacturingRuleCodes.ScrapPriceFutureMonth"/>. Reading it for a weighing gives the cost calculator's
/// <see cref="ScrapPriceInput"/>: the price when the month has one, <see cref="ScrapPriceInput.Missing"/> otherwise, so a
/// month without a price leaves the cost pending (URT-D47 (2), URT-D65) and never becomes a silent zero.
/// </summary>
public sealed class MonthlyScrapPrice
{
    public const int RecordedByMaximumLength = 128;

    /// <summary>URT-D72: the first entry of a month; it carries no reason.</summary>
    public const int FirstVersion = 1;

    /// <summary>URT-D72: the longest correction reason (trimmed) the stored column keeps; a longer one is refused.</summary>
    public const int ReasonMaximumLength = ProductionPlan.DecisionReasonMaximumLength;

    private MonthlyScrapPrice(Guid id, Guid companyId, ScrapPricePeriod period, ScrapPriceTlPerKg price, int version,
        string? reason, string recordedBy, DateTimeOffset recordedAtUtc)
    {
        Id = id;
        CompanyId = companyId;
        Period = period;
        Price = price;
        Version = version;
        Reason = reason;
        RecordedBy = recordedBy;
        RecordedAtUtc = recordedAtUtc;
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public ScrapPricePeriod Period { get; }

    public ScrapPriceTlPerKg Price { get; }

    /// <summary>URT-D72: 1 for the month's first entry, one more for each correction.</summary>
    public int Version { get; }

    /// <summary>URT-D72: the correction's reason; null on the first entry (version 1), never blank on a correction.</summary>
    public string? Reason { get; }

    public string RecordedBy { get; }

    public DateTimeOffset RecordedAtUtc { get; }

    /// <summary>
    /// Records a month's first price (version 1). The caller's company, server actor and server clock; nothing here is
    /// defaulted. URT-D73: a month after the Europe/Istanbul business day of <paramref name="recordedAtUtc"/> is refused.
    /// </summary>
    public static MonthlyScrapPrice Record(Guid id, Guid companyId, ScrapPricePeriod period, ScrapPriceTlPerKg price,
        string recordedBy, DateTimeOffset recordedAtUtc)
    {
        var recorded = Create(id, companyId, period, price, FirstVersion, reason: null, recordedBy, recordedAtUtc);
        RequireNotFutureMonth(period, recordedAtUtc);
        return recorded;
    }

    /// <summary>
    /// URT-D72: corrects this month's price with a new version (this version + 1) and a reason; this version stays as it
    /// is. In order, and nothing changes on a refusal: a stale <paramref name="expectedVersion"/> is a version conflict, a
    /// blank or too long reason is the caller's error, the same price again is
    /// <see cref="ManufacturingRuleCodes.ScrapPriceUnchanged"/>, and (URT-D73) a month after the Europe/Istanbul business
    /// day of <paramref name="recordedAtUtc"/> is <see cref="ManufacturingRuleCodes.ScrapPriceFutureMonth"/>.
    /// </summary>
    public MonthlyScrapPrice Correct(Guid id, int expectedVersion, ScrapPriceTlPerKg price, string reason,
        string recordedBy, DateTimeOffset recordedAtUtc)
    {
        if (expectedVersion != Version)
        {
            throw new ManufacturingVersionConflictException(expectedVersion, Version);
        }

        if (Version == int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), expectedVersion,
                "Hurda fiyatının sürümü artık artırılamıyor.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Hurda fiyatı düzeltmesinin gerekçesi girilmeli.", nameof(reason));
        }

        var corrected = Create(id, CompanyId, Period, price, Version + 1, reason, recordedBy, recordedAtUtc);
        if (corrected.Price == Price)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.ScrapPriceUnchanged,
                "Düzeltilen fiyat ayın geçerli fiyatıyla aynı; yeni sürüm açılmadı.");
        }

        RequireNotFutureMonth(Period, recordedAtUtc);
        return corrected;
    }

    /// <summary>
    /// URT-D73: the month a price is entered for is not after the month of the Europe/Istanbul business day of the entry
    /// instant (the current and past months are taken). The zone is the server's (<see cref="ManufacturingBusinessDay"/>);
    /// a host that cannot resolve it throws <see cref="ManufacturingBusinessDayUnavailableException"/>, nothing is guessed.
    /// </summary>
    public static void RequireNotFutureMonth(ScrapPricePeriod period, DateTimeOffset enteredAtUtc)
    {
        var current = ScrapPricePeriod.Of(ManufacturingBusinessDay.DateOf(enteredAtUtc));
        if (period.Year > current.Year || (period.Year == current.Year && period.Month > current.Month))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.ScrapPriceFutureMonth,
                "Henüz gelmemiş ay için hurda fiyatı girilmez; içinde bulunulan ay ve geçmiş aylar girilebilir.");
        }
    }

    /// <summary>
    /// Rebuilds a stored version of a month's price under the rules that wrote it (URT-D73's month rule is an entry rule
    /// and is not re-applied). A stored value the rules cannot have produced is refused with
    /// <see cref="ManufacturingRuleCodes.StoredRecordInvalid"/>, never repaired.
    /// </summary>
    public static MonthlyScrapPrice Restore(Guid id, Guid companyId, int year, int month, decimal tlPerKg, int version,
        string? reason, string recordedBy, DateTimeOffset recordedAtUtc)
    {
        try
        {
            return Create(id, companyId, new ScrapPricePeriod(year, month), new ScrapPriceTlPerKg(tlPerKg), version,
                reason, recordedBy, recordedAtUtc);
        }
        catch (Exception exception) when (exception is ArgumentException or ManufacturingRuleException)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                "Kayıtlı hurda fiyatı kuralların üretebileceği bir değer değil.");
        }
    }

    /// <summary>The cost calculator's input for a weighing of this month (URT-D47 (1)).</summary>
    public ScrapPriceInput ToInput() => new(ScrapPriceReadStatus.Value, Price.Value);

    private static MonthlyScrapPrice Create(Guid id, Guid companyId, ScrapPricePeriod period, ScrapPriceTlPerKg price,
        int version, string? reason, string recordedBy, DateTimeOffset recordedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Hurda fiyatı kimliği boş olamaz.", nameof(id));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Hurda fiyatı şirketi boş olamaz.", nameof(companyId));
        }

        if (period == default)
        {
            throw new ArgumentException("Hurda fiyatının ayı belirtilmeli.", nameof(period));
        }

        if (price == default)
        {
            throw new ArgumentException("Hurda fiyatı belirtilmeli.", nameof(price));
        }

        if (version < FirstVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Hurda fiyatı sürümü 1 ya da daha büyük olmalı.");
        }

        // URT-D72: the first entry carries no reason; a correction always carries one, trimmed and within the column.
        var trimmedReason = reason?.Trim();
        if (version == FirstVersion
                ? reason is not null
                : string.IsNullOrEmpty(trimmedReason) || trimmedReason.Length > ReasonMaximumLength)
        {
            throw new ArgumentException("Hurda fiyatı düzeltmesinin gerekçesi geçersiz.", nameof(reason));
        }

        if (string.IsNullOrWhiteSpace(recordedBy) || recordedBy.Trim().Length > RecordedByMaximumLength)
        {
            throw new ArgumentException("Hurda fiyatını giren kimliği geçersiz.", nameof(recordedBy));
        }

        return new MonthlyScrapPrice(id, companyId, period, price, version, trimmedReason, recordedBy.Trim(),
            recordedAtUtc.ToUniversalTime());
    }
}

using GoymenERP.Modules.Manufacturing.Domain.Common;

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
/// A zero or negative price is refused (fail-closed). Whether a zero scrap price may be entered is not decided
/// (NEEDS-SINAN): the cost calculator reads a zero value as a value, so this entry door refuses it until it is decided.
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
/// The company's scrap price of one month (URT-D47, URT-D65). It is recorded once and never changed or deleted here:
/// correcting a recorded month is not decided (NEEDS-SINAN), so a second record for the same month is refused with
/// <see cref="ManufacturingRuleCodes.ScrapPriceAlreadyRecorded"/>. Reading it for a weighing gives the cost calculator's
/// <see cref="ScrapPriceInput"/>: the price when the month has one, <see cref="ScrapPriceInput.Missing"/> otherwise, so a
/// month without a price leaves the cost pending (URT-D47 (2), URT-D65) and never becomes a silent zero.
/// </summary>
public sealed class MonthlyScrapPrice
{
    public const int RecordedByMaximumLength = 128;

    private MonthlyScrapPrice(Guid id, Guid companyId, ScrapPricePeriod period, ScrapPriceTlPerKg price,
        string recordedBy, DateTimeOffset recordedAtUtc)
    {
        Id = id;
        CompanyId = companyId;
        Period = period;
        Price = price;
        RecordedBy = recordedBy;
        RecordedAtUtc = recordedAtUtc;
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public ScrapPricePeriod Period { get; }

    public ScrapPriceTlPerKg Price { get; }

    public string RecordedBy { get; }

    public DateTimeOffset RecordedAtUtc { get; }

    /// <summary>Records a month's price. The caller's company, server actor and server clock; nothing here is defaulted.</summary>
    public static MonthlyScrapPrice Record(Guid id, Guid companyId, ScrapPricePeriod period, ScrapPriceTlPerKg price,
        string recordedBy, DateTimeOffset recordedAtUtc)
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

        if (string.IsNullOrWhiteSpace(recordedBy) || recordedBy.Trim().Length > RecordedByMaximumLength)
        {
            throw new ArgumentException("Hurda fiyatını giren kimliği geçersiz.", nameof(recordedBy));
        }

        return new MonthlyScrapPrice(id, companyId, period, price, recordedBy.Trim(), recordedAtUtc.ToUniversalTime());
    }

    /// <summary>
    /// Rebuilds a stored month's price under the rules that wrote it. A stored value the rules cannot have produced is
    /// refused with <see cref="ManufacturingRuleCodes.StoredRecordInvalid"/>, never repaired.
    /// </summary>
    public static MonthlyScrapPrice Restore(Guid id, Guid companyId, int year, int month, decimal tlPerKg,
        string recordedBy, DateTimeOffset recordedAtUtc)
    {
        try
        {
            return Record(id, companyId, new ScrapPricePeriod(year, month), new ScrapPriceTlPerKg(tlPerKg), recordedBy,
                recordedAtUtc);
        }
        catch (Exception exception) when (exception is ArgumentException or ManufacturingRuleException)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                "Kayıtlı hurda fiyatı kuralların üretebileceği bir değer değil.");
        }
    }

    /// <summary>The cost calculator's input for a weighing of this month (URT-D47 (1)).</summary>
    public ScrapPriceInput ToInput() => new(ScrapPriceReadStatus.Value, Price.Value);
}

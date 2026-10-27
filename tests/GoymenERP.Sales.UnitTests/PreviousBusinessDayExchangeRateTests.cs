using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D54/D55 (DEC-FIN-010): the previous-business-day TCMB selling rate. The day records are SENTETIK:
/// the dates are real calendar dates so the scenarios read naturally, but which day is published or
/// confirmed "no bulletin" is set by each case, never by the resolver. Spec letters:
/// <c>P</c> published with USD→TRY and EUR→TRY, <c>E</c> published with EUR→TRY only,
/// <c>X</c> confirmed no bulletin; a date that is not listed has no record (unknown).
/// </summary>
public sealed class PreviousBusinessDayExchangeRateTests
{
    private static readonly Guid Try = Guid.Parse("10000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Usd = Guid.Parse("10000000-0000-4000-8000-0000000000a2");
    private static readonly Guid Eur = Guid.Parse("10000000-0000-4000-8000-0000000000a3");
    private const ExchangeRateBulletinSource Tcmb = ExchangeRateBulletinSource.Tcmb;

    private const string Unconfirmed = PreviousBusinessDayExchangeRateCodes.DayUnconfirmed;
    private const string Exceeded = PreviousBusinessDayExchangeRateCodes.LookbackExceeded;
    private const string Missing = PreviousBusinessDayExchangeRateCodes.CurrencyMissing;

    public static TheoryData<string, string, string, int, string?, string?, int> Cases => new()
    {
        // name, check day D, day records, cap, expected bulletin date, expected code (+ stoppedAt in the date column), skipped
        { "Monday uses Friday (Sat/Sun confirmed no bulletin)",
            "2026-10-26", "2026-10-23=P;2026-10-24=X;2026-10-25=X", 7, "2026-10-23", null, 2 },
        { "Thursday with a Wednesday holiday uses Tuesday",
            "2026-10-29", "2026-10-27=P;2026-10-28=X", 7, "2026-10-27", null, 1 },
        { "A published arife is a business day",
            "2026-10-30", "2026-10-28=P;2026-10-29=X", 7, "2026-10-28", null, 1 },
        { "An arife the source did not publish is skipped like any holiday",
            "2026-10-30", "2026-10-27=P;2026-10-28=X;2026-10-29=X", 7, "2026-10-27", null, 2 },
        { "Multi-day bayram plus a bridge day, exactly at the cap",
            "2026-06-01", "2026-05-22=P;2026-05-23=X;2026-05-24=X;2026-05-25=X;2026-05-26=X;2026-05-27=X;2026-05-28=X;2026-05-29=X;2026-05-30=X;2026-05-31=X",
            10, "2026-05-22", null, 9 },
        { "Multi-day bayram plus a bridge day, one day past the cap",
            "2026-06-01", "2026-05-22=P;2026-05-23=X;2026-05-24=X;2026-05-25=X;2026-05-26=X;2026-05-27=X;2026-05-28=X;2026-05-29=X;2026-05-30=X;2026-05-31=X",
            9, "2026-05-23", Exceeded, 9 },
        { "A cap of one with a confirmed no-bulletin D-1 is exceeded",
            "2026-10-26", "2026-10-23=P;2026-10-24=X;2026-10-25=X", 1, "2026-10-25", Exceeded, 1 },
        { "An unconfirmed gap stops the walk before an older published day",
            "2026-10-26", "2026-10-23=P;2026-10-25=X", 7, "2026-10-24", Unconfirmed, 1 },
        { "An unknown D-1 stops the walk at once",
            "2026-10-27", "2026-10-23=P;2026-10-24=X;2026-10-25=X", 7, "2026-10-26", Unconfirmed, 0 },
        { "An unconfirmed gap wins over a cap that would be reached later",
            "2026-10-26", "2026-10-25=X", 2, "2026-10-24", Unconfirmed, 1 },
        { "D's own bulletin is never used",
            "2026-10-27", "2026-10-26=P;2026-10-27=P;2026-10-28=P", 7, "2026-10-26", null, 0 },
        { "D's own bulletin does not fill an unknown D-1",
            "2026-10-27", "2026-10-23=P;2026-10-27=P", 7, "2026-10-26", Unconfirmed, 0 },
        { "The pair missing on the nearest published day never falls back",
            "2026-10-26", "2026-10-22=P;2026-10-23=E;2026-10-24=X;2026-10-25=X", 7, "2026-10-23", Missing, 2 },
        { "D on a holiday itself uses the day before",
            "2026-10-29", "2026-10-28=P;2026-10-29=X", 7, "2026-10-28", null, 0 },
        { "D on a Sunday uses Friday",
            "2026-10-25", "2026-10-23=P;2026-10-24=X;2026-10-25=X", 7, "2026-10-23", null, 1 },
        { "Year boundary: Jan 2 after a New Year holiday uses Dec 31",
            "2026-01-02", "2025-12-31=P;2026-01-01=X", 7, "2025-12-31", null, 1 },
        { "Year boundary: first Monday uses Jan 2",
            "2026-01-05", "2025-12-31=P;2026-01-01=X;2026-01-02=P;2026-01-03=X;2026-01-04=X", 7, "2026-01-02", null, 2 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_rate_comes_from_the_nearest_published_day_strictly_before_D(
        string name, string checkDate, string records, int cap, string? expectedDate, string? expectedCode, int expectedSkipped)
    {
        var request = UsdToTry(Day(checkDate), cap);
        var result = PreviousBusinessDayExchangeRate.Resolve(request, Days(records));

        Assert.Equal(Day(checkDate), result.CheckDate);
        Assert.Equal(Tcmb, result.Source);
        Assert.Equal(expectedSkipped, result.SkippedDays);
        if (expectedCode is null)
        {
            Assert.True(result.IsResolved, name);
            Assert.Null(result.Code);
            Assert.Null(result.StoppedAt);
            Assert.Equal(Day(expectedDate!), result.BulletinDate);
            Assert.Equal(ExchangeRate.Of(Usd, Try, UsdRateOf(Day(expectedDate!))), result.Rate);
        }
        else
        {
            Assert.False(result.IsResolved, name);
            Assert.Equal(expectedCode, result.Code);
            Assert.Equal(Day(expectedDate!), result.StoppedAt);
            Assert.Null(result.BulletinDate);
            Assert.Null(result.Rate);
        }
    }

    [Fact]
    public void The_rate_is_the_published_selling_rate_of_the_requested_pair_only()
    {
        var days = Days("2026-10-23=P;2026-10-24=X;2026-10-25=X");
        var eur = PreviousBusinessDayExchangeRate.Resolve(
            new PreviousBusinessDayExchangeRateRequest(Day("2026-10-26"), Tcmb, Eur, Try, 7), days);
        Assert.Equal(ExchangeRate.Of(Eur, Try, EurRateOf(Day("2026-10-23"))), eur.Rate);

        // The inverse pair is its own published rate; it is never derived as 1 / rate.
        var inverse = PreviousBusinessDayExchangeRate.Resolve(
            new PreviousBusinessDayExchangeRateRequest(Day("2026-10-26"), Tcmb, Try, Usd, 7), days);
        Assert.Equal((false, Missing, Day("2026-10-23")), (inverse.IsResolved, inverse.Code, inverse.StoppedAt));
    }

    [Fact]
    public void The_answer_is_deterministic_whatever_the_record_order()
    {
        var records = "2026-05-22=P;2026-05-23=X;2026-05-24=X;2026-05-25=X;2026-05-26=X;2026-05-27=X;2026-05-28=X;"
            + "2026-05-29=X;2026-05-30=X;2026-05-31=X;2026-06-01=P";
        var ordered = Days(records).ToArray();
        var request = UsdToTry(Day("2026-06-01"), 10);
        var expected = PreviousBusinessDayExchangeRate.Resolve(request, ordered);
        Assert.Equal((Day("2026-05-22"), 9), (expected.BulletinDate, expected.SkippedDays));

        var random = new Random(20261027);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var shuffled = ordered.OrderBy(_ => random.Next()).ToArray();
            Assert.Equal(expected, PreviousBusinessDayExchangeRate.Resolve(request, shuffled));
        }

        Assert.Equal(expected, PreviousBusinessDayExchangeRate.Resolve(request, Enumerable.Reverse(ordered)));
    }

    [Fact]
    public void D_is_the_istanbul_business_date_the_caller_derives_not_the_utc_date()
    {
        // SAT-D10: an instant late on Monday UTC is already Tuesday in Europe/Istanbul. The resolver takes the
        // derived DateOnly; deriving it from the UTC calendar would pick the wrong (older) bulletin.
        var instant = new DateTimeOffset(2026, 10, 26, 22, 0, 0, TimeSpan.Zero);
        var istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var istanbulDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, istanbul).DateTime);
        var utcDate = DateOnly.FromDateTime(instant.UtcDateTime);
        Assert.Equal((Day("2026-10-27"), Day("2026-10-26")), (istanbulDate, utcDate));

        var days = Days("2026-10-23=P;2026-10-24=X;2026-10-25=X;2026-10-26=P").ToArray();
        Assert.Equal(Day("2026-10-26"), PreviousBusinessDayExchangeRate.Resolve(UsdToTry(istanbulDate, 7), days).BulletinDate);
        Assert.Equal(Day("2026-10-23"), PreviousBusinessDayExchangeRate.Resolve(UsdToTry(utcDate, 7), days).BulletinDate);
    }

    [Fact]
    public void The_walk_never_goes_below_the_first_calendar_day()
    {
        var result = PreviousBusinessDayExchangeRate.Resolve(UsdToTry(DateOnly.MinValue, 7), []);
        Assert.Equal((false, Unconfirmed, (DateOnly?)null, 0), (result.IsResolved, result.Code, result.StoppedAt, result.SkippedDays));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void The_lookback_cap_has_no_default_and_must_be_positive(int cap) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PreviousBusinessDayExchangeRate.Resolve(UsdToTry(Day("2026-10-26"), cap), []));

    [Fact]
    public void The_request_and_the_records_are_validated_before_the_walk()
    {
        var monday = Day("2026-10-26");
        Assert.Throws<ArgumentException>(() => PreviousBusinessDayExchangeRate.Resolve(
            new PreviousBusinessDayExchangeRateRequest(monday, Tcmb, Usd, Usd, 7), []));
        Assert.Throws<ArgumentException>(() => PreviousBusinessDayExchangeRate.Resolve(
            new PreviousBusinessDayExchangeRateRequest(monday, Tcmb, Guid.Empty, Try, 7), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => PreviousBusinessDayExchangeRate.Resolve(
            new PreviousBusinessDayExchangeRateRequest(monday, (ExchangeRateBulletinSource)0, Usd, Try, 7), []));

        var duplicate = Days("2026-10-23=P").Concat([ExchangeRateBulletinDay.ConfirmedNoBulletin(Day("2026-10-23"), Tcmb)]);
        Assert.Throws<ArgumentException>(() => PreviousBusinessDayExchangeRate.Resolve(UsdToTry(monday, 7), duplicate));

        Assert.Throws<ArgumentOutOfRangeException>(() => ExchangeRateBulletinDay.ConfirmedNoBulletin(monday, (ExchangeRateBulletinSource)99));
        Assert.Throws<ArgumentException>(() => ExchangeRateBulletinDay.Published(monday, Tcmb, []));
        Assert.Throws<ArgumentException>(() => ExchangeRateBulletinDay.Published(monday, Tcmb,
            [ExchangeRate.Of(Usd, Try, 41m), ExchangeRate.Of(Usd, Try, 42m)]));
    }

    [Fact]
    public void A_confirmed_no_bulletin_day_carries_no_rate()
    {
        var day = ExchangeRateBulletinDay.ConfirmedNoBulletin(Day("2026-10-24"), Tcmb);
        Assert.Equal((ExchangeRateBulletinDayStatus.ConfirmedNoBulletin, 0), (day.Status, day.SellingRates.Count));
    }

    private static PreviousBusinessDayExchangeRateRequest UsdToTry(DateOnly checkDate, int cap) =>
        new(checkDate, Tcmb, Usd, Try, cap);

    private static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // A distinct rate per day so a test proves which day's bulletin was chosen.
    private static decimal UsdRateOf(DateOnly date) => 41.0000m + date.DayNumber % 1000 / 10000m;

    private static decimal EurRateOf(DateOnly date) => 48.0000m + date.DayNumber % 1000 / 10000m;

    private static IEnumerable<ExchangeRateBulletinDay> Days(string records) =>
        records.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(record =>
        {
            var parts = record.Split('=');
            var date = Day(parts[0]);
            return parts[1] switch
            {
                "P" => ExchangeRateBulletinDay.Published(date, Tcmb,
                    [ExchangeRate.Of(Usd, Try, UsdRateOf(date)), ExchangeRate.Of(Eur, Try, EurRateOf(date))]),
                "E" => ExchangeRateBulletinDay.Published(date, Tcmb, [ExchangeRate.Of(Eur, Try, EurRateOf(date))]),
                "X" => ExchangeRateBulletinDay.ConfirmedNoBulletin(date, Tcmb),
                _ => throw new ArgumentException($"Unknown day spec '{record}'."),
            };
        }).ToArray();
}

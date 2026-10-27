using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D54/D55 (DEC-FIN-010): the previous-business-day TCMB selling rate. The day records are SENTETIK:
/// the dates are real calendar dates so the scenarios read naturally, but which day is published or
/// confirmed "no bulletin" is set by each case, never by the resolver. Spec letters (joined with <c>+</c>
/// when a day has an official and a manual record):
/// <c>P</c> official published with USD→TRY and EUR→TRY, <c>E</c> official published with EUR→TRY only,
/// <c>X</c> official definite "no bulletin", <c>U</c> official attempt without a definite answer,
/// <c>M</c> manual "no bulletin", <c>R</c> manual USD→TRY rate. A date that is not listed has no record.
/// </summary>
public sealed class PreviousBusinessDayExchangeRateTests
{
    private static readonly Guid Try = Guid.Parse("10000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Usd = Guid.Parse("10000000-0000-4000-8000-0000000000a2");
    private static readonly Guid Eur = Guid.Parse("10000000-0000-4000-8000-0000000000a3");
    private static readonly ExchangeRateManualEntry ManualEntry =
        new(Guid.Parse("10000000-0000-4000-8000-0000000000b1"), "SENTETIK gerekçe");
    private const ExchangeRateBulletinSource Tcmb = ExchangeRateBulletinSource.Tcmb;

    private const string Unconfirmed = PreviousBusinessDayExchangeRateCodes.DayUnconfirmed;
    private const string Missing = PreviousBusinessDayExchangeRateCodes.CurrencyMissing;

    private const string Bayram = "2026-05-22=P;2026-05-23=X;2026-05-24=X;2026-05-25=X;2026-05-26=X;2026-05-27=X;"
        + "2026-05-28=X;2026-05-29=X;2026-05-30=X;2026-05-31=X";

    public static TheoryData<string, string, string, string?, string?, int> OfficialCases => new()
    {
        // name, check day D, day records, expected bulletin date (or stoppedAt when a code is expected), code, skipped
        { "Monday uses Friday (Sat/Sun confirmed no bulletin)",
            "2026-10-26", "2026-10-23=P;2026-10-24=X;2026-10-25=X", "2026-10-23", null, 2 },
        { "Thursday with a Wednesday holiday uses Tuesday",
            "2026-10-29", "2026-10-27=P;2026-10-28=X", "2026-10-27", null, 1 },
        { "A published arife is a business day",
            "2026-10-30", "2026-10-28=P;2026-10-29=X", "2026-10-28", null, 1 },
        { "An arife the source did not publish is skipped like any holiday",
            "2026-10-30", "2026-10-27=P;2026-10-28=X;2026-10-29=X", "2026-10-27", null, 2 },
        { "Multi-day bayram plus a bridge day needs no lookback setting",
            "2026-06-01", Bayram, "2026-05-22", null, 9 },
        { "An unknown weekend day (no record) stops the walk",
            "2026-10-26", "2026-10-23=P;2026-10-25=X", "2026-10-24", Unconfirmed, 1 },
        { "An unconfirmed weekend day (no definite answer) stops the walk",
            "2026-10-26", "2026-10-23=P;2026-10-24=U;2026-10-25=X", "2026-10-24", Unconfirmed, 1 },
        { "An unconfirmed weekday stops the walk before an older published day",
            "2026-10-27", "2026-10-23=P;2026-10-24=X;2026-10-25=X;2026-10-26=U", "2026-10-26", Unconfirmed, 0 },
        { "An unknown day inside a bayram stops the walk",
            "2026-06-01", Bayram.Replace("2026-05-27=X;", string.Empty, StringComparison.Ordinal), "2026-05-27", Unconfirmed, 4 },
        { "An unknown D-1 stops the walk at once",
            "2026-10-27", "2026-10-23=P;2026-10-24=X;2026-10-25=X", "2026-10-26", Unconfirmed, 0 },
        { "No records at all stop the walk at D-1",
            "2026-10-27", "", "2026-10-26", Unconfirmed, 0 },
        { "D's own bulletin is never used",
            "2026-10-27", "2026-10-26=P;2026-10-27=P;2026-10-28=P", "2026-10-26", null, 0 },
        { "D's own bulletin does not fill an unknown D-1",
            "2026-10-27", "2026-10-23=P;2026-10-27=P", "2026-10-26", Unconfirmed, 0 },
        { "The pair missing on the nearest published day never steps back",
            "2026-10-26", "2026-10-22=P;2026-10-23=E;2026-10-24=X;2026-10-25=X", "2026-10-23", Missing, 2 },
        { "The pair missing on D-1 never steps back",
            "2026-10-27", "2026-10-23=P;2026-10-24=X;2026-10-25=X;2026-10-26=E", "2026-10-26", Missing, 0 },
        { "D on a holiday itself uses the day before",
            "2026-10-29", "2026-10-28=P;2026-10-29=X", "2026-10-28", null, 0 },
        { "D on a Sunday uses Friday",
            "2026-10-25", "2026-10-23=P;2026-10-24=X;2026-10-25=X", "2026-10-23", null, 1 },
        { "Year boundary: Jan 2 after a New Year holiday uses Dec 31",
            "2026-01-02", "2025-12-31=P;2026-01-01=X", "2025-12-31", null, 1 },
        { "Year boundary: first Monday uses Jan 2",
            "2026-01-05", "2025-12-31=P;2026-01-01=X;2026-01-02=P;2026-01-03=X;2026-01-04=X", "2026-01-02", null, 2 },
    };

    [Theory]
    [MemberData(nameof(OfficialCases))]
    public void The_rate_comes_from_the_nearest_published_day_strictly_before_D(
        string name, string checkDate, string records, string? expectedDate, string? expectedCode, int expectedSkipped)
    {
        var result = PreviousBusinessDayExchangeRate.Resolve(UsdToTry(Day(checkDate)), Days(records));

        Assert.Equal(Day(checkDate), result.CheckDate);
        Assert.Equal(Tcmb, result.Source);
        Assert.Equal((expectedSkipped, 0), (result.SkippedDays, result.ManualNoBulletinDaysSkipped));
        if (expectedCode is null)
        {
            Assert.True(result.IsResolved, name);
            Assert.Null(result.Code);
            Assert.Null(result.StoppedAt);
            Assert.Equal(Day(expectedDate!), result.BulletinDate);
            Assert.Equal(ExchangeRate.Of(Usd, Try, UsdRateOf(Day(expectedDate!))), result.Rate);
            Assert.Equal(ExchangeRateBulletinDayProvenance.Official, result.RateProvenance);
        }
        else
        {
            Assert.False(result.IsResolved, name);
            Assert.Equal(expectedCode, result.Code);
            Assert.Equal(Day(expectedDate!), result.StoppedAt);
            Assert.Null(result.BulletinDate);
            Assert.Null(result.Rate);
            Assert.Null(result.RateProvenance);
        }
    }

    public static TheoryData<string, string, string, bool, string, bool, int, int> ManualCases => new()
    {
        // name, D, records, ask EUR?, expected bulletin date, expected manual rate?, skipped, manual skipped
        { "A manual 'no bulletin' over an official published day moves to an older rate",
            "2026-10-26", "2026-10-22=P;2026-10-23=P+M;2026-10-24=X;2026-10-25=X", false, "2026-10-22", false, 3, 1 },
        { "A manual 'no bulletin' settles an unconfirmed day",
            "2026-10-26", "2026-10-23=P;2026-10-24=U+M;2026-10-25=X", false, "2026-10-23", false, 2, 1 },
        { "A manual rate is used next to the official bulletin",
            "2026-10-26", "2026-10-23=P+R;2026-10-24=X;2026-10-25=X", false, "2026-10-23", true, 2, 0 },
        { "A manual rate for one pair keeps the official rate of another pair",
            "2026-10-26", "2026-10-23=P+R;2026-10-24=X;2026-10-25=X", true, "2026-10-23", false, 2, 0 },
        { "A manual rate on an unconfirmed day is that day's rate",
            "2026-10-26", "2026-10-23=U+R;2026-10-24=X;2026-10-25=X", false, "2026-10-23", true, 2, 0 },
    };

    [Theory]
    [MemberData(nameof(ManualCases))]
    public void A_manual_record_is_a_dec_fin_010_manual_change_next_to_the_official_one(
        string name, string checkDate, string records, bool askEur, string expectedDate, bool expectManualRate, int skipped, int manualSkipped)
    {
        var currency = askEur ? Eur : Usd;
        var result = PreviousBusinessDayExchangeRate.Resolve(
            new PreviousBusinessDayExchangeRateRequest(Day(checkDate), Tcmb, currency, Try), Days(records));

        Assert.True(result.IsResolved, name);
        var date = Day(expectedDate);
        var expectedRate = expectManualRate ? ManualUsdRateOf(date) : askEur ? EurRateOf(date) : UsdRateOf(date);
        Assert.Equal(date, result.BulletinDate);
        Assert.Equal(ExchangeRate.Of(currency, Try, expectedRate), result.Rate);
        Assert.Equal(expectManualRate ? ExchangeRateBulletinDayProvenance.Manual : ExchangeRateBulletinDayProvenance.Official,
            result.RateProvenance);
        Assert.Equal((skipped, manualSkipped), (result.SkippedDays, result.ManualNoBulletinDaysSkipped));
    }

    [Fact]
    public void A_manual_rate_without_the_pair_on_an_unconfirmed_day_is_a_missing_pair()
    {
        var result = PreviousBusinessDayExchangeRate.Resolve(
            new PreviousBusinessDayExchangeRateRequest(Day("2026-10-26"), Tcmb, Eur, Try),
            Days("2026-10-22=P;2026-10-23=U+R;2026-10-24=X;2026-10-25=X"));
        Assert.Equal((false, Missing, Day("2026-10-23")), (result.IsResolved, result.Code, result.StoppedAt));
    }

    [Fact]
    public void Manual_records_carry_an_actor_and_a_reason()
    {
        var day = Day("2026-10-23");
        var manual = ExchangeRateBulletinDay.ManualNoBulletin(day, Tcmb, ManualEntry);
        Assert.Equal((ExchangeRateBulletinDayProvenance.Manual, ManualEntry), (manual.Provenance, manual.ManualEntry));
        var official = ExchangeRateBulletinDay.ConfirmedNoBulletin(day, Tcmb);
        Assert.Equal((ExchangeRateBulletinDayProvenance.Official, (ExchangeRateManualEntry?)null), (official.Provenance, official.ManualEntry));

        Assert.Throws<ArgumentException>(() => new ExchangeRateManualEntry(Guid.Empty, "gerekçe"));
        Assert.Throws<ArgumentException>(() => new ExchangeRateManualEntry(ManualEntry.ActorUserId, "  "));
        Assert.Throws<ArgumentNullException>(() => ExchangeRateBulletinDay.ManualNoBulletin(day, Tcmb, null!));
        Assert.Throws<ArgumentNullException>(() => ExchangeRateBulletinDay.ManualPublished(day, Tcmb, [ExchangeRate.Of(Usd, Try, 41m)], null!));
    }

    [Fact]
    public void The_rate_is_the_published_selling_rate_of_the_requested_pair_only()
    {
        var days = Days("2026-10-23=P;2026-10-24=X;2026-10-25=X");
        var eur = PreviousBusinessDayExchangeRate.Resolve(
            new PreviousBusinessDayExchangeRateRequest(Day("2026-10-26"), Tcmb, Eur, Try), days);
        Assert.Equal(ExchangeRate.Of(Eur, Try, EurRateOf(Day("2026-10-23"))), eur.Rate);

        // The inverse pair is its own published rate; it is never derived as 1 / rate.
        var inverse = PreviousBusinessDayExchangeRate.Resolve(
            new PreviousBusinessDayExchangeRateRequest(Day("2026-10-26"), Tcmb, Try, Usd), days);
        Assert.Equal((false, Missing, Day("2026-10-23")), (inverse.IsResolved, inverse.Code, inverse.StoppedAt));
    }

    [Fact]
    public void The_answer_is_deterministic_whatever_the_record_order()
    {
        var ordered = Days(Bayram + ";2026-05-26=M;2026-06-01=P;2026-05-22=R").ToArray();
        var request = UsdToTry(Day("2026-06-01"));
        var expected = PreviousBusinessDayExchangeRate.Resolve(request, ordered);
        Assert.Equal((Day("2026-05-22"), 9, 1, ExchangeRateBulletinDayProvenance.Manual),
            (expected.BulletinDate, expected.SkippedDays, expected.ManualNoBulletinDaysSkipped, expected.RateProvenance));

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
        Assert.Equal(Day("2026-10-26"), PreviousBusinessDayExchangeRate.Resolve(UsdToTry(istanbulDate), days).BulletinDate);
        Assert.Equal(Day("2026-10-23"), PreviousBusinessDayExchangeRate.Resolve(UsdToTry(utcDate), days).BulletinDate);
    }

    [Fact]
    public void The_walk_never_goes_below_the_first_calendar_day()
    {
        var result = PreviousBusinessDayExchangeRate.Resolve(UsdToTry(DateOnly.MinValue), []);
        Assert.Equal((false, Unconfirmed, (DateOnly?)null, 0), (result.IsResolved, result.Code, result.StoppedAt, result.SkippedDays));

        var second = DateOnly.MinValue.AddDays(1);
        var onlyNoBulletin = PreviousBusinessDayExchangeRate.Resolve(
            UsdToTry(second.AddDays(1)), [ExchangeRateBulletinDay.ConfirmedNoBulletin(DateOnly.MinValue, Tcmb),
                ExchangeRateBulletinDay.ConfirmedNoBulletin(second, Tcmb)]);
        Assert.Equal((false, Unconfirmed, (DateOnly?)null, 2),
            (onlyNoBulletin.IsResolved, onlyNoBulletin.Code, onlyNoBulletin.StoppedAt, onlyNoBulletin.SkippedDays));
    }

    [Fact]
    public void The_request_and_the_records_are_validated_before_the_walk()
    {
        var monday = Day("2026-10-26");
        Assert.Throws<ArgumentException>(() => PreviousBusinessDayExchangeRate.Resolve(
            new PreviousBusinessDayExchangeRateRequest(monday, Tcmb, Usd, Usd), []));
        Assert.Throws<ArgumentException>(() => PreviousBusinessDayExchangeRate.Resolve(
            new PreviousBusinessDayExchangeRateRequest(monday, Tcmb, Guid.Empty, Try), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => PreviousBusinessDayExchangeRate.Resolve(
            new PreviousBusinessDayExchangeRateRequest(monday, (ExchangeRateBulletinSource)0, Usd, Try), []));

        Assert.Throws<ArgumentException>(() => PreviousBusinessDayExchangeRate.Resolve(UsdToTry(monday), Days("2026-10-23=P+X")));
        Assert.Throws<ArgumentException>(() => PreviousBusinessDayExchangeRate.Resolve(UsdToTry(monday), Days("2026-10-23=M+R")));

        Assert.Throws<ArgumentOutOfRangeException>(() => ExchangeRateBulletinDay.ConfirmedNoBulletin(monday, (ExchangeRateBulletinSource)99));
        Assert.Throws<ArgumentException>(() => ExchangeRateBulletinDay.Published(monday, Tcmb, []));
        Assert.Throws<ArgumentException>(() => ExchangeRateBulletinDay.Published(monday, Tcmb,
            [ExchangeRate.Of(Usd, Try, 41m), ExchangeRate.Of(Usd, Try, 42m)]));
    }

    [Fact]
    public void Only_a_published_record_carries_rates()
    {
        var date = Day("2026-10-24");
        Assert.Equal((ExchangeRateBulletinDayStatus.ConfirmedNoBulletin, 0),
            (ExchangeRateBulletinDay.ConfirmedNoBulletin(date, Tcmb).Status, ExchangeRateBulletinDay.ConfirmedNoBulletin(date, Tcmb).SellingRates.Count));
        Assert.Equal((ExchangeRateBulletinDayStatus.Unconfirmed, 0),
            (ExchangeRateBulletinDay.Unconfirmed(date, Tcmb).Status, ExchangeRateBulletinDay.Unconfirmed(date, Tcmb).SellingRates.Count));
    }

    private static PreviousBusinessDayExchangeRateRequest UsdToTry(DateOnly checkDate) => new(checkDate, Tcmb, Usd, Try);

    private static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // A distinct rate per day (and per provenance) so a test proves which record was chosen.
    private static decimal UsdRateOf(DateOnly date) => 41.0000m + date.DayNumber % 1000 / 10000m;

    private static decimal ManualUsdRateOf(DateOnly date) => 39.0000m + date.DayNumber % 1000 / 10000m;

    private static decimal EurRateOf(DateOnly date) => 48.0000m + date.DayNumber % 1000 / 10000m;

    private static IEnumerable<ExchangeRateBulletinDay> Days(string records) =>
        records.Split(';', StringSplitOptions.RemoveEmptyEntries).SelectMany(record =>
        {
            var parts = record.Split('=');
            var date = Day(parts[0]);
            return parts[1].Split('+').Select(letter => letter switch
            {
                "P" => ExchangeRateBulletinDay.Published(date, Tcmb,
                    [ExchangeRate.Of(Usd, Try, UsdRateOf(date)), ExchangeRate.Of(Eur, Try, EurRateOf(date))]),
                "E" => ExchangeRateBulletinDay.Published(date, Tcmb, [ExchangeRate.Of(Eur, Try, EurRateOf(date))]),
                "X" => ExchangeRateBulletinDay.ConfirmedNoBulletin(date, Tcmb),
                "U" => ExchangeRateBulletinDay.Unconfirmed(date, Tcmb),
                "M" => ExchangeRateBulletinDay.ManualNoBulletin(date, Tcmb, ManualEntry),
                "R" => ExchangeRateBulletinDay.ManualPublished(date, Tcmb, [ExchangeRate.Of(Usd, Try, ManualUsdRateOf(date))], ManualEntry),
                _ => throw new ArgumentException($"Unknown day spec '{record}'."),
            });
        }).ToArray();
}

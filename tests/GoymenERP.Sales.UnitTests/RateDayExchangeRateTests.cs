using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// DEC-FIN-010 with SAT-D54/D55 and SAT-D170/D171: the rate-day calculator. The records are SENTETIK (real calendar dates so
/// the scenarios read naturally; which day is published, "no bulletin", half or full is set by each case). Spec: each entry is
/// <c>date=B/H</c> where B is <c>P</c> (official published with USD→TRY at a rate equal to the day of month), <c>X</c>
/// (official definite "no bulletin"), <c>U</c> (official unconfirmed) or <c>-</c> (no bulletin record), optionally <c>+R</c>
/// (manual USD→TRY rate of 99 next to it); and H is <c>F</c> (full day), <c>H</c> (arife / half day), <c>?</c> (undetermined)
/// or <c>-</c> (no mark).
/// </summary>
public sealed class RateDayExchangeRateTests
{
    private static readonly Guid Try = Guid.Parse("10000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Usd = Guid.Parse("10000000-0000-4000-8000-0000000000a2");
    private static readonly Guid Eur = Guid.Parse("10000000-0000-4000-8000-0000000000a3");
    private const string Undetermined = RateDayExchangeRateCodes.HalfDayUndetermined;
    private const string Unconfirmed = PreviousBusinessDayExchangeRateCodes.DayUnconfirmed;
    private const string Missing = PreviousBusinessDayExchangeRateCodes.CurrencyMissing;
    private const string Conflict = PreviousBusinessDayExchangeRateCodes.ManualConflict;

    // Oct 30 (Fri) .. Nov 10 (Tue) 2026, every day published or definitely not, every day marked full.
    private const string Fortnight = "2026-10-30=P/F;2026-10-31=X/F;2026-11-01=X/F;2026-11-02=P/F;2026-11-03=P/F;"
        + "2026-11-04=P/F;2026-11-05=P/F;2026-11-06=P/F;2026-11-07=X/F;2026-11-08=X/F;2026-11-09=P/F;2026-11-10=P/F";

    public static TheoryData<string, string, string, string?, string?, bool, string?> GoldenCases => new()
    {
        // name, rate day D, records, expected bulletin date (or stoppedAt when a code is expected), code, half-day rule, F
        { "Golden SAT-D54: Monday uses Friday", "2026-11-09", Fortnight, "2026-11-06", null, false, null },
        { "Golden SAT-D54: Thursday with a Wednesday holiday uses Tuesday",
            "2026-11-05", With(Fortnight, "2026-11-04=X/F"), "2026-11-03", null, false, null },
        { "Golden SAT-D170: a Tuesday arife uses Friday (full Monday's Resmî Gazete rate)",
            "2026-11-10", With(Fortnight, "2026-11-10=P/H"), "2026-11-06", null, true, "2026-11-09" },
        { "Golden SAT-D170: Monday and Tuesday half days, Tuesday uses Thursday",
            "2026-11-10", With(Fortnight, "2026-11-09=P/H;2026-11-10=P/H"), "2026-11-05", null, true, "2026-11-06" },
        { "Golden SAT-D170: shipped 04.11.2026 uses the rate announced 03.11.2026", "2026-11-04", Fortnight, "2026-11-03", null,
            false, null },
        { "A Monday shipment invoiced on a Tuesday arife is normal: the rate day is Monday",
            "2026-11-09", With(Fortnight, "2026-11-10=P/H"), "2026-11-06", null, false, null },
        { "The first business day after a bayram is plain SAT-D54 (yesterday's arife bulletin)",
            "2026-11-05", With(Fortnight, "2026-11-04=P/H"), "2026-11-04", null, false, null },
        { "An arife on a day TCMB did not publish still walks to the previous full business day",
            "2026-11-10", With(Fortnight, "2026-11-10=X/H"), "2026-11-06", null, true, "2026-11-09" },
        { "An arife right after a weekend: F is Friday, the rate Thursday's",
            "2026-11-09", With(Fortnight, "2026-11-09=P/H"), "2026-11-05", null, true, "2026-11-06" },
        { "An unmarked rate day is undetermined (SAT-D171)",
            "2026-11-10", With(Fortnight, "2026-11-10=P/-"), "2026-11-10", Undetermined, false, null },
        { "An undetermined rate day is undetermined (SAT-D171)",
            "2026-11-10", With(Fortnight, "2026-11-10=P/?"), "2026-11-10", Undetermined, false, null },
        { "An undetermined published day on the walk to F stops it",
            "2026-11-10", With(Fortnight, "2026-11-10=P/H;2026-11-09=P/?"), "2026-11-09", Undetermined, true, null },
        { "An unmarked published day on the walk to F stops it",
            "2026-11-10", With(Fortnight, "2026-11-10=P/H;2026-11-09=P/-"), "2026-11-09", Undetermined, true, null },
        { "An unconfirmed day on the walk to F stops it",
            "2026-11-10", With(Fortnight, "2026-11-10=P/H;2026-11-09=U/F"), "2026-11-09", Unconfirmed, true, null },
        { "A day without a bulletin record on the walk to F stops it",
            "2026-11-10", With(Fortnight, "2026-11-10=P/H;2026-11-09=-/F"), "2026-11-09", Unconfirmed, true, null },
        { "An unconfirmed day between F and its previous business day stops it",
            "2026-11-10", With(Fortnight, "2026-11-10=P/H;2026-11-08=U/F"), "2026-11-08", Unconfirmed, true, "2026-11-09" },
        { "A full-day rate day with an unconfirmed D-1 stops (SAT-D55)",
            "2026-11-10", With(Fortnight, "2026-11-09=U/F"), "2026-11-09", Unconfirmed, false, null },
        { "Weekend no-bulletin days need no half-day mark",
            "2026-11-09", With(Fortnight, "2026-11-07=X/-;2026-11-08=X/?"), "2026-11-06", null, false, null },
        { "A manual rate next to an official published day is honoured on the walk",
            "2026-11-10", With(Fortnight, "2026-11-10=P/H;2026-11-06=P+R/F"), "2026-11-06", null, true, "2026-11-09" },
        { "A manual rate next to an official no-bulletin day stops the walk to F",
            "2026-11-10", With(Fortnight, "2026-11-10=P/H;2026-11-09=X+R/F"), "2026-11-09", Conflict, true, null },
    };

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void The_rate_day_rule_is_deterministic(string name, string rateDay, string records, string? expectedDate,
        string? expectedCode, bool halfDayRule, string? previousFullBusinessDay)
    {
        var (days, marks) = Parse(records);
        var result = RateDayExchangeRate.Resolve(UsdToTry(Day(rateDay)), days, marks);

        Assert.True(expectedCode == result.Code, $"{name}: code {result.Code}");
        Assert.Equal(Day(rateDay), result.RateDay);
        Assert.Equal(halfDayRule, result.HalfDayRuleApplied);
        Assert.Equal(previousFullBusinessDay is null ? null : Day(previousFullBusinessDay), result.PreviousFullBusinessDay);
        if (expectedCode is null)
        {
            Assert.True(result.IsResolved, name);
            Assert.Equal(Day(expectedDate!), result.BulletinDate);
            Assert.Null(result.StoppedAt);
            var expectedRate = records.Contains($"{expectedDate}=P+R", StringComparison.Ordinal) ? 99m : Day(expectedDate!).Day;
            Assert.Equal(expectedRate, result.Rate!.Value.Rate);
            Assert.Equal(expectedRate == 99m ? ExchangeRateBulletinDayProvenance.Manual : ExchangeRateBulletinDayProvenance.Official,
                result.RateProvenance);
        }
        else
        {
            Assert.False(result.IsResolved, name);
            Assert.Equal(expectedDate is null ? null : Day(expectedDate), result.StoppedAt);
            Assert.Null(result.Rate);
            Assert.Null(result.BulletinDate);
        }
    }

    [Fact]
    public void A_currency_missing_on_the_rate_bulletin_never_steps_back()
    {
        var (days, marks) = Parse(Fortnight);
        var result = RateDayExchangeRate.Resolve(
            new RateDayExchangeRateRequest(Day("2026-11-09"), ExchangeRateBulletinSource.Tcmb, Eur, Try), days, marks);

        Assert.Equal(Missing, result.Code);
        Assert.Equal(Day("2026-11-06"), result.StoppedAt);
    }

    [Fact]
    public void The_order_of_records_never_changes_the_answer()
    {
        var (days, marks) = Parse(With(Fortnight, "2026-11-09=P/H;2026-11-10=P/H"));
        var forward = RateDayExchangeRate.Resolve(UsdToTry(Day("2026-11-10")), days, marks);
        var backward = RateDayExchangeRate.Resolve(UsdToTry(Day("2026-11-10")), days.AsEnumerable().Reverse().ToArray(),
            marks.AsEnumerable().Reverse().ToArray());

        Assert.Equal(forward, backward);
    }

    [Fact]
    public void Two_marks_for_one_day_are_refused()
    {
        var (days, _) = Parse(Fortnight);
        Assert.Throws<ArgumentException>(() => RateDayExchangeRate.Resolve(UsdToTry(Day("2026-11-10")), days,
            [new ExchangeRateHalfDayMark(Day("2026-11-10"), ExchangeRateHalfDayStatus.FullDay),
                new ExchangeRateHalfDayMark(Day("2026-11-10"), ExchangeRateHalfDayStatus.HalfDay)]));
    }

    [Fact]
    public void An_undefined_mark_is_refused()
    {
        var (days, _) = Parse(Fortnight);
        Assert.Throws<ArgumentOutOfRangeException>(() => RateDayExchangeRate.Resolve(UsdToTry(Day("2026-11-10")), days,
            [new ExchangeRateHalfDayMark(Day("2026-11-10"), (ExchangeRateHalfDayStatus)9)]));
    }

    [Fact]
    public void An_invalid_request_is_refused_even_on_a_half_day()
    {
        var (days, marks) = Parse(With(Fortnight, "2026-11-10=P/H"));
        Assert.Throws<ArgumentException>(() => RateDayExchangeRate.Resolve(
            new RateDayExchangeRateRequest(Day("2026-11-10"), ExchangeRateBulletinSource.Tcmb, Usd, Usd), days, marks));
    }

    private static RateDayExchangeRateRequest UsdToTry(DateOnly day) =>
        new(day, ExchangeRateBulletinSource.Tcmb, Usd, Try);

    private static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd");

    private static string With(string records, string overrides)
    {
        var map = records.Split(';').Select(entry => entry.Split('=')).ToDictionary(parts => parts[0], parts => parts[1]);
        foreach (var entry in overrides.Split(';').Select(entry => entry.Split('=')))
        {
            map[entry[0]] = entry[1];
        }

        return string.Join(';', map.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static (ExchangeRateBulletinDay[] Days, ExchangeRateHalfDayMark[] Marks) Parse(string records)
    {
        var days = new List<ExchangeRateBulletinDay>();
        var marks = new List<ExchangeRateHalfDayMark>();
        foreach (var entry in records.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('=');
            var date = Day(parts[0]);
            var states = parts[1].Split('/');
            var bulletin = states[0].Split('+');
            switch (bulletin[0])
            {
                case "P":
                    days.Add(ExchangeRateBulletinDay.Published(date, ExchangeRateBulletinSource.Tcmb,
                        [ExchangeRate.Of(Usd, Try, date.Day)]));
                    break;
                case "X":
                    days.Add(ExchangeRateBulletinDay.ConfirmedNoBulletin(date, ExchangeRateBulletinSource.Tcmb));
                    break;
                case "U":
                    days.Add(ExchangeRateBulletinDay.Unconfirmed(date, ExchangeRateBulletinSource.Tcmb));
                    break;
            }

            if (bulletin.Length > 1)
            {
                days.Add(ExchangeRateBulletinDay.ManualPublished(date, ExchangeRateBulletinSource.Tcmb,
                    [ExchangeRate.Of(Usd, Try, 99m)], new ExchangeRateManualEntry("sentetik-kullanici-1", "SENTETIK gerekçe")));
            }

            ExchangeRateHalfDayStatus? status = states[1] switch
            {
                "F" => ExchangeRateHalfDayStatus.FullDay,
                "H" => ExchangeRateHalfDayStatus.HalfDay,
                "?" => ExchangeRateHalfDayStatus.Undetermined,
                _ => null,
            };
            if (status is { } known)
            {
                marks.Add(new ExchangeRateHalfDayMark(date, known));
            }
        }

        return (days.ToArray(), marks.ToArray());
    }
}

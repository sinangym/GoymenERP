using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// MOD-03 (SAT-D73/D74/D82/D93): the one calculator of the tax master — timelines, VAT and withholding resolution, and the
/// fail-closed "kaynak yok" answers. Every rate and code here is SENTETIK test input; no real rate or UBL-TR code is asserted
/// (EK13: rates need the mali müşavir's confirmation).
/// </summary>
public sealed class TaxMasterCalculatorV1Tests
{
    private static readonly Guid General = Guid.Parse("7b000000-0000-4000-8000-000000000001");
    private static readonly Guid Reduced = Guid.Parse("7b000000-0000-4000-8000-000000000002");
    private static readonly Guid Steel = Guid.Parse("7b000000-0000-4000-8000-000000000003");
    private static readonly Guid Taxpayer = Guid.Parse("7b000000-0000-4000-8000-000000000004");
    private static readonly Guid Consumer = Guid.Parse("7b000000-0000-4000-8000-000000000005");
    private static readonly DateOnly Day = new(2026, 11, 1);

    [Fact]
    public void An_empty_master_answers_every_part_unresolved_never_zero()
    {
        var data = Data();

        var unclassified = TaxMasterCalculatorV1.Resolve(data, null, null, Taxpayer, Day);
        Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.ProductNotClassified, (decimal?)null),
            (unclassified.VatState, unclassified.VatReason, unclassified.VatPercent));
        Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.ProductNotClassified),
            (unclassified.WithholdingState, unclassified.WithholdingReason));

        var noRate = TaxMasterCalculatorV1.Resolve(data, General, null, null, Day);
        Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.VatRateMissing, (decimal?)null),
            (noRate.VatState, noRate.VatReason, noRate.VatPercent));
        var unknownClass = TaxMasterCalculatorV1.Resolve(data, Guid.NewGuid(), null, null, Day);
        Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.VatClassUnknown),
            (unknownClass.VatState, unknownClass.VatReason));
        // A withholding class id used as the VAT class is not a VAT class.
        Assert.Equal(TaxResolutionReasonsV1.VatClassUnknown, TaxMasterCalculatorV1.Resolve(data, Steel, null, null, Day).VatReason);
        Assert.Null(TaxMasterCalculatorV1.ListInForce(data, TaxCodeListKindsV1.Withholding, Day));
    }

    [Fact]
    public void A_vat_class_without_a_record_in_force_is_kaynak_yok_and_a_dated_record_applies_from_its_date()
    {
        var data = Data(records: [Vat(General, 1, "20", Day.AddDays(5))]);

        var before = TaxMasterCalculatorV1.Resolve(data, General, null, null, Day);
        Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.VatRateMissing),
            (before.VatState, before.VatReason));
        // No withholding class on the product: withholding does not apply (SAT-D74), not "kaynak yok".
        Assert.Equal(TaxResolutionStatesV1.NotApplicable, before.WithholdingState);

        var from = TaxMasterCalculatorV1.Resolve(data, General, null, null, Day.AddDays(5));
        Assert.Equal((TaxResolutionStatesV1.Resolved, 20m, 1L), (from.VatState, from.VatPercent, from.VatRecord!.RecordVersion));
    }

    [Fact]
    public void A_rate_change_keeps_the_old_rate_for_earlier_dates_and_ignores_withdrawn_records()
    {
        // SAT-D82: a rate change (e.g. 18 → 20, SENTETIK) never rewrites an earlier date.
        var data = Data(records:
        [
            Vat(General, 1, "18", Day),
            Vat(General, 2, "20", Day.AddDays(30)),
            Vat(General, 3, "25", Day.AddDays(60), withdrawn: true),
        ]);

        Assert.Equal(18m, TaxMasterCalculatorV1.Resolve(data, General, null, null, Day.AddDays(29)).VatPercent);
        Assert.Equal(20m, TaxMasterCalculatorV1.Resolve(data, General, null, null, Day.AddDays(30)).VatPercent);
        Assert.Equal(20m, TaxMasterCalculatorV1.Resolve(data, General, null, null, Day.AddDays(90)).VatPercent);
        Assert.Equal([2L], TaxMasterCalculatorV1.ScheduledRates(data, TaxRateKindsV1.Vat, General, null, Day)
            .Select(record => record.RecordVersion));
    }

    [Fact]
    public void Of_two_records_on_one_date_the_highest_version_applies_and_dates_order_before_versions()
    {
        var data = Data(records:
        [
            Vat(General, 1, "10", Day.AddDays(10)),
            Vat(General, 2, "11", Day.AddDays(10)),
            Vat(General, 3, "9", Day),
        ]);

        Assert.Equal(9m, TaxMasterCalculatorV1.Resolve(data, General, null, null, Day.AddDays(9)).VatPercent);
        Assert.Equal(11m, TaxMasterCalculatorV1.Resolve(data, General, null, null, Day.AddDays(10)).VatPercent);
        Assert.Single(TaxMasterCalculatorV1.ScheduledRates(data, TaxRateKindsV1.Vat, General, null, Day));
    }

    [Fact]
    public void Withholding_needs_the_buyer_category_a_record_and_a_listed_code_or_it_is_unresolved()
    {
        var list = new TaxCodeListVersionDataV1(TaxCodeListKindsV1.Withholding, 1, Day,
            [new TaxCodeListEntryViewV1("T1", "SENTETIK tevkifat kodu")]);
        var data = Data(
            records:
            [
                Vat(General, 1, "20", Day),
                Withholding(Steel, Taxpayer, 1, Day, "50", "T1"),
                Withholding(Steel, Consumer, 2, Day, null, null),
            ],
            lists: [list]);

        var noBuyer = TaxMasterCalculatorV1.Resolve(data, General, Steel, null, Day);
        Assert.Equal((TaxResolutionStatesV1.Resolved, TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.BuyerCategoryMissing),
            (noBuyer.VatState, noBuyer.WithholdingState, noBuyer.WithholdingReason));

        var taxpayer = TaxMasterCalculatorV1.Resolve(data, General, Steel, Taxpayer, Day);
        Assert.Equal((TaxResolutionStatesV1.Resolved, 50m, "T1"),
            (taxpayer.WithholdingState, taxpayer.WithholdingPercent, taxpayer.WithholdingCode));

        // An explicit "no withholding" record for the buyer category (e.g. nihai tüketici, SENTETIK).
        var consumer = TaxMasterCalculatorV1.Resolve(data, General, Steel, Consumer, Day);
        Assert.Equal((TaxResolutionStatesV1.NotApplied, (decimal?)null, (string?)null),
            (consumer.WithholdingState, consumer.WithholdingPercent, consumer.WithholdingCode));

        // Before the records: nothing in force.
        var earlier = TaxMasterCalculatorV1.Resolve(data, General, Steel, Taxpayer, Day.AddDays(-1));
        Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.WithholdingRateMissing),
            (earlier.WithholdingState, earlier.WithholdingReason));

        // An unknown buyer category or a buyer category id used as a class is refused, never guessed.
        Assert.Equal(TaxResolutionReasonsV1.BuyerCategoryUnknown,
            TaxMasterCalculatorV1.Resolve(data, General, Steel, Guid.NewGuid(), Day).WithholdingReason);
        Assert.Equal(TaxResolutionReasonsV1.WithholdingClassUnknown,
            TaxMasterCalculatorV1.Resolve(data, General, Taxpayer, Taxpayer, Day).WithholdingReason);
    }

    [Fact]
    public void A_withholding_code_the_list_in_force_no_longer_carries_leaves_the_withholding_without_a_source()
    {
        var data = Data(
            records: [Vat(General, 1, "20", Day), Withholding(Steel, Taxpayer, 1, Day, "50", "T1")],
            lists:
            [
                new TaxCodeListVersionDataV1(TaxCodeListKindsV1.Withholding, 1, Day, [new TaxCodeListEntryViewV1("T1", "SENTETIK")]),
                new TaxCodeListVersionDataV1(TaxCodeListKindsV1.Withholding, 2, Day.AddDays(3), [new TaxCodeListEntryViewV1("T2", "SENTETIK")]),
            ]);

        Assert.Equal(TaxResolutionStatesV1.Resolved,
            TaxMasterCalculatorV1.Resolve(data, General, Steel, Taxpayer, Day.AddDays(2)).WithholdingState);
        var later = TaxMasterCalculatorV1.Resolve(data, General, Steel, Taxpayer, Day.AddDays(3));
        Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.WithholdingCodeNotInList),
            (later.WithholdingState, later.WithholdingReason));
    }

    [Fact]
    public void Code_lists_follow_the_same_timeline_and_a_withdrawn_version_is_ignored()
    {
        var data = Data(lists:
        [
            new TaxCodeListVersionDataV1(TaxCodeListKindsV1.Exemption, 1, Day, [new TaxCodeListEntryViewV1("E1", "SENTETIK")]),
            new TaxCodeListVersionDataV1(TaxCodeListKindsV1.Exemption, 2, Day.AddDays(5), [new TaxCodeListEntryViewV1("E2", "SENTETIK")],
                IsWithdrawn: true),
            new TaxCodeListVersionDataV1(TaxCodeListKindsV1.Exemption, 3, Day.AddDays(7), [new TaxCodeListEntryViewV1("E3", "SENTETIK")]),
        ]);

        Assert.Equal(1L, TaxMasterCalculatorV1.ListInForce(data, TaxCodeListKindsV1.Exemption, Day.AddDays(6))!.ListVersion);
        Assert.Equal(3L, TaxMasterCalculatorV1.ListInForce(data, TaxCodeListKindsV1.Exemption, Day.AddDays(7))!.ListVersion);
        Assert.Null(TaxMasterCalculatorV1.ListInForce(data, TaxCodeListKindsV1.Withholding, Day.AddDays(7)));
        Assert.Equal([3L], TaxMasterCalculatorV1.ScheduledLists(data, TaxCodeListKindsV1.Exemption, Day)
            .Select(list => list.ListVersion));
    }

    [Theory]
    [InlineData("0", true, true, "0")]
    [InlineData("0", false, false, null)]
    [InlineData("20", true, true, "20")]
    [InlineData("100", true, true, "100")]
    [InlineData("0.5000", false, true, "0.5")]
    [InlineData("100.0001", true, false, null)]
    [InlineData("1.23456", true, false, null)]
    [InlineData("01", true, false, null)]
    [InlineData("-1", true, false, null)]
    [InlineData("1e2", true, false, null)]
    [InlineData(" 20", true, false, null)]
    [InlineData("20.", true, false, null)]
    [InlineData("", true, false, null)]
    public void Percents_are_lossless_text_never_rounded(string text, bool allowZero, bool valid, string? canonical)
    {
        Assert.Equal(valid, TaxMasterCalculatorV1.TryParsePercent(text, allowZero, out var value));
        if (valid)
        {
            Assert.Equal(canonical, TaxMasterCalculatorV1.Format(value));
        }
    }

    [Theory]
    [InlineData("50", "0.5")]
    [InlineData("20", "0.2")]
    [InlineData("90", "0.9")]
    [InlineData("100", "1")]
    public void The_withheld_share_is_the_percent_over_one_hundred_exactly(string percent, string share)
    {
        Assert.True(TaxMasterCalculatorV1.TryParsePercent(percent, allowZero: false, out var value));
        Assert.Equal(share, TaxMasterCalculatorV1.Format(TaxMasterCalculatorV1.WithholdingShare(value)));
    }

    [Fact]
    public void A_zero_or_out_of_range_withholding_percent_has_no_share()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TaxMasterCalculatorV1.WithholdingShare(0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => TaxMasterCalculatorV1.WithholdingShare(100.0001m));
    }

    [Theory]
    [InlineData("genel", true, "GENEL")]
    [InlineData("DEMIR-CELIK_1", true, "DEMIR-CELIK_1")]
    [InlineData("-X", false, null)]
    [InlineData("İNDİRİMLİ", false, null)]
    [InlineData("A23456789012345678901234567890123", false, null)]
    public void Category_codes_are_upper_case_ascii(string code, bool valid, string? normalized)
    {
        Assert.Equal(valid, TaxMasterCalculatorV1.TryNormalizeCategoryCode(code, out var value));
        if (valid)
        {
            Assert.Equal(normalized, value);
        }
    }

    [Theory]
    [InlineData("601", true)]
    [InlineData("t1", true)]
    [InlineData("6-01", false)]
    [InlineData("12345678901234567", false)]
    public void List_codes_are_letters_and_digits_only(string code, bool valid) =>
        Assert.Equal(valid, TaxMasterCalculatorV1.TryNormalizeListCode(code, out _));

    private static TaxMasterDataV1 Data(IReadOnlyList<TaxRateRecordDataV1>? records = null,
        IReadOnlyList<TaxCodeListVersionDataV1>? lists = null) =>
        new(new Dictionary<Guid, TaxCategoryDataV1>
            {
                [General] = new(General, TaxCategoryKindsV1.VatClass, "GENEL", "SENTETIK genel"),
                [Reduced] = new(Reduced, TaxCategoryKindsV1.VatClass, "INDIRIMLI", "SENTETIK indirimli"),
                [Steel] = new(Steel, TaxCategoryKindsV1.WithholdingClass, "DEMIR-CELIK", "SENTETIK demir-çelik"),
                [Taxpayer] = new(Taxpayer, TaxCategoryKindsV1.BuyerCategory, "MUKELLEF", "SENTETIK KDV mükellefi"),
                [Consumer] = new(Consumer, TaxCategoryKindsV1.BuyerCategory, "NIHAI", "SENTETIK nihai tüketici"),
            },
            records ?? [],
            lists ?? []);

    private static TaxRateRecordDataV1 Vat(Guid classId, long version, string rate, DateOnly from, bool withdrawn = false) =>
        new(Guid.NewGuid(), TaxRateKindsV1.Vat, classId, null, version, from, true,
            decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), null, withdrawn);

    private static TaxRateRecordDataV1 Withholding(Guid classId, Guid buyer, long version, DateOnly from, string? rate,
        string? code) =>
        new(Guid.NewGuid(), TaxRateKindsV1.Withholding, classId, buyer, version, from, rate is not null,
            rate is null ? null : decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), code);
}

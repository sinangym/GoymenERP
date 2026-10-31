using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// MOD-03 (SAT-D93/D133/D135/D139/D140, EK19): the one calculator of the tax master and the reviewed SAT-D139 catalog — item-type
/// timelines, the buyer-situation rule, the code checks and the fail-closed "kaynak yok" answers. Code values and every rate
/// other than the SAT-D139 catalog's are SENTETIK test input.
/// </summary>
public sealed class TaxMasterCalculatorV1Tests
{
    private static readonly Guid Gdc = ApprovedUnitProfileCatalog.GdcCompanyId;
    private static readonly DateOnly Day = new(2026, 11, 1);

    [Fact]
    public void The_reviewed_catalog_holds_sinans_sat_d139_defaults_for_each_of_the_three_companies()
    {
        Assert.Equal(9, ApprovedTaxItemTypeDefaultCatalog.Entries.Count);
        foreach (var company in new[] { ApprovedUnitProfileCatalog.GdcCompanyId, ApprovedUnitProfileCatalog.GspCompanyId,
                     ApprovedUnitProfileCatalog.GinCompanyId })
        {
            var entries = ApprovedTaxItemTypeDefaultCatalog.For(company).ToDictionary(entry => entry.ItemType);
            Assert.Equal(TaxItemTypesV1.All.Order(), entries.Keys.Order());
            Assert.Equal((true, (decimal?)20m, true, (decimal?)50m), (entries[TaxItemTypesV1.Steel].VatApplies,
                entries[TaxItemTypesV1.Steel].VatPercent, entries[TaxItemTypesV1.Steel].WithholdingApplies,
                entries[TaxItemTypesV1.Steel].WithholdingPercent));
            Assert.Equal((false, (decimal?)null, false), (entries[TaxItemTypesV1.Scrap].VatApplies,
                entries[TaxItemTypesV1.Scrap].VatPercent, entries[TaxItemTypesV1.Scrap].WithholdingApplies));
            Assert.Equal((true, (decimal?)20m, false), (entries[TaxItemTypesV1.Service].VatApplies,
                entries[TaxItemTypesV1.Service].VatPercent, entries[TaxItemTypesV1.Service].WithholdingApplies));
            Assert.All(entries.Values, entry =>
            {
                // No code is decided (mali müşavir): the catalog names none.
                Assert.Null(entry.ExemptionCode);
                Assert.Null(entry.WithholdingCode);
                Assert.Equal((ApprovedTaxItemTypeDefaultCatalog.CatalogRecordVersion, ApprovedTaxItemTypeDefaultCatalog.EffectiveFrom,
                    TaxDefaultSourcesV1.Catalog, company), (entry.RecordVersion, entry.EffectiveFrom, entry.Source, entry.CompanyId));
            });
        }

        Assert.Equal(9, ApprovedTaxItemTypeDefaultCatalog.Entries.Select(entry => entry.RecordId).Distinct().Count());
        Assert.Empty(ApprovedTaxItemTypeDefaultCatalog.For(Guid.NewGuid()));
    }

    [Fact]
    public void Steel_defaults_to_vat_and_withholding_with_the_withholding_code_kaynak_yok_until_listed()
    {
        var steel = TaxMasterCalculatorV1.Resolve(Catalog(), TaxItemTypesV1.Steel, TaxBuyerSituationsV1.Normal, Day);
        Assert.Equal((TaxResolutionStatesV1.Resolved, TaxResolutionStatesV1.Resolved, (decimal?)20m,
            TaxResolutionStatesV1.NotRequired), (steel.State, steel.VatState, steel.VatPercent, steel.ExemptionCodeState));
        Assert.Equal((TaxResolutionStatesV1.Resolved, (decimal?)50m, TaxResolutionStatesV1.Unresolved,
            TaxResolutionReasonsV1.WithholdingCodeMissing), (steel.WithholdingState, steel.WithholdingPercent,
            steel.WithholdingCodeState, steel.WithholdingCodeReason));
        Assert.Equal(TaxDefaultSourcesV1.Catalog, steel.Record!.Source);
    }

    [Fact]
    public void Scrap_is_vat_exempt_with_the_exemption_code_kaynak_yok_and_service_carries_vat_only()
    {
        var scrap = TaxMasterCalculatorV1.Resolve(Catalog(), TaxItemTypesV1.Scrap, TaxBuyerSituationsV1.Normal, Day);
        Assert.Equal((TaxResolutionStatesV1.Exempt, (decimal?)null, TaxResolutionStatesV1.Unresolved,
            TaxResolutionReasonsV1.ExemptionCodeMissing, TaxResolutionStatesV1.NotApplied, TaxResolutionStatesV1.NotRequired),
            (scrap.VatState, scrap.VatPercent, scrap.ExemptionCodeState, scrap.ExemptionCodeReason, scrap.WithholdingState,
                scrap.WithholdingCodeState));

        var service = TaxMasterCalculatorV1.Resolve(Catalog(), TaxItemTypesV1.Service, TaxBuyerSituationsV1.Normal, Day);
        Assert.Equal((TaxResolutionStatesV1.Resolved, (decimal?)20m, TaxResolutionStatesV1.NotApplied),
            (service.VatState, service.VatPercent, service.WithholdingState));
    }

    [Fact]
    public void Without_an_item_type_a_default_or_a_known_type_the_answer_is_unresolved_never_zero()
    {
        var none = TaxMasterCalculatorV1.Resolve(Catalog(), null, TaxBuyerSituationsV1.Normal, Day);
        Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.ProductItemTypeMissing, (decimal?)null),
            (none.State, none.Reason, none.VatPercent));
        Assert.Equal(TaxResolutionReasonsV1.ItemTypeUnknown,
            TaxMasterCalculatorV1.Resolve(Catalog(), "Wood", TaxBuyerSituationsV1.Normal, Day).Reason);
        // Another company has no catalog entries: "kaynak yok".
        Assert.Equal(TaxResolutionReasonsV1.ItemTypeDefaultMissing,
            TaxMasterCalculatorV1.Resolve(Data(Guid.NewGuid()), TaxItemTypesV1.Steel, TaxBuyerSituationsV1.Normal, Day).Reason);
        // Before the catalog date nothing is in force.
        Assert.Equal(TaxResolutionReasonsV1.ItemTypeDefaultMissing, TaxMasterCalculatorV1.Resolve(Catalog(), TaxItemTypesV1.Steel,
            TaxBuyerSituationsV1.Normal, ApprovedTaxItemTypeDefaultCatalog.EffectiveFrom.AddDays(-1)).Reason);
    }

    [Fact]
    public void A_settings_record_replaces_the_catalog_from_its_date_and_outranks_it_on_the_same_day()
    {
        var sameDay = Record(TaxItemTypesV1.Steel, 1, ApprovedTaxItemTypeDefaultCatalog.EffectiveFrom, 18m, 70m, "T1");
        var later = Record(TaxItemTypesV1.Steel, 2, Day.AddDays(30), 20m, 50m, "T1");
        var withdrawn = Record(TaxItemTypesV1.Steel, 3, Day.AddDays(40), 10m, null, null) with { IsWithdrawn = true };
        var data = Data(Gdc, [sameDay, later, withdrawn], [List(TaxCodeListKindsV1.Withholding, "T1")]);

        var now = TaxMasterCalculatorV1.Resolve(data, TaxItemTypesV1.Steel, TaxBuyerSituationsV1.Normal, Day);
        Assert.Equal(((decimal?)18m, (decimal?)70m, TaxResolutionStatesV1.Resolved, "T1", TaxDefaultSourcesV1.Record),
            (now.VatPercent, now.WithholdingPercent, now.WithholdingCodeState, now.WithholdingCode, now.Record!.Source));
        Assert.Equal((decimal?)20m,
            TaxMasterCalculatorV1.Resolve(data, TaxItemTypesV1.Steel, TaxBuyerSituationsV1.Normal, Day.AddDays(45)).VatPercent);
        Assert.Equal([2L], TaxMasterCalculatorV1.ScheduledDefaults(data, TaxItemTypesV1.Steel, Day)
            .Select(record => record.RecordVersion));
    }

    [Fact]
    public void A_code_the_list_in_force_does_not_carry_is_unresolved()
    {
        var data = Data(Gdc, [Record(TaxItemTypesV1.Scrap, 1, Day, null, null, null, exemptionCode: "E1")],
            [List(TaxCodeListKindsV1.Exemption, "E2")]);
        var scrap = TaxMasterCalculatorV1.Resolve(data, TaxItemTypesV1.Scrap, TaxBuyerSituationsV1.Normal, Day);
        Assert.Equal((TaxResolutionStatesV1.Unresolved, "E1", TaxResolutionReasonsV1.CodeNotInList),
            (scrap.ExemptionCodeState, scrap.ExemptionCode, scrap.ExemptionCodeReason));
    }

    [Theory]
    [InlineData(TaxBuyerSituationsV1.NotSubjectToWithholding)]
    [InlineData(TaxBuyerSituationsV1.FinalConsumer)]
    public void A_buyer_not_subject_to_withholding_removes_it_and_keeps_the_vat(string situation)
    {
        var steel = TaxMasterCalculatorV1.Resolve(Catalog(), TaxItemTypesV1.Steel, situation, Day);
        Assert.Equal((TaxResolutionStatesV1.Resolved, (decimal?)20m, TaxResolutionStatesV1.NotApplied, (decimal?)null,
            TaxResolutionStatesV1.NotRequired, TaxResolutionReasonsV1.BuyerNotSubjectToWithholding),
            (steel.VatState, steel.VatPercent, steel.WithholdingState, steel.WithholdingPercent, steel.WithholdingCodeState,
                steel.BuyerAdjustment));
        // A default without withholding is unchanged and carries no adjustment.
        Assert.Null(TaxMasterCalculatorV1.Resolve(Catalog(), TaxItemTypesV1.Service, situation, Day).BuyerAdjustment);
    }

    [Theory]
    [InlineData(TaxBuyerSituationsV1.ExportRegistered)]
    [InlineData(TaxBuyerSituationsV1.ExemptionCertificate)]
    public void A_document_based_buyer_situation_leaves_vat_and_withholding_undecided(string situation)
    {
        var steel = TaxMasterCalculatorV1.Resolve(Catalog(), TaxItemTypesV1.Steel, situation, Day);
        Assert.Equal((TaxResolutionStatesV1.Resolved, TaxResolutionStatesV1.Unresolved, (decimal?)null,
            TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.BuyerSituationUndecided),
            (steel.State, steel.VatState, steel.VatPercent, steel.WithholdingState, steel.BuyerAdjustment));
    }

    [Fact]
    public void An_unknown_buyer_situation_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TaxMasterCalculatorV1.Resolve(Catalog(), TaxItemTypesV1.Steel, "Somebody", Day));

    [Fact]
    public void Code_lists_follow_the_same_timeline_and_a_withdrawn_version_is_ignored()
    {
        var data = Data(Gdc, [],
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

    [Fact]
    public void The_withholding_threshold_starts_empty_and_follows_the_same_timeline()
    {
        // SAT-D135: no threshold is assumed; before a record the answer is "kaynak yok" (null).
        Assert.Null(TaxMasterCalculatorV1.ThresholdInForce(Catalog(), Day));
        var data = new TaxMasterDataV1([], [],
        [
            new TaxWithholdingThresholdDataV1(Guid.NewGuid(), 1, Day, 1000m, "TRY"),
            new TaxWithholdingThresholdDataV1(Guid.NewGuid(), 2, Day.AddDays(10), 2000m, "TRY", IsWithdrawn: true),
            new TaxWithholdingThresholdDataV1(Guid.NewGuid(), 3, Day.AddDays(20), 3000m, "TRY"),
        ]);

        Assert.Null(TaxMasterCalculatorV1.ThresholdInForce(data, Day.AddDays(-1)));
        Assert.Equal(1000m, TaxMasterCalculatorV1.ThresholdInForce(data, Day.AddDays(15))!.Amount);
        Assert.Equal(3000m, TaxMasterCalculatorV1.ThresholdInForce(data, Day.AddDays(20))!.Amount);
        Assert.Equal([3L], TaxMasterCalculatorV1.ScheduledThresholds(data, Day).Select(record => record.RecordVersion));
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
    [InlineData("12000", true)]
    [InlineData("12000.5", true)]
    [InlineData("999999999999999.9999", true)]
    [InlineData("0", false)]
    [InlineData("0.0000", false)]
    [InlineData("012000", false)]
    [InlineData("1234567890123456", false)]
    [InlineData("1.23456", false)]
    [InlineData("-1", false)]
    [InlineData("1,000", false)]
    public void Threshold_amounts_are_positive_lossless_text(string text, bool valid) =>
        Assert.Equal(valid, TaxMasterCalculatorV1.TryParseAmount(text, out _));

    [Theory]
    [InlineData("try", true)]
    [InlineData("TRY", true)]
    [InlineData("TL", false)]
    [InlineData("TR1", false)]
    public void Currency_codes_are_three_letters(string code, bool valid) =>
        Assert.Equal(valid, TaxMasterCalculatorV1.TryNormalizeCurrencyCode(code, out _));

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
    [InlineData("601", true)]
    [InlineData("t1", true)]
    [InlineData("6-01", false)]
    [InlineData("12345678901234567", false)]
    public void List_codes_are_letters_and_digits_only(string code, bool valid) =>
        Assert.Equal(valid, TaxMasterCalculatorV1.TryNormalizeListCode(code, out _));

    [Fact]
    public void A_zero_vat_rate_never_resolves_without_the_exemption_code_and_the_command_parser_refuses_it()
    {
        // Review B P2-1: "KDV uygulanır, %0" must not bypass the exemption code gate. The command parses VAT with
        // allowZero: false (and the CHECK holds [VatPercent] > 0); a record that still carried 0 waits on the exemption code.
        Assert.False(TaxMasterCalculatorV1.TryParsePercent("0", allowZero: false, out _));
        Assert.False(TaxMasterCalculatorV1.TryParsePercent("0.0000", allowZero: false, out _));
        var zero = Record(TaxItemTypesV1.Scrap, 1, Day, 0m, 50m, "T1");
        var resolution = TaxMasterCalculatorV1.Resolve(Data(Gdc, [zero], [List(TaxCodeListKindsV1.Withholding, "T1")]),
            TaxItemTypesV1.Scrap, TaxBuyerSituationsV1.Normal, Day);
        Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.ExemptionCodeMissing),
            (resolution.ExemptionCodeState, resolution.ExemptionCodeReason));
    }

    private static TaxMasterDataV1 Catalog() => Data(Gdc);

    private static TaxMasterDataV1 Data(Guid company, IReadOnlyList<TaxItemTypeDefaultDataV1>? records = null,
        IReadOnlyList<TaxCodeListVersionDataV1>? lists = null) =>
        new([.. ApprovedTaxItemTypeDefaultCatalog.For(company), .. records ?? []], lists ?? [], []);

    private static TaxItemTypeDefaultDataV1 Record(string itemType, long version, DateOnly from, decimal? vat, decimal? withholding,
        string? withholdingCode, string? exemptionCode = null) =>
        new(Guid.NewGuid(), Gdc, itemType, TaxDefaultSourcesV1.Record, version, from, vat is not null, vat, exemptionCode,
            withholding is not null, withholding, withholdingCode);

    private static TaxCodeListVersionDataV1 List(string kind, string code) =>
        new(kind, 1, ApprovedTaxItemTypeDefaultCatalog.EffectiveFrom, [new TaxCodeListEntryViewV1(code, "SENTETIK")]);
}

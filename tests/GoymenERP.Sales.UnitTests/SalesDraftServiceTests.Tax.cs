using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// MOD-03 → Sales over SYNTHETIC owners (every rate, code and list here is SENTETIK test data, never a policy): the order's buyer
/// tax situation (SAT-D140/D147/D158), the lines' tax snapshots from the Settings line tax port (SAT-D93/D139), the reasoned line
/// change (SAT-D97), the revision rule (SAT-D105), the totals (API-03) and the risk read's VAT-included amounts (SAT-D46) — each
/// through the ONE calculator, nothing re-implemented, and every missing Settings value fail-closed ("kaynak yok").
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task A_create_resolves_every_lines_tax_for_the_normal_buyer_and_the_totals_compute_vat_withholding_gross_and_payable()
    {
        var world = new World { LineTaxes = new FakeLineTaxes() };
        var request = PricedRequest("10", "100");

        var created = (await world.Service().CreateDraftAsync(request, Metadata("k-tax-create"))).Detail;
        var tax = await world.Service().GetDraftTaxAsync(created.Draft.Id);

        Assert.Equal(new SalesDraftBuyerTaxV1(TaxBuyerSituationsV1.Normal), tax.BuyerTax);
        var line = Assert.Single(tax.Lines);
        Assert.Equal((true, "Default", "Resolved", "Resolved", "20", "Resolved", "50", "Unresolved", "Computed"),
            (line.HasSnapshot, line.Origin, line.State, line.VatState, line.VatPercent, line.WithholdingState, line.WithholdingPercent,
                line.WithholdingCodeState, line.RateState));
        // SAT-D135 / SAT-D148: no withholding code is entered — the rate is usable, the invoice waits on the code.
        Assert.Equal([TaxResolutionReasonsV1.WithholdingCodeMissing], line.InvoiceBlockers);

        // 10 kg × 100 = 1000.00; VAT 20 % = 200.00; withheld 5/10 of the rounded VAT = 100.00; gross 1200.00; payable 1100.00.
        var totals = await world.Service().GetDraftTotalsAsync(created.Draft.Id);
        Assert.Equal(("Computed", "Computed", "1000.00", "200.00", "100.00", "1200.00", "1100.00", 0),
            (totals.TaxState, totals.Totals.TaxState, totals.Totals.NetAmount, totals.Totals.VatAmount, totals.Totals.WithheldVatAmount,
                totals.Totals.GrossAmount, totals.Totals.PayableAmount, totals.Totals.TaxUnavailableLineCount));
        Assert.Null(Assert.Single(totals.Lines).TaxReason);
    }

    [Fact]
    public async Task Empty_settings_values_stay_kaynak_yok_with_the_ports_reason_never_a_guessed_rate()
    {
        // SAT-D148: nothing is entered for the item type: the line's tax is "Unresolved" and no VAT, gross or payable is shown.
        var world = new World { LineTaxes = new FakeLineTaxes { Defaults = [] } };
        var created = (await world.Service().CreateDraftAsync(PricedRequest("10", "100"), Metadata("k-tax-empty"))).Detail;

        var line = Assert.Single((await world.Service().GetDraftTaxAsync(created.Draft.Id)).Lines);
        Assert.Equal(("Unresolved", TaxResolutionReasonsV1.ItemTypeDefaultMissing, "NoSource"), (line.State, line.Reason, line.RateState));
        var totals = await world.Service().GetDraftTotalsAsync(created.Draft.Id);
        Assert.Equal(("NoSource", "1000.00", null, null, 1), (totals.Totals.TaxState, totals.Totals.NetAmount, totals.Totals.GrossAmount,
            totals.Totals.PayableAmount, totals.Totals.TaxUnavailableLineCount));
        Assert.Equal(TaxResolutionReasonsV1.ItemTypeDefaultMissing, Assert.Single(totals.Lines).TaxReason);
    }

    [Fact]
    public async Task An_export_registered_buyer_is_zero_rated_without_withholding_and_the_invoice_waits_for_the_code_and_document()
    {
        var world = new World { LineTaxes = new FakeLineTaxes { ExemptionCodes = ["701"] } };
        var request = PricedRequest("10", "100") with { BuyerTax = new SalesDraftBuyerTaxV1(TaxBuyerSituationsV1.ExportRegistered) };

        var created = (await world.Service().CreateDraftAsync(request, Metadata("k-tax-export"))).Detail;

        var line = Assert.Single((await world.Service().GetDraftTaxAsync(created.Draft.Id)).Lines);
        Assert.Equal(("ZeroRated", "NotApplied", "Unresolved", "Computed"),
            (line.VatState, line.WithholdingState, line.ExemptionCodeState, line.RateState));
        Assert.Equal([TaxResolutionReasonsV1.ExemptionCodeMissing, "buyer_document_missing"], line.InvoiceBlockers);
        var totals = await world.Service().GetDraftTotalsAsync(created.Draft.Id);
        Assert.Equal(("1000.00", "0.00", "0.00", "1000.00", "1000.00"), (totals.Totals.NetAmount, totals.Totals.VatAmount,
            totals.Totals.WithheldVatAmount, totals.Totals.GrossAmount, totals.Totals.PayableAmount));

        // With the code from the list and the buyer's document, nothing makes the invoice wait.
        var complete = PricedRequest("10", "100") with
        {
            BuyerTax = new SalesDraftBuyerTaxV1(TaxBuyerSituationsV1.ExportRegistered, "701", "SENTETIK-IK-1", Day),
        };
        var second = (await world.Service().CreateDraftAsync(complete, Metadata("k-tax-export-2"))).Detail;
        var resolved = Assert.Single((await world.Service().GetDraftTaxAsync(second.Draft.Id)).Lines);
        Assert.Equal(("Resolved", "701"), (resolved.ExemptionCodeState, resolved.ExemptionCode));
        Assert.Empty(resolved.InvoiceBlockers);
    }

    [Theory]
    [InlineData("Unlisted", null, null, "buyerTax.situation", SalesRuleCodes.TaxBuyerSituationUnknown)]
    [InlineData("Normal", "701", null, "buyerTax.exemptionCode", SalesRuleCodes.TaxExemptionCodeNotApplicable)]
    [InlineData("ExportRegistered", "999", null, "buyerTax.exemptionCode", SalesRuleCodes.TaxExemptionCodeUnknown)]
    [InlineData("Normal", null, "SENTETIK-1", "buyerTax.documentNo", SalesRuleCodes.TaxBuyerDocumentNotApplicable)]
    [InlineData("not a code", null, null, "buyerTax", SalesRuleCodes.TaxBuyerSituationInvalid)]
    public async Task Only_values_of_the_settings_lists_are_accepted_for_the_buyer_tax(string situation, string? code, string? document,
        string field, string expected)
    {
        var world = new World { LineTaxes = new FakeLineTaxes { ExemptionCodes = ["701"] } };
        var request = PricedRequest("1", "1") with
        {
            BuyerTax = new SalesDraftBuyerTaxV1(situation, code, document, document is null ? null : Day),
        };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateDraftAsync(request, Metadata("k-tax-refused")));

        Assert.Equal((422, field, expected), (refused.Status, refused.Errors[0].Field, refused.Errors[0].Code));
        Assert.Empty(world.Store.Envelopes);
    }

    [Fact]
    public async Task A_line_tax_change_needs_the_price_write_name_a_reason_and_settings_values_and_is_kept_with_who_and_why()
    {
        var world = new World { LineTaxes = new FakeLineTaxes() };
        var change = new SalesDraftLineTaxOverrideV1(TaxBuyerSituationsV1.NotSubjectToWithholding, "SENTETIK alıcı tevkifata tabi değil");
        var request = WithLineTax(PricedRequest("10", "100"), change);

        // SAT-D93: a tax change moves the amounts — the separate price-write name is asked first (403, nothing read or stored).
        world.Authority.Denied.Add(PricePermission);
        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateDraftAsync(request, Metadata("k-tax-change")));
        Assert.Equal(403, forbidden.Status);
        world.Authority.Denied.Clear();

        var blank = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(
            WithLineTax(PricedRequest("10", "100"), change with { Reason = "  " }), Metadata("k-tax-change-blank")));
        Assert.Equal(("lines[0].taxOverride.reason", SalesRuleCodes.TaxOverrideReasonRequired), (blank.Errors[0].Field, blank.Errors[0].Code));
        var itemType = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(
            WithLineTax(PricedRequest("10", "100"), change with { ItemType = "Gold" }), Metadata("k-tax-change-type")));
        Assert.Equal(("lines[0].taxOverride.itemType", SalesRuleCodes.TaxItemTypeUnknown), (itemType.Errors[0].Field, itemType.Errors[0].Code));

        var created = (await world.Service().CreateDraftAsync(request, Metadata("k-tax-change"))).Detail;
        var line = Assert.Single((await world.Service().GetDraftTaxAsync(created.Draft.Id)).Lines);
        Assert.Equal(("Override", "NotApplied", TaxResolutionReasonsV1.BuyerNotSubjectToWithholding),
            (line.Origin, line.WithholdingState, line.BuyerAdjustment));
        Assert.Equal(("SENTETIK alıcı tevkifata tabi değil", Actor), (line.Override!.Reason, line.Override.ChangedBy));
        var totals = await world.Service().GetDraftTotalsAsync(created.Draft.Id);
        Assert.Equal(("200.00", "0.00", "1200.00"), (totals.Totals.VatAmount, totals.Totals.WithheldVatAmount, totals.Totals.PayableAmount));

        // An item type default from the list instead of the product's (hurda: "KDV yok").
        var scrap = (await world.Service().CreateDraftAsync(WithLineTax(PricedRequest("10", "100"),
            change with { BuyerSituation = TaxBuyerSituationsV1.Normal, ItemType = TaxItemTypesV1.Scrap }), Metadata("k-tax-scrap"))).Detail;
        var scrapLine = Assert.Single((await world.Service().GetDraftTaxAsync(scrap.Draft.Id)).Lines);
        Assert.Equal(("Scrap", "Exempt", "Computed"), (scrapLine.ItemType, scrapLine.VatState, scrapLine.RateState));
    }

    [Fact]
    public async Task A_tax_change_of_an_approved_order_is_a_revision_that_archives_the_approved_tax_and_reenters_approval()
    {
        var world = new World { LineTaxes = new FakeLineTaxes() };
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var before = Assert.Single((await world.Service().GetDraftTaxAsync(approved.Draft.Id)).Lines);
        Assert.Equal("Default", before.Origin);

        // Re-resolved with no change of value and no price change: no revision for nothing (SAT-D105 / ASSUMPTION-SALESP-5).
        var nothing = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(approved.Draft.Id,
            Revision(approved.Draft.Version, new SalesDraftLineCommercialV1(line)), Metadata("k-tax-rev-none")));
        Assert.Equal(SalesRuleCodes.RevisionNoChange, nothing.Errors[0].Code);

        // SAT-D105: the line's VAT/withholding changes on an approved order → Rev.1 back in preparation, Rev.0 archived with its tax.
        var revised = await world.Service().ReviseCommercialAsync(approved.Draft.Id, Revision(approved.Draft.Version,
            new SalesDraftLineCommercialV1(line, Tax: new SalesDraftLineTaxChoiceV1(SalesDraftLineTaxModesV1.Override,
                TaxBuyerSituationsV1.FinalConsumer, "SENTETIK nihai tüketici"))), Metadata("k-tax-rev"));
        Assert.Equal(("Preparing", "Rev.1"), (revised.Detail.Draft.ApprovalState, revised.Detail.Draft.Revision));
        var archived = Assert.Single(world.Store.Archived);
        Assert.Equal(("Resolved", (object?)null), (archived.Lines[0].Tax!.WithholdingState, (object?)archived.Lines[0].Tax!.Override));
        var after = Assert.Single((await world.Service().GetDraftTaxAsync(approved.Draft.Id)).Lines);
        Assert.Equal(("Override", "NotApplied"), (after.Origin, after.WithholdingState));

        // The same change again keeps who/when (no change); "Default" drops it.
        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(approved.Draft.Id,
            Revision(revised.Detail.Draft.Version, new SalesDraftLineCommercialV1(line, Tax: new SalesDraftLineTaxChoiceV1(
                SalesDraftLineTaxModesV1.Override, TaxBuyerSituationsV1.FinalConsumer, "SENTETIK nihai tüketici"))), Metadata("k-tax-rev-again")));
        Assert.Equal(SalesRuleCodes.RevisionNoChange, again.Errors[0].Code);
        await world.Service().ReviseCommercialAsync(approved.Draft.Id, Revision(revised.Detail.Draft.Version,
            new SalesDraftLineCommercialV1(line, Tax: new SalesDraftLineTaxChoiceV1(SalesDraftLineTaxModesV1.Default))), Metadata("k-tax-rev-default"));
        Assert.Equal("Default", Assert.Single((await world.Service().GetDraftTaxAsync(approved.Draft.Id)).Lines).Origin);
    }

    [Fact]
    public async Task A_buyer_tax_change_in_a_header_revision_reresolves_the_default_lines_and_reenters_approval()
    {
        var world = new World { LineTaxes = new FakeLineTaxes() };
        Owners(world);
        var (approved, _) = await AcceptedApprovedAsync(world);

        var revised = await world.Service().ReviseHeaderAsync(approved.Id, HeaderRevision("5", reason: "SENTETIK alıcı tevkifata tabi değil") with
        {
            BuyerTax = new SalesDraftBuyerTaxV1(TaxBuyerSituationsV1.NotSubjectToWithholding),
        }, Metadata("k-tax-header"));

        Assert.Equal(("Preparing", "Rev.1"), (revised.Detail.Draft.ApprovalState, revised.Detail.Draft.Revision));
        var archived = Assert.Single(world.Store.Archived);
        Assert.Equal(TaxBuyerSituationsV1.Normal, archived.Header!.BuyerTax!.Situation);
        var tax = await world.Service().GetDraftTaxAsync(approved.Id);
        Assert.Equal(TaxBuyerSituationsV1.NotSubjectToWithholding, tax.BuyerTax!.Situation);
        Assert.Equal(("Default", TaxBuyerSituationsV1.NotSubjectToWithholding, "NotApplied"),
            (tax.Lines[0].Origin, tax.Lines[0].BuyerSituation, tax.Lines[0].WithholdingState));

        // A situation the Settings list does not carry is refused (nothing re-resolved, nothing recorded).
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Id,
            HeaderRevision(revised.Detail.Draft.Version) with { BuyerTax = new SalesDraftBuyerTaxV1("Unlisted") }, Metadata("k-tax-header-2")));
        Assert.Equal(("buyerTax.situation", SalesRuleCodes.TaxBuyerSituationUnknown), (refused.Errors[0].Field, refused.Errors[0].Code));
    }

    [Fact]
    public async Task Entering_only_the_buyer_document_of_an_approved_accepted_order_is_recorded_in_place_never_a_revision()
    {
        // Review B probe / SAT-D185 (EK27): approved and accepted on ExportRegistered/701, then the document (no, date) alone.
        var world = new World { LineTaxes = new FakeLineTaxes { ExemptionCodes = ["701"] } };
        Owners(world);
        var buyer = new SalesDraftBuyerTaxV1(TaxBuyerSituationsV1.ExportRegistered, "701");
        var created = (await world.Service().CreateDraftAsync(Request() with { BuyerTax = buyer }, Metadata("k-doc-create"))).Detail.Draft;
        await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-doc-submit"));
        await world.Service().ApproveDraftAsync(created.Id, Command("3"), Metadata("k-doc-approve"));
        await world.Service().RecordCustomerAcceptanceAsync(created.Id, Command("4"), Metadata("k-doc-accept"));
        var before = await world.Service().GetDraftTaxAsync(created.Id);
        Assert.Contains("buyer_document_missing", before.Lines[0].InvoiceBlockers);

        var revised = await world.Service().ReviseHeaderAsync(created.Id, HeaderRevision("5", reason: "SENTETIK ihraç kayıtlı belge geldi") with
        {
            BuyerTax = buyer with { DocumentNo = "IK-2026-1", DocumentDate = Day },
        }, Metadata("k-doc"));

        // In place: the same revision, still approved, no archive; the lines are not re-resolved; the document is on the order.
        Assert.Equal(("Rev.0", "Approved", "6"), (revised.Detail.Draft.Revision, revised.Detail.Draft.ApprovalState, revised.Detail.Draft.Version));
        Assert.Empty(world.Store.Archived);
        var after = await world.Service().GetDraftTaxAsync(created.Id);
        Assert.Equal(("IK-2026-1", Day), (after.BuyerTax!.DocumentNo, after.BuyerTax.DocumentDate));
        Assert.Equal(before.Lines[0] with { InvoiceBlockers = after.Lines[0].InvoiceBlockers }, after.Lines[0]);
        Assert.DoesNotContain("buyer_document_missing", after.Lines[0].InvoiceBlockers);
    }

    [Fact]
    public async Task The_tax_read_needs_the_price_read_and_a_port_fault_is_503_or_403_never_a_guessed_tax()
    {
        var taxes = new FakeLineTaxes();
        var world = new World { LineTaxes = taxes };
        var created = (await world.Service().CreateDraftAsync(PricedRequest("1", "1"), Metadata("k-tax-read"))).Detail;

        world.Authority.Denied.Add(PriceReadPermission);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftTaxAsync(created.Draft.Id))).Status);
        world.Authority.Denied.Clear();

        taxes.Fault = TaxMasterProblemCodesV1.ProviderUnavailable;
        var unavailable = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateDraftAsync(PricedRequest("1", "1"), Metadata("k-tax-down")));
        Assert.Equal((503, SalesDraftProblemCodesV1.TaxProviderUnavailable), (unavailable.Status, unavailable.Code));
        taxes.Fault = TaxMasterProblemCodesV1.AccessDenied;
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateDraftAsync(PricedRequest("1", "1"), Metadata("k-tax-denied")))).Status);

        // Without a composed port: a plain create stores no snapshot (named "tax_snapshot_missing"); a tax choice is 503.
        var bare = new World();
        var plain = (await bare.Service().CreateDraftAsync(PricedRequest("1", "1"), Metadata("k-tax-bare"))).Detail;
        Assert.False(Assert.Single((await bare.Service().GetDraftTaxAsync(plain.Draft.Id)).Lines).HasSnapshot);
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => bare.Service().CreateDraftAsync(
            PricedRequest("1", "1") with { BuyerTax = new SalesDraftBuyerTaxV1(TaxBuyerSituationsV1.Normal) }, Metadata("k-tax-bare-2")))).Status);
    }

    [Fact]
    public async Task The_tax_options_are_the_settings_lists_in_force_and_an_empty_list_says_why()
    {
        var world = new World { LineTaxes = new FakeLineTaxes() };

        var options = await world.Service().GetTaxOptionsAsync(Day);

        Assert.Equal(("Resolved", 5, "Unresolved", TaxResolutionReasonsV1.CodeListMissing, 0),
            (options.BuyerSituationsState, options.BuyerSituations.Count, options.ExemptionCodesState, options.ExemptionCodesReason,
                options.ExemptionCodes.Count));
        Assert.Equal(TaxItemTypesV1.All, options.ItemTypes);
        // SAT-D181: without the rate list port the lists are offered as "kaynak yok", never a default list.
        Assert.Equal(("Unresolved", TaxMasterProblemCodesV1.ProviderUnavailable, 0), (options.VatRatesState, options.VatRatesReason,
            options.VatRates.Count));

        world.RateLists = new FakeRateLists();
        var empty = await world.Service().GetTaxOptionsAsync(Day);
        Assert.Equal(("Unresolved", TaxRateListReasonsV1.RateListMissing, "Unresolved"), (empty.VatRatesState, empty.VatRatesReason,
            empty.WithholdingRatiosState));
    }

    [Fact]
    public async Task A_line_change_picks_a_vat_rate_and_a_withholding_ratio_only_from_the_settings_lists()
    {
        var rates = new FakeRateLists
        {
            Vat = [new("10", "SENTETIK %10", 10m), new("0", "SENTETIK %0", 0m)],
            Withholding = [new("2/10", "SENTETIK 2/10", 20m), new("9/10", "SENTETIK 9/10", 90m, "625")],
        };
        var world = new World { LineTaxes = new FakeLineTaxes { ExemptionCodes = ["701"], WithholdingCodes = ["625"] }, RateLists = rates };
        var change = new SalesDraftLineTaxOverrideV1(TaxBuyerSituationsV1.Normal, "SENTETIK indirimli oran", VatRateCode: "10",
            WithholdingRatioCode: "2/10");

        var created = (await world.Service().CreateDraftAsync(WithLineTax(PricedRequest("10", "100"), change), Metadata("k-rate"))).Detail;

        var line = Assert.Single((await world.Service().GetDraftTaxAsync(created.Draft.Id)).Lines);
        Assert.Equal(("10", "20", "Unresolved", "10", "2/10"), (line.VatPercent, line.WithholdingPercent, line.WithholdingCodeState,
            line.Override!.VatRateCode, line.Override.WithholdingRatioCode));
        // 1000.00 net; VAT 10 % = 100.00; withheld 2/10 of it = 20.00; payable 1080.00 — the list's percents, the one calculator.
        var totals = await world.Service().GetDraftTotalsAsync(created.Draft.Id);
        Assert.Equal(("100.00", "20.00", "1080.00"), (totals.Totals.VatAmount, totals.Totals.WithheldVatAmount, totals.Totals.PayableAmount));

        // A code the list does not carry, a withholding over a 0 rate, and an empty list are refused (422), never typed or guessed.
        var unknown = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(
            WithLineTax(PricedRequest("1", "1"), change with { VatRateCode = "18" }), Metadata("k-rate-unknown")));
        Assert.Equal(("lines[0].taxOverride.vatRateCode", SalesRuleCodes.TaxVatRateUnknown), (unknown.Errors[0].Field, unknown.Errors[0].Code));
        // SAT-D181 (EK26): a 0 rate names its exemption code; no withholding over it.
        var noCode = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(
            WithLineTax(PricedRequest("1", "1"), change with { VatRateCode = "0", WithholdingRatioCode = null }), Metadata("k-rate-no-code")));
        Assert.Equal(("lines[0].taxOverride.exemptionCode", SalesRuleCodes.TaxExemptionCodeRequired), (noCode.Errors[0].Field, noCode.Errors[0].Code));
        var zero = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(
            WithLineTax(PricedRequest("1", "1"), change with { VatRateCode = "0", ExemptionCode = "701" }), Metadata("k-rate-zero")));
        Assert.Equal(("lines[0].taxOverride.withholdingRatioCode", SalesRuleCodes.TaxWithholdingNotApplicable),
            (zero.Errors[0].Field, zero.Errors[0].Code));
        var zeroLine = (await world.Service().CreateDraftAsync(WithLineTax(PricedRequest("1", "1"),
            change with { VatRateCode = "0", WithholdingRatioCode = null, ExemptionCode = "701" }), Metadata("k-rate-zero-ok"))).Detail;
        var zeroTax = Assert.Single((await world.Service().GetDraftTaxAsync(zeroLine.Draft.Id)).Lines);
        Assert.Equal(("0", "Resolved", "701", "NotApplied"), (zeroTax.VatPercent, zeroTax.ExemptionCodeState, zeroTax.ExemptionCode,
            zeroTax.WithholdingState));

        // SAT-D181 (EK26): a ratio mapped to a GİB code of the withholding code list is invoiced under it.
        var mapped = (await world.Service().CreateDraftAsync(WithLineTax(PricedRequest("1", "1"), change with { WithholdingRatioCode = "9/10" }),
            Metadata("k-rate-mapped"))).Detail;
        var mappedTax = Assert.Single((await world.Service().GetDraftTaxAsync(mapped.Draft.Id)).Lines);
        Assert.Equal(("90", "Resolved", "625"), (mappedTax.WithholdingPercent, mappedTax.WithholdingCodeState, mappedTax.WithholdingCode));

        world.RateLists = new FakeRateLists();
        var none = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(
            WithLineTax(PricedRequest("1", "1"), change with { WithholdingRatioCode = null }), Metadata("k-rate-none")));
        Assert.Equal(SalesRuleCodes.TaxVatRateUnknown, none.Errors[0].Code);
        world.RateLists = null;
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(
            WithLineTax(PricedRequest("1", "1"), change with { WithholdingRatioCode = null }), Metadata("k-rate-port")))).Status);
    }

    [Fact]
    public async Task A_kept_rate_pick_no_longer_listed_leaves_the_line_unresolved_at_the_next_revision_never_the_old_rate()
    {
        var rates = new FakeRateLists { Vat = [new("10", "SENTETIK %10", 10m)] };
        var world = new World { LineTaxes = new FakeLineTaxes(), RateLists = rates };
        var created = (await world.Service().CreateDraftAsync(WithLineTax(PricedRequest("10", "100"),
            new SalesDraftLineTaxOverrideV1(TaxBuyerSituationsV1.Normal, "SENTETIK", VatRateCode: "10")), Metadata("k-kept"))).Detail;
        rates.Vat = [];

        await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(created.Lines[0].LineId, "90", KgUnit, null)), Metadata("k-kept-rev"));

        var line = Assert.Single((await world.Service().GetDraftTaxAsync(created.Draft.Id)).Lines);
        Assert.Equal(("Unresolved", SalesLineTaxReasons.VatRateNotInList, "NoSource"), (line.State, line.Reason, line.RateState));
    }

    [Fact]
    public async Task The_risk_check_values_a_never_approved_order_vat_included_from_its_line_taxes()
    {
        var world = new World { LineTaxes = new FakeLineTaxes(), PartyRisk = new FakePartyRisk(FinanceLike([new(Lira, 100m)])) };
        var created = (await world.Service().CreateDraftAsync(PricedRequest("10", "100"), Metadata("k-tax-risk"))).Detail;

        var check = await world.Service().GetDraftRiskCheckAsync(created.Draft.Id);

        // SAT-D46 / SAT-D183: this order's PAYABLE amount — 1000.00 + 200.00 VAT − 100.00 withheld — never the net or the gross.
        Assert.Equal(("Available", "IncludingTax"), (check.ThisOrder.State, check.ThisOrder.TaxBasis));
        Assert.Equal("1100.00", Assert.Single(check.ThisOrder.Amounts!).Amount);

        // SAT-D183's own example: 100.000 + %20 KDV = 120.000; with 5/10 withheld the customer owes 110.000.
        var large = (await world.Service().CreateDraftAsync(PricedRequest("1000", "100"), Metadata("k-tax-risk-large"))).Detail;
        Assert.Equal("110000.00", Assert.Single((await world.Service().GetDraftRiskCheckAsync(large.Draft.Id)).ThisOrder.Amounts!).Amount);

        // A line without a usable tax keeps it "hesaplanamıyor" (never the net in its place).
        var empty = new World { LineTaxes = new FakeLineTaxes { Defaults = [] }, PartyRisk = new FakePartyRisk(FinanceLike([new(Lira, 100m)])) };
        var unresolved = (await empty.Service().CreateDraftAsync(PricedRequest("10", "100"), Metadata("k-tax-risk-2"))).Detail;
        var unavailable = await empty.Service().GetDraftRiskCheckAsync(unresolved.Draft.Id);
        Assert.Equal(("Unavailable", SalesDraftRiskReasonCodesV1.VatInclusiveAmountUnavailable),
            (unavailable.ThisOrder.State, unavailable.ThisOrder.ReasonCode));
    }

    /// <summary>A SYNTHETIC Settings rate list port: the lists the test gives (empty = "kaynak yok"), in force on every date.</summary>
    private sealed class FakeRateLists : ISettingsTaxRateListReaderV1
    {
        public IReadOnlyList<TaxRateListEntryViewV1> Vat { get; set; } = [];

        public IReadOnlyList<TaxRateListEntryViewV1> Withholding { get; set; } = [];

        public Task<SettingsTaxRateListResolutionV1> GetRateListAsync(string kind, DateOnly on, CancellationToken cancellationToken = default)
        {
            var entries = kind == TaxRateListKindsV1.VatRate ? Vat : Withholding;
            return Task.FromResult(entries.Count == 0
                ? new SettingsTaxRateListResolutionV1(Company, kind, on, TaxResolutionStatesV1.Unresolved, null, null, [],
                    TaxRateListReasonsV1.RateListMissing)
                : new SettingsTaxRateListResolutionV1(Company, kind, on, TaxResolutionStatesV1.Resolved, 1, Day, entries, null));
        }
    }

    private static CreateSalesOrderDraftRequest PricedRequest(string quantity, string price) =>
        new("Order", Party, Account, Day, Currency,
            [new SalesOrderDraftLineRequest(Guid.NewGuid(), Product, "7", KgUnit, quantity, price, KgUnit)],
            DueTerms: TestDueTerms.PointDue30Wire);

    private static CreateSalesOrderDraftRequest WithLineTax(CreateSalesOrderDraftRequest request, SalesDraftLineTaxOverrideV1 change) =>
        request with { Lines = [request.Lines[0] with { TaxOverride = change }] };

    /// <summary>
    /// A SYNTHETIC Settings line tax port over the REAL Settings calculator (<see cref="TaxMasterCalculatorV1"/>) with SENTETIK data
    /// effective on the test day: the product is "Steel"; the defaults are the test's; the buyer situation list is the reviewed
    /// version-0 entries; the exemption list is empty unless given. <see cref="Fault"/> makes every read throw the port's code.
    /// </summary>
    private sealed class FakeLineTaxes : ISettingsLineTaxResolverV1
    {
        public IReadOnlyList<TaxItemTypeDefaultDataV1> Defaults { get; init; } =
        [
            Default(TaxItemTypesV1.Steel, true, 20m, true, 50m),
            Default(TaxItemTypesV1.Scrap, false, null, false, null),
            Default(TaxItemTypesV1.Service, true, 20m, false, null),
        ];

        public IReadOnlyList<string> ExemptionCodes { get; init; } = [];

        public IReadOnlyList<string> WithholdingCodes { get; init; } = [];

        public string? Fault { get; set; }

        private TaxMasterDataV1 Data => new(Defaults,
            [
                .. ExemptionCodes.Count == 0 ? [] : new[] { new TaxCodeListVersionDataV1(TaxCodeListKindsV1.Exemption, 1, Day,
                    [.. ExemptionCodes.Select(code => new TaxCodeListEntryViewV1(code, "SENTETIK istisna"))]) },
                .. WithholdingCodes.Count == 0 ? [] : new[] { new TaxCodeListVersionDataV1(TaxCodeListKindsV1.Withholding, 1, Day,
                    [.. WithholdingCodes.Select(code => new TaxCodeListEntryViewV1(code, "SENTETIK tevkifat"))]) },
            ],
            [],
            [new TaxBuyerSituationListVersionDataV1(0, Day, TaxDefaultSourcesV1.Catalog, ApprovedTaxBuyerSituationCatalog.Entries)]);

        private static TaxItemTypeDefaultDataV1 Default(string itemType, bool vat, decimal? vatPercent, bool withholding, decimal? withheld) =>
            new(Guid.NewGuid(), Company, itemType, TaxDefaultSourcesV1.Record, 1, Day, vat, vatPercent, null, withholding, withheld, null);

        public Task<SettingsTaxDefaultResolutionV1> ResolveAsync(Guid productId, string buyerSituation, DateOnly on,
            string? exemptionCode = null, CancellationToken cancellationToken = default) =>
            Answer(productId, TaxItemTypesV1.Steel, buyerSituation, on, exemptionCode);

        public Task<SettingsTaxDefaultResolutionV1> GetItemTypeDefaultAsync(string itemType, string buyerSituation, DateOnly on,
            string? exemptionCode = null, CancellationToken cancellationToken = default) =>
            Answer(null, itemType, buyerSituation, on, exemptionCode);

        public Task<SettingsTaxBuyerSituationListResolutionV1> GetBuyerSituationsAsync(DateOnly on, CancellationToken cancellationToken = default)
        {
            Throw();
            var list = TaxMasterCalculatorV1.BuyerSituationListInForce(Data, on);
            return Task.FromResult(list is null
                ? new SettingsTaxBuyerSituationListResolutionV1(Company, on, TaxResolutionStatesV1.Unresolved, null, null, [],
                    TaxResolutionReasonsV1.BuyerSituationListMissing)
                : new SettingsTaxBuyerSituationListResolutionV1(Company, on, TaxResolutionStatesV1.Resolved, list.ListVersion,
                    list.EffectiveFrom, list.Entries, null));
        }

        public Task<SettingsTaxCodeListResolutionV1> GetCodeListAsync(string kind, DateOnly on, CancellationToken cancellationToken = default)
        {
            Throw();
            var list = TaxMasterCalculatorV1.ListInForce(Data, kind, on);
            return Task.FromResult(list is null
                ? new SettingsTaxCodeListResolutionV1(Company, kind, on, TaxResolutionStatesV1.Unresolved, null, null, [],
                    TaxResolutionReasonsV1.CodeListMissing)
                : new SettingsTaxCodeListResolutionV1(Company, kind, on, TaxResolutionStatesV1.Resolved, list.ListVersion,
                    list.EffectiveFrom, list.Entries, null));
        }

        public Task<SettingsWithholdingThresholdResolutionV1> GetWithholdingThresholdAsync(DateOnly on,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsWithholdingThresholdResolutionV1(Company, on, TaxResolutionStatesV1.Unresolved, null, null, null,
                null, null, TaxResolutionReasonsV1.WithholdingThresholdMissing));

        private Task<SettingsTaxDefaultResolutionV1> Answer(Guid? productId, string itemType, string buyerSituation, DateOnly on,
            string? exemptionCode)
        {
            Throw();
            var parts = TaxMasterCalculatorV1.Resolve(Data, itemType, buyerSituation, on, exemptionCode);
            return Task.FromResult(new SettingsTaxDefaultResolutionV1(Company, productId, itemType, buyerSituation, on, parts.State,
                parts.Reason, parts.Record?.Source, parts.Record?.RecordId, parts.Record?.RecordVersion, parts.VatState, parts.VatPercent,
                parts.ExemptionCodeState, parts.ExemptionCode, parts.ExemptionCodeReason, parts.WithholdingState, parts.WithholdingPercent,
                parts.WithholdingCodeState, parts.WithholdingCode, parts.WithholdingCodeReason, parts.BuyerAdjustment,
                parts.BuyerSituationList?.ListVersion, parts.BuyerSituation?.VatMode, parts.BuyerSituation?.DocumentRequired ?? false));
        }

        private void Throw()
        {
            if (Fault is { } code)
            {
                throw new SettingsPrerequisiteException(code);
            }
        }
    }
}

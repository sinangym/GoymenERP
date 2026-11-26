using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task SAT_S12_zero_owner_cost_is_unavailable_and_does_not_block_internal_approval()
    {
        var source = new MarginCostSource { Cost = 0m };
        var world = new World { MarginCosts = source };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("margin-zero-create"))).Detail;
        var preview = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Lines!);
        Assert.Equal("CostUnreadable", preview.UnavailableReason);
        Assert.Null(preview.Amount);
        Assert.Null(preview.Percent);

        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("margin-zero-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("margin-zero-approve")));

        var draft = Assert.Single(world.Store.Drafts);
        Assert.Equal(InternalApprovalState.Approved, draft.ApprovalState);
        var pin = Assert.Single(draft.MarginCosts);
        Assert.Equal(SalesLineMarginUnavailableReason.CostUnreadable, pin.UnavailableReason);
        Assert.Null(pin.CostPerUnit);
        Assert.Null(pin.CurrencyId);
    }

    [Fact]
    public async Task SAT_S12_restored_zero_snapshot_is_not_rewritten_or_disclosed_as_one_hundred_percent()
    {
        var source = new MarginCostSource();
        var world = new World { MarginCosts = source };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("margin-zero-restored"))).Detail;
        var draft = Assert.Single(world.Store.Drafts);
        draft.Submit(draft.Version, Actor, DateTimeOffset.UtcNow);
        draft.Approve(draft.Version, Actor, DateTimeOffset.UtcNow);
        var line = draft.Lines[0];
        var original = new SalesMarginCostSnapshot(line.Id, draft.Revision, line.Product.ProductId,
            line.OrderUnit.UnitId, line.Quantity.Value, Currency, 0m, null,
            "{\"syntheticRestoredZeroCost\":true}", DateTimeOffset.UtcNow);
        draft.RestoreMarginCost(original);

        var result = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Lines!);

        Assert.Equal("CostUnreadable", result.UnavailableReason);
        Assert.Null(result.Amount);
        Assert.Null(result.Percent);
        Assert.Equal(original, Assert.Single(draft.MarginCosts));
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task SAT_S12_legacy_approved_line_is_not_backfilled_when_a_cost_provider_arrives()
    {
        var source = new MarginCostSource();
        var world = new World { MarginCosts = source };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("legacy-margin"))).Detail;
        var draft = Assert.Single(world.Store.Drafts);
        // Represents an approval committed before the cost snapshot feature existed.
        draft.Submit(draft.Version, Actor, DateTimeOffset.UtcNow);
        draft.Approve(draft.Version, Actor, DateTimeOffset.UtcNow);
        Assert.Equal("CostSourceMissing", Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Lines!).UnavailableReason);
        Assert.Empty(draft.MarginCosts);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task SAT_S12_price_only_revision_preserves_the_cost_snapshot_and_does_not_read_new_inventory_cost()
    {
        var source = new MarginCostSource();
        var world = new World { MarginCosts = source }.AllowDiscountsUpTo(10m);
        var created = (await world.Service().CreateDraftAsync(Priced(), Metadata("margin-priced"))).Detail;
        var draft = Assert.Single(world.Store.Drafts);
        await ApproveDiscountChecksAsync(world, created.Draft.Id, draft.Version.ToString(), created.Lines[0].LineId);
        await world.Service().SubmitDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("margin-price-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("margin-price-approve")));
        var pin = Assert.Single(draft.MarginCosts);
        source.Cost = 99m;
        Owners(world);
        await world.Service().ReviseCommercialAsync(draft.Id,
            Revision(draft.Version.ToString(), new SalesDraftLineCommercialV1(created.Lines[0].LineId, "13.00", KgUnit, "5")), Metadata("margin-price-revise"));
        await ApproveDiscountChecksAsync(world, created.Draft.Id, draft.Version.ToString(), created.Lines[0].LineId);
        await world.Service().SubmitDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("margin-price-resubmit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("margin-price-reapprove")));
        Assert.Equal(pin, Assert.Single(draft.MarginCosts));
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task SAT_D426_a_foreign_order_freezes_the_approval_day_rate_and_its_margin_is_computed_in_TL_from_it()
    {
        var source = new MarginCostSource { CostCurrency = SettingsCurrencyIdentitiesV1.TurkishLira, Cost = 3m };
        var rates = new FakeExchangeRates(ExchangeRateResolutionStatesV1.Resolved, "34.5678");
        var world = new World { MarginCosts = source, ExchangeRates = rates }.AllowDiscountsUpTo(10m);
        var created = (await world.Service().CreateDraftAsync(Priced(), Metadata("fx-margin-create"))).Detail;
        var draft = Assert.Single(world.Store.Drafts);
        Assert.Equal("CurrencyConversionUndecided",
            Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Lines!).UnavailableReason);

        await ApproveDiscountChecksAsync(world, created.Draft.Id, draft.Version.ToString(), created.Lines[0].LineId);
        await world.Service().SubmitDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("fx-margin-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("fx-margin-approve")));

        var pin = Assert.Single(draft.MarginCosts);
        Assert.Equal((Currency, 34.5678m), (pin.SaleRate!.CurrencyId, pin.SaleRate.Rate));
        Assert.Contains(rates.Asked, asked => asked.Source == Currency && asked.Target == SettingsCurrencyIdentitiesV1.TurkishLira
            && asked.Day == pin.SaleRate.RateDay);

        // Golden D16: the margin uses the frozen rate; a later Settings rate changes nothing.
        var margin = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Lines!);
        Assert.Null(margin.UnavailableReason);
        Assert.NotNull(margin.Amount);
        rates.Rate = "50";
        var again = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Lines!);
        Assert.Equal(margin, again);
        Assert.Equal(pin, Assert.Single(draft.MarginCosts));
    }

    [Fact]
    public async Task SAT_D426_a_revision_approval_reuses_the_order_rate_frozen_at_the_first_approval()
    {
        var source = new MarginCostSource { CostCurrency = SettingsCurrencyIdentitiesV1.TurkishLira, Cost = 3m };
        var rates = new FakeExchangeRates(ExchangeRateResolutionStatesV1.Resolved, "34.5678");
        var world = new World { MarginCosts = source, ExchangeRates = rates };
        var (approved, _) = await AcceptedApprovedAsync(world);
        var draft = Assert.Single(world.Store.Drafts);
        var first = Assert.Single(draft.MarginCosts).SaleRate!;
        Assert.Equal((Currency, 34.5678m), (first.CurrencyId, first.Rate));

        rates.Rate = "50"; // A later Settings rate (the risk check may still read it) never re-freezes the order rate.
        Owners(world);
        var added = AddedLine();
        await world.Service().ReviseLinesAsync(approved.Id, LineRevision("5", [added]), Metadata("fx-revision-add"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("fx-revision-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("fx-revision-approve")));

        var repinned = Assert.Single(draft.MarginCosts, cost => cost.LineId == added.LineId);
        Assert.Equal(first, repinned.SaleRate);
        Assert.All(draft.MarginCosts, cost => Assert.Equal(first, cost.SaleRate));
    }

    [Fact]
    public async Task SAT_D432_a_revision_preview_of_a_frozen_rate_order_shows_the_new_line_margin_at_that_rate()
    {
        var source = new MarginCostSource { CostCurrency = SettingsCurrencyIdentitiesV1.TurkishLira, Cost = 3m };
        var rates = new FakeExchangeRates(ExchangeRateResolutionStatesV1.Resolved, "34.5678");
        var world = new World { MarginCosts = source, ExchangeRates = rates };
        var (approved, _) = await AcceptedApprovedAsync(world);
        var draft = Assert.Single(world.Store.Drafts);
        rates.Rate = "50"; // A later Settings rate is never used for the preview.
        Owners(world);
        var added = AddedLine(price: "12.00");
        await world.Service().ReviseLinesAsync(approved.Id, LineRevision("5", [added]), Metadata("fx-preview-add"));

        var preview = Assert.Single((await world.Service().GetDraftLinePricingAsync(approved.Id)).Margin.Lines!,
            item => item.LineId == added.LineId);
        Assert.Null(preview.UnavailableReason);
        Assert.NotNull(preview.Amount);
        Assert.DoesNotContain(draft.MarginCosts, cost => cost.LineId == added.LineId); // A preview is never stored.

        var frozen = Assert.Single(draft.MarginCosts).SaleRate!;
        var expected = SalesDraftService.CalculateMarginInTurkishLira(
            SalesOrderTotalsCalculator.CalculateWithLineTax(draft).Lines.Single(item => item.LineId == added.LineId).NetAmountBeforeSurcharge,
            draft.Lines.Single(item => item.Id == added.LineId).OrderUnit.UnitId, 3m,
            new SalesMarginCostSnapshot(added.LineId, draft.Revision, Product, KgUnit, 3m, SettingsCurrencyIdentitiesV1.TurkishLira, 3m,
                null, "{}", DateTimeOffset.UnixEpoch, frozen));
        Assert.Equal(expected.Amount!.Value.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), preview.Amount);
    }

    [Fact]
    public async Task SAT_D430_an_order_approved_without_a_rate_freezes_it_at_its_next_successful_approval()
    {
        var source = new MarginCostSource { CostCurrency = SettingsCurrencyIdentitiesV1.TurkishLira, Cost = 3m };
        var rates = new FakeExchangeRates("NotFound", null);
        var world = new World { MarginCosts = source, ExchangeRates = rates };
        var (approved, firstLine) = await AcceptedApprovedAsync(world);
        var draft = Assert.Single(world.Store.Drafts);
        Assert.Null(Assert.Single(draft.MarginCosts).SaleRate);

        rates.State = ExchangeRateResolutionStatesV1.Resolved;
        rates.Rate = "34.5678";
        Owners(world);
        var added = AddedLine();
        await world.Service().ReviseLinesAsync(approved.Id, LineRevision("5", [added]), Metadata("fx-late-add"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("fx-late-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("fx-late-approve")));

        var frozen = Assert.Single(draft.MarginCosts, cost => cost.LineId == added.LineId).SaleRate!;
        Assert.Equal((Currency, 34.5678m), (frozen.CurrencyId, frozen.Rate));
        // The first line's pinned snapshot is append-only: it is not rewritten and its cost is not re-read (SAT-D269). The order
        // rate still converts it (golden T4 / D15, measured on a priced order in the next test).
        Assert.Null(Assert.Single(draft.MarginCosts, cost => cost.LineId == firstLine).SaleRate);
    }

    [Fact]
    public async Task SAT_D430_golden_D15_a_revision_approval_that_repins_no_line_freezes_the_order_rate_and_converts_every_line()
    {
        // Golden D15: the first approval read no rate (the approval went on, no rate); the revision approval reads USD/TRY and
        // freezes it at that moment (SAT-D430). The revision is price-only, so no cost is re-read (SAT-D269).
        var source = new MarginCostSource { CostCurrency = SettingsCurrencyIdentitiesV1.TurkishLira, Cost = 3m };
        var rates = new FakeExchangeRates("NotFound", null);
        var world = new World { MarginCosts = source, ExchangeRates = rates }.AllowDiscountsUpTo(10m);
        var created = (await world.Service().CreateDraftAsync(Priced(), Metadata("fx-d15-create"))).Detail;
        var draft = Assert.Single(world.Store.Drafts);
        var line = created.Lines[0].LineId;
        await ApproveDiscountChecksAsync(world, created.Draft.Id, draft.Version.ToString(), line);
        await world.Service().SubmitDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("fx-d15-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("fx-d15-approve")));
        var firstPin = Assert.Single(draft.MarginCosts);
        Assert.Null(firstPin.SaleRate);
        Assert.Equal("CurrencyConversionUndecided",
            Assert.Single((await world.Service().GetDraftLinePricingAsync(draft.Id)).Margin.Lines!).UnavailableReason); // D19

        rates.State = ExchangeRateResolutionStatesV1.Resolved;
        rates.Rate = "34.6000";
        source.Cost = 99m; // a later owner cost is never read for the unchanged line
        Owners(world);
        await world.Service().ReviseCommercialAsync(draft.Id,
            Revision(draft.Version.ToString(), new SalesDraftLineCommercialV1(line, "13.00", KgUnit, "5")), Metadata("fx-d15-revise"));
        await ApproveDiscountChecksAsync(world, created.Draft.Id, draft.Version.ToString(), line);
        await world.Service().SubmitDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("fx-d15-resubmit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("fx-d15-reapprove")));

        Assert.Equal(1, source.Reads);
        var carrier = Assert.Single(draft.MarginCosts, cost => cost.Revision == draft.Revision);
        Assert.True(draft.Revision > firstPin.Revision);
        Assert.Equal((Currency, 34.6000m), (carrier.SaleRate!.CurrencyId, carrier.SaleRate.Rate));
        // The cost stays exactly as pinned at the first approval; only the order rate travels with the revision's copy.
        Assert.Equal(firstPin with { Revision = draft.Revision, SaleRate = carrier.SaleRate, RecordedAtUtc = carrier.RecordedAtUtc }, carrier);
        Assert.Equal(firstPin, Assert.Single(draft.MarginCosts, cost => cost.Revision == firstPin.Revision));

        var margin = Assert.Single((await world.Service().GetDraftLinePricingAsync(draft.Id)).Margin.Lines!);
        Assert.Null(margin.UnavailableReason);
        Assert.Equal(ExpectedMargin(draft, line, firstPin with { SaleRate = carrier.SaleRate }), margin.Amount);

        // T4: a later approval never re-freezes it.
        rates.Rate = "50";
        Owners(world);
        await world.Service().ReviseCommercialAsync(draft.Id,
            Revision(draft.Version.ToString(), new SalesDraftLineCommercialV1(line, "13.50", KgUnit, "5")), Metadata("fx-d15-revise-2"));
        await ApproveDiscountChecksAsync(world, created.Draft.Id, draft.Version.ToString(), line);
        await world.Service().SubmitDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("fx-d15-resubmit-2"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("fx-d15-reapprove-2")));
        Assert.Equal(2, draft.MarginCosts.Count); // no further copy: the rate is already frozen
        var again = Assert.Single((await world.Service().GetDraftLinePricingAsync(draft.Id)).Margin.Lines!);
        Assert.Equal(ExpectedMargin(draft, line, firstPin with { SaleRate = carrier.SaleRate }), again.Amount);
    }

    private static string? ExpectedMargin(SalesOrderDraft draft, Guid lineId, SalesMarginCostSnapshot cost)
    {
        var line = draft.Lines.Single(item => item.Id == lineId);
        var expected = SalesDraftService.CalculateMarginInTurkishLira(
            SalesOrderTotalsCalculator.CalculateWithLineTax(draft).Lines.Single(item => item.LineId == lineId).NetAmountBeforeSurcharge,
            line.OrderUnit.UnitId, line.Quantity.Value, cost);
        return expected.Amount!.Value.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task SAT_D426_an_unreadable_approval_day_rate_does_not_block_approval_and_leaves_the_margin_unavailable()
    {
        var source = new MarginCostSource { CostCurrency = SettingsCurrencyIdentitiesV1.TurkishLira, Cost = 3m };
        var rates = new FakeExchangeRates("NotFound", null);
        var world = new World { MarginCosts = source, ExchangeRates = rates }.AllowDiscountsUpTo(10m);
        var created = (await world.Service().CreateDraftAsync(Priced(), Metadata("fx-missing-create"))).Detail;
        var draft = Assert.Single(world.Store.Drafts);
        await ApproveDiscountChecksAsync(world, created.Draft.Id, draft.Version.ToString(), created.Lines[0].LineId);
        await world.Service().SubmitDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("fx-missing-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("fx-missing-approve")));

        Assert.Equal(InternalApprovalState.Approved, draft.ApprovalState);
        Assert.Null(Assert.Single(draft.MarginCosts).SaleRate);
        Assert.Equal("CurrencyConversionUndecided",
            Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Lines!).UnavailableReason);
    }

    [Fact]
    public async Task SAT_S12_reinstated_same_product_and_quantity_preserve_the_first_cost_and_do_not_repin()
    {
        var source = new MarginCostSource();
        var world = new World { MarginCosts = source };
        var (approved, lineId) = await AcceptedApprovedAsync(world);
        Owners(world);
        var draft = Assert.Single(world.Store.Drafts);
        var original = Assert.Single(draft.MarginCosts);
        await world.Service().ReviseLinesAsync(approved.Id, LineRevision("5", [AddedLine()], [new SalesDraftLineRemovalV1(lineId)]),
            Metadata("margin-reinstated-remove"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("margin-reinstated-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("margin-reinstated-approve")));
        await world.Service().RecordCustomerRejectionAsync(approved.Id, Command("8", "SENTETIK satır geri gelsin"),
            Metadata("margin-reinstated-reject"));
        var reads = source.Reads;
        source.Cost = 99m;
        await world.Service().ReviseLinesAsync(approved.Id, LineRevision("9", [], []), Metadata("margin-reinstated-restore"));
        var pricing = await world.Service().GetDraftLinePricingAsync(approved.Id);
        var margin = Assert.Single(pricing.Margin.Lines!, item => item.LineId == lineId);
        Assert.Equal("SalesAmountUnavailable", margin.UnavailableReason);
        Assert.Equal(original, Assert.Single(draft.MarginCosts, item => item.LineId == lineId));
        Assert.Equal(reads, source.Reads);
    }

    private static async Task<(World World, Guid DraftId)> ApprovedPieceOrderAsync(MarginCostSource source, string key)
    {
        // The world's order currency is a synthetic foreign one: its approval-day rate is frozen with the cost (SAT-D426).
        var world = new World
        {
            MarginCosts = source, KilogramsPerUnit = new FakeKilogramsPerUnit { Figure = 12.345m },
            ExchangeRates = new FakeExchangeRates(ExchangeRateResolutionStatesV1.Resolved, "34.5678"),
        };
        world.Settings.PieceUnits.Add(PieceUnit);
        var priced = Priced(discount: null);
        var request = priced with
        {
            Lines = [priced.Lines[0] with { OrderUnitId = PieceUnit, Quantity = "100", RequestedDeliverySlices = null,
                UnitPrice = "10.00", PriceUnitId = PieceUnit }],
        };
        var created = (await world.Service().CreateDraftAsync(request, Metadata(key + "-create"))).Detail;
        var version = await ApproveDiscountChecksAsync(world, created.Draft.Id, created.Draft.Version, created.Lines[0].LineId);
        var submitted = await world.Service().SubmitDraftAsync(created.Draft.Id, Command(version), Metadata(key + "-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(created.Draft.Id, Command(submitted.Detail.Draft.Version), Metadata(key + "-approve")));
        return (world, created.Draft.Id);
    }

    [Fact]
    public async Task SAT_D438_golden_M08_an_approved_piece_line_reads_the_kg_cost_and_shows_the_margin_at_the_pinned_kg_per_unit()
    {
        // M08: 100 adet × 10,00 = 1 000,00 (foreign) at the frozen 34,5678 → 34 567,80 TL; c = 19,9936 TL/kg; k = 12,345 kg/adet
        // pinned at approval → 9 885,70 TL, 28,60 %.
        var source = new MarginCostSource { Cost = 19.9936m, CostCurrency = SettingsCurrencyIdentitiesV1.TurkishLira };
        var (world, draftId) = await ApprovedPieceOrderAsync(source, "m08-piece");

        var margin = Assert.Single((await world.Service().GetDraftLinePricingAsync(draftId)).Margin.Lines!);

        Assert.Equal(("9885.70", "28.60", null), (margin.Amount, margin.Percent, margin.UnavailableReason));
        // The cost is asked and pinned per kilogram, never per piece: no converted cost crosses the port (TM1).
        Assert.All(source.AskedUnits, unit => Assert.Equal(SettingsUnitIdentitiesV1.Kilogram, unit));
        var pin = Assert.Single(Assert.Single(world.Store.Drafts).MarginCosts);
        Assert.Equal((SettingsUnitIdentitiesV1.Kilogram, (decimal?)19.9936m), (pin.UnitId, pin.CostPerUnit));
    }

    [Theory]
    [InlineData(SalesLineMarginUnavailableReason.CostNoSource)]
    [InlineData(SalesLineMarginUnavailableReason.CostUnreadable)]
    [InlineData(SalesLineMarginUnavailableReason.ProducedItem)]
    [InlineData(SalesLineMarginUnavailableReason.MixedSourceItem)]
    public async Task SAT_D438_golden_M14_a_piece_line_keeps_the_cost_owners_reason_and_shows_no_figure(
        SalesLineMarginUnavailableReason reason)
    {
        var source = new MarginCostSource { Reason = reason };
        var (world, draftId) = await ApprovedPieceOrderAsync(source, "m14-" + reason);

        var margin = Assert.Single((await world.Service().GetDraftLinePricingAsync(draftId)).Margin.Lines!);

        Assert.Equal((null, null, reason.ToString()), (margin.Amount, margin.Percent, margin.UnavailableReason));
    }

    private sealed class MarginCostSource : ISalesMarginCostReadPortV1
    {
        public int Reads { get; private set; }
        public decimal Cost { get; set; } = 12.3456m;
        public Guid CostCurrency { get; set; } = Currency;
        public Guid? WrongCompany { get; set; }
        public Action? OnRead { get; set; }
        public SalesLineMarginUnavailableReason? Reason { get; set; }
        public List<Guid> AskedUnits { get; } = [];
        public Task<SalesMarginCostReadAnswerV1> ReadAsync(Guid companyId, Guid productId, Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            AskedUnits.Add(unitId);
            OnRead?.Invoke();
            return Task.FromResult(new SalesMarginCostReadAnswerV1(1, WrongCompany ?? companyId, productId, unitId,
                Reason is null ? CostCurrency : null, Reason is null ? Cost : null, Reason, "{\"syntheticOwnerVersion\":17}"));
        }
    }

    [Fact]
    public async Task SAT_S12_unpriced_live_cost_preview_stays_unavailable_without_persisting_or_disclosing_revoked_cost()
    {
        var source = new MarginCostSource();
        var world = new World { MarginCosts = source };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("margin-preview"))).Detail;
        var pricing = await world.Service().GetDraftLinePricingAsync(created.Draft.Id);
        Assert.Equal("SalesAmountUnavailable", Assert.Single(pricing.Margin.Lines!).UnavailableReason);
        Assert.Empty(Assert.Single(world.Store.Drafts).MarginCosts);
        Assert.Equal(1, source.Reads);
        world.Authority.Denied.Add(MarginReadPermission);
        Assert.Equal(SalesMarginDisclosure.Withheld, (await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Disclosure);
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task SAT_S12_reviewed_G01_reaches_the_actual_TL_preview_without_persisting_a_snapshot()
    {
        var tl = SettingsCurrencyIdentitiesV1.TurkishLira;
        var source = new MarginCostSource { Cost = 7.1234m, CostCurrency = tl };
        var world = new World { MarginCosts = source };
        var request = Priced(unitPrice: "10", discount: null);
        request = request with { CurrencyId = tl, Lines = [request.Lines[0] with { Quantity = "100" }] };
        var created = (await world.Service().CreateDraftAsync(request, Metadata("margin-reviewed-g01"))).Detail;

        var margin = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Lines!);

        Assert.Equal("Computed", margin.State);
        Assert.Null(margin.UnavailableReason);
        Assert.Equal("287.66", margin.Amount);
        Assert.Equal(tl, margin.CurrencyId);
        Assert.Equal("28.77", margin.Percent);
        Assert.Empty(Assert.Single(world.Store.Drafts).MarginCosts);
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task SAT_S12_foreign_priced_preview_stays_unavailable_after_the_golden_is_accepted()
    {
        var source = new MarginCostSource();
        var world = new World { MarginCosts = source };
        var request = Priced(discount: null);
        var created = (await world.Service().CreateDraftAsync(request, Metadata("margin-reviewed-foreign"))).Detail;

        var margin = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Lines!);

        Assert.Equal("CurrencyConversionUndecided", margin.UnavailableReason);
        Assert.Null(margin.Amount);
        Assert.Null(margin.Percent);
        Assert.Empty(Assert.Single(world.Store.Drafts).MarginCosts);
    }

    [Fact]
    public async Task SAT_S12_internal_approval_pins_actual_cost_once_replay_and_later_source_change_cannot_rewrite_it()
    {
        var source = new MarginCostSource();
        var world = new World { MarginCosts = source };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("margin-create"))).Detail;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("margin-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("margin-approve")));
        var pin = Assert.Single(Assert.Single(world.Store.Drafts).MarginCosts);
        Assert.Equal((Currency, (decimal?)12.3456m, 0), (pin.CurrencyId, pin.CostPerUnit, pin.Revision));
        Assert.Contains("17", pin.SourceProof, StringComparison.Ordinal);
        source.Cost = 99;
        Assert.True((await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("margin-approve")))).IsReplay);
        var pricing = await world.Service().GetDraftLinePricingAsync(created.Draft.Id);
        Assert.Equal("Snapshot", pricing.Margin.Kind);
        Assert.Equal(pin, Assert.Single(Assert.Single(world.Store.Drafts).MarginCosts));
        Assert.Equal(1, source.Reads);
    }

    [Theory]
    [InlineData(SalesLineMarginUnavailableReason.CostNoSource)]
    [InlineData(SalesLineMarginUnavailableReason.CostUnreadable)]
    public async Task SAT_S12_unavailable_cost_does_not_block_approval_and_is_never_filled_later(SalesLineMarginUnavailableReason reason)
    {
        var source = new MarginCostSource { Reason = reason };
        var world = new World { MarginCosts = source };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("margin-missing-create"))).Detail;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("margin-missing-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("margin-missing-approve")));
        source.Reason = null;
        Assert.Equal(reason.ToString(), Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Lines!).UnavailableReason);
        Assert.Null(Assert.Single(Assert.Single(world.Store.Drafts).MarginCosts).CostPerUnit);
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task SAT_S12_foreign_cost_is_unreadable_and_permission_revoked_during_owner_read_discloses_nothing()
    {
        var source = new MarginCostSource { WrongCompany = Guid.NewGuid() };
        var world = new World { MarginCosts = source };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("margin-foreign"))).Detail;
        Assert.Equal("CostUnreadable", Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Lines!).UnavailableReason);
        source.WrongCompany = null;
        source.OnRead = () => world.Authority.Denied.Add(MarginReadPermission);
        Assert.Equal(SalesMarginDisclosure.Withheld, (await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Disclosure);
        Assert.Empty(Assert.Single(world.Store.Drafts).MarginCosts);
    }

    [Fact]
    public async Task SAT_S12_quantity_revision_repins_only_changed_basis_and_preserves_previous_snapshot_history()
    {
        var source = new MarginCostSource();
        var world = new World { MarginCosts = source };
        var (approved, line) = await AcceptedApprovedAsync(world);
        Owners(world);
        var old = Assert.Single(Assert.Single(world.Store.Drafts).MarginCosts);
        source.Cost = 22m;
        await world.Service().ReviseQuantityAsync(approved.Id,
            new SalesDraftQuantityRevisionRequest("5", "SENTETIK miktar değişimi", [new SalesDraftLineQuantityV1(line, "2")]), Metadata("margin-quantity"));
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("margin-quantity-submit"));
        await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("margin-quantity-approve")));
        var pins = Assert.Single(world.Store.Drafts).MarginCosts;
        Assert.Equal(2, pins.Count);
        Assert.Equal(old, pins[0]);
        Assert.Equal((22m, 2m, 1), (pins[1].CostPerUnit, pins[1].Quantity, pins[1].Revision));
        Assert.Equal(2, source.Reads);
    }
}

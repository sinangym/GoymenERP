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
        await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("margin-zero-approve"));

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
        await world.Service().ApproveDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("margin-price-approve"));
        var pin = Assert.Single(draft.MarginCosts);
        source.Cost = 99m;
        Owners(world);
        await world.Service().ReviseCommercialAsync(draft.Id,
            Revision(draft.Version.ToString(), new SalesDraftLineCommercialV1(created.Lines[0].LineId, "13.00", KgUnit, "5")), Metadata("margin-price-revise"));
        await ApproveDiscountChecksAsync(world, created.Draft.Id, draft.Version.ToString(), created.Lines[0].LineId);
        await world.Service().SubmitDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("margin-price-resubmit"));
        await world.Service().ApproveDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("margin-price-reapprove"));
        Assert.Equal(pin, Assert.Single(draft.MarginCosts));
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task SAT_D426_a_foreign_order_freezes_the_approval_day_rate_and_its_margin_stays_undecided_until_its_golden_is_in_main()
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
        await world.Service().ApproveDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("fx-margin-approve"));

        var pin = Assert.Single(draft.MarginCosts);
        Assert.Equal((Currency, 34.5678m), (pin.SaleRate!.CurrencyId, pin.SaleRate.Rate));
        Assert.Contains(rates.Asked, asked => asked.Source == Currency && asked.Target == SettingsCurrencyIdentitiesV1.TurkishLira
            && asked.Day == pin.SaleRate.RateDay);

        // SAT-D265(5): main's golden G20 still keeps a non-TL sale undecided; the frozen rate is stored, not yet applied.
        rates.Rate = "50";
        var margin = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Lines!);
        Assert.Equal("CurrencyConversionUndecided", margin.UnavailableReason);
        Assert.Null(margin.Amount);
        Assert.Null(margin.Percent);
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
        await world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("fx-revision-approve"));

        var repinned = Assert.Single(draft.MarginCosts, cost => cost.LineId == added.LineId);
        Assert.Equal(first, repinned.SaleRate);
        Assert.All(draft.MarginCosts, cost => Assert.Equal(first, cost.SaleRate));
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
        await world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("fx-late-approve"));

        var frozen = Assert.Single(draft.MarginCosts, cost => cost.LineId == added.LineId).SaleRate!;
        Assert.Equal((Currency, 34.5678m), (frozen.CurrencyId, frozen.Rate));
        Assert.Null(Assert.Single(draft.MarginCosts, cost => cost.LineId == firstLine).SaleRate);
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
        await world.Service().ApproveDraftAsync(draft.Id, Command(draft.Version.ToString()), Metadata("fx-missing-approve"));

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
        await world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("margin-reinstated-approve"));
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

    private sealed class MarginCostSource : ISalesMarginCostReadPortV1
    {
        public int Reads { get; private set; }
        public decimal Cost { get; set; } = 12.3456m;
        public Guid CostCurrency { get; set; } = Currency;
        public Guid? WrongCompany { get; set; }
        public Action? OnRead { get; set; }
        public SalesLineMarginUnavailableReason? Reason { get; set; }
        public Task<SalesMarginCostReadAnswerV1> ReadAsync(Guid companyId, Guid productId, Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Reads++;
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
        await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("margin-approve"));
        var pin = Assert.Single(Assert.Single(world.Store.Drafts).MarginCosts);
        Assert.Equal((Currency, (decimal?)12.3456m, 0), (pin.CurrencyId, pin.CostPerUnit, pin.Revision));
        Assert.Contains("17", pin.SourceProof, StringComparison.Ordinal);
        source.Cost = 99;
        Assert.True((await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("margin-approve"))).IsReplay);
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
        await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("margin-missing-approve"));
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
        await world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("margin-quantity-approve"));
        var pins = Assert.Single(world.Store.Drafts).MarginCosts;
        Assert.Equal(2, pins.Count);
        Assert.Equal(old, pins[0]);
        Assert.Equal((22m, 2m, 1), (pins[1].CostPerUnit, pins[1].Quantity, pins[1].Revision));
        Assert.Equal(2, source.Reads);
    }
}

using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
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

    private sealed class MarginCostSource : ISalesMarginCostReadPortV1
    {
        public int Reads { get; private set; }
        public decimal Cost { get; set; } = 12.3456m;
        public Guid? WrongCompany { get; set; }
        public Action? OnRead { get; set; }
        public SalesLineMarginUnavailableReason? Reason { get; set; }
        public Task<SalesMarginCostReadAnswerV1> ReadAsync(Guid companyId, Guid productId, Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            OnRead?.Invoke();
            return Task.FromResult(new SalesMarginCostReadAnswerV1(1, WrongCompany ?? companyId, productId, unitId,
                Reason is null ? Currency : null, Reason is null ? Cost : null, Reason, "{\"syntheticOwnerVersion\":17}"));
        }
    }

    [Fact]
    public async Task SAT_S12_live_cost_preview_is_not_persisted_and_independent_golden_gate_remains_closed()
    {
        var source = new MarginCostSource();
        var world = new World { MarginCosts = source };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("margin-preview"))).Detail;
        var pricing = await world.Service().GetDraftLinePricingAsync(created.Draft.Id);
        Assert.Equal("FormulaGoldenPending", Assert.Single(pricing.Margin.Lines!).UnavailableReason);
        Assert.Empty(Assert.Single(world.Store.Drafts).MarginCosts);
        Assert.Equal(1, source.Reads);
        world.Authority.Denied.Add(MarginReadPermission);
        Assert.Equal(SalesMarginDisclosure.Withheld, (await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Margin.Disclosure);
        Assert.Equal(1, source.Reads);
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

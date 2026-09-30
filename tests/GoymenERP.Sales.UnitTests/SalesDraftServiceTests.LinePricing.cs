using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// AT-018 / AT-021 on the order line (user decision 2026-09-30): the list price is the line's reference and default; the
/// person may overwrite it, give a discount (percent or amount) and a maturity surcharge (percent or amount); reference and
/// adjustments are read back with the calculator's net; a net below the floor is refused; the owner is asked under the
/// price fence in the draft's currency on its business date. Synthetic ports, NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static readonly Guid PriceList = Guid.Parse("abababab-abab-4bab-8bab-abababababab");

    [Fact]
    public async Task A_line_priced_from_the_list_keeps_the_reference_and_the_adjustments_and_reads_back_the_calculators_net()
    {
        var references = new FakePriceReferences { Price = 12.50m, Floor = 10m };
        var world = new World { PriceReferences = references };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var lineId = created.Lines[0].LineId;

        var offered = await world.Service().GetLinePriceReferencesAsync(created.Draft.Id, lineId);
        var candidate = Assert.Single(offered.Items);
        Assert.Equal((PriceList, 1, "12.5", "10", "TRY", Day), (candidate.PriceListId, candidate.VersionNo, candidate.UnitPrice,
            candidate.FloorUnitPrice, offered.CurrencyCode, offered.On));

        var revised = await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(lineId, "12.50", KgUnit, "10", new SalesDraftPriceReferenceRequestV1(PriceList, 1),
                SurchargePercent: "2")), Metadata("k-r1"));
        Assert.Equal("12.50", revised.Detail.Lines[0].UnitPrice);
        // The owner was asked for exactly this list version, the line's product and price unit, on the draft's business date.
        Assert.Equal((PriceList, 1, Product, KgUnit, Day), references.LastGet);

        var pricing = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines);
        Assert.Equal(("Reference", "12.5", "10", "2", "11.475"), (pricing.PriceSource, pricing.PriceReference!.UnitPrice, pricing.DiscountPercent,
            pricing.SurchargePercent, pricing.NetUnitPrice));
        Assert.Equal(("SENTETIK liste", "10", "ExcludingVat"), (pricing.PriceReference.PriceListName, pricing.PriceReference.FloorUnitPrice,
            pricing.PriceReference.VatBasis));
    }

    [Fact]
    public async Task An_overwritten_price_with_amount_adjustments_is_manual_and_keeps_the_reference_visible()
    {
        var world = new World { PriceReferences = new FakePriceReferences { Price = 12.50m } };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var lineId = created.Lines[0].LineId;

        await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(lineId, "13.00", KgUnit, null, new SalesDraftPriceReferenceRequestV1(PriceList, 1),
                DiscountAmount: "0.50", SurchargeAmount: "0.25")), Metadata("k-r1"));

        var pricing = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines);
        Assert.Equal(("Manual", "13.00", "12.5", "0.50", "0.25", "12.75"), (pricing.PriceSource, pricing.UnitPrice, pricing.PriceReference!.UnitPrice,
            pricing.DiscountAmount, pricing.SurchargeAmount, pricing.NetUnitPrice));
    }

    [Fact]
    public async Task A_net_below_the_list_floor_is_refused_on_the_line_and_nothing_is_stored()
    {
        var world = new World { PriceReferences = new FakePriceReferences { Price = 12.50m, Floor = 12m } };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var lineId = created.Lines[0].LineId;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, new SalesDraftLineCommercialV1(lineId, "12.50", KgUnit, "10",
                new SalesDraftPriceReferenceRequestV1(PriceList, 1))), Metadata("k-r1")));

        Assert.Equal((422, "lines[0].unitPrice", SalesRuleCodes.PriceBelowFloor), (refused.Status, Assert.Single(refused.Errors).Field,
            Assert.Single(refused.Errors).Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseCommercialV1);
    }

    [Fact]
    public async Task A_version_that_no_longer_prices_the_line_another_currency_or_two_discount_kinds_are_refused_before_the_save()
    {
        var references = new FakePriceReferences { Price = 12.50m, Missing = true };
        var world = new World { PriceReferences = references };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var lineId = created.Lines[0].LineId;
        var named = new SalesDraftLineCommercialV1(lineId, "12.50", KgUnit, null, new SalesDraftPriceReferenceRequestV1(PriceList, 1));

        var gone = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, named), Metadata("k-r1")));
        Assert.Equal((422, "lines[0].priceReference", SalesDraftProblemCodesV1.PriceReferenceUnavailable),
            (gone.Status, Assert.Single(gone.Errors).Field, Assert.Single(gone.Errors).Code));

        references.Missing = false;
        references.Currency = "USD";
        var currency = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, named), Metadata("k-r2")));
        Assert.Equal(SalesDraftProblemCodesV1.PriceReferenceCurrencyMismatch, Assert.Single(currency.Errors).Code);

        references.Currency = "TRY";
        var both = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, named with { DiscountPercent = "5", DiscountAmount = "1" }), Metadata("k-r3")));
        Assert.Equal(("lines", SalesRuleCodes.AdjustmentKindConflict), (Assert.Single(both.Errors).Field, Assert.Single(both.Errors).Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseCommercialV1);
    }

    [Fact]
    public async Task Without_the_owner_the_reference_is_503_and_each_read_needs_its_own_price_name()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var lineId = created.Lines[0].LineId;

        var unavailable = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetLinePriceReferencesAsync(created.Draft.Id, lineId));
        Assert.Equal((503, SalesDraftProblemCodesV1.PriceListProviderUnavailable), (unavailable.Status, unavailable.Code));
        var revision = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, new SalesDraftLineCommercialV1(lineId, "1", KgUnit, null, new SalesDraftPriceReferenceRequestV1(PriceList, 1))),
            Metadata("k-r1")));
        Assert.Equal(503, revision.Status);

        world.PriceReferences = new FakePriceReferences { Price = 1m };
        world.Authority.Denied.Add(PricePermission);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetLinePriceReferencesAsync(created.Draft.Id, lineId))).Status);
        world.Authority.Denied.Clear();
        world.Authority.Denied.Add(PriceReadPermission);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftLinePricingAsync(created.Draft.Id))).Status);
        world.Authority.Denied.Clear();
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetLinePriceReferencesAsync(created.Draft.Id, Guid.NewGuid()))).Status);
    }

    private sealed class FakePriceReferences : IProductPriceReferenceReaderV1
    {
        public decimal Price { get; set; }
        public decimal? Floor { get; set; }
        public string Currency { get; set; } = "TRY";
        public bool Missing { get; set; }
        public (Guid List, int Version, Guid Product, Guid Unit, DateOnly On)? LastGet { get; private set; }

        public Task<IReadOnlyList<ProductPriceReferenceV1>> SearchAsync(Guid productId, IReadOnlyCollection<Guid> priceUnitIds, string currencyCode,
            DateOnly on, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductPriceReferenceV1>>(Missing || currencyCode != Currency
                ? []
                : [.. priceUnitIds.Where(unit => unit == KgUnit).Select(unit => Reference(productId, unit))]);

        public Task<ProductPriceReferenceV1?> GetAsync(Guid priceListId, int versionNo, Guid productId, Guid priceUnitId, DateOnly on,
            CancellationToken cancellationToken = default)
        {
            LastGet = (priceListId, versionNo, productId, priceUnitId, on);
            return Task.FromResult(Missing || priceListId != PriceList ? null : Reference(productId, priceUnitId));
        }

        private ProductPriceReferenceV1 Reference(Guid productId, Guid unitId) =>
            new(PriceList, "SENTETIK liste", Guid.NewGuid(), 1, Day, null, productId, unitId, Currency, "Sales", "ExcludingVat", Price, Floor);
    }
}

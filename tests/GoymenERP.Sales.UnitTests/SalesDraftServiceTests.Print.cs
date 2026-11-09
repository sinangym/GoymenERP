using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// MOD-07: the sales order PDF's price facts. Under the draft read; the price facts (currency code and every line's pricing
/// with the calculator's net) only under the price read, asked after the Settings read; without it the answer is the
/// withheld marker with nothing else — not a refusal. Synthetic ports, NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Print_pricing_carries_the_currency_and_every_lines_stored_pricing_with_the_calculators_net_at_the_drafts_version()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Priced(), Metadata("k-print-1"))).Detail;
        world.Settings.Calls = 0;
        world.Authority.Measured.Clear();

        var pricing = await world.Service().GetOrderPrintPricingAsync(created.Draft.Id);

        Assert.Equal((created.Draft.Version, SalesPriceDisclosure.Visible, "TRY"), (pricing.Version, pricing.PriceDisclosure, pricing.CurrencyCode));
        var line = Assert.Single(pricing.Lines);
        Assert.Equal((created.Lines[0].LineId, 1, "12.50", "5", "11.875"), (line.LineId, line.LineNo, line.UnitPrice, line.DiscountPercent,
            line.NetUnitPrice));
        // The draft's currency on its business date, for the Sales operation: the money profile the draft was created under.
        Assert.Equal((SalesMoneyOperations.OrderDraftV1, created.Draft.CurrencyId, Day), world.Settings.MoneyAsked);
        Assert.Equal(1, world.Settings.Calls);
        // The price read is the last authority measured: nothing is released on an earlier answer.
        Assert.Equal(PriceReadPermission, world.Authority.Measured[^1]);
        Assert.Equal(2, world.Authority.Measured.Count(name => name == PriceReadPermission));
    }

    [Fact]
    public async Task Print_pricing_without_the_price_read_is_the_withheld_marker_with_no_currency_and_no_line_and_asks_no_owner()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Priced(), Metadata("k-print-2"))).Detail;
        world.Authority.Denied.Add(PriceReadPermission);
        world.Settings.Calls = 0;

        var pricing = await world.Service().GetOrderPrintPricingAsync(created.Draft.Id);

        Assert.Equal((created.Draft.Version, SalesPriceDisclosure.Withheld, (string?)null), (pricing.Version, pricing.PriceDisclosure,
            pricing.CurrencyCode));
        Assert.Empty(pricing.Lines);
        Assert.Equal(0, world.Settings.Calls);
    }

    [Fact]
    public async Task Print_pricing_lost_while_the_currency_was_read_releases_nothing_priced()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Priced(), Metadata("k-print-3"))).Detail;
        world.Settings.Calls = 0;
        // Granted at the first price-read measurement, lost from the next one on (after the Settings read).
        world.Authority.OnRead = (permission, _) =>
        {
            if (permission == PriceReadPermission)
            {
                world.Authority.Denied.Add(PriceReadPermission);
            }
        };

        var pricing = await world.Service().GetOrderPrintPricingAsync(created.Draft.Id);

        Assert.Equal((SalesPriceDisclosure.Withheld, (string?)null, 0), (pricing.PriceDisclosure, pricing.CurrencyCode, pricing.Lines.Count));
        // The currency WAS read under the first grant; it is not released once the grant is gone.
        Assert.Equal(1, world.Settings.Calls);
    }

    [Fact]
    public async Task Print_pricing_of_an_unknown_or_other_companys_draft_is_404_and_without_the_draft_read_403()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Priced(), Metadata("k-print-4"))).Detail;

        Assert.Equal((404, "not_found"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetOrderPrintPricingAsync(Guid.NewGuid()))));
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetOrderPrintPricingAsync(Guid.Empty))).Status);

        world.Authority.Denied.Add(ReadPermission);
        world.Settings.Calls = 0;
        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetOrderPrintPricingAsync(created.Draft.Id))));
        Assert.Equal(0, world.Settings.Calls);
    }

    [Theory]
    [InlineData("money-unavailable", 503, "settings_provider_unavailable")]
    [InlineData("money-not-found", 422, SalesDraftProblemCodesV1.UnsupportedCurrency)]
    [InlineData("money-other-company", 503, SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    public async Task Print_pricing_whose_currency_cannot_be_named_is_refused_never_printed_without_it(string fault, int status, string code)
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Priced(), Metadata("k-print-5"))).Detail;
        world.Settings.Fault = fault;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetOrderPrintPricingAsync(created.Draft.Id));

        Assert.Equal((status, code), (refused.Status, refused.Code));
    }

    [Fact]
    public async Task Print_pricing_carries_the_calculators_line_vat_withholding_and_document_totals_at_the_current_revision()
    {
        // SAT-D11 / SAT-D75 (MOD-07): the PDF prints the ONE totals calculator's answer — never a figure of its own.
        var world = new World { LineTaxes = new FakeLineTaxes() };
        var created = (await world.Service().CreateDraftAsync(PricedRequest("10", "100"), Metadata("k-print-tax-1"))).Detail;
        var calculated = await world.Service().GetDraftTotalsAsync(created.Draft.Id);

        var pricing = await world.Service().GetOrderPrintPricingAsync(created.Draft.Id);

        var totals = pricing.Totals!;
        Assert.Equal(created.Draft.Revision, totals.Revision);
        // 10 kg × 100 = 1000.00; VAT 20 % = 200.00; withheld 50 % of the rounded VAT = 100.00; gross 1200.00; payable 1100.00.
        Assert.Equal(("Computed", "1000.00", "Computed", "200.00", "100.00", "1200.00", "1100.00"), (totals.Document.State,
            totals.Document.NetAmount, totals.Document.TaxState, totals.Document.VatAmount, totals.Document.WithheldVatAmount,
            totals.Document.GrossAmount, totals.Document.PayableAmount));
        Assert.Equal(calculated.Totals, totals.Document);
        var line = Assert.Single(totals.Lines);
        Assert.Equal(Assert.Single(calculated.Lines), line.Total);
        Assert.Equal(("50", (string?)null, (string?)null), (line.WithholdingPercent, line.ExemptionCode, line.WithholdingCode));
    }

    [Fact]
    public async Task Print_pricing_of_a_line_without_a_tax_source_has_its_net_and_no_tax_and_the_withheld_marker_has_no_totals()
    {
        var world = new World { LineTaxes = new FakeLineTaxes { Defaults = [] } };
        var created = (await world.Service().CreateDraftAsync(PricedRequest("10", "100"), Metadata("k-print-tax-2"))).Detail;

        var totals = (await world.Service().GetOrderPrintPricingAsync(created.Draft.Id)).Totals!;

        Assert.Equal(("1000.00", "NoSource", (string?)null, (string?)null, 1), (totals.Document.NetAmount, totals.Document.TaxState,
            totals.Document.VatAmount, totals.Document.PayableAmount, totals.Document.TaxUnavailableLineCount));
        var line = Assert.Single(totals.Lines);
        Assert.Equal(("1000.00", (string?)null, (string?)null), (line.Total.NetAmount, line.Total.VatAmount, line.WithholdingPercent));

        world.Authority.Denied.Add(PriceReadPermission);
        Assert.Null((await world.Service().GetOrderPrintPricingAsync(created.Draft.Id)).Totals);
    }
}

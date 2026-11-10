using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D250 / SAT-D265 (5) / SAT-D266 / SAT-D267 (S-05): the margin block of the line-pricing read — a SEPARATE disclosure
/// measured after the price read; withheld (every field null) without the margin name, and with it every line unavailable
/// with <c>CostSourceMissing</c> until a cost source exists — never a margin of zero. Synthetic ports, NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private const string MarginReadPermission = SalesPermissionManifestV1.OrdersMarginRead;

    [Fact]
    public async Task With_the_margin_read_every_line_margin_is_unavailable_because_no_cost_source_exists_never_zero()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-margin"))).Detail;
        world.Authority.Measured.Clear();

        var pricing = await world.Service().GetDraftLinePricingAsync(created.Draft.Id);

        Assert.Equal((SalesMarginDisclosure.Visible, "PurchaseCostOnly", "Preview"),
            (pricing.Margin.Disclosure, pricing.Margin.CostBasis, pricing.Margin.Kind));
        Assert.Equal(pricing.Lines.Select(line => (line.LineId, line.LineNo)).OrderBy(pair => pair.LineNo),
            pricing.Margin.Lines!.Select(line => (line.LineId, line.LineNo)));
        Assert.All(pricing.Margin.Lines!, line => Assert.Equal(
            ("Unavailable", (string?)"CostSourceMissing", (string?)null, (Guid?)null, (string?)null, (string?)null),
            (line.State, line.UnavailableReason, line.Amount, line.CurrencyId, line.Percent, line.PercentUnavailableReason)));
        // The price read is measured first; the margin read is its own measurement after it.
        Assert.Equal([ReadPermission, PriceReadPermission, MarginReadPermission],
            world.Authority.Measured.Where(name => name is ReadPermission or PriceReadPermission or MarginReadPermission).Distinct());
        Assert.Equal(MarginReadPermission, world.Authority.Measured[^1]);
    }

    [Fact]
    public async Task Without_the_margin_read_the_block_is_withheld_with_every_field_null_and_the_pricing_still_answers()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-margin-denied"))).Detail;
        world.Authority.Denied.Add(MarginReadPermission);

        var pricing = await world.Service().GetDraftLinePricingAsync(created.Draft.Id);

        Assert.Single(pricing.Lines);
        Assert.Equal(new SalesDraftMarginV1(SalesMarginDisclosure.Withheld, null, null, null), pricing.Margin);
        var json = System.Text.Json.JsonSerializer.Serialize(pricing.Margin, SalesDraftJson.Options);
        Assert.Contains("\"Withheld\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CostSourceMissing", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_price_read_never_confers_the_margin_and_without_the_price_read_the_margin_is_never_measured()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-margin-price"))).Detail;
        world.Authority.Denied.Add(PriceReadPermission);
        world.Authority.Measured.Clear();

        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftLinePricingAsync(created.Draft.Id));

        Assert.Equal((403, "forbidden"), (forbidden.Status, forbidden.Code));
        Assert.DoesNotContain(MarginReadPermission, world.Authority.Measured);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task A_margin_read_answered_for_a_drifted_actor_or_company_is_withheld(string drift)
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-margin-drift"))).Detail;
        world.Authority.OnRead = (permission, _) =>
        {
            if (permission != MarginReadPermission)
            {
                return;
            }

            if (drift == "actor") world.User.Id = "synthetic-other-actor";
            else world.User.Company = Guid.NewGuid();
        };

        var drifted = await world.Service().GetDraftLinePricingAsync(created.Draft.Id);

        Assert.Equal(new SalesDraftMarginV1(SalesMarginDisclosure.Withheld, null, null, null), drifted.Margin);
    }

    [Fact]
    public async Task A_draft_the_tenant_filter_does_not_return_is_not_found_before_any_margin_is_measured()
    {
        var world = new World();
        world.Authority.Measured.Clear();

        var missing = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftLinePricingAsync(Guid.NewGuid()));

        Assert.Equal((404, "not_found"), (missing.Status, missing.Code));
        Assert.DoesNotContain(MarginReadPermission, world.Authority.Measured);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sales.synthetic.unregistered")]
    public async Task An_unbound_or_unregistered_margin_name_is_the_hosts_fault_never_a_disclosure(string? permission)
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-margin-unbound"))).Detail;
        world.Bindings = world.Bindings with { MarginReadPermission = permission };

        var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftLinePricingAsync(created.Draft.Id));

        Assert.Equal((503, SalesDraftProblemCodesV1.PermissionUnregistered), (failure.Status, failure.Code));
    }
}

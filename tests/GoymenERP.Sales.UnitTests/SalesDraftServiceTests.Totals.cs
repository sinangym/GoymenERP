using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-03 / SAT-D11 over SYNTHETIC owners: the totals are the one calculator's answer from the stored lines and money profile
/// snapshot; the tax is NoSource (no VAT master exists) and never zero; the read needs the draft read AND the price read, the
/// price read measured before the draft is read and again after it; another company's draft is 404; nothing is written.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Totals_are_the_calculators_amounts_of_the_current_lines_with_the_tax_no_source_and_nothing_written()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-totals"))).Detail;
        var (first, second) = (created.Lines[0].LineId, created.Lines[1].LineId);
        var revised = (await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(first, "12.50", KgUnit, "10", SurchargePercent: "2"),
            new SalesDraftLineCommercialV1(second, "100", KgUnit, null, DiscountAmount: "0.50")), Metadata("k-totals-price"))).Detail;
        var attempts = world.Store.Attempts;
        var envelopes = world.Store.Envelopes.Count;
        world.Authority.Measured.Clear();

        var totals = await world.Service().GetDraftTotalsAsync(created.Draft.Id);

        Assert.Equal((revised.Draft.Version, revised.Draft.Revision, Currency, 2, "NoSource"),
            (totals.DraftVersion, totals.Revision, totals.CurrencyId, totals.AmountScale, totals.TaxState));
        // 1.25 kg × 11.475 = 14.34375 → 14.34; 2.25 kg × 99.5 = 223.875 → 223.88 (SAT-D11: half away from zero, per line).
        Assert.Equal(
            [
                new SalesDraftLineTotalV1(first, 1, KgUnit, "1.25", "Computed", "11.475", "14.34", "NoSource", null, null, null, null, null, null),
                new SalesDraftLineTotalV1(second, 2, KgUnit, "2.25", "Computed", "99.5", "223.88", "NoSource", null, null, null, null, null, null),
            ],
            totals.Lines);
        Assert.Equal(new SalesDraftDocumentTotalsV1("Computed", 0, 0, 0, "238.22", "NoSource", null, null, null, null), totals.Totals);
        // The draft read, the price read BEFORE the draft is read, the draft read's fence after it, the price read last.
        Assert.Equal([ReadPermission, PriceReadPermission, ReadPermission, PriceReadPermission], world.Authority.Measured);
        Assert.Equal((attempts, envelopes), (world.Store.Attempts, world.Store.Envelopes.Count));
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task An_unpriced_order_has_no_document_total_and_no_line_amount_never_zero()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-unpriced"))).Detail;

        var totals = await world.Service().GetDraftTotalsAsync(created.Draft.Id);

        var line = Assert.Single(totals.Lines);
        Assert.Equal(("Unpriced", null, null, "NotComputed"), (line.State, line.NetUnitPrice, line.NetAmount, line.TaxState));
        Assert.Equal(new SalesDraftDocumentTotalsV1("Incomplete", 1, 0, 0, null, "NotComputed", null, null, null, null), totals.Totals);
    }

    [Fact]
    public async Task An_amount_beyond_the_decimal_range_is_a_line_state_and_an_incomplete_document_never_an_exception()
    {
        // Review B P2-1: the domain accepts 999999999999999.999 kg (decimal(18,3)) at a price of 100000000000000; the exact
        // product (≈ 1e29) does not fit System.Decimal. The read answers it as a state — no amount, never a 500.
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(RequestWithQuantity("999999999999999.999", null),
            Metadata("k-totals-overflow"))).Detail;
        var lineId = created.Lines[0].LineId;
        await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(lineId, "100000000000000", KgUnit, null)), Metadata("k-totals-overflow-price"));

        var totals = await world.Service().GetDraftTotalsAsync(created.Draft.Id);

        Assert.Equal(new SalesDraftLineTotalV1(lineId, 1, KgUnit, "999999999999999.999", "AmountOutOfRange", null, null, "NotComputed",
            null, null, null, null, null, null), Assert.Single(totals.Lines));
        Assert.Equal(new SalesDraftDocumentTotalsV1("Incomplete", 0, 0, 1, null, "NotComputed", null, null, null, null), totals.Totals);
    }

    [Fact]
    public async Task A_document_sum_with_more_digits_than_decimal_holds_is_out_of_range_never_a_silently_rounded_total()
    {
        // Review B P2-3 repro through the service: two lines of 1 × 600000000000000000000000000.01. Each line is exact; the
        // document sum 1200000000000000000000000000.02 is not representable exactly — AmountOutOfRange, never …0.00 "Computed".
        var world = new World();
        var request = Request(lines: 2);
        request = request with { Lines = [.. request.Lines.Select(line => line with { Quantity = "1", RequestedDeliverySlices = null })] };
        var created = (await world.Service().CreateDraftAsync(request, Metadata("k-totals-digits"))).Detail;
        await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(created.Lines[0].LineId, "600000000000000000000000000.01", KgUnit, null),
            new SalesDraftLineCommercialV1(created.Lines[1].LineId, "600000000000000000000000000.01", KgUnit, null)),
            Metadata("k-totals-digits-price"));

        var totals = await world.Service().GetDraftTotalsAsync(created.Draft.Id);

        Assert.All(totals.Lines, line => Assert.Equal(("Computed", "600000000000000000000000000.01"), (line.State, line.NetAmount)));
        Assert.Equal(new SalesDraftDocumentTotalsV1("AmountOutOfRange", 0, 0, 0, null, "NotComputed", null, null, null, null),
            totals.Totals);
    }

    [Fact]
    public async Task The_risk_check_of_the_same_out_of_range_order_answers_its_unavailable_path_never_an_exception()
    {
        // Review B P2-1, second half: the risk read never multiplies an order amount (SAT-D46: its VAT-included amount is API-03's,
        // whose VAT has no source), so the out-of-range order is "Unavailable" there too — no OverflowException, no 500.
        var world = new World { PartyRisk = new FakePartyRisk(FinanceLike([new(Lira, 100m)])) };
        var created = (await world.Service().CreateDraftAsync(RequestWithQuantity("999999999999999.999", null),
            Metadata("k-risk-overflow"))).Detail;
        await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(created.Lines[0].LineId, "100000000000000", KgUnit, null)), Metadata("k-risk-overflow-price"));

        var check = await world.Service().GetDraftRiskCheckAsync(created.Draft.Id);

        Assert.Equal("Unavailable", check.Result);
        Assert.Equal(("ThisOrder", "Unavailable", SalesDraftRiskReasonCodesV1.VatInclusiveAmountUnavailable),
            (check.ThisOrder.Kind, check.ThisOrder.State, check.ThisOrder.ReasonCode));
    }

    [Fact]
    public async Task Totals_need_the_price_read_before_and_after_the_draft_is_read_and_another_companys_draft_is_404()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-totals-auth"))).Detail;
        var finds = 0;
        world.Store.OnFindDraft = () => finds++;

        // Without the price read: 403 before the draft is read (nothing about it is touched).
        world.Authority.Denied.Add(PriceReadPermission);
        world.Authority.Measured.Clear();
        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftTotalsAsync(created.Draft.Id))));
        Assert.Equal(0, finds);
        Assert.Equal([ReadPermission, PriceReadPermission], world.Authority.Measured);
        world.Authority.Denied.Clear();

        // The price read revoked while the draft was being read: 403, nothing released.
        world.Store.OnFindDraft = () =>
        {
            finds++;
            world.Authority.Denied.Add(PriceReadPermission);
        };
        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftTotalsAsync(created.Draft.Id))));
        Assert.Equal(1, finds);
        world.Authority.Denied.Clear();
        world.Store.OnFindDraft = null;

        // Without the draft read: 403.
        world.Authority.Denied.Add(ReadPermission);
        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftTotalsAsync(created.Draft.Id))));
        world.Authority.Denied.Clear();

        // An unknown id — and another company's draft, which the tenant filter answers as null — is 404, never 403.
        Assert.Equal((404, "not_found"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftTotalsAsync(Guid.NewGuid()))));
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftTotalsAsync(Guid.Empty))).Status);

        // An unregistered price-read name is a setup gap (503), never a grant.
        world.Bindings = world.Bindings with { PriceReadPermission = "sales.synthetic.unregistered" };
        Assert.Equal((503, SalesDraftProblemCodesV1.PermissionUnregistered), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftTotalsAsync(created.Draft.Id))));
    }
}

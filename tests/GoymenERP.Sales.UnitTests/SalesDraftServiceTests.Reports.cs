using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-41 / API-42 (plan §3.4, UI-80; KPI No 124, No 25, SAT-D25) over SYNTHETIC owners: the reports are measured under their own
/// <c>sales.reports.read</c> before anything is read and after every store and owner read; the open quantity is the API-08 basis
/// minus the Logistics shipped total (never below zero, never across units); a lost quote's amount is the totals calculator's and
/// is withheld without the price read; the price variance has no source and says so. Nothing is written. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private const string ReportPermission = SalesPermissionManifestV1.ReportsRead;

    [Fact]
    public async Task Reports_open_orders_group_the_approved_minus_shipped_quantity_per_customer_product_and_unit_and_write_nothing()
    {
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        var (first, second) = (approved.Lines[0].LineId, approved.Lines[1].LineId);
        var draft = (await world.Store.FindDraftAsync(approved.Draft.Id))!;
        // Line 1 (1.25 kg) over-shipped 1.5: nothing open, never negative. Line 2 (2.25 kg) shipped 1 in two entries: 1.25 open.
        var deliveries = new FakeDeliveries(order => new(Company, order,
            [new(first, KgUnit, 1.5m, 1m), new(second, KgUnit, 0.75m, 0m), new(second, KgUnit, 0.25m, 0m)]));
        world.Deliveries = deliveries;
        var key = new SalesOpenOrderGroupKey(Account, Party, Product, KgUnit);
        world.Reports!.OpenOrders = (_, _, _, _) => new SalesOpenOrderGroupPage([key], [draft], false, "next-page");
        var (attempts, envelopes, transitions) = (world.Store.Attempts, world.Store.Envelopes.Count, world.Store.Transitions.Count);
        world.Authority.Measured.Clear();

        var report = await world.Service().GetOpenOrderReportAsync(
            new SalesOpenOrderReportQuery(Day, Day.AddDays(30), Account, 20, null));

        Assert.Equal((SalesOpenOrderMeasureView.ApprovedMinusShipped, "next-page"), (report.Measure, report.NextCursor));
        var group = Assert.Single(report.Groups);
        Assert.Equal((Party, Account, $"CAR-{Party:N}", $"Legal {Party:N}"),
            (group.Customer.PartyId, group.Customer.CompanyAccountId, group.Customer.Code, group.Customer.LegalName));
        Assert.Equal((Product, "GDC-001", KgUnit, "kg", 1, "3.5", "2.5", "1.25", true),
            (group.ProductId, group.ProductCode, group.UnitId, group.UnitCode, group.OrderCount, group.ApprovedQuantity,
                group.ShippedQuantity, group.RemainingQuantity, group.Complete));
        Assert.Equal(
            [
                new SalesOpenOrderLineView(draft.Id, draft.DocumentNo, Day, draft.Lines[0].RequestedDeliveryDate ?? draft.Header.RequestedDeliveryDate,
                    SalesDraftApiV1.Rev0, first, 1, "1.25", "1.5", "0"),
                new SalesOpenOrderLineView(draft.Id, draft.DocumentNo, Day, draft.Lines[1].RequestedDeliveryDate ?? draft.Header.RequestedDeliveryDate,
                    SalesDraftApiV1.Rev0, second, 2, "2.25", "1", "1.25"),
            ],
            group.Lines);
        Assert.Empty(group.Unmeasured);

        // The store is asked once with the filter, the page and the technical order bound; the owner once per order.
        var asked = Assert.Single(world.Reports.OpenOrderCalls);
        Assert.Equal((new SalesOpenOrderReportFilter(Day, Day.AddDays(30), Account), 20, (string?)null, SalesReportApiV1.MaximumOrdersPerPage),
            asked);
        Assert.Equal([draft.Id], deliveries.Asked);
        // The report read before, after the store, after the delivery owner and after the Cari owner; no other name.
        Assert.Equal([ReportPermission, ReportPermission, ReportPermission, ReportPermission], world.Authority.Measured);
        // Read-only: no attempt, envelope, transition or refusal anywhere in Sales.
        Assert.Equal((attempts, envelopes, transitions), (world.Store.Attempts, world.Store.Envelopes.Count, world.Store.Transitions.Count));
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task Reports_open_orders_measure_a_revision_awaiting_re_approval_against_the_last_approved_quantity()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        Owners(world);
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(line, KgUnit, 1m, 1m)]));
        // Rev.1 asks 2 kg and awaits re-approval: the open quantity stays 1.25 − 1 of the APPROVED Rev.0, never 2 − 1.
        await world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("4", "SENTETIK revizyon", [new SalesDraftLineQuantityV1(line, "2")]), Metadata("k-quantity"));
        var draft = (await world.Store.FindDraftAsync(approved.Draft.Id))!;
        world.Reports!.OpenOrders = (_, _, _, _) =>
            new SalesOpenOrderGroupPage([new SalesOpenOrderGroupKey(Account, Party, Product, KgUnit)], [draft], false, null);

        var group = Assert.Single((await world.Service().GetOpenOrderReportAsync(new SalesOpenOrderReportQuery(null, null, null, 50, null))).Groups);

        Assert.Equal(("1.25", "1", "0.25", true), (group.ApprovedQuantity, group.ShippedQuantity, group.RemainingQuantity, group.Complete));
        Assert.Equal(SalesDraftApiV1.Rev0, Assert.Single(group.Lines).BasisRevision);
    }

    [Fact]
    public async Task Reports_open_orders_are_never_a_guess_an_absent_or_malformed_owner_is_503_and_a_wide_page_is_refused()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        var draft = (await world.Store.FindDraftAsync(approved.Draft.Id))!;
        var key = new SalesOpenOrderGroupKey(Account, Party, Product, KgUnit);
        world.Reports!.OpenOrders = (_, _, _, _) => new SalesOpenOrderGroupPage([key], [draft], false, null);
        var query = new SalesOpenOrderReportQuery(null, null, null, 50, null);

        // No delivery owner composed: 503, never "nothing shipped".
        world.Deliveries = null;
        var absent = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetOpenOrderReportAsync(query));
        Assert.Equal((503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable), (absent.Status, absent.Code));

        // A total recorded in another unit is no number of this unit (no conversion).
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(line, Guid.NewGuid(), 1m, 0m)]));
        var malformed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetOpenOrderReportAsync(query));
        Assert.Equal((503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable), (malformed.Status, malformed.Code));

        // The Cari owner not composed: 503, never a guessed customer.
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));
        world.PartyDisplays.Fault = "unavailable";
        var noCustomer = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetOpenOrderReportAsync(query));
        Assert.Equal((503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable), (noCustomer.Status, noCustomer.Code));
        world.PartyDisplays.Fault = "";

        // Nothing open: an empty page; with nothing open the group shows the full approved quantity.
        var open = Assert.Single((await world.Service().GetOpenOrderReportAsync(query)).Groups);
        Assert.Equal(("1.25", "0", "1.25"), (open.ApprovedQuantity, open.ShippedQuantity, open.RemainingQuantity));

        // More orders than one page may measure: 422, never a cut group.
        world.Reports.OpenOrders = (_, _, _, _) => new SalesOpenOrderGroupPage([key], [], true, null);
        var wide = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetOpenOrderReportAsync(query));
        Assert.Equal((422, SalesReportProblemCodesV1.ReportScopeTooLarge), (wide.Status, wide.Code));

        // A foreign cursor is the store's refusal: 400 invalid_cursor.
        world.Reports.OpenOrders = (_, _, _, _) => throw new SalesDraftStoreException(SalesDraftStoreCodes.InvalidCursor, "synthetic");
        var cursor = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetOpenOrderReportAsync(query with { Cursor = "foreign" }));
        Assert.Equal((400, SalesDraftProblemCodesV1.InvalidCursor), (cursor.Status, cursor.Code));

        // The store not composed: 503.
        world.Reports = null;
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetOpenOrderReportAsync(query))).Status);
    }

    [Theory]
    [InlineData(0, null, null, null)]
    [InlineData(101, null, null, null)]
    [InlineData(50, "2026-09-19", "2026-09-18", null)]
    [InlineData(50, null, null, "00000000-0000-0000-0000-000000000000")]
    public async Task Reports_refuse_a_malformed_query_before_any_store_is_read(int pageSize, string? from, string? to, string? account)
    {
        var world = new World();
        var query = new SalesOpenOrderReportQuery(from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to),
            account is null ? null : Guid.Parse(account), pageSize, null);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetOpenOrderReportAsync(query));
        var lost = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetLostOpportunityReportAsync(
            new SalesLostOpportunityReportQuery(query.From, query.To, query.CompanyAccountId, null, pageSize, null)));
        var variance = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetPriceVarianceReportAsync(
            new SalesPriceVarianceReportQuery(query.From, query.To, query.CompanyAccountId, pageSize, null)));

        Assert.All([refused, lost, variance], exception => Assert.Equal((400, "validation_failed"), (exception.Status, exception.Code)));
        Assert.Empty(world.Reports!.OpenOrderCalls);
        Assert.Empty(world.Reports.LostQuoteCalls);
    }

    [Fact]
    public async Task Reports_need_their_own_read_name_another_sales_name_does_not_open_them_and_nothing_is_read_without_it()
    {
        var world = new World();
        world.Authority.Denied.Add(ReportPermission);

        var open = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetOpenOrderReportAsync(new SalesOpenOrderReportQuery(null, null, null, 50, null)));
        var lost = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetLostOpportunityReportAsync(new SalesLostOpportunityReportQuery(null, null, null, null, 50, null)));
        var variance = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetPriceVarianceReportAsync(new SalesPriceVarianceReportQuery(null, null, null, 50, null)));

        Assert.All([open, lost, variance], exception => Assert.Equal(403, exception.Status));
        Assert.Empty(world.Reports!.OpenOrderCalls);
        Assert.Empty(world.Reports.LostQuoteCalls);
        // Only the report name was measured (once per read): the draft read or the price read never stands in for it.
        Assert.Equal([ReportPermission, ReportPermission, ReportPermission], world.Authority.Measured);

        // An unregistered binding is a setup gap (503), never a grant.
        world.Bindings = world.Bindings with { ReportReadPermission = "sales.synthetic.unregistered" };
        var unregistered = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetOpenOrderReportAsync(new SalesOpenOrderReportQuery(null, null, null, 50, null)));
        Assert.Equal((503, SalesDraftProblemCodesV1.PermissionUnregistered), (unregistered.Status, unregistered.Code));
    }

    [Fact]
    public async Task Reports_a_grant_lost_after_the_store_read_is_403_and_no_owner_is_asked()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var draft = (await world.Store.FindDraftAsync(approved.Draft.Id))!;
        var deliveries = new FakeDeliveries(order => new(Company, order, []));
        world.Deliveries = deliveries;
        world.Reports!.OpenOrders = (_, _, _, _) =>
        {
            world.Authority.Granted = false;
            return new SalesOpenOrderGroupPage([new SalesOpenOrderGroupKey(Account, Party, Product, KgUnit)], [draft], false, null);
        };

        var lostGrant = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetOpenOrderReportAsync(new SalesOpenOrderReportQuery(null, null, null, 50, null)));

        Assert.Equal(403, lostGrant.Status);
        Assert.Empty(deliveries.Asked);
        Assert.Equal(0, world.PartyDisplays.Calls);
    }

    [Fact]
    public async Task Reports_lost_opportunities_carry_the_sat_d25_reason_and_the_calculators_net_amount_withheld_without_the_price_read()
    {
        var world = new World { Clock = QuoteClock() };
        var id = (await world.Service().CreateQuoteAsync(QuoteOf(), Metadata("q-1"))).Detail.Quote.Id;
        world.QuotePolicies = new FakeQuotePolicies { List = LossList() };
        await world.Service().RecordQuoteLossV2Async(id, new SalesQuoteLossRequestV2("1", OtherReason, "SENTETIK rakip daha ucuz"),
            Metadata("q-1-lose"));
        var quote = (await world.Quotes!.FindQuoteAsync(id))!;
        world.Reports!.Lost.Add(quote);
        world.Authority.Measured.Clear();

        var report = await world.Service().GetLostOpportunityReportAsync(
            new SalesLostOpportunityReportQuery(Day, Day, Account, OtherReason, 20, null));

        Assert.Equal((SalesPriceDisclosure.Visible, (string?)null), (report.AmountDisclosure, report.NextCursor));
        var item = Assert.Single(report.Items);
        Assert.Equal((id, quote.DocumentNo, SalesDraftApiV1.Rev0, Day, Currency, Day, 1),
            (item.QuoteId, item.DocumentNo, item.Revision, item.BusinessDate, item.CurrencyId, item.LostOn, item.LineCount));
        Assert.Equal(new SalesLostReasonView(OtherReason, "SENTETIK-DIGER", "Sentetik diğer", quote.Loss!.ListReason!.ProfileId, 1),
            item.Reason);
        Assert.Equal(("SENTETIK rakip daha ucuz", Actor), (item.Explanation, item.LostBy));
        Assert.Equal((Party, Account), (item.Customer.PartyId, item.Customer.CompanyAccountId));
        // 1.25 kg × (12.50 − 5 %) = 14.84375 → 14.84 at the money profile's scale (SAT-D11), net only (no VAT source).
        Assert.Equal(new SalesLostAmountView("Computed", Currency, 2, "14.84", 0, 0), item.Amount);

        // The loss DAY range is the Europe/Istanbul day turned into the store's UTC instants [start, next start).
        var filter = Assert.Single(world.Reports.LostQuoteCalls).Filter;
        Assert.Equal(new SalesLostQuoteReportFilter(new DateTimeOffset(2026, 9, 17, 21, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 18, 21, 0, 0, TimeSpan.Zero), Account, OtherReason), filter);
        // The report read before, after the store and after the Cari owner; the price read is ASKED last.
        Assert.Equal([ReportPermission, ReportPermission, ReportPermission, PriceReadPermission], world.Authority.Measured);

        // Without the price read every amount is withheld; the reason and the explanation stay.
        world.Authority.Denied.Add(PriceReadPermission);
        var withheld = await world.Service().GetLostOpportunityReportAsync(new SalesLostOpportunityReportQuery(null, null, null, null, 20, null));
        Assert.Equal(SalesPriceDisclosure.Withheld, withheld.AmountDisclosure);
        Assert.Null(Assert.Single(withheld.Items).Amount);
        Assert.Equal("SENTETIK-DIGER", withheld.Items[0].Reason!.Code);

        // An empty reason id is a malformed query.
        var empty = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetLostOpportunityReportAsync(
            new SalesLostOpportunityReportQuery(null, null, null, Guid.Empty, 20, null)));
        Assert.Equal(400, empty.Status);

        // The store answering anything but a lost quote of this company is malformed: 503, never shown.
        world.Authority.Denied.Remove(PriceReadPermission);
        var open = (await world.Service().CreateQuoteAsync(QuoteOf(price: "10"), Metadata("q-2"))).Detail.Quote.Id;
        world.Reports.Lost.Add((await world.Quotes.FindQuoteAsync(open))!);
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetLostOpportunityReportAsync(
            new SalesLostOpportunityReportQuery(null, null, null, null, 20, null)))).Status);
    }

    [Fact]
    public async Task Reports_price_variance_is_no_source_never_an_empty_list_and_needs_the_price_read()
    {
        var world = new World();
        world.Authority.Measured.Clear();

        var report = await world.Service().GetPriceVarianceReportAsync(new SalesPriceVarianceReportQuery(Day, Day, Account, 50, null));

        // No 25: no invoice owner exists (S5 API-32) — "kaynak yok", items and cursor null, never [] (which would claim no variance).
        Assert.Equal(new SalesPriceVarianceReportResponse(SalesPriceVarianceSourceStateView.NoSource, null, null), report);
        Assert.Equal([ReportPermission, PriceReadPermission], world.Authority.Measured);

        world.Authority.Denied.Add(PriceReadPermission);
        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetPriceVarianceReportAsync(new SalesPriceVarianceReportQuery(null, null, null, 50, null)));
        Assert.Equal(403, forbidden.Status);
    }

    /// <summary>The Sales report queries in memory: the case sets the open-order answer; lost quotes are those it added.</summary>
    private sealed class FakeReportStore : ISalesReportReadStore
    {
        public Func<SalesOpenOrderReportFilter, int, string?, int, SalesOpenOrderGroupPage>? OpenOrders { get; set; }
        public List<(SalesOpenOrderReportFilter Filter, int PageSize, string? Cursor, int MaximumOrders)> OpenOrderCalls { get; } = [];
        public List<SalesQuote> Lost { get; } = [];
        public List<(SalesLostQuoteReportFilter Filter, int PageSize, string? Cursor)> LostQuoteCalls { get; } = [];

        public Task<SalesOpenOrderGroupPage> ListOpenOrderGroupsAsync(SalesOpenOrderReportFilter filter, int pageSize, string? cursor,
            int maximumOrders, CancellationToken cancellationToken = default)
        {
            OpenOrderCalls.Add((filter, pageSize, cursor, maximumOrders));
            return Task.FromResult(OpenOrders?.Invoke(filter, pageSize, cursor, maximumOrders) ?? new SalesOpenOrderGroupPage([], [], false, null));
        }

        public Task<SalesQuotePage> ListLostQuotesAsync(SalesLostQuoteReportFilter filter, int pageSize, string? cursor,
            CancellationToken cancellationToken = default)
        {
            LostQuoteCalls.Add((filter, pageSize, cursor));
            return Task.FromResult(new SalesQuotePage([.. Lost.Take(pageSize)], null));
        }
    }
}

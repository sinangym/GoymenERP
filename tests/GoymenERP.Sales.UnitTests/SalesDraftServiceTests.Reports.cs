using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
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
    public async Task Reports_open_orders_measure_the_accepted_quantity_minus_shipped_per_group_leave_closed_lines_out_and_write_nothing()
    {
        var world = new World();
        var accepted = await AcceptedTwoLineAsync(world);
        var (first, second) = (accepted.Lines[0].LineId, accepted.Lines[1].LineId);
        var draft = (await world.Store.FindDraftAsync(accepted.Draft.Id))!;
        // Line 1 (1.25 kg) over-shipped 1.5: nothing open, nothing awaiting — closed history, not listed. Line 2 (2.25 kg) shipped 1
        // in two entries (55 % short, beyond SAT-D5's 1 %): 1.25 open.
        var deliveries = new FakeDeliveries(order => new(Company, order,
            [new(first, KgUnit, 1.5m, 1m), new(second, KgUnit, 0.75m, 0m), new(second, KgUnit, 0.25m, 0m)]));
        world.Deliveries = deliveries;
        var policies = new FakeSalesPolicies(Ladder123());
        world.SalesPolicies = policies;
        world.Reports!.Orders.Add(draft);
        var (attempts, envelopes, transitions) = (world.Store.Attempts, world.Store.Envelopes.Count, world.Store.Transitions.Count);
        world.Authority.Measured.Clear();

        var report = await world.Service().GetOpenOrderReportAsync(new SalesOpenOrderReportQuery(Day, Day.AddDays(30), Account, 20, null));

        Assert.Equal((SalesOpenOrderMeasureView.ApprovedMinusShipped, (string?)null), (report.Measure, report.NextCursor));
        var group = Assert.Single(report.Groups);
        Assert.Equal((Party, Account, $"CAR-{Party:N}", $"Legal {Party:N}"),
            (group.Customer.PartyId, group.Customer.CompanyAccountId, group.Customer.Code, group.Customer.LegalName));
        Assert.Equal((Product, "GDC-001", KgUnit, "kg", SalesOpenOrderGroupStateView.Measured, 1, 1),
            (group.ProductId, group.ProductCode, group.UnitId, group.UnitCode, group.State, group.ScopeOrderCount, group.OrderCount));
        Assert.Equal(("2.25", "1", "1.25", "0", true), (group.ApprovedQuantity, group.ShippedQuantity, group.RemainingQuantity,
            group.AwaitingAcceptanceQuantity, group.Complete));
        Assert.Equal(
            [
                new SalesOpenOrderLineView(draft.Id, draft.DocumentNo, Day, draft.Lines[1].RequestedDeliveryDate ?? draft.Header.RequestedDeliveryDate,
                    SalesDraftApiV1.Rev0, second, 2, "2.25", "1", "1.25", false, "0", null),
            ],
            group.Lines);
        Assert.Empty(group.Unmeasured);

        // The store is asked for the filter's group keys and that group's orders; the owner once (one batch); the tolerance once.
        Assert.Equal([new SalesOpenOrderReportFilter(Day, Day.AddDays(30), Account)], world.Reports.GroupCalls.Select(call => call.Filter));
        Assert.Single(world.Reports.OrderCalls);
        Assert.Equal([draft.Id], deliveries.Asked);
        Assert.Equal([Day], policies.Asked);
        // The report read before, after the key scan, the order read, the delivery owner, Settings and the Cari owner.
        Assert.Equal(Enumerable.Repeat(ReportPermission, 6), world.Authority.Measured);
        // Read-only: no attempt, envelope, transition or refusal anywhere in Sales.
        Assert.Equal((attempts, envelopes, transitions), (world.Store.Attempts, world.Store.Envelopes.Count, world.Store.Transitions.Count));
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task Reports_open_orders_count_an_order_awaiting_customer_acceptance_apart_never_as_open_tonnage()
    {
        // Q9: a new order requires the customer's acceptance; approved inside but not accepted, nothing of it can ship or invoice.
        var world = new World();
        var approved = await ApprovedTwoLineAsync(world);
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));
        world.Reports!.Orders.Add((await world.Store.FindDraftAsync(approved.Draft.Id))!);

        var group = Assert.Single((await world.Service().GetOpenOrderReportAsync(new SalesOpenOrderReportQuery(null, null, null, 50, null))).Groups);

        // Sinan 2026-10-31 (EK19 pending): not open tonnage — shown apart as "müşteri onayı bekliyor".
        Assert.Equal(("0", "0", "0", "3.5", true), (group.ApprovedQuantity, group.ShippedQuantity, group.RemainingQuantity,
            group.AwaitingAcceptanceQuantity, group.Complete));
        Assert.Equal([((string?)null, "0", "1.25", (string?)SalesDraftApiV1.Rev0), (null, "0", "2.25", SalesDraftApiV1.Rev0)],
            group.Lines.Select(line => (line.BasisRevision, line.RemainingQuantity, line.AwaitingAcceptanceQuantity, line.AwaitingAcceptanceRevision)));
        Assert.Equal((1, 1), (group.OrderCount, group.ScopeOrderCount));

        // Once the customer accepts, the same quantity is open and nothing awaits.
        await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("5"), Metadata("k-accept"));
        world.Reports.Orders[0] = (await world.Store.FindDraftAsync(approved.Draft.Id))!;
        var accepted = Assert.Single((await world.Service().GetOpenOrderReportAsync(new SalesOpenOrderReportQuery(null, null, null, 50, null))).Groups);
        Assert.Equal(("3.5", "3.5", "0"), (accepted.ApprovedQuantity, accepted.RemainingQuantity, accepted.AwaitingAcceptanceQuantity));
    }

    [Fact]
    public async Task Reports_open_orders_measure_an_accepted_rev0_while_a_larger_rev1_awaits_approval_or_acceptance()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        Owners(world);
        await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("4"), Metadata("k-accept"));
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(line, KgUnit, 1m, 1m)]));
        world.SalesPolicies = new FakeSalesPolicies(Ladder123());
        var query = new SalesOpenOrderReportQuery(null, null, null, 50, null);

        // Rev.1 asks 2 kg and awaits internal approval: the accepted Rev.0 (1.25) is measured — 1.25 − 1 open, nothing awaits acceptance.
        await world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest("5", "SENTETIK revizyon", [new SalesDraftLineQuantityV1(line, "2")]), Metadata("k-quantity"));
        world.Reports!.Orders.Add((await world.Store.FindDraftAsync(approved.Draft.Id))!);
        var pending = Assert.Single((await world.Service().GetOpenOrderReportAsync(query)).Groups);
        Assert.Equal(("1.25", "1", "0.25", "0"), (pending.ApprovedQuantity, pending.ShippedQuantity, pending.RemainingQuantity,
            pending.AwaitingAcceptanceQuantity));
        Assert.Equal(SalesDraftApiV1.Rev0, Assert.Single(pending.Lines).BasisRevision);

        // Rev.1 approved inside, the customer has not accepted it: still 1.25 − 1 open; the +0.75 awaits acceptance (Rev.1).
        await world.Service().SubmitDraftAsync(approved.Draft.Id, Command("6"), Metadata("k-submit-1"));
        await world.Service().ApproveDraftAsync(approved.Draft.Id, Command("7"), Metadata("k-approve-1"));
        world.Reports.Orders[0] = (await world.Store.FindDraftAsync(approved.Draft.Id))!;
        var awaiting = Assert.Single((await world.Service().GetOpenOrderReportAsync(query)).Groups);
        Assert.Equal(("1.25", "1", "0.25", "0.75"), (awaiting.ApprovedQuantity, awaiting.ShippedQuantity, awaiting.RemainingQuantity,
            awaiting.AwaitingAcceptanceQuantity));
        var awaitingLine = Assert.Single(awaiting.Lines);
        Assert.Equal((SalesDraftApiV1.Rev0, (string?)SalesDraftApiV1.RevisionText(1)), (awaitingLine.BasisRevision, awaitingLine.AwaitingAcceptanceRevision));

        // The customer accepts Rev.1: 2 − 1 open, nothing awaits.
        await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("8"), Metadata("k-accept-1"));
        world.Reports.Orders[0] = (await world.Store.FindDraftAsync(approved.Draft.Id))!;
        var accepted = Assert.Single((await world.Service().GetOpenOrderReportAsync(query)).Groups);
        Assert.Equal(("2", "1", "1", "0"), (accepted.ApprovedQuantity, accepted.ShippedQuantity, accepted.RemainingQuantity,
            accepted.AwaitingAcceptanceQuantity));
    }

    [Fact]
    public async Task Reports_open_orders_count_nothing_open_on_a_line_completed_within_the_sat_d5_tolerance_sat_d130()
    {
        var world = new World();
        var accepted = await AcceptedTwoLineAsync(world);
        var (first, second) = (accepted.Lines[0].LineId, accepted.Lines[1].LineId);
        // Line 1: 1.24 of 1.25 kg (0.8 % short, within the 1 % warn bound): "Tamamlandı", nothing open. Line 2: 1 of 2.25 kg: Partial.
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(first, KgUnit, 1.24m, 1.24m), new(second, KgUnit, 1m, 0m)]));
        var policies = new FakeSalesPolicies(Ladder123());
        world.SalesPolicies = policies;
        world.Reports!.Orders.Add((await world.Store.FindDraftAsync(accepted.Draft.Id))!);
        var query = new SalesOpenOrderReportQuery(null, null, null, 50, null);

        var group = Assert.Single((await world.Service().GetOpenOrderReportAsync(query)).Groups);

        // The completed line is closed history: only line 2 is listed and summed.
        Assert.Equal(("2.25", "1", "1.25", true), (group.ApprovedQuantity, group.ShippedQuantity, group.RemainingQuantity, group.Complete));
        Assert.Equal([second], group.Lines.Select(line => line.LineId));
        Assert.Equal([Day], policies.Asked);

        // No readable tolerance (Settings not composed): the short-shipped lines are unmeasured — never open, never closed.
        world.SalesPolicies = null;
        var unknown = Assert.Single((await world.Service().GetOpenOrderReportAsync(query)).Groups);
        Assert.False(unknown.Complete);
        Assert.Empty(unknown.Lines);
        Assert.Equal([(first, SalesOpenOrderUnmeasuredReasonView.TolerancePolicyUnavailable), (second, SalesOpenOrderUnmeasuredReasonView.TolerancePolicyUnavailable)],
            unknown.Unmeasured.Select(line => (line.LineId, line.Reason)));
        Assert.Equal(("0", "0", "0", 0), (unknown.ApprovedQuantity, unknown.ShippedQuantity, unknown.RemainingQuantity, unknown.OrderCount));

        // A policy of another company is no policy (the API-08 validation): unmeasured as well.
        world.SalesPolicies = new FakeSalesPolicies(on => Ladder123()(on) with { CompanyId = Guid.NewGuid() });
        Assert.Equal(2, Assert.Single((await world.Service().GetOpenOrderReportAsync(query)).Groups).Unmeasured.Count);

        // Nothing shipped needs no tolerance: open in full, the policy is not asked.
        var unasked = new FakeSalesPolicies(Ladder123());
        world.SalesPolicies = unasked;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));
        var untouched = Assert.Single((await world.Service().GetOpenOrderReportAsync(query)).Groups);
        Assert.Equal(("3.5", "0", "3.5", true), (untouched.ApprovedQuantity, untouched.ShippedQuantity, untouched.RemainingQuantity, untouched.Complete));
        Assert.Empty(unasked.Asked);

        // Everything shipped: the group is closed history and the report has nothing to show.
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(first, KgUnit, 1.25m, 1.25m), new(second, KgUnit, 2.25m, 2.25m)]));
        Assert.Empty((await world.Service().GetOpenOrderReportAsync(query)).Groups);
    }

    [Fact]
    public async Task Reports_open_orders_isolate_one_malformed_owner_answer_and_never_guess_an_absent_owner()
    {
        var world = new World();
        var accepted = await AcceptedAsync(world);
        var line = accepted.Lines[0].LineId;
        var draft = (await world.Store.FindDraftAsync(accepted.Draft.Id))!;
        world.Reports!.Orders.Add(draft);
        var query = new SalesOpenOrderReportQuery(null, null, null, 50, null);

        // No delivery owner composed: 503, never "nothing shipped".
        world.Deliveries = null;
        var absent = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetOpenOrderReportAsync(query));
        Assert.Equal((503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable), (absent.Status, absent.Code));

        // The owner fails: 503.
        world.Deliveries = new FakeDeliveries(_ => throw new InvalidOperationException("SENTETIK outage"));
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetOpenOrderReportAsync(query))).Status);

        // A total recorded in another unit is no number of this unit: that order's line is unmeasured, the report stands.
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(line, Guid.NewGuid(), 1m, 0m)]));
        var malformed = Assert.Single((await world.Service().GetOpenOrderReportAsync(query)).Groups);
        Assert.Equal((false, SalesOpenOrderUnmeasuredReasonView.DeliveryAnswerInvalid),
            (malformed.Complete, Assert.Single(malformed.Unmeasured).Reason));

        // The Cari owner not composed: 503, never a guessed customer.
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));
        world.PartyDisplays.Fault = "unavailable";
        var noCustomer = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetOpenOrderReportAsync(query));
        Assert.Equal((503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable), (noCustomer.Status, noCustomer.Code));
        world.PartyDisplays.Fault = "";

        // A null or foreign row from the store is a malformed store answer: 503, never a 500.
        world.Reports.Orders[0] = null!;
        world.Reports.ExtraKeys.Add((new SalesOpenOrderGroupKey(Account, Party, Product, KgUnit), 1));
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetOpenOrderReportAsync(query))).Status);

        // The store not composed: 503.
        world.Reports = null;
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetOpenOrderReportAsync(query))).Status);
    }

    [Fact]
    public async Task Reports_open_orders_bound_the_work_answer_a_huge_group_without_sums_and_always_give_a_cursor_to_continue()
    {
        var world = new World();
        var accepted = await AcceptedAsync(world);
        var draft = (await world.Store.FindDraftAsync(accepted.Draft.Id))!;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));
        world.Reports!.Orders.Add(draft);
        var real = new SalesOpenOrderGroupKey(Account, Party, Product, KgUnit);
        // A group (sorted first) whose scope counts 600 orders, then the real group also counted 600: the second exceeds what is
        // left of the request's 1000-order bound, so the page ends after the first with a cursor — never a 422, never a cut sum.
        var synthetic = new SalesOpenOrderGroupKey(Guid.Parse("00000000-0000-4000-8000-000000000001"), Party, Product, KgUnit);
        world.Reports.ExtraKeys.Add((synthetic, 600));
        world.Reports.CountOverride[real] = 600;
        var query = new SalesOpenOrderReportQuery(null, null, null, 50, null);

        var first = await world.Service().GetOpenOrderReportAsync(query);
        Assert.Empty(first.Groups);
        Assert.NotNull(first.NextCursor);
        var second = await world.Service().GetOpenOrderReportAsync(query with { Cursor = first.NextCursor });
        Assert.Equal((real.CompanyAccountId, (string?)null), (Assert.Single(second.Groups).Customer.CompanyAccountId, second.NextCursor));

        // A group above 1000 orders is answered "too many" with no sums, and the scan moves past it.
        world.Reports.CountOverride[real] = SalesReportApiV1.MaximumOrdersPerGroup + 1;
        var huge = await world.Service().GetOpenOrderReportAsync(query);
        var tooMany = Assert.Single(huge.Groups);
        Assert.Equal((SalesOpenOrderGroupStateView.TooManyOrders, SalesReportApiV1.MaximumOrdersPerGroup + 1, (string?)null, (string?)null, false),
            (tooMany.State, tooMany.ScopeOrderCount, tooMany.ApprovedQuantity, tooMany.RemainingQuantity, tooMany.Complete));
        Assert.Empty(tooMany.Lines);
        Assert.Null(huge.NextCursor);

        // The cursor is bound to the company and the filter: another filter's cursor or a broken one is 400 invalid_cursor.
        var otherFilter = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetOpenOrderReportAsync(query with { From = Day, Cursor = first.NextCursor }));
        var broken = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetOpenOrderReportAsync(query with { Cursor = "not-a-cursor" }));
        Assert.All([otherFilter, broken], exception => Assert.Equal((400, SalesDraftProblemCodesV1.InvalidCursor), (exception.Status, exception.Code)));
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
        Assert.Empty(world.Reports!.GroupCalls);
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
        Assert.Empty(world.Reports!.GroupCalls);
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
        var accepted = await AcceptedAsync(world);
        var deliveries = new FakeDeliveries(order => new(Company, order, []));
        world.Deliveries = deliveries;
        world.Reports!.Orders.Add((await world.Store.FindDraftAsync(accepted.Draft.Id))!);
        world.Reports.OnGroupRead = () => world.Authority.Granted = false;

        var lostGrant = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetOpenOrderReportAsync(new SalesOpenOrderReportQuery(null, null, null, 50, null)));

        Assert.Equal(403, lostGrant.Status);
        Assert.Empty(world.Reports.OrderCalls);
        Assert.Empty(deliveries.Asked);
        Assert.Equal(0, world.PartyDisplays.Calls);
    }

    /// <summary>A one-line order (1.25 kg), approved and accepted by the customer: version 5.</summary>
    private static async Task<SalesOrderDraftDetailView> AcceptedAsync(World world)
    {
        var approved = await ApprovedAsync(world);
        return (await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("4"), Metadata("k-accept"))).Detail;
    }

    /// <summary>A two-line order (1.25 and 2.25 kg), approved and accepted by the customer: version 6.</summary>
    private static async Task<SalesOrderDraftDetailView> AcceptedTwoLineAsync(World world)
    {
        var approved = await ApprovedTwoLineAsync(world);
        return (await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command("5"), Metadata("k-accept"))).Detail;
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

    /// <summary>
    /// The Sales report queries in memory over the orders the case added (the store's open rule and key order), plus synthetic keys
    /// and order counts a case sets; lost quotes are those it added.
    /// </summary>
    private sealed class FakeReportStore : ISalesReportReadStore
    {
        public List<SalesOrderDraft> Orders { get; } = [];
        public List<(SalesOpenOrderGroupKey Key, int Count)> ExtraKeys { get; } = [];
        public Dictionary<SalesOpenOrderGroupKey, int> CountOverride { get; } = [];
        public Action? OnGroupRead { get; set; }
        public List<(SalesOpenOrderReportFilter Filter, SalesOpenOrderGroupKey? After, int Take)> GroupCalls { get; } = [];
        public List<(SalesOpenOrderGroupKey Key, Guid? After, int Take)> OrderCalls { get; } = [];
        public List<SalesQuote> Lost { get; } = [];
        public List<(SalesLostQuoteReportFilter Filter, int PageSize, string? Cursor)> LostQuoteCalls { get; } = [];

        private IEnumerable<(SalesOrderDraft Order, SalesOpenOrderGroupKey Key)> OpenLines(SalesOpenOrderReportFilter filter) =>
            Orders.Where(order => order is null || order.State == SalesDraftState.Draft
                    && (order.ApprovalState == InternalApprovalState.Approved || order.ArchivedRevisions.Count > 0)
                    && (filter.From is not { } from || order.Header.BusinessDate >= from)
                    && (filter.To is not { } to || order.Header.BusinessDate <= to)
                    && (filter.CompanyAccountId is not { } account || order.Header.CompanyAccountId == account))
                .Where(order => order is not null)
                .SelectMany(order => order.Lines.Select(line => (order, new SalesOpenOrderGroupKey(order.Header.CompanyAccountId,
                    order.Header.PartyId, line.Product.ProductId, line.OrderUnit.UnitId))));

        private static int Compare(SalesOpenOrderGroupKey a, SalesOpenOrderGroupKey b)
        {
            var result = a.CompanyAccountId.CompareTo(b.CompanyAccountId);
            if (result == 0) result = a.PartyId.CompareTo(b.PartyId);
            if (result == 0) result = a.ProductId.CompareTo(b.ProductId);
            return result == 0 ? a.UnitId.CompareTo(b.UnitId) : result;
        }

        public Task<IReadOnlyList<SalesOpenOrderGroupScope>> ListOpenOrderGroupsAsync(SalesOpenOrderReportFilter filter,
            SalesOpenOrderGroupKey? after, int take, CancellationToken cancellationToken = default)
        {
            GroupCalls.Add((filter, after, take));
            OnGroupRead?.Invoke();
            var groups = OpenLines(filter).GroupBy(entry => entry.Key)
                .Select(group => (group.Key, Count: CountOverride.GetValueOrDefault(group.Key, group.Select(entry => entry.Order.Id).Distinct().Count())))
                .Concat(ExtraKeys)
                .DistinctBy(group => group.Key)
                .Where(group => after is null || Compare(group.Key, after) > 0)
                .OrderBy(group => group.Key, Comparer<SalesOpenOrderGroupKey>.Create(Compare))
                .Take(take)
                .Select(group => new SalesOpenOrderGroupScope(group.Key, group.Count))
                .ToList();
            return Task.FromResult<IReadOnlyList<SalesOpenOrderGroupScope>>(groups);
        }

        public Task<IReadOnlyList<SalesOrderDraft>> ListOpenOrdersOfGroupAsync(SalesOpenOrderReportFilter filter, SalesOpenOrderGroupKey key,
            Guid? afterOrderId, int take, CancellationToken cancellationToken = default)
        {
            OrderCalls.Add((key, afterOrderId, take));
            if (Orders.Any(order => order is null))
            {
                return Task.FromResult<IReadOnlyList<SalesOrderDraft>>([null!]);
            }

            var orders = OpenLines(filter).Where(entry => entry.Key == key).Select(entry => entry.Order).DistinctBy(order => order.Id)
                .Where(order => afterOrderId is null || order.Id.CompareTo(afterOrderId.Value) > 0)
                .OrderBy(order => order.Id).Take(take).ToList();
            return Task.FromResult<IReadOnlyList<SalesOrderDraft>>(orders);
        }

        public Task<SalesQuotePage> ListLostQuotesAsync(SalesLostQuoteReportFilter filter, int pageSize, string? cursor,
            CancellationToken cancellationToken = default)
        {
            LostQuoteCalls.Add((filter, pageSize, cursor));
            return Task.FromResult(new SalesQuotePage([.. Lost.Take(pageSize)], null));
        }
    }
}

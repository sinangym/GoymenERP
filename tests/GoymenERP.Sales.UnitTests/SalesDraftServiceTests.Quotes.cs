using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;
using GoymenERP.Modules.Sales.Domain.Requests;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-22 / API-23 / API-24 over SYNTHETIC ports: the quote use cases on the draft service's fence — authority first, the
/// owners (Cari, the ORDER money profile, Product, Settings), No 163's same-customer requests, the revision, the loss with
/// its reason, and No 162's conversion through the draft's own creation path (both names, the price fence, the quote's
/// price/quantity snapshot, one order per quote, replay) with No 160 (approved requests only) and No 161 (no rejection of a
/// request whose order is approved). SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static CreateSalesQuoteRequest QuoteOf(IReadOnlyList<Guid>? requests = null, string price = "12.50", string? discount = "5",
        Guid? party = null, Guid? account = null) => new(party ?? Party, account ?? Account, Currency, Day,
        [new SalesQuoteLineRequest(Guid.Parse("0000000a-0000-4000-8000-00000000000b"), Product, "7", KgUnit, "1.25", price, KgUnit, discount,
            Note: "teklif satırı")],
        requests, new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 1), "Sentetik teklif");

    private static SalesQuoteRevisionRequest RevisionOf(string version, string price = "13.00") => new(version,
        [new SalesQuoteLineRequest(Guid.Parse("0000000a-0000-4000-8000-00000000000b"), Product, "7", KgUnit, "2.5", price, KgUnit)],
        new DateOnly(2026, 10, 30));

    [Fact]
    public async Task Quotes_create_is_stored_under_the_write_name_with_the_order_money_profile_and_its_prices_disclosed_under_the_price_read()
    {
        var world = new World { Clock = QuoteClock() };

        var response = await world.Service().CreateQuoteAsync(QuoteOf(), Metadata("q-1"));

        var quote = response.Detail.Quote;
        Assert.Equal(("SQ-2026-000001", "Open", "1", SalesDraftApiV1.Rev0, Currency, (DateOnly?)new DateOnly(2026, 10, 18), 1, (Guid?)null),
            (quote.DocumentNo, quote.State, quote.Version, quote.Revision, quote.CurrencyId, quote.ValidUntil, quote.LineCount, quote.ConvertedDraftId));
        var line = Assert.Single(response.Detail.Lines);
        Assert.Equal(("1.25", "12.50", (Guid?)KgUnit, "5", SalesPriceDisclosure.Visible),
            (line.Quantity, line.UnitPrice, line.PriceUnitId, line.DiscountPercent, line.PriceDisclosure));
        // The prices become the order's (No 162): they are checked against the ORDER operation's money profile.
        Assert.Equal((SalesMoneyOperations.OrderDraftV1, Currency, Day), world.Settings.MoneyAsked!.Value);
        Assert.Equal(SalesPermissionManifestV1.QuotesWrite, Assert.Single(world.Quotes!.Envelopes).PermissionCode);

        world.Authority.Denied.Add(PriceReadPermission);
        var withheld = await world.Service().GetQuoteDetailAsync(quote.Id);
        Assert.Equal((null, (Guid?)null, null, SalesPriceDisclosure.Withheld),
            (withheld.Lines[0].UnitPrice, withheld.Lines[0].PriceUnitId, withheld.Lines[0].DiscountPercent, withheld.Lines[0].PriceDisclosure));
    }

    [Fact]
    public async Task Quotes_without_the_write_name_is_403_before_anything_is_read_and_the_read_name_alone_lists()
    {
        var world = new World { Clock = QuoteClock() };
        world.Authority.Denied.Add(SalesPermissionManifestV1.QuotesWrite);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateQuoteAsync(QuoteOf(), Metadata("q-1")));

        Assert.Equal(403, refused.Status);
        Assert.Equal((0, 0, 0), (world.Accounts.Calls, world.Settings.Calls, world.Products.Calls));
        Assert.Empty((await world.Service().GetQuoteListAsync(new SalesQuoteListQuery())).Items);
        world.Authority.Denied.Add(SalesPermissionManifestV1.QuotesRead);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetQuoteListAsync(new SalesQuoteListQuery()))).Status);
        var malformed = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            new World { Clock = QuoteClock() }.Service().GetQuoteListAsync(new SalesQuoteListQuery(State: "open")));
        Assert.Equal(400, malformed.Status);
    }

    [Fact]
    public async Task Quotes_cover_requests_of_the_same_customer_only_and_a_request_is_covered_by_one_live_quote()
    {
        var world = new World { Clock = QuoteClock() };
        var first = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"))).Detail.Request.Id;
        var second = (await world.Service().CreateRequestAsync(RequestOf(lines: 2), Metadata("r-2"))).Detail.Request.Id;
        var rejected = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-3"))).Detail.Request.Id;
        await world.Service().RejectRequestAsync(rejected, Decision("1", "SENTETIK"), Metadata("r-3-reject"));

        var quote = await world.Service().CreateQuoteAsync(QuoteOf([second, first]), Metadata("q-1"));
        Assert.Equal(new[] { first, second }.Order(), quote.Detail.RequestIds);

        var taken = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateQuoteAsync(QuoteOf([first]), Metadata("q-2")));
        Assert.Equal((422, SalesQuoteProblemCodesV1.RequestUnavailable, "requestIds[0]"), (taken.Status, taken.Code, taken.Errors[0].Field));
        var refusedRequest = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateQuoteAsync(QuoteOf([rejected]), Metadata("q-3")));
        Assert.Equal(SalesQuoteProblemCodesV1.RequestUnavailable, refusedRequest.Code);
        var unknown = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateQuoteAsync(QuoteOf([Guid.NewGuid()]), Metadata("q-4")));
        Assert.Equal(SalesQuoteProblemCodesV1.RequestUnknown, unknown.Code);
        var otherCustomer = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateQuoteAsync(QuoteOf([first], party: Guid.NewGuid()), Metadata("q-5")));
        Assert.Equal(SalesQuoteProblemCodesV1.RequestCustomerMismatch, otherCustomer.Code);
        // No 163 "the same customer" is the same party AND the same company account (the stricter, fail-closed reading).
        var otherAccount = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateQuoteAsync(QuoteOf([first], account: Guid.NewGuid()), Metadata("q-5b")));
        Assert.Equal((422, SalesQuoteProblemCodesV1.RequestCustomerMismatch, "requestIds[0]"),
            (otherAccount.Status, otherAccount.Code, otherAccount.Errors[0].Field));
        Assert.Single(world.Quotes!.Envelopes);

        // A lost quote no longer covers its requests: they may be quoted again.
        world.QuotePolicies = new FakeQuotePolicies { List = LossList() };
        await world.Service().RecordQuoteLossV2Async(quote.Detail.Quote.Id, new SalesQuoteLossRequestV2("1", PriceReason), Metadata("q-1-lose"));
        var again = await world.Service().CreateQuoteAsync(QuoteOf([first]), Metadata("q-6"));
        Assert.Equal([first], again.Detail.RequestIds);
    }

    [Fact]
    public async Task Quotes_a_revision_replaces_the_lines_as_the_next_revision_and_a_stale_or_closed_quote_is_the_keys_terminal_409()
    {
        var world = new World { Clock = QuoteClock() };
        var id = (await world.Service().CreateQuoteAsync(QuoteOf(), Metadata("q-1"))).Detail.Quote.Id;

        var revised = await world.Service().ReviseQuoteAsync(id, RevisionOf("1"), Metadata("q-revise"));
        Assert.Equal(("Rev.1", "2", "2.5", "13.00", (DateOnly?)new DateOnly(2026, 10, 30)),
            (revised.Detail.Quote.Revision, revised.Detail.Quote.Version, revised.Detail.Lines[0].Quantity, revised.Detail.Lines[0].UnitPrice,
                revised.Detail.Quote.ValidUntil));
        var replay = await world.Service().ReviseQuoteAsync(id, RevisionOf("1"), Metadata("q-revise"));
        Assert.True(replay.IsReplay);

        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuoteAsync(id, RevisionOf("1", "14"), Metadata("q-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (stale.Status, stale.Code));
        var staleAgain = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuoteAsync(id, RevisionOf("1", "14"), Metadata("q-stale")));
        Assert.True(staleAgain.IsReplay);

        world.QuotePolicies = new FakeQuotePolicies { List = LossList() };
        var blank = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordQuoteLossV2Async(id, new SalesQuoteLossRequestV2("2", OtherReason, " "), Metadata("q-lose-blank")));
        Assert.Equal((422, SalesRuleCodes.QuoteLossReasonRequired, "explanation"), (blank.Status, blank.Code, Assert.Single(blank.Errors).Field));
        var lost = await world.Service().RecordQuoteLossV2Async(id, new SalesQuoteLossRequestV2("2", OtherReason, "Rakip daha erken teslim verdi"),
            Metadata("q-lose"));
        Assert.Equal(("Lost", "Rakip daha erken teslim verdi", (Guid?)OtherReason, "SENTETIK-DIGER", "Sentetik diğer"),
            (lost.Detail.Quote.State, lost.Detail.Loss!.Reason, lost.Detail.Loss.ReasonId, lost.Detail.Loss.ReasonCode, lost.Detail.Loss.ReasonLabel));
        var closed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuoteAsync(id, RevisionOf("3"), Metadata("q-revise-lost")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (closed.Status, closed.Code));
        var tooScaled = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateQuoteAsync(QuoteOf(price: "12.505"), Metadata("q-scale")));
        Assert.Equal((422, "lines[0]", SalesRuleCodes.PriceScaleExceeded), (tooScaled.Status, tooScaled.Errors[0].Field, tooScaled.Errors[0].Code));
    }

    [Fact]
    public async Task Quotes_an_actor_whose_prices_are_withheld_cannot_revise_or_write_a_price_and_nothing_is_read_or_recorded()
    {
        var world = new World { Clock = QuoteClock() };
        var id = (await world.Service().CreateQuoteAsync(QuoteOf(), Metadata("q-1"))).Detail.Quote.Id;
        world.Authority.Denied.Add(PriceReadPermission);
        var productCalls = world.Products.Calls;

        // A revision replaces every line: without the price read it would erase prices the actor cannot see — 403, fail-closed,
        // whatever the lines carry, before the quote or any owner is read and without a recorded attempt.
        var revision = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ReviseQuoteAsync(id, RevisionOf("1"), Metadata("q-revise")));
        Assert.Equal((403, SalesQuoteProblemCodesV1.PriceReadRequired, "lines"), (revision.Status, revision.Code, Assert.Single(revision.Errors).Field));
        var unpricedRevision = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuoteAsync(id,
            new SalesQuoteRevisionRequest("1", [new SalesQuoteLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "2.5")]), Metadata("q-revise-unpriced")));
        Assert.Equal(403, unpricedRevision.Status);

        // A create that carries a price field is refused the same way, naming each price field; a price-less create is not.
        var priced = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateQuoteAsync(QuoteOf(), Metadata("q-2")));
        Assert.Equal((403, SalesQuoteProblemCodesV1.PriceReadRequired), (priced.Status, priced.Code));
        Assert.Equal(["lines[0].unitPrice", "lines[0].priceUnitId", "lines[0].discountPercent"], priced.Errors.Select(error => error.Field));
        Assert.Equal(productCalls, world.Products.Calls);
        Assert.Single(world.Quotes!.Envelopes);
        var unpriced = await world.Service().CreateQuoteAsync(QuoteOf() with
        {
            Lines = [new SalesQuoteLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "1.25")],
        }, Metadata("q-3"));
        Assert.Equal(SalesPriceDisclosure.Withheld, Assert.Single(unpriced.Detail.Lines).PriceDisclosure);

        // With the price read the same actor revises.
        world.Authority.Denied.Remove(PriceReadPermission);
        Assert.Equal("Rev.1", (await world.Service().ReviseQuoteAsync(id, RevisionOf("1"), Metadata("q-revise-visible"))).Detail.Quote.Revision);
    }

    [Fact]
    public async Task Quotes_conversion_builds_one_order_from_the_quotes_snapshot_through_the_draft_path_and_replays_it()
    {
        var world = new World { Clock = QuoteClock() };
        var request = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"))).Detail.Request.Id;
        var id = (await world.Service().CreateQuoteAsync(QuoteOf([request]), Metadata("q-1"))).Detail.Quote.Id;
        var orderDay = Day.AddDays(1);

        // No 160: no order from a quote whose request is not approved; nothing is recorded, the same key may come back.
        var unapproved = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("1", orderDay), Metadata("q-convert")));
        Assert.Equal((409, SalesQuoteProblemCodesV1.RequestNotApproved, "requestIds"), (unapproved.Status, unapproved.Code, Assert.Single(unapproved.Errors).Field));
        Assert.Empty(world.Store.Envelopes);

        await world.Service().ApproveRequestAsync(request, Decision("1"), Metadata("r-1-approve"));
        world.Authority.Measured.Clear();
        var converted = await world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("1", orderDay), Metadata("q-convert"));

        var order = converted.Detail;
        Assert.False(converted.IsReplay);
        Assert.Equal((Party, Account, Currency, orderDay), (order.Draft.PartyId, order.Draft.CompanyAccountId, order.Draft.CurrencyId, order.Draft.BusinessDate));
        var line = Assert.Single(order.Lines);
        Assert.Equal((Guid.Parse("0000000a-0000-4000-8000-00000000000b"), "1.25", "12.50", (Guid?)KgUnit, "5", "teklif satırı"),
            (line.LineId, line.Quantity, line.UnitPrice, line.PriceUnitId, line.DiscountPercent, line.Note));
        Assert.Contains(SalesPermissionManifestV1.QuotesWrite, world.Authority.Measured);
        Assert.Contains(CreatePermission, world.Authority.Measured);
        Assert.Contains(PricePermission, world.Authority.Measured);
        var quote = await world.Service().GetQuoteDetailAsync(id);
        Assert.Equal(("Converted", (Guid?)order.Draft.Id), (quote.Quote.State, quote.Quote.ConvertedDraftId));

        var replay = await world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("1", orderDay), Metadata("q-convert", "again"));
        Assert.Equal((true, order.Draft.Id), (replay.IsReplay, replay.Detail.Draft.Id));
        // No 162: one order per quote — another key meets a converted quote.
        var second = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("2", orderDay), Metadata("q-convert-2")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (second.Status, second.Code));
        Assert.Single(world.Store.Envelopes);
    }

    [Fact]
    public async Task Quotes_expiry_is_the_servers_business_day_so_a_client_back_dated_conversion_is_still_refused_and_not_recorded()
    {
        // SAT-D24 (P2 of #828 review B): QuoteOf's validity date is 2026-10-18. The server's Europe/Istanbul business day decides
        // the expiry — a business date the client sends (even one inside the validity) never does. Refused before any owner is
        // asked; not recorded, so the same key converts once the approval exists.
        var world = new World { Clock = QuoteClock() };
        var id = (await world.Service().CreateQuoteAsync(QuoteOf(), Metadata("q-1"))).Detail.Quote.Id;
        var validUntil = new DateOnly(2026, 10, 18);
        // 2026-10-18 21:30 UTC is already 2026-10-19 in Istanbul (UTC+3): the quote is expired on the server's business day.
        world.Clock = new MutableClock(new DateTimeOffset(2026, 10, 18, 21, 30, 0, TimeSpan.Zero));
        world.ResetOwnerCalls();

        var backDated = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("1", validUntil.AddDays(-5)), Metadata("q-convert")));

        Assert.Equal((409, SalesQuoteProblemCodesV1.Expired, "validUntil"), (backDated.Status, backDated.Code, Assert.Single(backDated.Errors).Field));
        Assert.Equal((0, 0), (world.Accounts.Calls, world.Products.Calls));
        Assert.Empty(world.Store.Envelopes);
        var detail = await world.Service().GetQuoteDetailAsync(id);
        Assert.Equal(("Open", new SalesQuoteExpiryV1(validUntil.AddDays(1), true, false)), (detail.Quote.State, detail.Expiry));

        // One minute earlier it is still 2026-10-18 in Istanbul: the validity day itself converts, whatever date the order takes.
        world.Clock = new MutableClock(new DateTimeOffset(2026, 10, 18, 20, 59, 0, TimeSpan.Zero));
        var converted = await world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("1", validUntil.AddDays(10)), Metadata("q-convert"));
        Assert.Equal((false, validUntil.AddDays(10)), (converted.IsReplay, converted.Detail.Draft.BusinessDate));
        Assert.Equal("Converted", (await world.Service().GetQuoteDetailAsync(id)).Quote.State);
    }

    [Fact]
    public async Task Quotes_the_expiry_approval_needs_its_own_name_any_holder_even_the_creator_approves_and_the_same_key_then_converts()
    {
        var world = new World { Clock = QuoteClock() };
        var id = (await world.Service().CreateQuoteAsync(QuoteOf(), Metadata("q-1"))).Detail.Quote.Id;
        world.Clock = new MutableClock(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
        await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("1", Day), Metadata("q-convert")));

        // Authority first: without the approval's own name (the quote write and order names do not confer it) — 403, nothing read.
        world.Authority.Denied.Add(SalesPermissionManifestV1.QuotesExpiredConversionApprove);
        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveExpiredQuoteConversionAsync(id, new SalesQuoteExpiryApprovalRequest("1"), Metadata("q-approve")));
        Assert.Equal((403, "forbidden"), (forbidden.Status, forbidden.Code));
        Assert.Single(world.Quotes!.Envelopes);

        // SAT-D44 (2): the quote's creator (the same actor) holds the name and approves it; the version moves by one.
        world.Authority.Denied.Clear();
        world.Authority.Measured.Clear();
        var approved = await world.Service().ApproveExpiredQuoteConversionAsync(id,
            new SalesQuoteExpiryApprovalRequest("1", "Müşteri fiyatı kabul etti"), Metadata("q-approve"));
        Assert.Equal(("2", Actor, Actor), (approved.Detail.Quote.Version, approved.Detail.CreatedBy, Assert.Single(approved.Detail.ExpiryApprovals).ApprovedBy));
        Assert.Equal(new SalesQuoteExpiryApprovalV1(SalesDraftApiV1.Rev0, new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 25), Actor,
            approved.Detail.ExpiryApprovals[0].ApprovedAtUtc, "Müşteri fiyatı kabul etti"), approved.Detail.ExpiryApprovals[0]);
        Assert.Equal(new SalesQuoteExpiryV1(new DateOnly(2026, 10, 25), true, true), approved.Detail.Expiry);
        Assert.Contains(SalesPermissionManifestV1.QuotesExpiredConversionApprove, world.Authority.Measured);
        Assert.Equal(SalesPermissionManifestV1.QuotesExpiredConversionApprove, world.Quotes.Envelopes[^1].PermissionCode);
        // Idempotent: the same key and body answers again without a second step.
        var replay = await world.Service().ApproveExpiredQuoteConversionAsync(id,
            new SalesQuoteExpiryApprovalRequest("1", "Müşteri fiyatı kabul etti"), Metadata("q-approve"));
        Assert.Equal((true, "2"), (replay.IsReplay, replay.Detail.Quote.Version));
        var otherBody = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveExpiredQuoteConversionAsync(id, new SalesQuoteExpiryApprovalRequest("1"), Metadata("q-approve")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), (otherBody.Status, otherBody.Code));

        // Another key on the same revision: already approved — the key's recorded terminal 409.
        var twice = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveExpiredQuoteConversionAsync(id, new SalesQuoteExpiryApprovalRequest("2"), Metadata("q-approve-2")));
        Assert.Equal((409, SalesQuoteProblemCodesV1.ExpiryAlreadyApproved, false), (twice.Status, twice.Code, twice.IsReplay));
        var twiceAgain = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveExpiredQuoteConversionAsync(id, new SalesQuoteExpiryApprovalRequest("2"), Metadata("q-approve-2")));
        Assert.True(twiceAgain.IsReplay);
        // A stale version is the key's recorded terminal 409 as well.
        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveExpiredQuoteConversionAsync(id, new SalesQuoteExpiryApprovalRequest("1"), Metadata("q-approve-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (stale.Status, stale.Code));

        // The refused conversion key was not burned: it converts now, with the approved quote's version.
        var converted = await world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("2", Day), Metadata("q-convert"));
        Assert.Equal("Converted", (await world.Service().GetQuoteDetailAsync(id)).Quote.State);
        Assert.Equal(Day, converted.Detail.Draft.BusinessDate);
    }

    [Fact]
    public async Task Quotes_an_approval_belongs_to_one_revision_and_a_quote_that_has_not_expired_is_not_approved_nor_recorded()
    {
        var world = new World { Clock = QuoteClock() };
        var id = (await world.Service().CreateQuoteAsync(QuoteOf(), Metadata("q-1"))).Detail.Quote.Id;

        // Not expired on the server's business day: 409, not recorded — the same key succeeds once the quote expires.
        var early = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveExpiredQuoteConversionAsync(id, new SalesQuoteExpiryApprovalRequest("1"), Metadata("q-approve")));
        Assert.Equal((409, SalesQuoteProblemCodesV1.NotExpired, "validUntil"), (early.Status, early.Code, Assert.Single(early.Errors).Field));
        world.Clock = new MutableClock(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
        await world.Service().ApproveExpiredQuoteConversionAsync(id, new SalesQuoteExpiryApprovalRequest("1"), Metadata("q-approve"));

        // A revision (by another actor) is a new offer: Rev.1 is expired again and needs its own approval (still 2026-10-30).
        world.User.Id = "SENTETIK-reviser";
        var revised = await world.Service().ReviseQuoteAsync(id, RevisionOf("2"), Metadata("q-revise"));
        Assert.Equal(new SalesQuoteExpiryV1(new DateOnly(2026, 11, 2), true, false), revised.Detail.Expiry);
        Assert.Equal([(SalesDraftApiV1.Rev0, Actor), ("Rev.1", "SENTETIK-reviser")],
            revised.Detail.Revisions.Select(record => (record.Revision, record.RevisedBy)));
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("3", Day), Metadata("q-convert")));
        Assert.Equal(SalesQuoteProblemCodesV1.Expired, refused.Code);
        Assert.Empty(world.Store.Envelopes);
    }

    [Fact]
    public async Task Quotes_the_loss_takes_an_active_entry_of_the_settings_list_and_without_a_list_nothing_is_recorded()
    {
        var world = new World { Clock = QuoteClock() };
        var id = (await world.Service().CreateQuoteAsync(QuoteOf(), Metadata("q-1"))).Detail.Quote.Id;

        // SAT-D25 fail-closed: no Settings port, no list record, a passive list, a list outside its window, a foreign list.
        var notComposed = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordQuoteLossV2Async(id, new SalesQuoteLossRequestV2("1", PriceReason), Metadata("q-lose")));
        Assert.Equal((503, SalesQuoteProblemCodesV1.LossReasonsUnavailable), (notComposed.Status, notComposed.Code));
        foreach (var policies in new[]
                 {
                     new FakeQuotePolicies(),
                     new FakeQuotePolicies { List = LossList() with { IsActive = false } },
                     new FakeQuotePolicies { List = LossList() with { EffectiveFrom = new DateOnly(2026, 9, 19) } },
                     new FakeQuotePolicies { List = LossList() with { CompanyId = Guid.NewGuid() } },
                     new FakeQuotePolicies { FailWith = "settings_provider_unavailable" },
                 })
        {
            world.QuotePolicies = policies;
            var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                world.Service().RecordQuoteLossV2Async(id, new SalesQuoteLossRequestV2("1", PriceReason), Metadata("q-lose")));
            Assert.Equal((503, SalesQuoteProblemCodesV1.LossReasonsUnavailable), (refused.Status, refused.Code));
        }

        world.QuotePolicies = new FakeQuotePolicies { FailWith = "settings_access_denied" };
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordQuoteLossV2Async(id, new SalesQuoteLossRequestV2("1", PriceReason), Metadata("q-lose")))).Status);

        // The list is read on the server's business day; an unknown or passive entry is 422 on reasonId.
        var policiesInForce = new FakeQuotePolicies { List = LossList() };
        world.QuotePolicies = policiesInForce;
        foreach (var reason in new[] { Guid.NewGuid(), PassiveReason })
        {
            var unknown = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                world.Service().RecordQuoteLossV2Async(id, new SalesQuoteLossRequestV2("1", reason), Metadata("q-lose")));
            Assert.Equal((422, SalesQuoteProblemCodesV1.LossReasonUnknown, "reasonId"), (unknown.Status, unknown.Code, Assert.Single(unknown.Errors).Field));
        }

        Assert.Equal(Day, policiesInForce.AskedOn);
        Assert.Equal("Open", (await world.Service().GetQuoteDetailAsync(id)).Quote.State);
        Assert.Single(world.Quotes!.Envelopes);

        // An entry that does not ask for an explanation needs none; the snapshot of the entry and its list version is recorded.
        var lost = await world.Service().RecordQuoteLossV2Async(id, new SalesQuoteLossRequestV2("1", PriceReason), Metadata("q-lose"));
        Assert.Equal(new SalesQuoteLossV1(null, Actor, lost.Detail.Loss!.LostAtUtc, PriceReason, "SENTETIK-FIYAT", "Sentetik fiyat"), lost.Detail.Loss);
        var replay = await world.Service().RecordQuoteLossV2Async(id, new SalesQuoteLossRequestV2("1", PriceReason), Metadata("q-lose"));
        Assert.True(replay.IsReplay);
    }

    [Fact]
    public async Task Quotes_the_loss_reason_list_and_the_validity_suggestion_are_read_under_the_write_name_and_suggest_only_a_settings_value()
    {
        var world = new World { Clock = QuoteClock() };
        world.Authority.Denied.Add(SalesPermissionManifestV1.QuotesWrite);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetQuoteLossReasonsAsync())).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetQuoteValiditySuggestionAsync(Day))).Status);
        world.Authority.Denied.Clear();

        // Not composed: the reads cannot answer (503), never an empty list or a default.
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetQuoteLossReasonsAsync())).Status);
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetQuoteValiditySuggestionAsync(Day))).Status);

        // SAT-D39: no Settings value — no suggestion; SAT-D25: no list — not defined.
        world.QuotePolicies = new FakeQuotePolicies();
        Assert.Equal(new SalesQuoteValiditySuggestionV1(Day, null, null, null, null), await world.Service().GetQuoteValiditySuggestionAsync(Day));
        var undefined = await world.Service().GetQuoteLossReasonsAsync();
        Assert.Equal((Day, false, 0), (undefined.BusinessDay, undefined.IsDefined, undefined.Reasons.Count));

        var profile = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        var policies = new FakeQuotePolicies
        {
            List = LossList(),
            Validity = new SalesQuoteDefaultValidityPolicyV1(Company, new ProfileVersionV1(profile, 2), 30, true, new DateOnly(2026, 9, 1), null),
        };
        world.QuotePolicies = policies;
        Assert.Equal(new SalesQuoteValiditySuggestionV1(Day, 30, Day.AddDays(30), profile, "2"), await world.Service().GetQuoteValiditySuggestionAsync(Day));
        Assert.Equal(Day, policies.AskedOn);
        // Outside the policy's window, or passive: no suggestion.
        Assert.Null((await world.Service().GetQuoteValiditySuggestionAsync(new DateOnly(2026, 8, 31))).ValidityDays);
        policies.Validity = policies.Validity with { IsActive = false };
        Assert.Null((await world.Service().GetQuoteValiditySuggestionAsync(Day)).SuggestedValidUntil);

        // Only the active entries are offered, in the list's order, with the entry's own explanation flag.
        var reasons = await world.Service().GetQuoteLossReasonsAsync();
        Assert.True(reasons.IsDefined);
        Assert.Equal([(PriceReason, false), (OtherReason, true)], reasons.Reasons.Select(reason => (reason.ReasonId, reason.RequiresExplanation)));
    }

    [Theory]
    [InlineData("quote-write")]
    [InlineData("draft-create")]
    [InlineData("price-write")]
    public async Task Quotes_conversion_needs_both_names_and_a_priced_line_the_orders_own_price_write(string denied)
    {
        var world = new World { Clock = QuoteClock() };
        var id = (await world.Service().CreateQuoteAsync(QuoteOf(), Metadata("q-1"))).Detail.Quote.Id;
        world.Authority.Denied.Add(denied switch
        {
            "quote-write" => SalesPermissionManifestV1.QuotesWrite,
            "draft-create" => CreatePermission,
            _ => PricePermission,
        });
        world.ResetOwnerCalls();

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("1", Day), Metadata("q-convert")));

        Assert.Equal(403, refused.Status);
        Assert.Equal(denied == "price-write" ? SalesDraftProblemCodesV1.PriceWriteForbidden : "forbidden", refused.Code);
        Assert.Equal((0, 0), (world.Accounts.Calls, world.Products.Calls));
        Assert.Empty(world.Store.Envelopes);
        Assert.Equal("Open", (await new World { Quotes = world.Quotes, Requests = world.Requests, Clock = world.Clock }.Service().GetQuoteDetailAsync(id)).Quote.State);
    }

    [Fact]
    public async Task Requests_a_request_whose_quote_became_an_approved_order_is_not_rejected_and_the_refusal_is_the_keys()
    {
        var world = new World { Clock = QuoteClock() };
        var id = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"))).Detail.Request.Id;
        world.Requests!.HasApprovedOrder = request => request == id;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RejectRequestAsync(id, Decision("1", "SENTETIK"), Metadata("r-reject")));
        Assert.Equal((409, SalesRuleCodes.RequestHasApprovedOrder), (refused.Status, refused.Code));
        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RejectRequestAsync(id, Decision("1", "SENTETIK"), Metadata("r-reject")));
        Assert.True(again.IsReplay);
        Assert.Equal("Submitted", (await world.Service().GetRequestDetailAsync(id)).Request.State);
    }

    /// <summary>An in-memory quote store over the draft and request fakes: the contract's replay, the conversion's one write.</summary>
    private static readonly Guid PriceReason = Guid.Parse("e0000000-0000-4000-8000-000000000001");
    private static readonly Guid OtherReason = Guid.Parse("e0000000-0000-4000-8000-000000000002");
    private static readonly Guid PassiveReason = Guid.Parse("e0000000-0000-4000-8000-000000000003");

    /// <summary>A SENTETIK loss reason list (SAT-D25's content is a Settings record; nothing here is a real value).</summary>
    private static SalesQuoteLossReasonListV1 LossList() => new(Company,
        new ProfileVersionV1(Guid.Parse("e0000000-0000-4000-8000-0000000000ff"), 1),
        [
            new SalesQuoteLossReasonV1(PriceReason, "SENTETIK-FIYAT", "Sentetik fiyat", false, true),
            new SalesQuoteLossReasonV1(OtherReason, "SENTETIK-DIGER", "Sentetik diğer", true, true),
            new SalesQuoteLossReasonV1(PassiveReason, "SENTETIK-ESKI", "Sentetik eski", false, false),
        ],
        true, new DateOnly(2026, 9, 1), null);

    /// <summary>The quote tests' server clock: 2026-09-18 09:00 UTC, i.e. the quotes' business date in Istanbul.</summary>
    private static MutableClock QuoteClock() => new(new DateTimeOffset(2026, 9, 18, 9, 0, 0, TimeSpan.Zero));

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>The Settings quote policy port over SENTETIK records; null record = "not found".</summary>
    private sealed class FakeQuotePolicies : ISettingsSalesQuotePolicyProviderV1
    {
        public SalesQuoteLossReasonListV1? List { get; set; }
        public SalesQuoteDefaultValidityPolicyV1? Validity { get; set; }
        public string? FailWith { get; set; }
        public DateOnly? AskedOn { get; private set; }

        public Task<SalesQuoteLossReasonListV1> GetSalesQuoteLossReasonsAsync(DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            AskedOn = effectiveOn;
            return FailWith is { } code ? Task.FromException<SalesQuoteLossReasonListV1>(new SettingsPrerequisiteException(code))
                : List is { } list ? Task.FromResult(list)
                : Task.FromException<SalesQuoteLossReasonListV1>(new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyNotFound));
        }

        public Task<SalesQuoteDefaultValidityPolicyV1> GetSalesQuoteDefaultValidityAsync(DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            AskedOn = effectiveOn;
            return FailWith is { } code ? Task.FromException<SalesQuoteDefaultValidityPolicyV1>(new SettingsPrerequisiteException(code))
                : Validity is { } validity ? Task.FromResult(validity)
                : Task.FromException<SalesQuoteDefaultValidityPolicyV1>(new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyNotFound));
        }
    }

    private sealed class FakeQuoteStore(FakeStore drafts, FakeRequestStore? requestStore, Func<TimeProvider> clock) : ISalesQuoteStore
    {
        private readonly Dictionary<(string Command, string Key), (string Actor, string Hash, Guid? QuoteId, SalesDraftTerminalRefusal? Refusal, string Correlation)> attempts = [];
        private readonly List<SalesQuote> quotes = [];
        private readonly HashSet<Guid> lostQuotes = [];
        private int number;
        public List<SalesDraftWriteEnvelope> Envelopes { get; } = [];

        public Task<SalesQuoteStoreResult> StoreQuoteAsync(SalesDraftWriteEnvelope envelope, string requestHash, Func<string, SalesQuote> build,
            CancellationToken cancellationToken = default)
        {
            if (Replay(envelope, requestHash, SalesQuoteCommands.CreateV1) is { } replay) return Task.FromResult(replay);
            var quote = build($"SQ-2026-{++number:D6}");
            quotes.Add(quote);
            attempts[(SalesQuoteCommands.CreateV1, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, quote.Id, null, envelope.CorrelationId);
            Envelopes.Add(envelope);
            return Task.FromResult(new SalesQuoteStoreResult(quote, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<SalesQuoteReplay?> FindQuoteReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            CancellationToken cancellationToken = default)
        {
            if (!attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt)) return Task.FromResult<SalesQuoteReplay?>(null);
            if (attempt.Actor != envelope.ActorId || attempt.Hash != requestHash)
                throw new SalesDraftStoreException(SalesDraftStoreCodes.SameKeyPayloadMismatch, "mismatch");
            return Task.FromResult<SalesQuoteReplay?>(new SalesQuoteReplay(
                attempt.QuoteId is { } id ? quotes.Single(quote => quote.Id == id) : null, attempt.Refusal, attempt.Correlation));
        }

        public Task RecordQuoteRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid? quoteId,
            SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
        {
            attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, null, refusal, envelope.CorrelationId);
            return Task.CompletedTask;
        }

        public Task<SalesQuoteStoreResult?> UpdateQuoteAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid quoteId,
            Action<SalesQuote, DateTimeOffset> mutate, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
        {
            if (Replay(envelope, requestHash, commandName) is { } replay) return Task.FromResult<SalesQuoteStoreResult?>(replay);
            var quote = quotes.SingleOrDefault(candidate => candidate.Id == quoteId);
            if (quote is null) return Task.FromResult<SalesQuoteStoreResult?>(null);
            mutate(quote, clock().GetUtcNow());
            if (quote.State == SalesQuoteState.Lost) lostQuotes.Add(quote.Id);
            Envelopes.Add(envelope);
            attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, quote.Id, null, envelope.CorrelationId);
            return Task.FromResult<SalesQuoteStoreResult?>(new SalesQuoteStoreResult(quote, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<SalesQuote?> FindQuoteAsync(Guid quoteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(quotes.SingleOrDefault(quote => quote.Id == quoteId));

        public Task<SalesQuotePage> ListQuotesAsync(int pageSize, string? cursor, SalesQuoteState? state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesQuotePage([.. quotes.Where(quote => state is null || quote.State == state).Take(pageSize)], null));

        public Task<bool> IsRequestQuotedAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            Task.FromResult(quotes.Any(quote => !lostQuotes.Contains(quote.Id) && quote.RequestIds.Contains(requestId)));

        public async Task<SalesDraftStoreResult> StoreConvertedDraftAsync(SalesDraftWriteEnvelope envelope, string requestHash, Guid quoteId,
            long expectedQuoteVersion, Func<string, SalesOrderDraft> build, CancellationToken cancellationToken = default)
        {
            var quote = quotes.Single(candidate => candidate.Id == quoteId);
            quote.RequireConvertible(expectedQuoteVersion);
            foreach (var requestId in quote.RequestIds)
            {
                if ((await requestStore!.FindRequestAsync(requestId, cancellationToken))?.State != SalesRequestState.Approved)
                    throw new SalesRuleException(SalesRuleCodes.QuoteRequestNotApproved, "synthetic");
            }

            var stored = drafts.StoreUnder(SalesQuoteCommands.ConvertV1, envelope, requestHash, build);
            var now = clock().GetUtcNow();
            quote.Convert(expectedQuoteVersion, stored.Draft.Id, envelope.ActorId, SalesBusinessDay.DateOf(now), now);
            return stored;
        }

        private SalesQuoteStoreResult? Replay(SalesDraftWriteEnvelope envelope, string requestHash, string commandName)
        {
            if (!attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt)) return null;
            if (attempt.Actor != envelope.ActorId || attempt.Hash != requestHash)
                throw new SalesDraftStoreException(SalesDraftStoreCodes.SameKeyPayloadMismatch, "mismatch");
            if (attempt.QuoteId is not { } id) throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "refusal recorded");
            return new SalesQuoteStoreResult(quotes.Single(quote => quote.Id == id), SalesDraftWriteOutcome.Replayed, attempt.Correlation);
        }
    }
}

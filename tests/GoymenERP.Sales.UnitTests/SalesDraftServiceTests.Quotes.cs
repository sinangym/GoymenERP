using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;
using GoymenERP.Modules.Sales.Domain.Requests;

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
        Guid? party = null) => new(party ?? Party, Account, Currency, Day,
        [new SalesQuoteLineRequest(Guid.Parse("0000000a-0000-4000-8000-00000000000b"), Product, "7", KgUnit, "1.25", price, KgUnit, discount,
            Note: "teklif satırı")],
        requests, new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 1), "Sentetik teklif");

    private static SalesQuoteRevisionRequest RevisionOf(string version, string price = "13.00") => new(version,
        [new SalesQuoteLineRequest(Guid.Parse("0000000a-0000-4000-8000-00000000000b"), Product, "7", KgUnit, "2.5", price, KgUnit)],
        new DateOnly(2026, 10, 30));

    [Fact]
    public async Task Quotes_create_is_stored_under_the_write_name_with_the_order_money_profile_and_its_prices_disclosed_under_the_price_read()
    {
        var world = new World();

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
        var world = new World();
        world.Authority.Denied.Add(SalesPermissionManifestV1.QuotesWrite);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateQuoteAsync(QuoteOf(), Metadata("q-1")));

        Assert.Equal(403, refused.Status);
        Assert.Equal((0, 0, 0), (world.Accounts.Calls, world.Settings.Calls, world.Products.Calls));
        Assert.Empty((await world.Service().GetQuoteListAsync(new SalesQuoteListQuery())).Items);
        world.Authority.Denied.Add(SalesPermissionManifestV1.QuotesRead);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetQuoteListAsync(new SalesQuoteListQuery()))).Status);
        var malformed = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            new World().Service().GetQuoteListAsync(new SalesQuoteListQuery(State: "open")));
        Assert.Equal(400, malformed.Status);
    }

    [Fact]
    public async Task Quotes_cover_requests_of_the_same_customer_only_and_a_request_is_covered_by_one_live_quote()
    {
        var world = new World();
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
        Assert.Single(world.Quotes!.Envelopes);

        // A lost quote no longer covers its requests: they may be quoted again.
        await world.Service().RecordQuoteLossAsync(quote.Detail.Quote.Id, new SalesQuoteLossRequest("1", "Fiyat yüksek bulundu"), Metadata("q-1-lose"));
        var again = await world.Service().CreateQuoteAsync(QuoteOf([first]), Metadata("q-6"));
        Assert.Equal([first], again.Detail.RequestIds);
    }

    [Fact]
    public async Task Quotes_a_revision_replaces_the_lines_as_the_next_revision_and_a_stale_or_closed_quote_is_the_keys_terminal_409()
    {
        var world = new World();
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

        var blank = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RecordQuoteLossAsync(id, new SalesQuoteLossRequest("2", " "), Metadata("q-lose-blank")));
        Assert.Equal((422, SalesRuleCodes.QuoteLossReasonRequired), (blank.Status, blank.Code));
        var lost = await world.Service().RecordQuoteLossAsync(id, new SalesQuoteLossRequest("2", "Rakip daha erken teslim verdi"), Metadata("q-lose"));
        Assert.Equal(("Lost", "Rakip daha erken teslim verdi"), (lost.Detail.Quote.State, lost.Detail.Loss!.Reason));
        var closed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseQuoteAsync(id, RevisionOf("3"), Metadata("q-revise-lost")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (closed.Status, closed.Code));
        var tooScaled = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateQuoteAsync(QuoteOf(price: "12.505"), Metadata("q-scale")));
        Assert.Equal((422, "lines[0]", SalesRuleCodes.PriceScaleExceeded), (tooScaled.Status, tooScaled.Errors[0].Field, tooScaled.Errors[0].Code));
    }

    [Fact]
    public async Task Quotes_conversion_builds_one_order_from_the_quotes_snapshot_through_the_draft_path_and_replays_it()
    {
        var world = new World();
        var request = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"))).Detail.Request.Id;
        var id = (await world.Service().CreateQuoteAsync(QuoteOf([request]), Metadata("q-1"))).Detail.Quote.Id;
        var orderDay = Day.AddDays(1);

        // No 160: no order from a quote whose request is not approved; nothing is recorded, the same key may come back.
        var unapproved = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("1", orderDay), Metadata("q-convert")));
        Assert.Equal((409, SalesRuleCodes.QuoteRequestNotApproved, "requestIds[0]"), (unapproved.Status, unapproved.Code, unapproved.Errors[0].Field));
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

    [Theory]
    [InlineData("quote-write")]
    [InlineData("draft-create")]
    [InlineData("price-write")]
    public async Task Quotes_conversion_needs_both_names_and_a_priced_line_the_orders_own_price_write(string denied)
    {
        var world = new World();
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
        Assert.Equal("Open", (await new World { Quotes = world.Quotes, Requests = world.Requests }.Service().GetQuoteDetailAsync(id)).Quote.State);
    }

    [Fact]
    public async Task Requests_a_request_whose_quote_became_an_approved_order_is_not_rejected_and_the_refusal_is_the_keys()
    {
        var world = new World();
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
    private sealed class FakeQuoteStore(FakeStore drafts, FakeRequestStore? requestStore) : ISalesQuoteStore
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
            mutate(quote, DateTimeOffset.UtcNow);
            if (quote.State == SalesQuoteState.Lost) lostQuotes.Add(quote.Id);
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
            quote.Convert(expectedQuoteVersion, stored.Draft.Id, envelope.ActorId, DateTimeOffset.UtcNow);
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

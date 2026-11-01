using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D71 (SINAN_KARARLARI_2026-10-28_SATIS_EK12): an order's business date is never before the SERVER's business day
/// (Europe/Istanbul) — at a draft create and at a quote conversion alike; today or later is accepted, an earlier date is 422
/// <c>validation_failed</c> on <c>businessDate</c>, not recorded, and an unresolvable zone is 503. SAT-D72: a cancelled
/// converted order releases its request — a new quote covers it and converts again, one order per quote still holds.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Order_business_date_before_the_servers_istanbul_day_is_refused_unrecorded_and_today_or_later_is_created()
    {
        var world = new World { Clock = QuoteClock() }; // 2026-09-18 09:00 UTC = 12:00 Istanbul, the business day is Day

        var past = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateDraftAsync(Request() with { BusinessDate = Day.AddDays(-1) }, Metadata("k-1")));

        Assert.Equal((422, "validation_failed"), (past.Status, past.Code));
        var error = Assert.Single(past.Errors);
        Assert.Equal(("businessDate", SalesDraftProblemCodesV1.BusinessDateBeforeBusinessDay), (error.Field, error.Code));
        Assert.Equal("sales_business_date_before_business_day", SalesDraftProblemCodesV1.BusinessDateBeforeBusinessDay);
        // Nothing read from an owner, nothing stored or recorded: the key is not burned.
        Assert.Equal((0, 0, 0), (world.Accounts.Calls, world.Settings.Calls, world.Products.Calls));
        Assert.Empty(world.Store.Envelopes);
        Assert.Empty(world.Store.Refusals);

        // The same key with today's date creates the order; a later date is accepted too.
        var today = await world.Service().CreateDraftAsync(Request(), Metadata("k-1"));
        Assert.Equal((false, Day), (today.IsReplay, today.Detail.Draft.BusinessDate));
        var laterRequest = Request() with { BusinessDate = Day.AddDays(30) };
        var later = await world.Service().CreateDraftAsync(laterRequest, Metadata("k-2"));
        Assert.Equal(Day.AddDays(30), later.Detail.Draft.BusinessDate);

        // A recorded answer stays the answer: the next day the same key and body replay the stored order, never a 422.
        world.Clock = new MutableClock(new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero));
        var replay = await world.Service().CreateDraftAsync(laterRequest, Metadata("k-2"));
        Assert.Equal((true, later.Detail.Draft.Id), (replay.IsReplay, replay.Detail.Draft.Id));
        Assert.Equal(2, world.Store.Envelopes.Count);
    }

    [Theory]
    [InlineData("2026-09-17T21:30:00Z", false)] // 00:30 on 2026-09-18 in Istanbul: the UTC date (17th) does not decide
    [InlineData("2026-09-17T20:59:00Z", true)]  // 23:59 on 2026-09-17 in Istanbul: the 17th is still today
    public async Task Order_business_day_is_the_istanbul_calendar_day_of_the_server_clock_never_the_utc_date(string utc, bool accepted)
    {
        var world = new World { Clock = new MutableClock(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)) };
        var request = Request() with { BusinessDate = new DateOnly(2026, 9, 17) };

        if (accepted)
        {
            Assert.Equal(new DateOnly(2026, 9, 17), (await world.Service().CreateDraftAsync(request, Metadata("k-1"))).Detail.Draft.BusinessDate);
        }
        else
        {
            var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(request, Metadata("k-1")));
            Assert.Equal((422, SalesDraftProblemCodesV1.BusinessDateBeforeBusinessDay), (refused.Status, Assert.Single(refused.Errors).Code));
        }
    }

    [Fact]
    public async Task Order_business_day_that_cannot_be_resolved_is_503_and_nothing_is_stored()
    {
        // The service's single seam: the business day read fails the way an unresolvable Europe/Istanbul zone fails.
        var world = new World { Clock = QuoteClock() };
        world.Clock = new ZoneUnavailableClock();

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Request(), Metadata("k-1")));

        Assert.Equal((503, SalesQuoteProblemCodesV1.BusinessDayUnavailable), (refused.Status, refused.Code));
        Assert.Empty(world.Store.Envelopes);
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task Quote_conversion_refuses_an_order_date_before_the_servers_day_unrecorded_and_converts_today()
    {
        var world = new World { Clock = QuoteClock() };
        var request = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"))).Detail.Request.Id;
        await world.Service().ApproveRequestAsync(request, Decision("1"), Metadata("r-1-approve"));
        var id = (await world.Service().CreateQuoteAsync(QuoteOf([request]), Metadata("q-1"))).Detail.Quote.Id;

        // The quote's own business date is Day; the order may not be back-dated before the server's day either.
        world.Clock = new MutableClock(new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero));
        var past = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("1", Day), Metadata("q-convert")));
        Assert.Equal((422, "validation_failed", "businessDate", SalesDraftProblemCodesV1.BusinessDateBeforeBusinessDay),
            (past.Status, past.Code, Assert.Single(past.Errors).Field, past.Errors[0].Code));
        Assert.Empty(world.Store.Envelopes);
        Assert.Empty(world.Store.Refusals);
        Assert.Equal("Open", (await world.Service().GetQuoteDetailAsync(id)).Quote.State);

        // The same key converts with the server's day.
        var converted = await world.Service().ConvertQuoteAsync(id,
            new SalesQuoteConversionRequest("1", new DateOnly(2026, 9, 25)), Metadata("q-convert"));
        Assert.Equal(new DateOnly(2026, 9, 25), converted.Detail.Draft.BusinessDate);
    }

    [Fact]
    public async Task A_cancelled_converted_order_frees_its_request_for_a_new_quote_that_converts_again_one_order_per_quote_still_holds()
    {
        var world = new World { Clock = QuoteClock() };
        Owners(world);
        var request = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"))).Detail.Request.Id;
        await world.Service().ApproveRequestAsync(request, Decision("1"), Metadata("r-1-approve"));
        // A price-less quote: the order's discount approval (SAT-D43) is not what this test is about.
        var first = (await world.Service().CreateQuoteAsync(Unpriced(request), Metadata("q-1"))).Detail.Quote.Id;
        var order = (await world.Service().ConvertQuoteAsync(first, new SalesQuoteConversionRequest("1", Day), Metadata("q-1-convert"))).Detail.Draft;

        // While the order lives the request stays covered: no second quote (No 163).
        var covered = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateQuoteAsync(QuoteOf([request]), Metadata("q-2-early")));
        Assert.Equal((422, SalesQuoteProblemCodesV1.RequestUnavailable), (covered.Status, covered.Code));

        // SAT-D123: a converted order carries no due terms; they are entered by a header revision before the approval.
        var withTerms = (await world.Service().ReviseHeaderAsync(order.Id, new SalesDraftHeaderRevisionRequest(order.Version, "SENTETIK vade",
            order.PartyId, order.CompanyAccountId, order.CurrencyId, null, null, null, TestDueTerms.PointDue30Wire), Metadata("o-terms"))).Detail.Draft;
        var submitted = (await world.Service().SubmitDraftAsync(order.Id, Command(withTerms.Version), Metadata("o-submit"))).Detail.Draft;
        var approved = (await world.Service().ApproveDraftAsync(order.Id, Command(submitted.Version), Metadata("o-approve"))).Detail.Draft;
        var preview = await world.Service().GetCancellationImpactAsync(order.Id);
        await world.Service().CancelDraftAsync(order.Id, new SalesDraftCancelRequest(approved.Version, "SENTETIK müşteri vazgeçti",
            preview.ImpactToken), Metadata("o-cancel"));

        // SAT-D72: the request stays open (still Approved) and a NEW quote covers it and converts into a new order.
        Assert.Equal("Approved", (await world.Service().GetRequestDetailAsync(request)).Request.State);
        var second = (await world.Service().CreateQuoteAsync(Unpriced(request), Metadata("q-2"))).Detail.Quote.Id;
        var reordered = (await world.Service().ConvertQuoteAsync(second, new SalesQuoteConversionRequest("1", Day), Metadata("q-2-convert"))).Detail.Draft;
        Assert.NotEqual(order.Id, reordered.Id);
        Assert.Equal(("Converted", (Guid?)reordered.Id), ((await world.Service().GetQuoteDetailAsync(second)).Quote.State,
            (await world.Service().GetQuoteDetailAsync(second)).Quote.ConvertedDraftId));

        // No 162 unchanged: the first quote stays Converted with its cancelled order and never converts a second time.
        var firstQuote = (await world.Service().GetQuoteDetailAsync(first)).Quote;
        Assert.Equal(("Converted", (Guid?)order.Id), (firstQuote.State, firstQuote.ConvertedDraftId));
        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ConvertQuoteAsync(first, new SalesQuoteConversionRequest(firstQuote.Version, Day), Metadata("q-1-convert-again")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (again.Status, again.Code));
    }

    private static CreateSalesQuoteRequest Unpriced(Guid request) =>
        QuoteOf([request]) with { Lines = [new SalesQuoteLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "1.25")] };

    /// <summary>A server clock whose business-day read fails as an unresolvable time zone does (no UTC stands in).</summary>
    private sealed class ZoneUnavailableClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new SalesBusinessDayUnavailableException();
    }
}

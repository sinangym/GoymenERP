using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-S13 (URT-D47, URT-D65) at the service boundary: a month's scrap price is recorded once under the write
/// permission with the server's company, actor and clock, the same key replays the first answer, a recorded month is a
/// conflict that records nothing, and a request outside the grammar is refused before the authority is asked. The year
/// list is the resolved company's only and is answered only while the read permission still holds after the read.
/// No price value is chosen here; every value is a synthetic test figure.
/// </summary>
public sealed partial class ManufacturingCommandServiceTests
{
    private const string ScrapPriceWrite = "manufacturing.scrap-prices.write";
    private const string ScrapPriceRead = "manufacturing.scrap-prices.read";

    private static (Fixture Fixture, FakeScrapPrices Prices, ManufacturingCommandService Service) ScrapPriceCase(
        bool withStore = true)
    {
        var fixture = new Fixture();
        var prices = new FakeScrapPrices(fixture.Store);
        var service = new ManufacturingCommandService(fixture.Store, fixture.Store, fixture.Store,
            new ManufacturingMassVerifier(fixture.Settings), fixture.Authority, fixture.CompanyContext, fixture.User,
            fixture.Correlation, new FixedTime(), scrapPrices: withStore ? prices : null);
        return (fixture, prices, service);
    }

    [Fact]
    public async Task A_month_scrap_price_is_recorded_once_with_the_server_actor_and_clock_and_the_key_replays_it()
    {
        var (fixture, prices, service) = ScrapPriceCase();
        var request = new RecordMonthlyScrapPriceRequestV1(2026, 11, "7.2500");

        var first = await service.RecordMonthlyScrapPriceAsync(request, "scrap-key-1", ScrapPriceWrite,
            CancellationToken.None);
        var replay = await service.RecordMonthlyScrapPriceAsync(request, "scrap-key-1", ScrapPriceWrite,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Created, false), (first.Status, first.IsReplay));
        Assert.Equal((ManufacturingApiStatus.Created, true), (replay.Status, replay.IsReplay));
        var view = Assert.IsType<MonthlyScrapPriceViewV1>(first.Resource);
        // The price travels as lossless text without trailing zeros; the actor and the instant are the server's.
        Assert.Equal((2026, 11, "7.25", Actor.ToString("D"), At),
            (view.Year, view.Month, view.TlPerKg, view.RecordedBy, view.RecordedAtUtc));
        Assert.Equal(view, replay.Resource);
        var recorded = Assert.Single(prices.Recorded);
        Assert.Equal((Company, new ScrapPricePeriod(2026, 11), 7.25m),
            (recorded.CompanyId, recorded.Period, recorded.Price.Value));
        Assert.Single(fixture.Store.Commands);
        Assert.Equal(ScrapPriceWrite, fixture.Authority.Permissions[0]);
    }

    [Fact]
    public async Task The_same_key_with_another_price_is_an_idempotency_conflict_and_records_nothing_more()
    {
        var (fixture, prices, service) = ScrapPriceCase();
        await service.RecordMonthlyScrapPriceAsync(new(2026, 11, "7.25"), "scrap-key-1", ScrapPriceWrite,
            CancellationToken.None);

        var other = await service.RecordMonthlyScrapPriceAsync(new(2026, 11, "7.5"), "scrap-key-1", ScrapPriceWrite,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingApiCodes.IdempotencyConflict),
            (other.Status, other.Code));
        Assert.Single(prices.Recorded);
        Assert.Single(fixture.Store.Commands);
    }

    [Fact]
    public async Task A_month_that_already_has_a_price_is_a_conflict_that_records_nothing_and_a_retry_is_decided_again()
    {
        var (fixture, prices, service) = ScrapPriceCase();
        prices.AlreadyRecorded = true;

        var refused = await service.RecordMonthlyScrapPriceAsync(new(2026, 11, "7.25"), "scrap-key-1",
            ScrapPriceWrite, CancellationToken.None);
        var retried = await service.RecordMonthlyScrapPriceAsync(new(2026, 11, "7.25"), "scrap-key-1",
            ScrapPriceWrite, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingRuleCodes.ScrapPriceAlreadyRecorded, false),
            (refused.Status, refused.Code, refused.IsReplay));
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingRuleCodes.ScrapPriceAlreadyRecorded, false),
            (retried.Status, retried.Code, retried.IsReplay));
        // Not a terminal answer: nothing is recorded, so the same key is decided again and the store is asked twice.
        Assert.Equal(2, prices.Asked);
        Assert.Empty(prices.Recorded);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task A_fifth_decimal_is_refused_as_the_precision_rule_and_never_rounded()
    {
        var (fixture, prices, service) = ScrapPriceCase();

        var refused = await service.RecordMonthlyScrapPriceAsync(new(2026, 11, "7.12345"), "scrap-key-1",
            ScrapPriceWrite, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.ScrapPricePrecisionExceeded),
            (refused.Status, refused.Code));
        Assert.Equal(0, prices.Asked);
        Assert.Empty(fixture.Store.Commands);
    }

    [Theory]
    [InlineData(2026, 0, "7.25", "month")]
    [InlineData(2026, 13, "7.25", "month")]
    [InlineData(0, 11, "7.25", "year")]
    [InlineData(10000, 11, "7.25", "year")]
    [InlineData(2026, 11, null, "tlPerKg")]
    [InlineData(2026, 11, "", "tlPerKg")]
    [InlineData(2026, 11, "0", "tlPerKg")]
    [InlineData(2026, 11, "0.0000", "tlPerKg")]
    [InlineData(2026, 11, "-1", "tlPerKg")]
    [InlineData(2026, 11, "7,25", "tlPerKg")]
    [InlineData(2026, 11, "7.25e1", "tlPerKg")]
    [InlineData(2026, 11, " 7.25", "tlPerKg")]
    [InlineData(2026, 11, "100000000000000", "tlPerKg")]
    public async Task A_request_outside_the_grammar_is_refused_on_its_field_before_the_authority_is_asked(
        int year, int month, string? tlPerKg, string field)
    {
        var (fixture, prices, service) = ScrapPriceCase();

        var refused = await service.RecordMonthlyScrapPriceAsync(new(year, month, tlPerKg), "scrap-key-1",
            ScrapPriceWrite, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, field), (refused.Status, refused.Field));
        Assert.Equal(0, fixture.Authority.Asked);
        Assert.Equal(0, prices.Asked);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task A_missing_request_is_refused_on_request()
    {
        var (fixture, _, service) = ScrapPriceCase();

        var refused = await service.RecordMonthlyScrapPriceAsync(null, "scrap-key-1", ScrapPriceWrite,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "request"), (refused.Status, refused.Field));
        Assert.Equal(0, fixture.Authority.Asked);
    }

    [Fact]
    public async Task Without_the_write_grant_nothing_is_asked_or_recorded()
    {
        var (fixture, prices, service) = ScrapPriceCase();
        fixture.Authority.Scope = PermissionScope.Denied;

        var refused = await service.RecordMonthlyScrapPriceAsync(new(2026, 11, "7.25"), "scrap-key-1",
            ScrapPriceWrite, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Denied, refused.Status);
        Assert.Equal(0, prices.Asked);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task Without_the_scrap_price_store_the_record_is_a_retryable_503_and_nothing_is_recorded()
    {
        var (fixture, _, service) = ScrapPriceCase(withStore: false);

        var refused = await service.RecordMonthlyScrapPriceAsync(new(2026, 11, "7.25"), "scrap-key-1",
            ScrapPriceWrite, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Unavailable, refused.Status);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task The_year_list_answers_the_company_months_and_measures_the_read_grant_after_the_read()
    {
        var (fixture, prices, service) = ScrapPriceCase();
        prices.Listed.Add(Price(Company, 2026, 11, 7.25m));
        prices.Listed.Add(Price(Company, 2026, 10, 6.5m));

        var result = await service.ListMonthlyScrapPricesAsync(2026, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var list = result.Resource!;
        Assert.Equal(2026, list.Year);
        Assert.Equal([(11, "7.25"), (10, "6.5")], list.Items.Select(item => (item.Month, item.TlPerKg)));
        Assert.Equal([2026], prices.ListCalls);
        Assert.Equal((ScrapPriceRead, Company), fixture.Authority.LastAsked);
        Assert.Equal(1, fixture.Authority.Asked);
    }

    [Fact]
    public async Task An_empty_year_is_an_empty_list_not_a_zero_price()
    {
        var (_, _, service) = ScrapPriceCase();

        var result = await service.ListMonthlyScrapPricesAsync(2026, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Empty(result.Resource!.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(10000)]
    public async Task A_list_without_a_valid_year_is_refused_on_year_before_the_store_is_asked(int? year)
    {
        var (fixture, prices, service) = ScrapPriceCase();

        var result = await service.ListMonthlyScrapPricesAsync(year, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "year"), (result.Status, result.Field));
        Assert.Empty(prices.ListCalls);
        Assert.Equal(0, fixture.Authority.Asked);
    }

    [Fact]
    public async Task A_list_without_the_store_is_a_retryable_503_never_an_empty_year()
    {
        var (_, _, service) = ScrapPriceCase(withStore: false);

        var result = await service.ListMonthlyScrapPricesAsync(2026, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, (MonthlyScrapPriceListViewV1?)null),
            (result.Status, result.Resource));
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("other-year")]
    public async Task A_list_holding_a_row_outside_the_company_or_the_year_answers_nothing(string fault)
    {
        var (_, prices, service) = ScrapPriceCase();
        prices.Listed.Add(Price(Company, 2026, 11, 7.25m));
        prices.Listed.Add(fault == "other-company"
            ? Price(OtherCompany, 2026, 10, 6.5m)
            : Price(Company, 2025, 10, 6.5m));

        var result = await service.ListMonthlyScrapPricesAsync(2026, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, (MonthlyScrapPriceListViewV1?)null),
            (result.Status, result.Resource));
    }

    [Fact]
    public async Task A_read_grant_lost_during_the_read_answers_denied_not_the_list()
    {
        var (fixture, prices, service) = ScrapPriceCase();
        prices.Listed.Add(Price(Company, 2026, 11, 7.25m));
        fixture.Authority.Scope = PermissionScope.Denied;

        var result = await service.ListMonthlyScrapPricesAsync(2026, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, (MonthlyScrapPriceListViewV1?)null),
            (result.Status, result.Resource));
        Assert.Equal([2026], prices.ListCalls);
    }

    private static MonthlyScrapPrice Price(Guid company, int year, int month, decimal tlPerKg) =>
        MonthlyScrapPrice.Record(Guid.NewGuid(), company, new ScrapPricePeriod(year, month),
            new ScrapPriceTlPerKg(tlPerKg), Actor.ToString("D"), At);

    /// <summary>
    /// The scrap price port as the store keeps it: the price row and the command record join one write (the completion's
    /// record goes to the shared fake command log), and a month already priced is refused before anything is written.
    /// </summary>
    private sealed class FakeScrapPrices(FakeStore store) : IManufacturingMonthlyScrapPriceStore
    {
        public bool AlreadyRecorded { get; set; }

        public int Asked { get; private set; }

        public List<MonthlyScrapPrice> Recorded { get; } = [];

        public List<MonthlyScrapPrice> Listed { get; } = [];

        public List<int> ListCalls { get; } = [];

        public Task<MonthlyScrapPrice> RecordScrapPriceAsync(MonthlyScrapPrice price, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Asked++;
            if (AlreadyRecorded || Recorded.Any(recorded => recorded.Period == price.Period))
            {
                throw new ManufacturingRuleException(ManufacturingRuleCodes.ScrapPriceAlreadyRecorded,
                    "sentetik ay zaten fiyatlı");
            }

            Recorded.Add(price);
            if (metadata.Completion is { } completion)
            {
                var record = completion(price);
                store.Commands.Add((record.CommandName, record.IdempotencyKeyHash), record);
            }

            return Task.FromResult(price);
        }

        public Task<IReadOnlyList<MonthlyScrapPrice>> ListScrapPricesAsync(int year,
            CancellationToken cancellationToken = default)
        {
            ListCalls.Add(year);
            return Task.FromResult<IReadOnlyList<MonthlyScrapPrice>>([.. Listed]);
        }
    }
}

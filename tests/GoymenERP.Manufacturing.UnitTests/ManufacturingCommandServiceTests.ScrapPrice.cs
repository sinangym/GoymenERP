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
/// URT-D72: a correction is the next version with a reason under the same write permission, a stale version is a
/// terminal conflict, and the same price, a month without a price and a future month (URT-D73) write nothing.
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
        var request = new RecordMonthlyScrapPriceRequestV1(2026, 9, "7.2500");

        var first = await service.RecordMonthlyScrapPriceAsync(request, "scrap-key-1", ScrapPriceWrite,
            CancellationToken.None);
        var replay = await service.RecordMonthlyScrapPriceAsync(request, "scrap-key-1", ScrapPriceWrite,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Created, false), (first.Status, first.IsReplay));
        Assert.Equal((ManufacturingApiStatus.Created, true), (replay.Status, replay.IsReplay));
        var view = Assert.IsType<MonthlyScrapPriceViewV1>(first.Resource);
        // The price travels as lossless text without trailing zeros; the actor and the instant are the server's.
        Assert.Equal((2026, 9, "7.25", Actor.ToString("D"), At),
            (view.Year, view.Month, view.TlPerKg, view.RecordedBy, view.RecordedAtUtc));
        Assert.Equal(view, replay.Resource);
        var recorded = Assert.Single(prices.Recorded);
        Assert.Equal((Company, new ScrapPricePeriod(2026, 9), 7.25m),
            (recorded.CompanyId, recorded.Period, recorded.Price.Value));
        Assert.Single(fixture.Store.Commands);
        Assert.Equal(ScrapPriceWrite, fixture.Authority.Permissions[0]);
    }

    [Fact]
    public async Task The_same_key_with_another_price_is_an_idempotency_conflict_and_records_nothing_more()
    {
        var (fixture, prices, service) = ScrapPriceCase();
        await service.RecordMonthlyScrapPriceAsync(new(2026, 9, "7.25"), "scrap-key-1", ScrapPriceWrite,
            CancellationToken.None);

        var other = await service.RecordMonthlyScrapPriceAsync(new(2026, 9, "7.5"), "scrap-key-1", ScrapPriceWrite,
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

        var refused = await service.RecordMonthlyScrapPriceAsync(new(2026, 9, "7.25"), "scrap-key-1",
            ScrapPriceWrite, CancellationToken.None);
        var retried = await service.RecordMonthlyScrapPriceAsync(new(2026, 9, "7.25"), "scrap-key-1",
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

        var refused = await service.RecordMonthlyScrapPriceAsync(new(2026, 9, "7.12345"), "scrap-key-1",
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
    [InlineData(2026, 9, null, "tlPerKg")]
    [InlineData(2026, 9, "", "tlPerKg")]
    [InlineData(2026, 9, "0", "tlPerKg")]
    [InlineData(2026, 9, "0.0000", "tlPerKg")]
    [InlineData(2026, 9, "-1", "tlPerKg")]
    [InlineData(2026, 9, "7,25", "tlPerKg")]
    [InlineData(2026, 9, "7.25e1", "tlPerKg")]
    [InlineData(2026, 9, " 7.25", "tlPerKg")]
    [InlineData(2026, 9, "100000000000000", "tlPerKg")]
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

        var refused = await service.RecordMonthlyScrapPriceAsync(new(2026, 9, "7.25"), "scrap-key-1",
            ScrapPriceWrite, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Denied, refused.Status);
        Assert.Equal(0, prices.Asked);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task Without_the_scrap_price_store_the_record_is_a_retryable_503_and_nothing_is_recorded()
    {
        var (fixture, _, service) = ScrapPriceCase(withStore: false);

        var refused = await service.RecordMonthlyScrapPriceAsync(new(2026, 9, "7.25"), "scrap-key-1",
            ScrapPriceWrite, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Unavailable, refused.Status);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task The_year_list_answers_the_company_months_and_measures_the_read_grant_after_the_read()
    {
        var (fixture, prices, service) = ScrapPriceCase();
        prices.Listed.Add(Price(Company, 2026, 9, 7.25m));
        prices.Listed.Add(Price(Company, 2026, 8, 6.5m));

        var result = await service.ListMonthlyScrapPricesAsync(2026, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var list = result.Resource!;
        Assert.Equal(2026, list.Year);
        Assert.Equal([(9, "7.25"), (8, "6.5")], list.Items.Select(item => (item.Month, item.TlPerKg)));
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
        prices.Listed.Add(Price(Company, 2026, 9, 7.25m));
        prices.Listed.Add(fault == "other-company"
            ? Price(OtherCompany, 2026, 8, 6.5m)
            : Price(Company, 2025, 10, 6.5m));

        var result = await service.ListMonthlyScrapPricesAsync(2026, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, (MonthlyScrapPriceListViewV1?)null),
            (result.Status, result.Resource));
    }

    [Fact]
    public async Task A_read_grant_lost_during_the_read_answers_denied_not_the_list()
    {
        var (fixture, prices, service) = ScrapPriceCase();
        prices.Listed.Add(Price(Company, 2026, 9, 7.25m));
        fixture.Authority.Scope = PermissionScope.Denied;

        var result = await service.ListMonthlyScrapPricesAsync(2026, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, (MonthlyScrapPriceListViewV1?)null),
            (result.Status, result.Resource));
        Assert.Equal([2026], prices.ListCalls);
    }

    [Fact]
    public async Task A_month_after_the_business_day_is_refused_as_a_future_month_and_records_nothing()
    {
        var (fixture, prices, service) = ScrapPriceCase();

        // URT-D73: the clock is 2026-09-18 in Europe/Istanbul, so October is a future month.
        var refused = await service.RecordMonthlyScrapPriceAsync(new(2026, 10, "7.25"), "scrap-key-1",
            ScrapPriceWrite, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.ScrapPriceFutureMonth),
            (refused.Status, refused.Code));
        Assert.Equal(0, prices.Asked);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task A_correction_is_the_next_version_with_its_reason_and_the_key_replays_it()
    {
        var (fixture, prices, service) = ScrapPriceCase();
        prices.Recorded.Add(Price(Company, 2026, 9, 7.25m));
        var request = new CorrectMonthlyScrapPriceRequestV1(1, "7.5000", "  sentetik düzeltme  ");

        var first = await service.CorrectMonthlyScrapPriceAsync(2026, 9, request, "scrap-fix-1", ScrapPriceWrite,
            CancellationToken.None);
        var replay = await service.CorrectMonthlyScrapPriceAsync(2026, 9, request, "scrap-fix-1", ScrapPriceWrite,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, false), (first.Status, first.IsReplay));
        Assert.Equal((ManufacturingApiStatus.Ok, true), (replay.Status, replay.IsReplay));
        var view = Assert.IsType<MonthlyScrapPriceViewV1>(first.Resource);
        // URT-D72: the new version carries the trimmed reason; the actor and the instant are the server's.
        Assert.Equal((2026, 9, "7.5", 2, "sentetik düzeltme", Actor.ToString("D"), At),
            (view.Year, view.Month, view.TlPerKg, view.Version, view.Reason, view.RecordedBy, view.RecordedAtUtc));
        Assert.Equal(view, replay.Resource);
        // The old version stays beside the new one.
        Assert.Equal([(1, 7.25m), (2, 7.5m)], prices.Recorded.Select(price => (price.Version, price.Price.Value)));
        Assert.Equal(1, prices.CorrectAsked);
        Assert.Single(fixture.Store.Commands);
        Assert.Equal(ScrapPriceWrite, fixture.Authority.Permissions[0]);
    }

    [Fact]
    public async Task A_stale_correction_is_a_terminal_version_conflict_that_writes_no_version()
    {
        var (fixture, prices, service) = ScrapPriceCase();
        prices.Recorded.Add(Price(Company, 2026, 9, 7.25m));
        var request = new CorrectMonthlyScrapPriceRequestV1(3, "7.5", "sentetik düzeltme");

        var first = await service.CorrectMonthlyScrapPriceAsync(2026, 9, request, "scrap-fix-1", ScrapPriceWrite,
            CancellationToken.None);
        var again = await service.CorrectMonthlyScrapPriceAsync(2026, 9, request, "scrap-fix-1", ScrapPriceWrite,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingRuleCodes.VersionConflict, false, 3L, 1L),
            (first.Status, first.Code, first.IsReplay, first.ExpectedVersion, first.CurrentVersion));
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingRuleCodes.VersionConflict, true),
            (again.Status, again.Code, again.IsReplay));
        Assert.Single(prices.Recorded);
        Assert.Equal(1, prices.CorrectAsked);
        Assert.Single(fixture.Store.Commands);
    }

    [Fact]
    public async Task Correcting_a_month_without_a_price_is_a_not_found_without_a_code()
    {
        var (_, prices, service) = ScrapPriceCase();

        var refused = await service.CorrectMonthlyScrapPriceAsync(2026, 9,
            new CorrectMonthlyScrapPriceRequestV1(1, "7.5", "sentetik düzeltme"), "scrap-fix-1", ScrapPriceWrite,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.NotFound, (string?)null), (refused.Status, refused.Code));
        Assert.Empty(prices.Recorded);
    }

    [Fact]
    public async Task Correcting_to_the_same_price_is_refused_as_unchanged_and_writes_no_version()
    {
        var (fixture, prices, service) = ScrapPriceCase();
        prices.Recorded.Add(Price(Company, 2026, 9, 7.25m));

        var refused = await service.CorrectMonthlyScrapPriceAsync(2026, 9,
            new CorrectMonthlyScrapPriceRequestV1(1, "7.2500", "sentetik düzeltme"), "scrap-fix-1", ScrapPriceWrite,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.ScrapPriceUnchanged),
            (refused.Status, refused.Code));
        Assert.Single(prices.Recorded);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task Correcting_a_stored_future_month_is_refused_as_a_future_month()
    {
        var (_, prices, service) = ScrapPriceCase();
        prices.Recorded.Add(MonthlyScrapPrice.Restore(Guid.NewGuid(), Company, 2026, 10, 7.25m,
            MonthlyScrapPrice.FirstVersion, null, Actor.ToString("D"), At));

        var refused = await service.CorrectMonthlyScrapPriceAsync(2026, 10,
            new CorrectMonthlyScrapPriceRequestV1(1, "7.5", "sentetik düzeltme"), "scrap-fix-1", ScrapPriceWrite,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.ScrapPriceFutureMonth),
            (refused.Status, refused.Code));
        Assert.Single(prices.Recorded);
    }

    [Theory]
    [InlineData(2026, 0, 1, "7.5", "sentetik", "month")]
    [InlineData(2026, 13, 1, "7.5", "sentetik", "month")]
    [InlineData(0, 9, 1, "7.5", "sentetik", "year")]
    [InlineData(2026, 9, 0, "7.5", "sentetik", "expectedVersion")]
    [InlineData(2026, 9, 1, null, "sentetik", "tlPerKg")]
    [InlineData(2026, 9, 1, "0", "sentetik", "tlPerKg")]
    [InlineData(2026, 9, 1, "-1", "sentetik", "tlPerKg")]
    [InlineData(2026, 9, 1, "7,5", "sentetik", "tlPerKg")]
    [InlineData(2026, 9, 1, "7.5", null, "reason")]
    [InlineData(2026, 9, 1, "7.5", "", "reason")]
    [InlineData(2026, 9, 1, "7.5", "   ", "reason")]
    public async Task A_correction_outside_the_grammar_is_refused_on_its_field_before_the_authority_is_asked(
        int year, int month, int expectedVersion, string? tlPerKg, string? reason, string field)
    {
        var (fixture, prices, service) = ScrapPriceCase();

        var refused = await service.CorrectMonthlyScrapPriceAsync(year, month,
            new CorrectMonthlyScrapPriceRequestV1(expectedVersion, tlPerKg, reason), "scrap-fix-1", ScrapPriceWrite,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, field), (refused.Status, refused.Field));
        Assert.Equal(0, fixture.Authority.Asked);
        Assert.Equal(0, prices.CorrectAsked);
    }

    [Fact]
    public async Task A_correction_reason_longer_than_the_bound_is_refused_on_reason()
    {
        var (fixture, _, service) = ScrapPriceCase();

        var refused = await service.CorrectMonthlyScrapPriceAsync(2026, 9,
            new CorrectMonthlyScrapPriceRequestV1(1, "7.5", new string('x', MonthlyScrapPrice.ReasonMaximumLength + 1)),
            "scrap-fix-1", ScrapPriceWrite, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "reason"), (refused.Status, refused.Field));
        Assert.Equal(0, fixture.Authority.Asked);
    }

    [Fact]
    public async Task A_correction_without_the_write_grant_or_the_store_writes_nothing()
    {
        var (deniedFixture, deniedPrices, deniedService) = ScrapPriceCase();
        deniedFixture.Authority.Scope = PermissionScope.Denied;
        var (_, _, storeless) = ScrapPriceCase(withStore: false);
        var request = new CorrectMonthlyScrapPriceRequestV1(1, "7.5", "sentetik düzeltme");

        var denied = await deniedService.CorrectMonthlyScrapPriceAsync(2026, 9, request, "scrap-fix-1",
            ScrapPriceWrite, CancellationToken.None);
        var unavailable = await storeless.CorrectMonthlyScrapPriceAsync(2026, 9, request, "scrap-fix-1",
            ScrapPriceWrite, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Denied, denied.Status);
        Assert.Equal(0, deniedPrices.CorrectAsked);
        Assert.Empty(deniedFixture.Store.Commands);
        Assert.Equal(ManufacturingApiStatus.Unavailable, unavailable.Status);
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

        public int CorrectAsked { get; private set; }

        public Task<MonthlyScrapPrice> CorrectScrapPriceAsync(Guid correctionId, ScrapPricePeriod period,
            int expectedVersion, ScrapPriceTlPerKg price, string reason, ManufacturingCommandMetadata metadata,
            DateTimeOffset correctedAtUtc, CancellationToken cancellationToken = default)
        {
            CorrectAsked++;
            var current = Recorded.Where(recorded => recorded.Period == period).MaxBy(recorded => recorded.Version)
                ?? throw new ManufacturingStoreException(ManufacturingStoreCodes.ScrapPriceNotFound, "yok");
            var corrected = current.Correct(correctionId, expectedVersion, price, reason, metadata.ActorId,
                correctedAtUtc);
            Recorded.Add(corrected);
            if (metadata.Completion is { } completion)
            {
                var record = completion(corrected);
                store.Commands.Add((record.CommandName, record.IdempotencyKeyHash), record);
            }

            return Task.FromResult(corrected);
        }

        public Task<IReadOnlyList<MonthlyScrapPrice>> ListScrapPricesAsync(int year,
            CancellationToken cancellationToken = default)
        {
            ListCalls.Add(year);
            return Task.FromResult<IReadOnlyList<MonthlyScrapPrice>>([.. Listed]);
        }
    }
}

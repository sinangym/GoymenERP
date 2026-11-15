using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-S13 (URT-D47, URT-D65) at the store on a REAL SQL Server: a month's scrap price is stored once with its audit event,
/// listed for its own year and company only, never replaced (also when two writers race for the same month), read for the
/// cost calculator as a value or as missing, and the migration's Down passes while no price exists and refuses (51387)
/// once one is stored. Every price here is synthetic; no price value is chosen.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_monthly_scrap_price_is_stored_once_with_its_audit_event_and_listed_for_its_own_year_only()
    {
        await using var run = await Run.StartAsync();
        var november = ScrapPriceOf(CompanyA, 2026, 11, 7.25m, run);
        var october = ScrapPriceOf(CompanyA, 2026, 10, 6.5m, run);
        var nextYear = ScrapPriceOf(CompanyA, 2027, 1, 8m, run);
        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(november.Id, (await store.ScrapPrices.RecordScrapPriceAsync(november, Metadata(run))).Id);
            await store.ScrapPrices.RecordScrapPriceAsync(october, Metadata(run));
            await store.ScrapPrices.RecordScrapPriceAsync(nextYear, Metadata(run));
        }

        await using (var store = run.Store(CompanyA))
        {
            var listed = await store.ScrapPrices.ListScrapPricesAsync(2026);

            Assert.Equal(new[] { 11, 10 }, listed.Select(price => price.Period.Month).ToArray());
            Assert.Equal((7.25m, run.Actor.ToString("D")), (listed[0].Price.Value, listed[0].RecordedBy));
            Assert.Empty(await store.ScrapPrices.ListScrapPricesAsync(2025));
        }

        Assert.Equal(7.25m, await run.ScalarAsync<decimal>(
            "SELECT TlPerKg FROM manufacturing.MonthlyScrapPrices WHERE Id = @p", ("@p", november.Id)));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.scrap-price.recorded.v1'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.scrap-price.recorded.v1' "
            + "AND EntityId = @p", ("@p", november.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task A_second_price_for_a_recorded_month_is_refused_and_the_first_price_stands()
    {
        await using var run = await Run.StartAsync();
        var first = ScrapPriceOf(CompanyA, 2026, 11, 7.25m, run);
        await using (var store = run.Store(CompanyA))
        {
            await store.ScrapPrices.RecordScrapPriceAsync(first, Metadata(run));
        }

        await using (var store = run.Store(CompanyA))
        {
            var refused = await Assert.ThrowsAsync<ManufacturingRuleException>(() =>
                store.ScrapPrices.RecordScrapPriceAsync(ScrapPriceOf(CompanyA, 2026, 11, 9m, run), Metadata(run)));
            Assert.Equal(ManufacturingRuleCodes.ScrapPriceAlreadyRecorded, refused.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.MonthlyScrapPrices"));
        Assert.Equal(7.25m, await run.ScalarAsync<decimal>("SELECT TlPerKg FROM manufacturing.MonthlyScrapPrices"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.scrap-price.recorded.v1'"));

        // The same month of another company is that company's own month.
        await using (var store = run.Store(CompanyB))
        {
            await store.ScrapPrices.RecordScrapPriceAsync(ScrapPriceOf(CompanyB, 2026, 11, 9m, run), Metadata(run));
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.MonthlyScrapPrices"));
    }

    [SqlServerFact]
    public async Task Two_writers_that_both_found_the_month_empty_cannot_both_record_its_price()
    {
        await using var run = await Run.StartAsync();

        // Both writers find the month without a price, then wait for each other at the save.
        var barrier = new Barrier(2);
        async Task<object> WriteAsync(decimal tlPerKg)
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return await writer.ScrapPrices.RecordScrapPriceAsync(ScrapPriceOf(CompanyA, 2026, 11, tlPerKg, run),
                    Metadata(run));
            }
            catch (ManufacturingRuleException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => WriteAsync(7.25m)), Task.Run(() => WriteAsync(9m)));

        Assert.Single(results, result => result is MonthlyScrapPrice);
        Assert.Single(results,
            result => result is string code && code == ManufacturingRuleCodes.ScrapPriceAlreadyRecorded);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.MonthlyScrapPrices"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.scrap-price.recorded.v1'"));
    }

    [SqlServerFact]
    public async Task The_cost_reads_its_own_companys_month_as_a_value_or_as_missing_and_never_another_companys()
    {
        await using var run = await Run.StartAsync();
        await using (var store = run.Store(CompanyB))
        {
            await store.ScrapPrices.RecordScrapPriceAsync(ScrapPriceOf(CompanyB, 2026, 11, 9m, run), Metadata(run));
        }

        await using (var store = run.Store(CompanyA))
        {
            // URT-D47 (2): a month without a price is missing, never a zero, also when another company has one.
            Assert.Equal(ScrapPriceInput.Missing,
                await store.ScrapPriceReader.ReadScrapPriceAsync(CompanyA, new ScrapPricePeriod(2026, 11)));
            Assert.Empty(await store.ScrapPrices.ListScrapPricesAsync(2026));

            // Another company's month is not read through this company's context.
            Assert.Equal(new ScrapPriceInput(ScrapPriceReadStatus.Unreadable, null),
                await store.ScrapPriceReader.ReadScrapPriceAsync(CompanyB, new ScrapPricePeriod(2026, 11)));

            await store.ScrapPrices.RecordScrapPriceAsync(ScrapPriceOf(CompanyA, 2026, 11, 7.25m, run), Metadata(run));
        }

        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(new ScrapPriceInput(ScrapPriceReadStatus.Value, 7.25m),
                await store.ScrapPriceReader.ReadScrapPriceAsync(CompanyA, new ScrapPricePeriod(2026, 11)));
            Assert.Equal(ScrapPriceInput.Missing,
                await store.ScrapPriceReader.ReadScrapPriceAsync(CompanyA, new ScrapPricePeriod(2026, 12)));

            // A price stored for another company is refused at the store, before anything is written.
            var refused = await Assert.ThrowsAsync<ManufacturingStoreException>(() =>
                store.ScrapPrices.RecordScrapPriceAsync(ScrapPriceOf(CompanyB, 2026, 12, 9m, run), Metadata(run)));
            Assert.Equal(ManufacturingStoreCodes.CompanyContextMismatch, refused.Code);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.MonthlyScrapPrices"));
    }

    [SqlServerFact]
    public async Task The_scrap_price_migrations_Down_passes_with_no_price_and_refuses_while_a_price_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddManufacturingMonthlyScrapPrices().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + "; SELECT 1");

        await using (var store = run.Store(CompanyA))
        {
            await store.ScrapPrices.RecordScrapPriceAsync(ScrapPriceOf(CompanyA, 2026, 11, 7.25m, run), Metadata(run));
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(51387, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.MonthlyScrapPrices"));
    }

    private static MonthlyScrapPrice ScrapPriceOf(Guid company, int year, int month, decimal tlPerKg, Run run) =>
        MonthlyScrapPrice.Record(Guid.NewGuid(), company, new ScrapPricePeriod(year, month),
            new ScrapPriceTlPerKg(tlPerKg), run.Actor.ToString("D"), At);
}

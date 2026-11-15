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
/// once one is stored. URT-D72: a correction is the next version with its reason and its audit event, the old version
/// stays, the list and the cost read the latest version, a stale or racing correction is a version conflict that writes
/// nothing, the check refuses a version without its reason at the database, and the versions migration's Down refuses
/// (51627) once a correction is stored. Every price here is synthetic; no price value is chosen.
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

    [SqlServerFact]
    public async Task A_scrap_price_correction_is_stored_as_the_next_version_and_the_list_and_the_cost_read_it()
    {
        await using var run = await Run.StartAsync();
        var first = ScrapPriceOf(CompanyA, 2026, 11, 7.25m, run);
        await using (var store = run.Store(CompanyA))
        {
            await store.ScrapPrices.RecordScrapPriceAsync(first, Metadata(run));
        }

        MonthlyScrapPrice corrected;
        await using (var store = run.Store(CompanyA))
        {
            corrected = await store.ScrapPrices.CorrectScrapPriceAsync(Guid.NewGuid(), first.Period,
                MonthlyScrapPrice.FirstVersion, new ScrapPriceTlPerKg(7.5m), "sentetik düzeltme", Metadata(run),
                ScrapPriceEnteredAt);
        }

        Assert.Equal((2, "sentetik düzeltme"), (corrected.Version, corrected.Reason));
        await using (var store = run.Store(CompanyA))
        {
            // URT-D72 (2): the latest version is effective for the list and for a cost that is computed now.
            var listed = Assert.Single(await store.ScrapPrices.ListScrapPricesAsync(2026));
            Assert.Equal((corrected.Id, 2, 7.5m), (listed.Id, listed.Version, listed.Price.Value));
            Assert.Equal(new ScrapPriceInput(ScrapPriceReadStatus.Value, 7.5m),
                await store.ScrapPriceReader.ReadScrapPriceAsync(CompanyA, first.Period));
        }

        // The old version stays as it was; each version has its own audit event.
        Assert.Equal(7.25m, await run.ScalarAsync<decimal>(
            "SELECT TlPerKg FROM manufacturing.MonthlyScrapPrices WHERE Id = @p AND [Version] = 1", ("@p", first.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.MonthlyScrapPrices"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.scrap-price.corrected.v1' "
            + "AND EntityId = @p", ("@p", corrected.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task A_stale_or_missing_scrap_price_correction_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var first = ScrapPriceOf(CompanyA, 2026, 11, 7.25m, run);
        await using (var store = run.Store(CompanyA))
        {
            await store.ScrapPrices.RecordScrapPriceAsync(first, Metadata(run));
        }

        await using (var store = run.Store(CompanyA))
        {
            var stale = await Assert.ThrowsAsync<ManufacturingVersionConflictException>(() =>
                store.ScrapPrices.CorrectScrapPriceAsync(Guid.NewGuid(), first.Period, 2, new ScrapPriceTlPerKg(7.5m),
                    "sentetik düzeltme", Metadata(run), ScrapPriceEnteredAt));
            Assert.Equal((2L, 1L), (stale.ExpectedVersion, stale.CurrentVersion));
        }

        await using (var store = run.Store(CompanyA))
        {
            var missing = await Assert.ThrowsAsync<ManufacturingStoreException>(() =>
                store.ScrapPrices.CorrectScrapPriceAsync(Guid.NewGuid(), new ScrapPricePeriod(2026, 10),
                    MonthlyScrapPrice.FirstVersion, new ScrapPriceTlPerKg(7.5m), "sentetik düzeltme", Metadata(run),
                    ScrapPriceEnteredAt));
            Assert.Equal(ManufacturingStoreCodes.ScrapPriceNotFound, missing.Code);
        }

        // Another company's month is not this company's to correct.
        await using (var store = run.Store(CompanyB))
        {
            var foreign = await Assert.ThrowsAsync<ManufacturingStoreException>(() =>
                store.ScrapPrices.CorrectScrapPriceAsync(Guid.NewGuid(), first.Period, MonthlyScrapPrice.FirstVersion,
                    new ScrapPriceTlPerKg(7.5m), "sentetik düzeltme", Metadata(run), ScrapPriceEnteredAt));
            Assert.Equal(ManufacturingStoreCodes.ScrapPriceNotFound, foreign.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.MonthlyScrapPrices"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.scrap-price.corrected.v1'"));
    }

    [SqlServerFact]
    public async Task Two_correctors_of_the_same_version_cannot_both_store_the_next_version()
    {
        await using var run = await Run.StartAsync();
        var first = ScrapPriceOf(CompanyA, 2026, 11, 7.25m, run);
        await using (var store = run.Store(CompanyA))
        {
            await store.ScrapPrices.RecordScrapPriceAsync(first, Metadata(run));
        }

        // Both correctors read version 1, then wait for each other at the save.
        var barrier = new Barrier(2);
        async Task<object> CorrectAsync(decimal tlPerKg)
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return await writer.ScrapPrices.CorrectScrapPriceAsync(Guid.NewGuid(), first.Period,
                    MonthlyScrapPrice.FirstVersion, new ScrapPriceTlPerKg(tlPerKg), "sentetik düzeltme", Metadata(run),
                    ScrapPriceEnteredAt);
            }
            catch (ManufacturingVersionConflictException exception)
            {
                return exception;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => CorrectAsync(7.5m)), Task.Run(() => CorrectAsync(8m)));

        Assert.Single(results, result => result is MonthlyScrapPrice);
        Assert.Single(results, result => result is ManufacturingVersionConflictException);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.MonthlyScrapPrices WHERE [Version] = 2"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.scrap-price.corrected.v1'"));
    }

    [SqlServerFact]
    public async Task The_scrap_price_version_check_refuses_a_correction_without_a_reason_and_a_first_entry_with_one()
    {
        await using var run = await Run.StartAsync();
        var first = ScrapPriceOf(CompanyA, 2026, 11, 7.25m, run);
        await using (var store = run.Store(CompanyA))
        {
            await store.ScrapPrices.RecordScrapPriceAsync(first, Metadata(run));
        }

        // The check holds the rule at the database too, not only in the domain.
        var withoutReason = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE manufacturing.MonthlyScrapPrices SET [Version] = 2 WHERE Id = @p; SELECT 1", ("@p", first.Id)));
        Assert.Equal(547, withoutReason.Number);
        Assert.Contains("CK_MonthlyScrapPrices_Version", withoutReason.Message, StringComparison.Ordinal);
        var firstWithReason = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE manufacturing.MonthlyScrapPrices SET [Reason] = N'sentetik' WHERE Id = @p; SELECT 1",
            ("@p", first.Id)));
        Assert.Equal(547, firstWithReason.Number);
        Assert.Contains("CK_MonthlyScrapPrices_Version", firstWithReason.Message, StringComparison.Ordinal);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.MonthlyScrapPrices WHERE [Version] = 1 AND [Reason] IS NULL"));
    }

    [SqlServerFact]
    public async Task The_scrap_price_versions_migrations_Down_passes_with_first_entries_and_refuses_once_corrected()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddManufacturingMonthlyScrapPriceVersions().DownOperations[0]).Sql;
        var first = ScrapPriceOf(CompanyA, 2026, 11, 7.25m, run);
        await using (var store = run.Store(CompanyA))
        {
            await store.ScrapPrices.RecordScrapPriceAsync(first, Metadata(run));
        }

        // Only first entries: dropping the version loses nothing.
        await run.ScalarAsync<int>(guard + "; SELECT 1");

        await using (var store = run.Store(CompanyA))
        {
            await store.ScrapPrices.CorrectScrapPriceAsync(Guid.NewGuid(), first.Period, MonthlyScrapPrice.FirstVersion,
                new ScrapPriceTlPerKg(7.5m), "sentetik düzeltme", Metadata(run), ScrapPriceEnteredAt);
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(51627, refused.Number);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.MonthlyScrapPrices"));
    }

    /// <summary>
    /// URT-D73: a price is entered only for the current or a past month of the Europe/Istanbul business day, so the
    /// synthetic entries here are made after the last month they price.
    /// </summary>
    private static readonly DateTimeOffset ScrapPriceEnteredAt = new(2027, 2, 1, 9, 0, 0, TimeSpan.Zero);

    private static MonthlyScrapPrice ScrapPriceOf(Guid company, int year, int month, decimal tlPerKg, Run run) =>
        MonthlyScrapPrice.Record(Guid.NewGuid(), company, new ScrapPricePeriod(year, month),
            new ScrapPriceTlPerKg(tlPerKg), run.Actor.ToString("D"), ScrapPriceEnteredAt);
}

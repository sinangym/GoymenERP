using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class WorkOutboxDispatchSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Inbox_event_Down_allows_an_empty_store_and_racing_readers_record_one_arrival_per_cycle_on_real_sql()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var guard = Assert.IsType<SqlOperation>(new AddWorkApprovalInboxEvents().DownOperations[0]).Sql;
            await using var database = Db(connectionString, TestCompanyIds.CompanyA);
            await database.Database.ExecuteSqlRawAsync(guard);

            const string kind = "finance.manual-bank-payment.v1";
            var item = Guid.NewGuid();
            var since = DateTimeOffset.UtcNow.AddHours(-2);
            var company = new CompanyContext();
            company.Set(TestCompanyIds.CompanyA);
            await using var first = Db(connectionString, TestCompanyIds.CompanyA);
            await using var second = Db(connectionString, TestCompanyIds.CompanyA);
            await Task.WhenAll(
                new WorkApprovalInboxMeasurementStoreV1(first, company, TimeProvider.System)
                    .ObserveAsync(TestCompanyIds.CompanyA, kind, [new(item, since)], complete: true),
                new WorkApprovalInboxMeasurementStoreV1(second, company, TimeProvider.System)
                    .ObserveAsync(TestCompanyIds.CompanyA, kind, [new(item, since)], complete: true));

            Assert.Equal(1, await database.ApprovalInboxEvents.CountAsync(row =>
                row.ItemId == item && row.EventType == (int)WorkApprovalInboxEventTypeV1.Arrived));
            var duplicate = await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync(
                "INSERT INTO [work].[ApprovalInboxEvents] ([CompanyId],[Id],[Kind],[ItemId],[CycleStartedAtUtc],[EventType],[OccurredAtUtc],[RecordedAtUtc],[ClientEventId]) "
                + "VALUES ({0}, NEWID(), {1}, {2}, {3}, 1, {3}, SYSDATETIMEOFFSET(), NULL)",
                TestCompanyIds.CompanyA, kind, item, since));
            Assert.Contains(duplicate.Number, new[] { 2601, 2627 });

            var refusal = await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(52080, refusal.Number);
            Assert.Equal(1, await database.ApprovalInboxEvents.CountAsync());
        });
    }
}

using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Work.UnitTests;

public sealed class WorkApprovalInboxMeasurementTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000011");
    private static readonly Guid OtherCompany = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000012");
    private static readonly Guid First = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset Since = DateTimeOffset.Parse("2026-11-16T07:00:00Z");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-18T07:00:00Z");
    private const string Kind = "finance.manual-bank-payment.v1";

    [Fact]
    public void Inbox_event_migration_is_DDL_only_and_Down_first_refuses_while_events_exist()
    {
        var migration = new AddWorkApprovalInboxEvents();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal("ApprovalInboxEvents", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 52080", guard, StringComparison.Ordinal);
        Assert.Contains("[work].[ApprovalInboxEvents]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    [Fact]
    public void Inbox_event_model_is_tenant_filtered_with_one_arrival_and_resolution_per_cycle_and_no_actor_column()
    {
        using var database = SqlServerModel();
        var entity = Assert.IsAssignableFrom<IEntityType>(
            database.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(WorkApprovalInboxEventRow)));
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.Equal(["CompanyId", "Id"], entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.DoesNotContain(entity.GetProperties(), property =>
            property.Name.Contains("User", StringComparison.Ordinal) || property.Name.Contains("Actor", StringComparison.Ordinal)
            || property.Name.Contains("By", StringComparison.Ordinal));
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.GetFilter() == "[EventType] IN (1, 4, 5)" /* D10 (WP2): one owner decision per cycle too */
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "Kind", "ItemId", "CycleStartedAtUtc", "EventType"]));
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.GetFilter() == "[ClientEventId] IS NOT NULL");
        Assert.Contains(entity.GetCheckConstraints(), check => check.Sql == WorkDbContext.ApprovalInboxEventIdentityCheck);
        Assert.Contains(entity.GetCheckConstraints(), check => check.Sql == WorkDbContext.ApprovalInboxEventShapeCheck);
        Assert.Contains(entity.GetCheckConstraints(), check => check.Sql == WorkDbContext.ApprovalInboxEventTimeCheck);
    }

    [Fact]
    public async Task Observation_records_each_arrival_once_and_a_resolution_only_after_a_complete_read_no_longer_lists_it()
    {
        var name = Guid.NewGuid().ToString("N");
        await using (var database = InMemory(name))
        {
            var store = new WorkApprovalInboxMeasurementStoreV1(database, Context(Company), new FixedTime(Now));
            await store.ObserveAsync(Company, Kind, [new(First, Since), new(Second, Since)], complete: true);
            await store.ObserveAsync(Company, Kind, [new(First, Since), new(Second, Since)], complete: true);
            await store.ObserveAsync(Company, Kind, [new(First, Since)], complete: false);
            await store.ObserveAsync(Company, Kind, [new(First, Since)], complete: true);
        }

        await using var read = InMemory(name);
        var rows = await read.ApprovalInboxEvents.AsNoTracking()
            .OrderBy(row => row.EventType).ThenBy(row => row.ItemId).ToListAsync();
        Assert.Equal(
            [(First, WorkApprovalInboxEventTypeV1.Arrived, Since), (Second, WorkApprovalInboxEventTypeV1.Arrived, Since),
                (Second, WorkApprovalInboxEventTypeV1.ResolvedObserved, Now)],
            rows.Select(row => (row.ItemId, (WorkApprovalInboxEventTypeV1)row.EventType, row.OccurredAtUtc)).ToArray());
        Assert.All(rows, row => Assert.Equal((Company, Since, (Guid?)null), (row.CompanyId, row.CycleStartedAtUtc, row.ClientEventId)));
    }

    [Fact]
    public async Task A_resubmitted_record_opens_a_new_cycle_and_a_future_waiting_time_is_not_recorded_yet()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var database = InMemory(name);
        var store = new WorkApprovalInboxMeasurementStoreV1(database, Context(Company), new FixedTime(Now));

        await store.ObserveAsync(Company, Kind, [new(First, Since)], complete: true);
        await store.ObserveAsync(Company, Kind, [new(First, Now.AddHours(-1))], complete: true);
        await store.ObserveAsync(Company, Kind, [new(Second, Now.AddHours(1))], complete: false);

        var rows = await database.ApprovalInboxEvents.AsNoTracking().ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, row => row.CycleStartedAtUtc == Since && row.EventType == (int)WorkApprovalInboxEventTypeV1.ResolvedObserved);
        Assert.Contains(rows, row => row.CycleStartedAtUtc == Now.AddHours(-1) && row.EventType == (int)WorkApprovalInboxEventTypeV1.Arrived);
        Assert.DoesNotContain(rows, row => row.ItemId == Second);
    }

    [Fact]
    public async Task An_interaction_is_recorded_once_replayed_with_the_same_content_and_refused_with_other_content()
    {
        await using var database = InMemory(Guid.NewGuid().ToString("N"));
        var store = new WorkApprovalInboxMeasurementStoreV1(database, Context(Company), new FixedTime(Now));
        var clientEventId = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
        var opened = new WorkApprovalInboxInteractionV1(clientEventId, Kind, First, Since, WorkApprovalInboxEventTypeV1.Opened);

        Assert.Equal(WorkApprovalInboxInteractionOutcomeV1.Recorded, await store.RecordInteractionAsync(Company, opened));
        Assert.Equal(WorkApprovalInboxInteractionOutcomeV1.Replayed, await store.RecordInteractionAsync(Company, opened));
        Assert.Equal(WorkApprovalInboxInteractionOutcomeV1.Conflict, await store.RecordInteractionAsync(Company,
            opened with { EventType = WorkApprovalInboxEventTypeV1.OwnerScreenOpened }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordInteractionAsync(Company,
            opened with { ClientEventId = Guid.NewGuid(), EventType = WorkApprovalInboxEventTypeV1.Arrived }));

        var row = Assert.Single(await database.ApprovalInboxEvents.AsNoTracking().ToListAsync());
        Assert.Equal((clientEventId, (int)WorkApprovalInboxEventTypeV1.Opened, Now), (row.ClientEventId, row.EventType, row.OccurredAtUtc));
    }

    [Fact]
    public async Task Measurements_refuse_another_company_than_the_active_context()
    {
        await using var database = InMemory(Guid.NewGuid().ToString("N"));
        var store = new WorkApprovalInboxMeasurementStoreV1(database, Context(Company), new FixedTime(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ObserveAsync(OtherCompany, Kind, [new(First, Since)], complete: true));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ObserveAsync(Company, "not-a-kind", [new(First, Since)], complete: true));
        Assert.Empty(await database.ApprovalInboxEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Inbox_events_are_append_only()
    {
        await using var database = InMemory(Guid.NewGuid().ToString("N"));
        var row = new WorkApprovalInboxEventRow
        {
            CompanyId = Company, Id = Guid.NewGuid(), Kind = Kind, ItemId = First, CycleStartedAtUtc = Since,
            EventType = (int)WorkApprovalInboxEventTypeV1.Arrived, OccurredAtUtc = Since, RecordedAtUtc = Now,
        };
        database.Add(row);
        await database.SaveChangesAsync();
        row.OccurredAtUtc = Now;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();
        database.Remove(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }

    private static CompanyContext Context(Guid companyId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        return company;
    }

    private static WorkDbContext InMemory(string name) =>
        new(new DbContextOptionsBuilder<WorkDbContext>().UseInMemoryDatabase(name).Options, Context(Company));

    private static WorkDbContext SqlServerModel() =>
        new(new DbContextOptionsBuilder<WorkDbContext>().UseSqlServer("Server=invalid;Database=NOT_ENV;").Options, Context(Company));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

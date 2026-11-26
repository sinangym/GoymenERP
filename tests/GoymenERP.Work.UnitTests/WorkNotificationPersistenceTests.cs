using GoymenERP.Modules.Work.Domain;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Work.UnitTests;

public sealed class WorkNotificationPersistenceTests
{
    [Fact]
    public void Notification_migration_is_DDL_only_and_Down_first_protects_deliveries_dedup_and_passivation_history()
    {
        var migration = new AddPersonalNotifications();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(new[] { "NotificationInbox", "NotificationPassivations", "Notifications" },
            migration.UpOperations.OfType<CreateTableOperation>().Select(table => table.Name).Order());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51955", guard, StringComparison.Ordinal);
        foreach (var table in new[] { "NotificationInbox", "Notifications", "NotificationPassivations" })
            Assert.Contains("[work].[" + table + "]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    [Fact]
    public void Notification_model_has_tenant_filters_unique_source_grains_restrict_links_and_no_defaults()
    {
        using var database = Database();
        var model = database.GetService<IDesignTimeModel>().Model;
        // D9 (WP2): the event notifications live in the single store work.InboxNotifications (kinds 4 and 5).
        foreach (var type in new[] { typeof(WorkNotificationInboxRow), typeof(WorkInboxNotificationRow), typeof(WorkNotificationPassivationRow) })
        {
            var entity = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(type));
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.All(entity.GetForeignKeys(), foreign => Assert.Equal(DeleteBehavior.Restrict, foreign.DeleteBehavior));
            Assert.All(entity.GetProperties(), property => Assert.Null(property.GetDefaultValueSql()));
        }
        Assert.Null(model.GetEntityTypes().SingleOrDefault(entity => entity.GetTableName() == "Notifications"));
        var notification = model.FindEntityType(typeof(WorkInboxNotificationRow))!;
        Assert.Contains(notification.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "CompanyId", "SourceName", "EventId", "RecipientUserId", "SalesOrderId", "SalesOrderLineId", "PurchaseOrderId", "PurchaseOrderLineId" })
            && index.GetFilter() == "[EventId] IS NOT NULL" && index.GetDatabaseName() == WorkDbContext.EventGrainIndexName);
        Assert.Contains(notification.GetCheckConstraints(), check => check.Name == "CK_WorkInboxNotifications_Identity" && check.Sql == WorkDbContext.InboxNotificationCheck);
        Assert.Contains(notification.GetCheckConstraints(), check => check.Name == "CK_WorkInboxNotifications_Event" && check.Sql == WorkDbContext.InboxNotificationEventCheck);
        Assert.Contains(notification.GetForeignKeys(), foreign => foreign.PrincipalEntityType.ClrType == typeof(WorkNotificationInboxRow)
            && foreign.Properties.Select(property => property.Name).SequenceEqual(new[] { "CompanyId", "SourceName", "EventId" }));
        var passivation = model.FindEntityType(typeof(WorkNotificationPassivationRow))!;
        Assert.Contains(passivation.GetForeignKeys(), foreign => foreign.PrincipalEntityType.ClrType == typeof(WorkInboxNotificationRow));
    }

    [Fact]
    public void Stale_cost_notification_migration_only_widens_the_event_shape_and_first_protects_preparer_delivery_history()
    {
        using var database = Database();
        var row = database.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(WorkInboxNotificationRow))!;
        var identity = row.GetCheckConstraints().Single(check => check.Name == "CK_WorkInboxNotifications_Identity").Sql;
        var evidence = row.GetCheckConstraints().Single(check => check.Name == "CK_WorkInboxNotifications_Event").Sql;
        Assert.Equal(WorkDbContext.InboxNotificationCheck, identity);
        Assert.Equal(WorkDbContext.InboxNotificationEventCheck, evidence);
        Assert.Contains("[Kind] IN (4, 5, 10) AND [Class] = 2", identity);
        Assert.Contains("[Kind] = 10 AND [SourceName] = 'accounting.cost-proposals' AND [ReasonCode] = 'inventory_cost_preparation_stale'", evidence);
        Assert.Contains("[Kind] NOT IN (4, 5, 10)", evidence);
        var migration = new AddStaleCostProposalNotification();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Collection(migration.UpOperations,
            operation => Assert.IsType<DropCheckConstraintOperation>(operation),
            operation => Assert.IsType<DropCheckConstraintOperation>(operation),
            operation => Assert.Equal(evidence, Assert.IsType<AddCheckConstraintOperation>(operation).Sql),
            operation => Assert.Equal(identity, Assert.IsType<AddCheckConstraintOperation>(operation).Sql));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("[work].[InboxNotifications] WHERE [Kind] = 10", guard); Assert.Contains("THROW 52142", guard);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Equal(5, migration.DownOperations.Count);
        Assert.Equal(2, migration.DownOperations.OfType<DropCheckConstraintOperation>().Count());
        Assert.Equal(2, migration.DownOperations.OfType<AddCheckConstraintOperation>().Count());
        Assert.Equal(WorkInboxNotificationClassV1.Digest, WorkInboxNotificationClassesV1.Of(WorkInboxNotificationKindV1.AccountingCostProposalStale));
        Assert.Equal("accounting-cost-proposal-stale", WorkInboxNotificationClassesV1.Wire(WorkInboxNotificationKindV1.AccountingCostProposalStale));
        Assert.Equal(WorkInboxNotificationKindV1.AccountingCostProposalStale, WorkNotificationStoreV1.StoredKind(WorkNotificationKindV1.AccountingCostProposalStale));
    }

    [Fact]
    public void The_WP2_migration_moves_the_old_store_before_dropping_it_and_Down_first_refuses_while_moved_or_new_rows_exist()
    {
        var migration = new Wp2SingleNotificationStoreAndApprovalFollowUp();
        var operations = migration.UpOperations.ToList();
        var copy = Assert.Single(operations.OfType<SqlOperation>());
        var drop = operations.OfType<DropTableOperation>().Single(table => table.Name == "Notifications");
        Assert.True(operations.IndexOf(copy) < operations.IndexOf(drop));
        Assert.All(operations.OfType<AddColumnOperation>(), column => Assert.True(operations.IndexOf(column) < operations.IndexOf(copy)));
        Assert.Contains("INSERT INTO [work].[InboxNotifications]", copy.Sql, StringComparison.Ordinal);
        Assert.Contains("[Kind] + 3", copy.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 52111", copy.Sql, StringComparison.Ordinal);
        Assert.Equal(new[] { "ApprovalFollowUps" }, operations.OfType<CreateTableOperation>().Select(table => table.Name));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 52110", guard, StringComparison.Ordinal);
        foreach (var table in new[] { "InboxNotifications", "ApprovalFollowUps", "ApprovalInboxEvents" })
            Assert.Contains("[work].[" + table + "]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    [Fact]
    public void The_moved_kinds_are_digest_class_and_the_approval_kinds_are_now_class()
    {
        Assert.Equal(WorkInboxNotificationClassV1.Digest, WorkInboxNotificationClassesV1.Of(WorkInboxNotificationKindV1.AllocationReleased));
        Assert.Equal(WorkInboxNotificationClassV1.Digest, WorkInboxNotificationClassesV1.Of(WorkInboxNotificationKindV1.PurchaseSupplyRevived));
        foreach (var kind in new[] { WorkInboxNotificationKindV1.ApprovalWaiting, WorkInboxNotificationKindV1.ApprovalReminder,
                     WorkInboxNotificationKindV1.ApprovalEscalated, WorkInboxNotificationKindV1.ApprovalOrphaned })
            Assert.Equal(WorkInboxNotificationClassV1.Now, WorkInboxNotificationClassesV1.Of(kind));
        Assert.Equal(new[] { "allocation-released", "purchase-supply-revived", "approval-waiting", "approval-reminder", "approval-escalated", "approval-orphaned" },
            Enumerable.Range(4, 6).Select(value => WorkInboxNotificationClassesV1.Wire((WorkInboxNotificationKindV1)value)));
        Assert.Equal(WorkInboxNotificationKindV1.AllocationReleased, WorkNotificationStoreV1.StoredKind(WorkNotificationKindV1.AllocationReleased));
        Assert.Equal(WorkInboxNotificationKindV1.PurchaseSupplyRevived, WorkNotificationStoreV1.StoredKind(WorkNotificationKindV1.PurchaseSupplyRevived));
    }
    [Fact]
    public async Task Notification_inbox_and_passivation_are_immutable_and_foreign_or_delete_writes_refuse_before_sql()
    {
        await using var database = Database();
        var row = new WorkNotificationInboxRow { CompanyId = Guid.NewGuid(), SourceName = "inventory", EventId = Guid.NewGuid() };
        database.Add(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();
        row.CompanyId = Company;
        database.Attach(row);
        row.PayloadSha256 = new string('a', 64);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();
        var passive = new WorkNotificationPassivationRow { CompanyId = Company, NotificationId = Guid.NewGuid(), PolicyVersion = "1" };
        database.Attach(passive);
        passive.ActiveDays = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();
        database.Attach(row);
        database.Remove(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }
    private static WorkDbContext Database()
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(new DbContextOptionsBuilder<WorkDbContext>().UseSqlServer("Server=invalid;Database=NOT_ENV;").Options, company);
    }
}

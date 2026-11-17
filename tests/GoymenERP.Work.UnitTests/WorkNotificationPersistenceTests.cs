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
        foreach (var type in new[] { typeof(WorkNotificationInboxRow), typeof(WorkNotificationRow), typeof(WorkNotificationPassivationRow) })
        {
            var entity = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(type));
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.All(entity.GetForeignKeys(), foreign => Assert.Equal(DeleteBehavior.Restrict, foreign.DeleteBehavior));
            Assert.All(entity.GetProperties(), property => Assert.Null(property.GetDefaultValueSql()));
        }
        var notification = model.FindEntityType(typeof(WorkNotificationRow))!;
        Assert.Contains(notification.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "CompanyId", "SourceName", "EventId", "RecipientUserId", "SalesOrderId", "SalesOrderLineId", "PurchaseOrderId", "PurchaseOrderLineId" })
            && index.GetFilter() is null);
        Assert.Contains(notification.GetCheckConstraints(), check => check.Name == "CK_WorkNotifications_Identity" && check.Sql == WorkDbContext.NotificationIdentityCheck);
        Assert.Contains(notification.GetCheckConstraints(), check => check.Name == "CK_WorkNotifications_Target" && check.Sql == WorkDbContext.NotificationTargetCheck);
        Assert.Contains(notification.GetCheckConstraints(), check => check.Name == "CK_WorkNotifications_Evidence" && check.Sql == WorkDbContext.NotificationEvidenceCheck);
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

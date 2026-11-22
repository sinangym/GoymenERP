using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Work.UnitTests;

public sealed class WorkCollaborationPersistenceTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
    private static readonly string[] Tables =
    [
        "CollaborationAttempts", "InboxDigestItems", "InboxDigests", "InboxNotificationReads", "InboxNotifications",
        "RecordNoteMentions", "RecordNotePassivations", "RecordNotes", "Tasks",
    ];

    [Fact]
    public void Collaboration_migration_is_DDL_only_and_Down_first_refuses_with_THROW_52090_while_any_row_exists()
    {
        var migration = new AddWorkCollaboration();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.DoesNotContain(migration.UpOperations, operation => operation is AlterTableOperation or AlterColumnOperation
            or DropTableOperation or DropColumnOperation or DropCheckConstraintOperation);
        Assert.Equal(Tables, migration.UpOperations.OfType<CreateTableOperation>().Select(table => table.Name).Order(StringComparer.Ordinal));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 52090", guard, StringComparison.Ordinal);
        foreach (var table in Tables) Assert.Contains("[work].[" + table + "]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Equal(Tables, migration.DownOperations.OfType<DropTableOperation>().Select(table => table.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Collaboration_model_has_tenant_filters_restrict_links_no_defaults_and_the_decided_checks()
    {
        using var database = Database();
        var model = database.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[]
                 {
                     typeof(WorkRecordNoteRow), typeof(WorkRecordNoteMentionRow), typeof(WorkRecordNotePassivationRow), typeof(WorkTaskRow),
                     typeof(WorkInboxNotificationRow), typeof(WorkInboxNotificationReadRow), typeof(WorkInboxDigestRow),
                     typeof(WorkInboxDigestItemRow), typeof(WorkCollaborationAttemptRow),
                 })
        {
            var entity = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(type));
            Assert.Equal(WorkDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.All(entity.GetForeignKeys(), foreign => Assert.Equal(DeleteBehavior.Restrict, foreign.DeleteBehavior));
            Assert.All(entity.GetProperties(), property => Assert.Null(property.GetDefaultValueSql()));
        }

        Check(model, typeof(WorkRecordNoteRow), "CK_WorkRecordNotes_Identity", WorkDbContext.RecordNoteCheck);
        Check(model, typeof(WorkRecordNoteMentionRow), "CK_WorkRecordNoteMentions_Identity", WorkDbContext.RecordNoteMentionCheck);
        Check(model, typeof(WorkRecordNotePassivationRow), "CK_WorkRecordNotePassivations_Identity", WorkDbContext.RecordNotePassivationCheck);
        Check(model, typeof(WorkTaskRow), "CK_WorkTasks_Identity", WorkDbContext.TaskIdentityCheck);
        Check(model, typeof(WorkTaskRow), "CK_WorkTasks_State", WorkDbContext.TaskStateCheck);
        Check(model, typeof(WorkInboxNotificationRow), "CK_WorkInboxNotifications_Identity", WorkDbContext.InboxNotificationCheck);
        Check(model, typeof(WorkInboxDigestRow), "CK_WorkInboxDigests_Identity", WorkDbContext.InboxDigestCheck);
        Check(model, typeof(WorkCollaborationAttemptRow), "CK_WorkCollaborationAttempts_Identity", WorkDbContext.CollaborationAttemptCheck);

        var inbox = model.FindEntityType(typeof(WorkInboxNotificationRow))!;
        var dedup = Assert.Single(inbox.GetIndexes(), index => index.GetDatabaseName() == "UX_WorkInboxNotifications_RecipientEvent");
        Assert.True(dedup.IsUnique);
        Assert.Null(dedup.GetFilter());
        Assert.Equal(new[] { "CompanyId", "RecipientUserId", "Kind", "SourceId" }, dedup.Properties.Select(property => property.Name));
        var slot = Assert.Single(model.FindEntityType(typeof(WorkInboxDigestRow))!.GetIndexes(),
            index => index.GetDatabaseName() == "UX_WorkInboxDigests_RecipientSlot");
        Assert.True(slot.IsUnique);
        Assert.Equal(new[] { "CompanyId", "RecipientUserId", "SlotUtc" }, slot.Properties.Select(property => property.Name));
        var item = model.FindEntityType(typeof(WorkInboxDigestItemRow))!;
        Assert.Equal(new[] { "CompanyId", "NotificationId" }, item.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var task = model.FindEntityType(typeof(WorkTaskRow))!;
        Assert.True(task.FindProperty(nameof(WorkTaskRow.Version))!.IsConcurrencyToken);
        Assert.True(task.FindProperty(nameof(WorkTaskRow.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(4000, model.FindEntityType(typeof(WorkRecordNoteRow))!.FindProperty(nameof(WorkRecordNoteRow.Body))!.GetMaxLength());
    }

    [Fact]
    public async Task Notes_notifications_digests_and_attempts_are_append_only_and_foreign_company_writes_refuse_before_sql()
    {
        await using var database = Database();
        database.Add(new WorkRecordNoteRow { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid(), Body = "x", CorrelationId = "c" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        object[] rows =
        [
            new WorkRecordNoteRow { CompanyId = Company, Id = Guid.NewGuid(), Body = "x", CorrelationId = "c" },
            new WorkRecordNotePassivationRow { CompanyId = Company, NoteId = Guid.NewGuid(), Reason = "r" },
            new WorkRecordNoteReadRow { CompanyId = Company, NoteId = Guid.NewGuid(), ReaderUserId = Guid.NewGuid() },
            new WorkInboxNotificationRow { CompanyId = Company, Id = Guid.NewGuid(), CorrelationId = "c" },
            new WorkInboxNotificationReadRow { CompanyId = Company, NotificationId = Guid.NewGuid() },
            new WorkInboxDigestRow { CompanyId = Company, Id = Guid.NewGuid(), ScheduleVersion = "1" },
            new WorkCollaborationAttemptRow { CompanyId = Company, ActorUserId = Guid.NewGuid(), IdempotencyKeySha256 = new string('a', 64) },
        ];
        foreach (var row in rows)
        {
            database.Attach(row);
            database.Entry(row).State = EntityState.Modified;
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
            database.ChangeTracker.Clear();
            database.Attach(row);
            database.Remove(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
            database.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task A_task_row_closes_only_once_by_one_version_step_and_keeps_its_identity()
    {
        await using var database = Database();
        var row = new WorkTaskRow
        {
            CompanyId = Company, Id = Guid.NewGuid(), Title = "t", AssignerUserId = Guid.NewGuid(), AssigneeUserId = Guid.NewGuid(),
            Status = 1, Version = 1, RowVersion = [1],
        };
        database.Attach(row);
        row.Title = "changed";
        row.Status = 2;
        row.Version = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        row.Title = "t";
        row.Status = 1;
        row.Version = 1;
        database.Attach(row);
        row.Status = 2;
        row.Version = 3;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        row.Status = 2;
        row.Version = 2;
        database.Attach(row);
        row.Status = 3;
        row.Version = 3;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public void The_WP9_migration_widens_the_record_kinds_adds_note_reads_and_Down_first_refuses_with_THROW_52170()
    {
        Assert.Equal(WorkRecordTypesV1.StoredValues, WorkDbContext.RecordTypeList.Split(", ").Select(int.Parse));
        Assert.Equal(WorkRecordTypesV1.WireNames.Count, WorkRecordTypesV1.StoredValues.Count);
        var migration = new WidenWorkRecordTypesAndNoteReads();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation or DropTableOperation or DropColumnOperation
            or AlterColumnOperation);
        Assert.Equal(new[] { "RecordNoteReads" }, migration.UpOperations.OfType<CreateTableOperation>().Select(table => table.Name));
        Assert.Equal(new[] { "CK_WorkInboxNotifications_Identity", "CK_WorkRecordNotes_Identity", "CK_WorkTasks_Identity" },
            migration.UpOperations.OfType<AddCheckConstraintOperation>().Select(check => check.Name).Order(StringComparer.Ordinal));
        Assert.All(migration.UpOperations.OfType<AddCheckConstraintOperation>(),
            check => Assert.Contains("[RecordType] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)", check.Sql, StringComparison.Ordinal));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 52170", guard, StringComparison.Ordinal);
        foreach (var table in new[] { "RecordNoteReads", "RecordNotes", "Tasks", "InboxNotifications" })
            Assert.Contains("[work].[" + table + "]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Equal(new[] { "RecordNoteReads" }, migration.DownOperations.OfType<DropTableOperation>().Select(table => table.Name));

        using var database = Database();
        var model = database.GetService<IDesignTimeModel>().Model;
        Check(model, typeof(WorkRecordNoteReadRow), "CK_WorkRecordNoteReads_Identity", WorkDbContext.RecordNoteReadCheck);
        var reads = model.FindEntityType(typeof(WorkRecordNoteReadRow))!;
        Assert.Equal(new[] { "CompanyId", "NoteId", "ReaderUserId" }, reads.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(reads.GetDeclaredQueryFilters());
        Assert.All(reads.GetForeignKeys(), foreign => Assert.Equal(DeleteBehavior.Restrict, foreign.DeleteBehavior));
    }

    private static void Check(IModel model, Type type, string name, string sql) =>
        Assert.Contains(model.FindEntityType(type)!.GetCheckConstraints(), check => check.Name == name && check.Sql == sql);

    private static WorkDbContext Database()
    {
        var company = new CompanyContext();
        company.Set(Company);
        return new(new DbContextOptionsBuilder<WorkDbContext>().UseSqlServer("Server=invalid;Database=NOT_ENV;").Options, company);
    }
}

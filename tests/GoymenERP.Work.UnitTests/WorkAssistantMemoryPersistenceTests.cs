using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Work.UnitTests;

/// <summary>WP-C6 (AI-D30): the assistant memory tables, their checks, the write guards and the forward-fix Down.</summary>
public sealed class WorkAssistantMemoryPersistenceTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000c6");
    private static readonly string[] Tables = ["AssistantMemoryEntries", "AssistantMemoryOversightViews", "AssistantMemoryUserClosures"];

    [Fact]
    public void Assistant_memory_migration_is_DDL_only_and_Down_first_refuses_with_THROW_52210_while_any_row_exists()
    {
        var migration = new AddWorkAssistantMemory();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.DoesNotContain(migration.UpOperations, operation => operation is AlterTableOperation or AlterColumnOperation
            or DropTableOperation or DropColumnOperation or DropCheckConstraintOperation);
        Assert.Equal(Tables, migration.UpOperations.OfType<CreateTableOperation>().Select(table => table.Name).Order(StringComparer.Ordinal));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 52210", guard, StringComparison.Ordinal);
        foreach (var table in Tables) Assert.Contains("[work].[" + table + "]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Equal(Tables, migration.DownOperations.OfType<DropTableOperation>().Select(table => table.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Assistant_memory_model_has_tenant_filters_restrict_links_no_defaults_and_the_decided_checks()
    {
        using var database = Database();
        var model = database.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(WorkAssistantMemoryEntryRow), typeof(WorkAssistantMemoryUserClosureRow), typeof(WorkAssistantMemoryOversightViewRow) })
        {
            var entity = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(type));
            Assert.Equal(WorkDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.All(entity.GetForeignKeys(), foreign => Assert.Equal(DeleteBehavior.Restrict, foreign.DeleteBehavior));
            Assert.All(entity.GetProperties(), property => Assert.Null(property.GetDefaultValueSql()));
        }

        Check(model, typeof(WorkAssistantMemoryEntryRow), "CK_WorkAssistantMemoryEntries_Identity", WorkDbContext.AssistantMemoryEntryIdentityCheck);
        Check(model, typeof(WorkAssistantMemoryEntryRow), "CK_WorkAssistantMemoryEntries_State", WorkDbContext.AssistantMemoryEntryStateCheck);
        Check(model, typeof(WorkAssistantMemoryUserClosureRow), "CK_WorkAssistantMemoryUserClosures_Identity", WorkDbContext.AssistantMemoryUserClosureCheck);
        Check(model, typeof(WorkAssistantMemoryOversightViewRow), "CK_WorkAssistantMemoryOversightViews_Identity",
            WorkDbContext.AssistantMemoryOversightViewCheck);
        var entry = model.FindEntityType(typeof(WorkAssistantMemoryEntryRow))!;
        Assert.Equal(500, entry.FindProperty(nameof(WorkAssistantMemoryEntryRow.Body))!.GetMaxLength());
        Assert.True(entry.FindProperty(nameof(WorkAssistantMemoryEntryRow.Version))!.IsConcurrencyToken);
        Assert.True(entry.FindProperty(nameof(WorkAssistantMemoryEntryRow.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(new[] { "CompanyId", "SubjectUserId" },
            model.FindEntityType(typeof(WorkAssistantMemoryUserClosureRow))!.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(model.FindEntityType(typeof(WorkAssistantMemoryOversightViewRow))!.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { "CompanyId", "SubjectUserId", "ViewedAtUtc", "Id" }));
    }

    [Fact]
    public async Task View_rows_are_append_only_entries_and_closures_are_never_deleted_and_foreign_company_writes_refuse_before_sql()
    {
        await using var database = Database();
        database.Add(new WorkAssistantMemoryEntryRow { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid(), Body = "x", CorrelationId = "c" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        var view = new WorkAssistantMemoryOversightViewRow { CompanyId = Company, Id = Guid.NewGuid(), CorrelationId = "c" };
        database.Attach(view);
        database.Entry(view).State = EntityState.Modified;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        object[] rows =
        [
            view,
            new WorkAssistantMemoryEntryRow { CompanyId = Company, Id = Guid.NewGuid(), Body = "x", CorrelationId = "c", State = 1, Version = 1, RowVersion = [1] },
            new WorkAssistantMemoryUserClosureRow { CompanyId = Company, SubjectUserId = Guid.NewGuid(), Reason = "r", CorrelationId = "c", Version = 1, RowVersion = [1] },
        ];
        foreach (var row in rows)
        {
            database.Attach(row);
            database.Remove(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
            database.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task An_entry_changes_only_while_active_by_one_version_step_and_an_ended_entry_keeps_its_text()
    {
        var owner = Guid.NewGuid();
        WorkAssistantMemoryEntryRow Row(int state, long version) => new()
        {
            CompanyId = Company, Id = Guid.Parse("eeeeeeee-0000-4000-8000-0000000000c6"), OwnerUserId = owner, Kind = 1, Body = "metin",
            State = state, Version = version, CorrelationId = "c", RowVersion = [1],
        };

        await using var database = Database();
        // Two version steps at once.
        var skip = Row(1, 1);
        database.Attach(skip);
        skip.Body = "yeni";
        skip.Version = 3;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        // Deleting while rewriting the text.
        var rewrite = Row(1, 1);
        database.Attach(rewrite);
        rewrite.State = 2;
        rewrite.Body = "silinirken degisti";
        rewrite.Version = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        // An ended entry never changes again.
        var ended = Row(3, 2);
        database.Attach(ended);
        ended.State = 1;
        ended.Version = 3;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        // The owner cannot be moved.
        var moved = Row(1, 1);
        database.Attach(moved);
        moved.OwnerUserId = Guid.NewGuid();
        moved.Version = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        // A last-use touch only moves the passive-after date forward (AI-D38); moving it back is refused.
        var back = Row(1, 1);
        back.ExpiresAtUtc = new DateTimeOffset(2027, 11, 22, 0, 0, 0, TimeSpan.Zero);
        database.Attach(back);
        back.ExpiresAtUtc = back.ExpiresAtUtc.AddDays(-1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        // A touch cannot smuggle a text change past the version step.
        var smuggle = Row(1, 1);
        database.Attach(smuggle);
        smuggle.ExpiresAtUtc = smuggle.ExpiresAtUtc.AddDays(1);
        smuggle.Body = "gizlice degisti";
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        // A closure moves by exactly one version step.
        var closure = new WorkAssistantMemoryUserClosureRow
        {
            CompanyId = Company, SubjectUserId = owner, IsClosed = true, Version = 1, Reason = "r", CorrelationId = "c", RowVersion = [1],
        };
        database.Attach(closure);
        closure.IsClosed = false;
        closure.Version = 3;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }

    private static void Check(IModel model, Type type, string name, string sql) =>
        Assert.Equal(sql, Assert.Single(model.FindEntityType(type)!.GetCheckConstraints(), check => check.Name == name).Sql);

    private static WorkDbContext Database()
    {
        var company = new CompanyContext();
        company.Set(Company);
        return new(new DbContextOptionsBuilder<WorkDbContext>().UseSqlServer("Server=invalid;Database=NOT_ENV;").Options, company);
    }
}

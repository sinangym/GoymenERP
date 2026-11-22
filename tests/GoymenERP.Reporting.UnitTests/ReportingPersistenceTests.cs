using GoymenERP.Modules.Reporting.Infrastructure.Persistence;
using GoymenERP.Modules.Reporting.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Reporting.UnitTests;

/// <summary>SENTETIK. The Reporting schema: one DDL-only migration, a forward-fix Down guard and the decided checks.</summary>
public sealed class ReportingPersistenceTests
{
    private static readonly string[] Tables = ["CommandAttempts", "ExportRecords", "SavedViews"];

    [Fact]
    public void The_reporting_migration_is_DDL_only_and_Down_first_refuses_with_THROW_52270_while_any_row_exists()
    {
        var migration = new AddReportingCenter();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(Tables, migration.UpOperations.OfType<CreateTableOperation>().Select(table => table.Name).Order(StringComparer.Ordinal));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 52270", guard, StringComparison.Ordinal);
        foreach (var table in Tables) Assert.Contains("[reporting].[" + table + "]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Equal(Tables, migration.DownOperations.OfType<DropTableOperation>().Select(table => table.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_migration_is_the_only_one_and_the_snapshot_matches_the_model()
    {
        using var context = SqlServerContext();
        Assert.Equal(new[] { "AddReportingCenter" }, context.Database.GetMigrations().Select(name => name.Split('_', 2)[1]));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Every_table_is_tenant_filtered_restrict_linked_and_carries_its_decided_check()
    {
        using var context = SqlServerContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(ReportingSavedViewRow), typeof(ReportingExportRow), typeof(ReportingAttemptRow) })
        {
            var entity = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(type));
            Assert.Equal(ReportingDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.All(entity.GetForeignKeys(), foreign => Assert.Equal(DeleteBehavior.Restrict, foreign.DeleteBehavior));
            Assert.All(entity.GetProperties(), property => Assert.Null(property.GetDefaultValueSql()));
        }

        Assert.Equal(ReportingDbContext.SavedViewCheck, Check(model, typeof(ReportingSavedViewRow), "CK_ReportingSavedViews_Identity"));
        Assert.Equal(ReportingDbContext.ExportCheck, Check(model, typeof(ReportingExportRow), "CK_ReportingExportRecords_Identity"));
        Assert.Equal(ReportingDbContext.AttemptCheck, Check(model, typeof(ReportingAttemptRow), "CK_ReportingCommandAttempts_Identity"));
        var name = Assert.Single(model.FindEntityType(typeof(ReportingSavedViewRow))!.GetIndexes(),
            index => index.GetDatabaseName() == "UX_ReportingSavedViews_ActiveName");
        Assert.True(name.IsUnique);
        Assert.Equal("[IsActive] = 1", name.GetFilter());
        Assert.Equal(new[] { "CompanyId", "OwnerUserId", "ReportId", "Name" }, name.Properties.Select(property => property.Name).ToArray());
        Assert.True(model.FindEntityType(typeof(ReportingSavedViewRow))!.FindProperty(nameof(ReportingSavedViewRow.Version))!.IsConcurrencyToken);
    }

    [Fact]
    public async Task The_store_refuses_deletes_edits_of_export_records_and_rows_of_another_company()
    {
        var harness = new ReportingHarness();
        await using (var database = harness.Database())
        {
            database.Exports.Add(Export(ReportingHarness.Company));
            await database.SaveChangesAsync();
        }

        await using (var database = harness.Database())
        {
            var row = await database.Exports.SingleAsync();
            row.RowCount = 99;
            Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        }

        await using (var database = harness.Database())
        {
            database.Exports.Remove(await database.Exports.SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        }

        await using (var database = harness.Database())
        {
            database.Exports.Add(Export(ReportingHarness.OtherCompany));
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        }
    }

    private static ReportingExportRow Export(Guid company) => new()
    {
        CompanyId = company, Id = Guid.NewGuid(), ActorUserId = Guid.NewGuid(), Kind = 1, ReportId = "stock-aging", Format = "csv", Page = 1,
        PageSize = 50, RowCount = 1, ByteLength = 10, ContentSha256 = new string('a', 64), RecordedAtUtc = ReportingHarness.Now, CorrelationId = "c",
    };

    private static string? Check(IModel model, Type type, string name) =>
        Assert.Single(model.FindEntityType(type)!.GetCheckConstraints(), check => check.Name == name).Sql;

    private static ReportingDbContext SqlServerContext() => new(
        new DbContextOptionsBuilder<ReportingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ReportingDbContext.Schema))
            .Options,
        new FixedCompany(ReportingHarness.Company));
}

using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>WP15 (1.16): the legal deadline calendar tables are company-filtered, insert-only versions with a forward-fix Down.</summary>
public sealed class SettingsLegalDeadlinePersistenceModelTests
{
    [Fact]
    public void The_calendar_tables_are_company_filtered_versions_with_checks_and_a_restricting_foreign_key()
    {
        using var database = Create();
        var model = database.GetService<IDesignTimeModel>().Model;
        var version = model.FindEntityType(typeof(LegalDeadlineCalendarVersionRow))!;
        Assert.Equal(("settings", "LegalDeadlineCalendarVersions"), (version.GetSchema(), version.GetTableName()));
        Assert.Equal(["CompanyId", "Version"], version.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.NotNull(version.GetDeclaredQueryFilters().SingleOrDefault());
        Assert.Contains(version.GetCheckConstraints(), check => check.Name == "CK_SettingsLegalDeadlineCalendarVersions_Identity"
            && check.Sql == SettingsDbContext.LegalDeadlineCalendarVersionCheck);

        var entry = model.FindEntityType(typeof(LegalDeadlineCalendarEntryRow))!;
        Assert.Equal(("settings", "LegalDeadlineCalendarEntries"), (entry.GetSchema(), entry.GetTableName()));
        Assert.Equal(["CompanyId", "Version", "Obligation"], entry.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.NotNull(entry.GetDeclaredQueryFilters().SingleOrDefault());
        Assert.Contains(entry.GetCheckConstraints(), check => check.Name == "CK_SettingsLegalDeadlineCalendarEntries_Entry"
            && check.Sql == SettingsDbContext.LegalDeadlineCalendarEntryCheck);
        var foreignKey = Assert.Single(entry.GetForeignKeys());
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Contains("[PeriodMonths] IN (1, 3, 12)", SettingsDbContext.LegalDeadlineCalendarEntryCheck, StringComparison.Ordinal);
    }

    [Fact]
    public void The_migration_seeds_nothing_and_its_Down_first_refuses_while_a_version_exists()
    {
        var migration = new AddLegalDeadlineCalendars();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Empty(migration.UpOperations.OfType<InsertDataOperation>());
        Assert.Equal(["LegalDeadlineCalendarEntries", "LegalDeadlineCalendarVersions"],
            migration.UpOperations.OfType<CreateTableOperation>().Select(operation => operation.Name).Order(StringComparer.Ordinal));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 52280", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[LegalDeadlineCalendarVersions]", guard.Sql, StringComparison.Ordinal);
    }

    private static SettingsDbContext Create() => new(new DbContextOptionsBuilder<SettingsDbContext>()
        .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True").Options, new CompanyContext());
}

using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Organization.UnitTests;

public sealed class WorkCenterCalendarMigrationScriptTests
{
    private const string CalendarMigration = "AddWorkCenterCalendar";
    private const string PreviousMigration = "AddLocationOperatorParty";

    [Fact]
    public void The_calendar_migration_creates_the_three_tenant_tables_with_their_checks()
    {
        using var dbContext = CreateDbContext();
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: PreviousMigration,
            toMigration: CalendarMigration);

        Assert.Contains("CREATE TABLE [organization].[WorkCenterCalendars]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [organization].[WorkCenterCalendarShifts]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [organization].[WorkCenterCalendarClosedDates]", script, StringComparison.Ordinal);
        Assert.Contains("CK_WorkCenterCalendars_Version", script, StringComparison.Ordinal);
        Assert.Contains("CK_WorkCenterCalendarShifts_Day", script, StringComparison.Ordinal);
        Assert.Contains("CK_WorkCenterCalendarShifts_Window", script, StringComparison.Ordinal);
        Assert.Contains("CK_WorkCenterCalendarClosedDates_Revision", script, StringComparison.Ordinal);
        Assert.Contains("time(0)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE CASCADE", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Rolling_the_calendar_migration_back_refuses_before_any_table_is_dropped()
    {
        using var dbContext = CreateDbContext();
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: CalendarMigration,
            toMigration: PreviousMigration);

        var refusal = script.IndexOf("THROW 51361", StringComparison.Ordinal);
        var firstDrop = script.IndexOf("DROP TABLE", StringComparison.Ordinal);
        Assert.True(refusal >= 0, "The Down script must refuse while calendars exist (forward fix).");
        Assert.True(firstDrop > refusal, "The refusal must run before the first table is dropped.");
        Assert.Contains("[organization].[WorkCenterCalendars]", script[..firstDrop], StringComparison.Ordinal);
    }

    [Fact]
    public void The_calendar_migrations_Down_starts_with_its_only_guard_and_Up_is_pure_DDL()
    {
        var migration = new AddWorkCenterCalendar();

        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51361", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[organization].[WorkCenterCalendars]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal(3, migration.UpOperations.OfType<CreateTableOperation>().Count());
        Assert.Equal(3, migration.DownOperations.OfType<DropTableOperation>().Count());
    }

    private static OrganizationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=GoymenErpOrganizationCalendarScript;Trusted_Connection=True;")
            .Options;
        return new OrganizationDbContext(options, new CompanyContext(), new OrganizationBootstrapWriteScope());
    }
}

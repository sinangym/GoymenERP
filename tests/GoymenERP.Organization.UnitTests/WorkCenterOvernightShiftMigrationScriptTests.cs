using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Organization.UnitTests;

/// <summary>
/// KIM-S25 (KIM-D33): the overnight-shift migration only relaxes the window check from <c>Start &lt; End</c> to
/// <c>Start &lt;&gt; End</c>, and its Down refuses while an overnight shift is stored (forward fix) instead of deleting or
/// rewriting it to fit the same-day check again.
/// </summary>
public sealed class WorkCenterOvernightShiftMigrationScriptTests
{
    private const string OvernightMigration = "AllowWorkCenterOvernightShifts";
    private const string PreviousMigration = "AddCompanyTaxIdChangeRequests";

    [Fact]
    public void The_overnight_migration_replaces_the_window_check_with_start_differs_from_end()
    {
        using var dbContext = CreateDbContext();
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: PreviousMigration,
            toMigration: OvernightMigration);

        var drop = script.IndexOf("DROP CONSTRAINT [CK_WorkCenterCalendarShifts_Window]", StringComparison.Ordinal);
        var add = script.IndexOf("ADD CONSTRAINT [CK_WorkCenterCalendarShifts_Window] CHECK ([StartTime] <> [EndTime])", StringComparison.Ordinal);
        Assert.True(drop >= 0, "Up must drop the same-day window check.");
        Assert.True(add > drop, "Up must add the start-differs-from-end check after dropping the old one.");
        Assert.DoesNotContain("THROW", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM [organization].[WorkCenterCalendarShifts]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Rolling_the_overnight_migration_back_refuses_before_the_same_day_check_returns()
    {
        using var dbContext = CreateDbContext();
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: OvernightMigration,
            toMigration: PreviousMigration);

        var refusal = script.IndexOf("THROW 51444", StringComparison.Ordinal);
        var drop = script.IndexOf("DROP CONSTRAINT [CK_WorkCenterCalendarShifts_Window]", StringComparison.Ordinal);
        var sameDay = script.IndexOf("CHECK ([StartTime] < [EndTime])", StringComparison.Ordinal);
        Assert.True(refusal >= 0, "The Down script must refuse while an overnight shift exists (forward fix).");
        Assert.True(drop > refusal, "The refusal must run before the check is dropped.");
        Assert.True(sameDay > drop, "The same-day check returns only after the refusal and the drop.");
        Assert.DoesNotContain("DELETE FROM [organization].[WorkCenterCalendarShifts]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_overnight_migrations_Down_starts_with_its_guard_and_Up_has_no_sql()
    {
        var migration = new AllowWorkCenterOvernightShifts();

        var downGuard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51444", downGuard.Sql, StringComparison.Ordinal);
        Assert.Contains("[organization].[WorkCenterCalendarShifts]", downGuard.Sql, StringComparison.Ordinal);
        Assert.Contains("[EndTime] < [StartTime]", downGuard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal(
            "[StartTime] <> [EndTime]",
            Assert.Single(migration.UpOperations.OfType<AddCheckConstraintOperation>()).Sql);
        Assert.Equal(
            "[StartTime] < [EndTime]",
            Assert.Single(migration.DownOperations.OfType<AddCheckConstraintOperation>()).Sql);
    }

    private static OrganizationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=GoymenErpOrganizationOvernightShiftScript;Trusted_Connection=True;")
            .Options;
        return new OrganizationDbContext(options, new CompanyContext(), new OrganizationBootstrapWriteScope());
    }
}

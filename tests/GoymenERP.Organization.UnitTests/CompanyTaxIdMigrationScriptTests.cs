using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Organization.UnitTests;

/// <summary>
/// KIM-S23 (KIM-D28, DEC-ORG-005): the company VKN migration refuses to tighten the schema over data that breaks the
/// rule (a real company without a VKN, two companies sharing one) instead of guessing a fix, and its Down refuses
/// while a change request would be lost (forward fix).
/// </summary>
public sealed class CompanyTaxIdMigrationScriptTests
{
    private const string TaxIdMigration = "AddCompanyTaxIdChangeRequests";
    private const string PreviousMigration = "AddWorkCenterCalendar";

    [Fact]
    public void The_tax_id_migration_creates_the_request_table_and_the_company_vkn_rules()
    {
        using var dbContext = CreateDbContext();
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: PreviousMigration,
            toMigration: TaxIdMigration);

        Assert.Contains("CREATE TABLE [organization].[CompanyTaxIdChangeRequests]", script, StringComparison.Ordinal);
        Assert.Contains("CK_Companies_Vkn_RequiredUnlessPlaceholder", script, StringComparison.Ordinal);
        Assert.Contains("IX_Companies_Vkn", script, StringComparison.Ordinal);
        Assert.Contains("IX_CompanyTaxIdChangeRequests_CompanyId_Pending", script, StringComparison.Ordinal);
        Assert.Contains("CK_CompanyTaxIdChangeRequests_MakerChecker", script, StringComparison.Ordinal);
        Assert.Contains("CK_CompanyTaxIdChangeRequests_Decision", script, StringComparison.Ordinal);
        Assert.Contains("CK_CompanyTaxIdChangeRequests_ProposedVkn", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE CASCADE", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_up_script_refuses_bad_company_data_before_the_vkn_index_and_check_are_added()
    {
        using var dbContext = CreateDbContext();
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: PreviousMigration,
            toMigration: TaxIdMigration);

        var missingVkn = script.IndexOf("THROW 51445", StringComparison.Ordinal);
        var duplicateVkn = script.IndexOf("THROW 51442", StringComparison.Ordinal);
        var uniqueIndex = script.IndexOf("CREATE UNIQUE INDEX [IX_Companies_Vkn]", StringComparison.Ordinal);
        var requiredCheck = script.IndexOf("CK_Companies_Vkn_RequiredUnlessPlaceholder", StringComparison.Ordinal);
        Assert.True(missingVkn >= 0, "Up must refuse a real company without a VKN instead of inventing one.");
        Assert.True(duplicateVkn >= 0, "Up must refuse two companies sharing a VKN instead of choosing one.");
        Assert.True(uniqueIndex > duplicateVkn, "The duplicate refusal must run before the unique index is created.");
        Assert.True(requiredCheck > missingVkn, "The missing-VKN refusal must run before the check is added.");
    }

    [Fact]
    public void Rolling_the_tax_id_migration_back_refuses_before_the_request_table_is_dropped()
    {
        using var dbContext = CreateDbContext();
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: TaxIdMigration,
            toMigration: PreviousMigration);

        var refusal = script.IndexOf("THROW 51443", StringComparison.Ordinal);
        var drop = script.IndexOf("DROP TABLE [organization].[CompanyTaxIdChangeRequests]", StringComparison.Ordinal);
        Assert.True(refusal >= 0, "The Down script must refuse while change requests exist (forward fix).");
        Assert.True(drop > refusal, "The refusal must run before the request table is dropped.");
    }

    [Fact]
    public void The_tax_id_migrations_Down_starts_with_its_guard_and_Up_guards_come_first()
    {
        var migration = new AddCompanyTaxIdChangeRequests();

        var downGuard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51443", downGuard.Sql, StringComparison.Ordinal);
        Assert.Contains("[organization].[CompanyTaxIdChangeRequests]", downGuard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());

        var upGuards = migration.UpOperations.TakeWhile(operation => operation is SqlOperation).Cast<SqlOperation>().ToArray();
        Assert.Contains(upGuards, guard => guard.Sql.Contains("THROW 51445", StringComparison.Ordinal));
        Assert.Contains(upGuards, guard => guard.Sql.Contains("THROW 51442", StringComparison.Ordinal));
        Assert.Equal(upGuards.Length, migration.UpOperations.OfType<SqlOperation>().Count());
        Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Single(migration.DownOperations.OfType<DropTableOperation>());
    }

    private static OrganizationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=GoymenErpOrganizationTaxIdScript;Trusted_Connection=True;")
            .Options;
        return new OrganizationDbContext(options, new CompanyContext(), new OrganizationBootstrapWriteScope());
    }
}

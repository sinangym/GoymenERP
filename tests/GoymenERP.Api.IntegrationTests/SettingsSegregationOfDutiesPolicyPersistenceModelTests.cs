using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-024 (NS-01): the persistent segregation of duties table — company inside the key, no database default for the setting
/// (the decided default is the absence of a row), the identity and recorded checks, the company query filter, a pure DDL Up
/// and a forward-fix Down that refuses first (THROW 51622) while any version exists.
/// </summary>
public sealed class SettingsSegregationOfDutiesPolicyPersistenceModelTests
{
    [Fact]
    public void A_segregation_of_duties_version_is_keyed_by_company_and_version_with_its_checks_and_company_filter()
    {
        using var context = CreateDbContext();
        var policy = Entity<SegregationOfDutiesPolicyVersionRow>(context);

        Assert.Equal(("settings", "SegregationOfDutiesPolicyVersions"), (policy.GetSchema(), policy.GetTableName()));
        Assert.Equal(["CompanyId", "Version"], policy.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Empty(policy.GetIndexes());
        var checks = Checks(policy);
        Assert.Equal(2, checks.Count);
        Assert.Contains("[CompanyId] <>", checks["CK_SettingsSegregationOfDutiesPolicyVersions_Identity"], StringComparison.Ordinal);
        Assert.Contains("[Version] > 0", checks["CK_SettingsSegregationOfDutiesPolicyVersions_Identity"], StringComparison.Ordinal);
        Assert.Contains("[RecordedBy]", checks["CK_SettingsSegregationOfDutiesPolicyVersions_Recorded"], StringComparison.Ordinal);
        Assert.Contains("[Reason]", checks["CK_SettingsSegregationOfDutiesPolicyVersions_Recorded"], StringComparison.Ordinal);
        Assert.Null(policy.FindProperty("PreparerMayApprove")!.GetDefaultValueSql());
        Assert.Equal(SegregationOfDutiesPolicyInputLimitsV1.ReasonMaximumLength, policy.FindProperty("Reason")!.GetMaxLength());
        Assert.NotNull(policy.GetDeclaredQueryFilters().SingleOrDefault());
    }

    [Fact]
    public void The_migration_Up_is_pure_DDL_and_its_Down_refuses_first_while_any_segregation_of_duties_version_exists()
    {
        var migration = new AddSegregationOfDutiesPolicyVersions();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var created = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal(("settings", "SegregationOfDutiesPolicyVersions"), (created.Schema, created.Name));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51622", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[SegregationOfDutiesPolicyVersions]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<DropTableOperation>());
    }

    private static IEntityType Entity<T>(SettingsDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(T))
        ?? throw new InvalidOperationException($"{typeof(T).Name} is not mapped.");

    private static Dictionary<string, string> Checks(IEntityType entity) =>
        entity.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);

    private static SettingsDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True")
            .Options, new CompanyContext());
}

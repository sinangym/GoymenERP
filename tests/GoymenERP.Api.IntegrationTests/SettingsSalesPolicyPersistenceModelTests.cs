using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-029 S4b: the persistent Sales delivery tolerance / approval amount threshold tables — company inside every key, the
/// check constraints equal to the DbContext constants, the one-profile-per-company index, the company query filter, a pure
/// DDL Up and a forward-fix Down that refuses first (THROW 51311) while any version or transfer marker exists.
/// </summary>
public sealed class SettingsSalesPolicyPersistenceModelTests
{
    [Fact]
    public void The_transfer_marker_is_keyed_by_company_and_family_and_checks_both()
    {
        using var context = CreateDbContext();
        var marker = Entity<SalesPolicyBaselineTransferRow>(context);

        Assert.Equal(("settings", "SalesPolicyBaselineTransfers"), (marker.GetSchema(), marker.GetTableName()));
        Assert.Equal(["CompanyId", "PolicyFamily"], marker.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var checks = Checks(marker);
        Assert.Equal(SettingsDbContext.SalesPolicyFamilyCheck, checks["CK_SettingsSalesPolicyBaselineTransfers_Family"]);
        Assert.Equal("[EntryCount] >= 0", checks["CK_SettingsSalesPolicyBaselineTransfers_EntryCount"]);
        Assert.Contains("[CompanyId] <>", checks["CK_SettingsSalesPolicyBaselineTransfers_Company"], StringComparison.Ordinal);
        Assert.NotNull(marker.GetDeclaredQueryFilters().SingleOrDefault());
    }

    [Fact]
    public void A_tolerance_version_is_keyed_by_company_and_version_with_ordered_percents_and_one_row_per_profile()
    {
        using var context = CreateDbContext();
        var tolerance = Entity<SalesDeliveryToleranceVersionRow>(context);

        Assert.Equal(("settings", "SalesDeliveryToleranceVersions"), (tolerance.GetSchema(), tolerance.GetTableName()));
        Assert.Equal(["CompanyId", "Version"], tolerance.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var profile = Assert.Single(tolerance.GetIndexes());
        Assert.True(profile.IsUnique);
        Assert.Equal(["CompanyId", "ProfileId"], profile.Properties.Select(property => property.Name));
        var checks = Checks(tolerance);
        Assert.Equal(SettingsDbContext.SalesDeliveryTolerancePercentCheck, checks["CK_SettingsSalesDeliveryToleranceVersions_Percents"]);
        Assert.Equal(SettingsDbContext.SalesPolicySourceKindCheck, checks["CK_SettingsSalesDeliveryToleranceVersions_Source"]);
        Assert.All(new[] { "WarnPercent", "ApprovalPercent", "BlockPercent" }, name =>
            Assert.Equal<(int?, int?)>((7, 4), (tolerance.FindProperty(name)!.GetPrecision(), tolerance.FindProperty(name)!.GetScale())));
        Assert.True(tolerance.FindProperty("DecisionReference")!.IsNullable);
        Assert.NotNull(tolerance.GetDeclaredQueryFilters().SingleOrDefault());
    }

    [Fact]
    public void A_threshold_version_is_keyed_by_company_currency_and_version_and_an_enabled_one_carries_its_amount()
    {
        using var context = CreateDbContext();
        var threshold = Entity<SalesApprovalAmountThresholdVersionRow>(context);

        Assert.Equal(("settings", "SalesApprovalAmountThresholdVersions"), (threshold.GetSchema(), threshold.GetTableName()));
        Assert.Equal(["CompanyId", "CurrencyId", "Version"],
            threshold.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var profile = Assert.Single(threshold.GetIndexes());
        Assert.True(profile.IsUnique);
        Assert.Equal(["CompanyId", "ProfileId"], profile.Properties.Select(property => property.Name));
        var checks = Checks(threshold);
        Assert.Equal(SettingsDbContext.SalesApprovalThresholdAmountCheck,
            checks["CK_SettingsSalesApprovalAmountThresholdVersions_Amount"]);
        Assert.Equal(SettingsDbContext.SalesPolicySourceKindCheck, checks["CK_SettingsSalesApprovalAmountThresholdVersions_Source"]);
        var amount = threshold.FindProperty("ThresholdAmount")!;
        Assert.Equal<(int?, int?, bool)>((28, 8, true), (amount.GetPrecision(), amount.GetScale(), amount.IsNullable));
        Assert.True(threshold.FindProperty("ThresholdAmountScale")!.IsNullable);
        Assert.NotNull(threshold.GetDeclaredQueryFilters().SingleOrDefault());
    }

    [Fact]
    public void The_migration_Up_is_pure_DDL_and_its_Down_refuses_first_while_any_sales_policy_row_exists()
    {
        var migration = new AddSalesPolicyVersions();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(3, migration.UpOperations.OfType<CreateTableOperation>().Count());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51311", guard.Sql, StringComparison.Ordinal);
        Assert.All(new[] { "SalesPolicyBaselineTransfers", "SalesDeliveryToleranceVersions", "SalesApprovalAmountThresholdVersions" },
            table => Assert.Contains($"[settings].[{table}]", guard.Sql, StringComparison.Ordinal));
        Assert.Equal(3, migration.DownOperations.OfType<DropTableOperation>().Count());
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

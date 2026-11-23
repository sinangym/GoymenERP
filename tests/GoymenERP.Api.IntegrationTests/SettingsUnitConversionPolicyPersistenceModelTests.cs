using System.Globalization;
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
/// F-004: the persistent unit conversion policy table — company inside the key, one row per profile, the check constraints
/// equal to the DbContext constants and to the published code lists and limits, the company query filter, a pure DDL Up and a
/// forward-fix Down that refuses first (THROW 51330) while any version exists.
/// </summary>
public sealed class SettingsUnitConversionPolicyPersistenceModelTests
{
    [Fact]
    public void A_unit_conversion_version_is_keyed_by_company_and_version_with_one_row_per_profile_and_its_checks()
    {
        using var context = CreateDbContext();
        var policy = Entity<UnitConversionPolicyVersionRow>(context);

        Assert.Equal(("settings", "UnitConversionPolicyVersions"), (policy.GetSchema(), policy.GetTableName()));
        Assert.Equal(["CompanyId", "Version"], policy.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var profile = Assert.Single(policy.GetIndexes());
        Assert.True(profile.IsUnique);
        Assert.Equal(["CompanyId", "ProfileId"], profile.Properties.Select(property => property.Name));
        var checks = Checks(policy);
        Assert.Equal(SettingsDbContext.UnitConversionDecimalsCheck, checks["CK_SettingsUnitConversionPolicyVersions_Decimals"]);
        Assert.Equal(SettingsDbContext.UnitConversionRoundingCheck, checks["CK_SettingsUnitConversionPolicyVersions_Rounding"]);
        Assert.Equal(SettingsDbContext.UnitConversionToleranceCheck, checks["CK_SettingsUnitConversionPolicyVersions_Tolerance"]);
        Assert.Equal("[DensityGramsPerCubicCentimetre] > 0", checks["CK_SettingsUnitConversionPolicyVersions_Density"]);
        Assert.Contains("[CompanyId] <>", checks["CK_SettingsUnitConversionPolicyVersions_Identity"], StringComparison.Ordinal);
        Assert.Contains("[Reason]", checks["CK_SettingsUnitConversionPolicyVersions_Recorded"], StringComparison.Ordinal);
        Assert.Equal<(int?, int?)>((12, 6),
            (policy.FindProperty("DensityGramsPerCubicCentimetre")!.GetPrecision(),
                policy.FindProperty("DensityGramsPerCubicCentimetre")!.GetScale()));
        Assert.Equal<(int?, int?)>((7, 4),
            (policy.FindProperty("WeighbridgeTolerancePercent")!.GetPrecision(),
                policy.FindProperty("WeighbridgeTolerancePercent")!.GetScale()));
        Assert.Equal(UnitConversionPolicyInputLimitsV1.ReasonMaximumLength, policy.FindProperty("Reason")!.GetMaxLength());
        Assert.NotNull(policy.GetDeclaredQueryFilters().SingleOrDefault());
    }

    [Fact]
    public void The_checks_hold_exactly_the_published_code_lists_and_limits()
    {
        Assert.All(UnitConversionRoundingModesV1.All,
            code => Assert.Contains($"'{code}'", SettingsDbContext.UnitConversionRoundingCheckValues, StringComparison.Ordinal));
        Assert.Equal(UnitConversionRoundingModesV1.All.Count,
            SettingsDbContext.UnitConversionRoundingCheckValues.Count(character => character == ',') + 1);
        Assert.All(UnitConversionToleranceBoundariesV1.All.Concat(UnitConversionToleranceBasesV1.All),
            code => Assert.Contains($"'{code}'", SettingsDbContext.UnitConversionToleranceCheck, StringComparison.Ordinal));
        var maximumDecimals = UnitConversionPolicyInputLimitsV1.MaximumRoundingDecimals.ToString(CultureInfo.InvariantCulture);
        Assert.Contains($"BETWEEN 0 AND {maximumDecimals}", SettingsDbContext.UnitConversionDecimalsCheck, StringComparison.Ordinal);
        Assert.Contains(
            $"<= {UnitConversionPolicyInputLimitsV1.MaximumPercent.ToString(CultureInfo.InvariantCulture)}",
            SettingsDbContext.UnitConversionToleranceCheck, StringComparison.Ordinal);
    }

    [Fact]
    public void The_migration_Up_is_pure_DDL_and_its_Down_refuses_first_while_any_unit_conversion_version_exists()
    {
        var migration = new AddUnitConversionPolicyVersions();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var created = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal(("settings", "UnitConversionPolicyVersions"), (created.Schema, created.Name));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51330", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[UnitConversionPolicyVersions]", guard.Sql, StringComparison.Ordinal);
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

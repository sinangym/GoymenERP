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
/// F-029 S1/S2 (URN-D2…D5): the persistent unit profile tables — company inside both keys, the version rows keyed by
/// (company, unit, version) with one row per profile id, the check constraints equal to the DbContext constants and to the
/// published scale limits (Ö2's kilogram bound included), the company query filter on both tables, a pure DDL Up and a
/// forward-fix Down that refuses first (THROW 51341) while any version or a baseline transfer exists.
/// </summary>
public sealed class SettingsUnitProfilePersistenceModelTests
{
    [Fact]
    public void A_unit_profile_version_is_keyed_by_company_unit_and_version_with_one_row_per_profile_and_its_checks()
    {
        using var context = CreateDbContext();
        var profile = Entity<UnitProfileVersionRow>(context);

        Assert.Equal(("settings", "UnitProfileVersions"), (profile.GetSchema(), profile.GetTableName()));
        Assert.Equal(["CompanyId", "UnitId", "Version"], profile.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var byProfile = Assert.Single(profile.GetIndexes());
        Assert.True(byProfile.IsUnique);
        Assert.Equal(["CompanyId", "ProfileId"], byProfile.Properties.Select(property => property.Name));
        var checks = Checks(profile);
        Assert.Equal(SettingsDbContext.UnitProfileScaleCheck, checks["CK_SettingsUnitProfileVersions_Scale"]);
        Assert.Equal(SettingsDbContext.UnitProfileCodesCheck, checks["CK_SettingsUnitProfileVersions_Codes"]);
        Assert.Equal(SettingsDbContext.SalesPolicySourceKindCheck, checks["CK_SettingsUnitProfileVersions_Source"]);
        Assert.Contains("[CompanyId] <>", checks["CK_SettingsUnitProfileVersions_Identity"], StringComparison.Ordinal);
        Assert.Contains("[Version] > 0", checks["CK_SettingsUnitProfileVersions_Identity"], StringComparison.Ordinal);
        Assert.Contains("[Reason]", checks["CK_SettingsUnitProfileVersions_Recorded"], StringComparison.Ordinal);
        Assert.Equal(UnitProfileInputLimitsV1.ReasonMaximumLength, profile.FindProperty("Reason")!.GetMaxLength());
        Assert.Equal(UnitProfileInputLimitsV1.DecisionReferenceMaximumLength,
            profile.FindProperty("DecisionReference")!.GetMaxLength());
        Assert.True(profile.FindProperty("DecisionReference")!.IsNullable);
        Assert.False(profile.FindProperty("UnitCode")!.IsNullable);
        Assert.NotNull(profile.GetDeclaredQueryFilters().SingleOrDefault());
    }

    [Fact]
    public void The_baseline_transfer_marker_is_one_row_per_company_with_its_checks_and_the_company_filter()
    {
        using var context = CreateDbContext();
        var transfer = Entity<UnitProfileBaselineTransferRow>(context);

        Assert.Equal(("settings", "UnitProfileBaselineTransfers"), (transfer.GetSchema(), transfer.GetTableName()));
        Assert.Equal(["CompanyId"], transfer.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var checks = Checks(transfer);
        Assert.Contains("[CompanyId] <>", checks["CK_SettingsUnitProfileBaselineTransfers_Company"], StringComparison.Ordinal);
        Assert.Equal("[EntryCount] >= 0", checks["CK_SettingsUnitProfileBaselineTransfers_EntryCount"]);
        Assert.Contains("[TransferredBy]", checks["CK_SettingsUnitProfileBaselineTransfers_Recorded"], StringComparison.Ordinal);
        Assert.Equal(UnitProfileInputLimitsV1.ReasonMaximumLength, transfer.FindProperty("Reason")!.GetMaxLength());
        Assert.NotNull(transfer.GetDeclaredQueryFilters().SingleOrDefault());
    }

    [Fact]
    public void The_scale_check_holds_exactly_the_published_scale_limits()
    {
        var maximum = UnitProfileInputLimitsV1.MaximumScale.ToString(CultureInfo.InvariantCulture);
        var kilogram = UnitProfileInputLimitsV1.KilogramMaximumScale.ToString(CultureInfo.InvariantCulture);

        Assert.Contains($"[Scale] BETWEEN 0 AND {maximum}", SettingsDbContext.UnitProfileScaleCheck, StringComparison.Ordinal);
        Assert.Contains($"[IsKilogram] = 0 OR [Scale] <= {kilogram}", SettingsDbContext.UnitProfileScaleCheck,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_migration_Up_is_pure_DDL_and_its_Down_refuses_first_while_any_unit_profile_or_transfer_exists()
    {
        var migration = new AddUnitProfileVersions();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(
            [("settings", "UnitProfileBaselineTransfers"), ("settings", "UnitProfileVersions")],
            migration.UpOperations.OfType<CreateTableOperation>().Select(table => (table.Schema!, table.Name))
                .OrderBy(table => table.Name, StringComparer.Ordinal));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51341", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[UnitProfileVersions]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[UnitProfileBaselineTransfers]", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(2, migration.DownOperations.OfType<DropTableOperation>().Count());
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

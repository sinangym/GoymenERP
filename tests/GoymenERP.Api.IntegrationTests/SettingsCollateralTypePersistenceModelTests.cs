using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsCollateralTypePersistenceModelTests
{
    [Fact]
    public void The_generated_catalog_migration_is_pure_DDL_and_refuses_unsafe_Down_first()
    {
        var migration = new AddCollateralTypeCatalog();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(["CollateralTypes", "CollateralTypeVersions"], migration.UpOperations.OfType<CreateTableOperation>().Select(operation => operation.Name));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51941", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[CollateralTypes]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[CollateralTypeVersions]", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(2, migration.DownOperations.OfType<DropTableOperation>().Count());
    }

    [Fact]
    public void The_catalog_is_company_keyed_append_only_with_a_unique_code_company_FK_and_no_seed_or_defaults()
    {
        using var context = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True").Options, new CompanyContext());
        var model = context.GetService<IDesignTimeModel>().Model;
        var entry = model.FindEntityType(typeof(CollateralTypeRow))!;
        var version = model.FindEntityType(typeof(CollateralTypeVersionRow))!;
        Assert.Equal(["CompanyId", "EntryId"], entry.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(["CompanyId", "EntryId", "Version"], version.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(["CompanyId", "Code"], Assert.Single(entry.GetIndexes(), index => index.IsUnique).Properties.Select(property => property.Name));
        Assert.Equal(["CompanyId", "EntryId"], Assert.Single(version.GetForeignKeys()).Properties.Select(property => property.Name));
        Assert.Single(entry.GetDeclaredQueryFilters()); Assert.Single(version.GetDeclaredQueryFilters());
        Assert.Empty(entry.GetSeedData()); Assert.Empty(version.GetSeedData());
        Assert.All(entry.GetProperties().Concat(version.GetProperties()), property => { Assert.Null(property.GetDefaultValue()); Assert.Null(property.GetDefaultValueSql()); });
        Assert.Equal(64, version.FindProperty("RatePercent")!.GetMaxLength());
        var kind = Assert.Single(entry.GetCheckConstraints(), check => check.Name == "CK_SettingsCollateralTypes_Kind").Sql;
        Assert.All(Enum.GetNames<SettingsCollateralKindV1>(), name => Assert.Contains("'" + name + "'", kind, StringComparison.Ordinal));
        Assert.DoesNotContain("Cheque", kind, StringComparison.Ordinal); Assert.DoesNotContain("Note", kind, StringComparison.Ordinal);
    }

    [Fact]
    public void The_name_is_registered_company_wide_without_granting_or_changing_Critical_classification()
    {
        Assert.Equal(ErpPermissions.SettingsCollateralTypesManage, SettingsCollateralTypePermissionsV1.Manage);
        Assert.True(ErpPermissions.IsKnown(SettingsCollateralTypePermissionsV1.Manage));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(SettingsCollateralTypePermissionsV1.Manage));
        Assert.False(ErpPermissions.IsCritical(SettingsCollateralTypePermissionsV1.Manage));
    }
}

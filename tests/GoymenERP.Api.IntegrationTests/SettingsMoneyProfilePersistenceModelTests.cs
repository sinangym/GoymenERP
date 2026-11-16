using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsMoneyProfilePersistenceModelTests
{
    [Fact]
    public void Money_profile_versions_are_company_operation_scoped_with_closed_scale_bounds_and_no_seed()
    {
        using var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True").Options, new CompanyContext());
        var entity = database.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MoneyProfileVersionRow))!;
        Assert.Equal(("settings", "MoneyProfileVersions"), (entity.GetSchema(), entity.GetTableName()));
        Assert.Equal(["CompanyId", "CurrencyId", "Operation", "Version"], entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.Equal(SettingsDbContext.MoneyProfileScaleCheck,
            Assert.Single(entity.GetCheckConstraints(), constraint => constraint.Name == "CK_SettingsMoneyProfileVersions_Scale").Sql);
        Assert.Equal(128, entity.FindProperty(nameof(MoneyProfileVersionRow.Operation))!.GetMaxLength());
        Assert.All(entity.GetProperties(), property => { Assert.Null(property.GetDefaultValue()); Assert.Null(property.GetDefaultValueSql()); });
    }

    [Fact]
    public void Money_profile_migration_is_DDL_and_refuses_Down_before_dropping_recorded_profiles()
    {
        var migration = new AddMoneyProfileVersions();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(["MoneyProfileBaselineTransfers", "MoneyProfileVersions"],
            migration.UpOperations.OfType<CreateTableOperation>().Select(operation => operation.Name).Order(StringComparer.Ordinal));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51940", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Equal(2, migration.DownOperations.OfType<DropTableOperation>().Count());
    }
}

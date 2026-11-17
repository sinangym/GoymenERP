using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsWarehouseTransferScalePersistenceModelTests
{
    [Fact]
    public void Warehouse_scale_migration_is_generated_DDL_without_seed_and_refuses_down_before_history_is_dropped()
    {
        using var database = Context();
        Assert.False(database.Database.HasPendingModelChanges());
        Assert.EndsWith("_AddWarehouseTransferScalePolicies", database.Database.GetMigrations().Last(), StringComparison.Ordinal);
        var migration = new AddWarehouseTransferScalePolicies();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation or InsertDataOperation or UpdateDataOperation or DeleteDataOperation);
        Assert.Equal("WarehouseTransferScaleVersions", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51990", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[WarehouseTransferScaleVersions]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
    [Fact]
    public void Warehouse_scale_versions_have_company_location_version_identity_no_default_and_a_company_filter()
    {
        using var database = Context();
        var entity = database.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(WarehouseTransferScaleVersionRow))!;
        Assert.Equal(new[] { "CompanyId", "LocationId", "Version" }, entity.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.Equal(SettingsDbContext.WarehouseTransferScaleIdentityCheck, entity.GetCheckConstraints().Single(x => x.Name?.EndsWith("_Identity", StringComparison.Ordinal) == true).Sql);
        Assert.Equal(SettingsDbContext.WarehouseTransferScaleAuditCheck, entity.GetCheckConstraints().Single(x => x.Name?.EndsWith("_Audit", StringComparison.Ordinal) == true).Sql);
        foreach (var property in entity.GetProperties())
        {
            Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
            Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql));
        }
    }
    [Fact]
    public void A_recorded_warehouse_scale_choice_cannot_be_rewritten_or_deleted_before_database_access()
    {
        using var database = Context();
        var row = new WarehouseTransferScaleVersionRow { CompanyId = TestCompanyIds.CompanyA, LocationId = Guid.NewGuid(), Version = 1,
            RecordId = Guid.NewGuid(), RequireExitWeighing = false, LocationVersion = 1, RecordedBy = "SENTETIK",
            RecordedAtUtc = DateTimeOffset.UtcNow, Reason = "SENTETIK" };
        database.Attach(row); row.RequireExitWeighing = true;
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        database.ChangeTracker.Clear(); database.Attach(row); database.Remove(row);
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
    }
    private static SettingsDbContext Context()
    {
        var company = new CompanyContext(); company.Set(TestCompanyIds.CompanyA);
        return new(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer("Server=unused;Database=SyntheticModelOnly;Integrated Security=True;TrustServerCertificate=True").Options, company);
    }
}

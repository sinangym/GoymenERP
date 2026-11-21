using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsManufacturingProfileBaselinePersistenceModelTests
{
    [Fact]
    public void Baseline_transfer_is_company_kind_unique_filtered_append_only_and_rollback_protects_its_source()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var context = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True")
            .Options, company);
        Assert.False(context.Database.HasPendingModelChanges());
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ManufacturingProfileBaselineTransferRow))!;
        Assert.Equal(new[] { "CompanyId", "Kind" }, entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.True(entity.FindProperty(nameof(ManufacturingProfileBaselineTransferRow.Version))!.IsConcurrencyToken);
        Assert.Contains(entity.GetCheckConstraints(), check => check.Sql.Contains("ISJSON", StringComparison.Ordinal));
        var migration = new AddManufacturingProfileBaselineTransfers();
        Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation or InsertDataOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51966", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        var row = new ManufacturingProfileBaselineTransferRow { CompanyId = company.CompanyId, Kind = "parameters", Version = 1 };
        context.Attach(row);
        row.Reason = "SENTETIK modification";
        Assert.Contains("Ayarlar kaydı değiştirilemez", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message, StringComparison.Ordinal);
    }
}

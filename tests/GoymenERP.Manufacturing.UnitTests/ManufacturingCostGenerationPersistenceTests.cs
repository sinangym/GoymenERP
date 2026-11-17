using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed class ManufacturingCostGenerationPersistenceTests
{
    [Fact]
    public void Generation_migration_is_DDL_only_and_refuses_before_dropping_any_immutable_cost_evidence()
    {
        var migration = new AddManufacturingCostGenerations();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51960", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[manufacturing].[CostGenerations]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }
    [Fact]
    public async Task Cost_generation_evidence_is_company_scoped_immutable_and_uniquely_versions_each_physical_close()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        await using var context = new ManufacturingDbContext(new DbContextOptionsBuilder<ManufacturingDbContext>()
            .UseSqlServer("Server=unopened.invalid;Database=synthetic;Integrated Security=true;Encrypt=false").Options, company);
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CostGenerationRow))!;
        Assert.Equal(["CompanyId", "GenerationId", "Revision"], entity.FindPrimaryKey()!.Properties.Select(item => item.Name));
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(item => item.Name)
            .SequenceEqual(["CompanyId", "OrderId", "ClosedOrderVersion", "Revision"]));
        Assert.Equal(ManufacturingDbContext.CostGenerationCheck, Assert.Single(entity.GetCheckConstraints()).Sql);
        Assert.Single(entity.GetDeclaredQueryFilters());
        var row = new CostGenerationRow { CompanyId = company.CompanyId, GenerationId = Guid.NewGuid(), Revision = 1, InputFingerprint = new string('a', 64) };
        context.CostGenerations.Attach(row); row.EvidenceJson = "{\"changed\":true}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear(); context.CostGenerations.Add(new CostGenerationRow { CompanyId = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }
}

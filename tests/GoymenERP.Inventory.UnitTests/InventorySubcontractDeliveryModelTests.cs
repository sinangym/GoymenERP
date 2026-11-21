using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventorySubcontractDeliveryModelTests
{
    [Fact]
    public void Subcontract_source_model_has_company_filter_and_same_company_allocation_and_label_foreign_keys()
    {
        using var db = Context();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(InventorySubcontractAllocationSourceRow))!;
        Assert.Equal("inventory", entity.GetSchema());
        Assert.Equal(["CompanyId", "AllocationId"], entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.Equal(2, entity.GetForeignKeys().Count());
        Assert.All(entity.GetForeignKeys(), key =>
        {
            Assert.Equal("CompanyId", key.Properties[0].Name);
            Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior);
        });
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public void Recorded_subcontract_source_evidence_is_immutable_before_a_connection_is_opened(EntityState state)
    {
        using var db = Context();
        db.Entry(new InventorySubcontractAllocationSourceRow
        {
            CompanyId = CompanyA, AllocationId = Guid.NewGuid(), LabelId = Guid.NewGuid(),
            SourceJson = "{}", SourceHash = new string('a', 64),
        }).State = state;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public void Subcontract_source_migration_uses_ddl_and_refuses_51934_before_dropping_evidence_or_narrowing_demand_kind()
    {
        var migration = new AddInventorySubcontractDeliverySources();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51934,", guard, StringComparison.Ordinal);
        Assert.Contains("[inventory].[SubcontractAllocationSources]", guard, StringComparison.Ordinal);
        Assert.Contains("N'sales.subcontract-service-order-line'", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    private static InventoryDbContext Context()
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        return new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=SENTETIK.invalid;Database=SENTETIK;Connect Timeout=1;").Options, company);
    }
}

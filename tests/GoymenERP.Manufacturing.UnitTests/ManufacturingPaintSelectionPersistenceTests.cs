using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed class ManufacturingPaintSelectionPersistenceTests
{
    [Fact]
    public void Typed_paint_snapshots_are_nullable_json_on_creation_and_every_immutable_revision_with_a_guarded_down()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var db = new ManufacturingDbContext(new DbContextOptionsBuilder<ManufacturingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(PlanRow), typeof(PlanRevisionRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Single(entity.GetDeclaredQueryFilters());
            var proof = entity.FindProperty("PaintSelectionJson")!;
            Assert.True(proof.IsNullable); Assert.Equal("nvarchar(max)", proof.GetColumnType());
            Assert.Contains(entity.GetCheckConstraints(), check => check.Sql == ManufacturingDbContext.PlanPaintSelectionJsonCheck);
        }
        var migration = new AddManufacturingPaintSelections();
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51976", guard.Sql); Assert.Contains("[manufacturing].[Plans]", guard.Sql);
        Assert.Contains("[manufacturing].[PlanRevisions]", guard.Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

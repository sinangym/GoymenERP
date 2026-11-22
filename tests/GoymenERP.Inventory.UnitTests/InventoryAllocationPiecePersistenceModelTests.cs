using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>KLN-D119 / SAT-D434 (AK-4b): the piece columns of a stock allocation, their check and the forward-fix Down.</summary>
public sealed class InventoryAllocationPiecePersistenceModelTests
{
    [Theory]
    [InlineData("AllocatedPieces")]
    [InlineData("RemainingPieces")]
    public void Every_allocation_piece_column_is_a_nullable_int_under_the_piece_check(string column)
    {
        using var db = Context();
        var model = db.GetService<IDesignTimeModel>().Model;
        var entity = model.GetEntityTypes().Single(type => type.ClrType.Name == nameof(AllocationRow));

        var property = entity.FindProperty(column)!;

        Assert.True(property.IsNullable);
        Assert.Equal(typeof(int?), property.ClrType);
        Assert.Equal(InventoryDbContext.AllocationPiecesCheck,
            entity.GetCheckConstraints().Single(candidate => candidate.Name == "CK_Allocations_Pieces").Sql);
    }

    [Fact]
    public void The_allocation_piece_migration_is_pure_DDL_and_its_Down_refuses_first_while_any_allocation_holds_pieces()
    {
        var migration = new AddInventoryAllocationPieces();

        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal(2, migration.UpOperations.OfType<AddColumnOperation>().Count());
        Assert.All(migration.UpOperations.OfType<AddColumnOperation>(), column => Assert.True(column.IsNullable));
        Assert.Single(migration.UpOperations.OfType<AddCheckConstraintOperation>());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 52022", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[Allocations]", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(2, migration.DownOperations.OfType<DropColumnOperation>().Count());
    }

    private static InventoryDbContext Context()
    {
        var company = new CompanyContext(); company.Set(new("11000000-0000-0000-0000-000000000001"));
        return new(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}

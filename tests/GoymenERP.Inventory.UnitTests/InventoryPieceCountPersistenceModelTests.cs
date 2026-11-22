using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>KLN-D119 (AK-2): the piece-count columns, their checks and the forward-fix Down of their migration.</summary>
public sealed class InventoryPieceCountPersistenceModelTests
{
    public static TheoryData<string, string, string> PieceColumns => new()
    {
        { nameof(BalanceRow), "PieceCount", "CK_Balances_PieceCount" },
        { nameof(MovementLineRow), "PieceCount", "CK_MovementLines_PieceCount" },
        { nameof(LabelTransformationInputRow), "PieceCount", "CK_LabelTransformationInputs_PieceCount" },
        { nameof(LabelTransformationOutputRow), "PieceCount", "CK_LabelTransformationOutputs_PieceCount" },
        { nameof(StockCountRow), "BookPieceCount", "CK_StockCounts_PieceCount" },
        { nameof(StockCountRow), "CountedPieceCount", "CK_StockCounts_PieceCount" },
    };

    [Theory]
    [MemberData(nameof(PieceColumns))]
    public void Every_piece_count_column_is_a_nullable_int_with_its_check(string row, string column, string check)
    {
        using var db = Context();
        var model = db.GetService<IDesignTimeModel>().Model;
        var entity = model.GetEntityTypes().Single(type => type.ClrType.Name == row);

        var property = entity.FindProperty(column)!;

        Assert.True(property.IsNullable);
        Assert.Equal(typeof(int?), property.ClrType);
        Assert.Equal(ExpectedCheck(check), entity.GetCheckConstraints().Single(candidate => candidate.Name == check).Sql);
    }

    [Fact]
    public void The_piece_count_migration_is_pure_DDL_and_its_Down_refuses_first_while_any_piece_count_exists()
    {
        var migration = new AddInventoryLabelPieceCounts();

        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal(6, migration.UpOperations.OfType<AddColumnOperation>().Count());
        Assert.All(migration.UpOperations.OfType<AddColumnOperation>(), column => Assert.True(column.IsNullable));
        Assert.Equal(5, migration.UpOperations.OfType<AddCheckConstraintOperation>().Count());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 52021", guard.Sql, StringComparison.Ordinal);
        foreach (var table in new[] { "[inventory].[Balances]", "[inventory].[MovementLines]", "[inventory].[LabelTransformationInputs]",
                     "[inventory].[LabelTransformationOutputs]", "[inventory].[StockCounts]" })
            Assert.Contains(table, guard.Sql, StringComparison.Ordinal);
        Assert.Equal(6, migration.DownOperations.OfType<DropColumnOperation>().Count());
    }

    private static string ExpectedCheck(string name) => name switch
    {
        "CK_Balances_PieceCount" => InventoryDbContext.BalancePieceCountCheck,
        "CK_MovementLines_PieceCount" => InventoryDbContext.MovementLinePieceCountCheck,
        "CK_StockCounts_PieceCount" => InventoryDbContext.StockCountPieceCountCheck,
        _ => InventoryDbContext.LabelTransformationPieceCountCheck,
    };

    private static InventoryDbContext Context()
    {
        var company = new CompanyContext(); company.Set(new("11000000-0000-0000-0000-000000000001"));
        return new(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}

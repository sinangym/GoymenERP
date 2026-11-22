using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>KLN-D119 (AK-2b): the piece-count columns of the remaining movements, their checks and the forward-fix Down.</summary>
public sealed class InventoryMovementPieceCountPersistenceModelTests
{
    public static TheoryData<string, string, string> PieceColumns => new()
    {
        { nameof(InventoryTransferBucketRow), "PieceCount", "CK_TransferBuckets_PieceCount" },
        { nameof(InventoryTransferReceiptRow), "DispatchedPieceCount", "CK_TransferReceipts_PieceCount" },
        { nameof(InventoryTransferReceiptRow), "ReceivedPieceCount", "CK_TransferReceipts_PieceCount" },
        { nameof(InventoryTransferReceiptRow), "ShortagePieceCount", "CK_TransferReceipts_PieceCount" },
        { nameof(InventoryTransferReceiptRow), "OveragePieceCount", "CK_TransferReceipts_PieceCount" },
        { nameof(InventoryTransferOverageRenewalRow), "OveragePieceCount", "CK_TransferOverageRenewals_PieceCount" },
        { nameof(StockScrapRow), "BookPieceCount", "CK_StockScraps_PieceCount" },
        { nameof(StockScrapRow), "ScrapPieceCount", "CK_StockScraps_PieceCount" },
    };

    [Theory]
    [MemberData(nameof(PieceColumns))]
    public void Every_movement_piece_count_column_is_a_nullable_int_with_its_check(string row, string column, string check)
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
    public void The_movement_piece_count_migration_is_pure_DDL_and_its_Down_refuses_first_while_any_piece_count_exists()
    {
        var migration = new AddInventoryMovementPieceCounts();

        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal(8, migration.UpOperations.OfType<AddColumnOperation>().Count());
        Assert.All(migration.UpOperations.OfType<AddColumnOperation>(), column => Assert.True(column.IsNullable));
        Assert.Equal(4, migration.UpOperations.OfType<AddCheckConstraintOperation>().Count());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 52031", guard.Sql, StringComparison.Ordinal);
        foreach (var table in new[] { "[inventory].[TransferBuckets]", "[inventory].[TransferReceipts]",
                     "[inventory].[TransferOverageRenewals]", "[inventory].[StockScraps]" })
            Assert.Contains(table, guard.Sql, StringComparison.Ordinal);
        Assert.Equal(8, migration.DownOperations.OfType<DropColumnOperation>().Count());
    }

    private static string ExpectedCheck(string name) => name switch
    {
        "CK_TransferBuckets_PieceCount" => InventoryDbContext.TransferBucketPieceCountCheck,
        "CK_TransferReceipts_PieceCount" => InventoryDbContext.TransferReceiptPieceCountCheck,
        "CK_TransferOverageRenewals_PieceCount" => InventoryDbContext.TransferOverageRenewalPieceCountCheck,
        _ => InventoryDbContext.StockScrapPieceCountCheck,
    };

    private static InventoryDbContext Context()
    {
        var company = new CompanyContext(); company.Set(new("11000000-0000-0000-0000-000000000001"));
        return new(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}

using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryGoodsReceiptReversalModelTests
{
    [Fact]
    public void Receipt_reversal_migration_is_ddl_only_and_refuses_history_loss_before_every_down_drop()
    {
        var migration = new AddInventoryGoodsReceiptReversals();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51950", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[GoodsReceiptReversals]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[FutureSupplyConversionReversals]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Kind] = 16", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains(migration.UpOperations.OfType<AddCheckConstraintOperation>(),
            operation => operation.Name == "CK_Movements_Kind" && operation.Sql == "[Kind] BETWEEN 1 AND 16");
    }

    [Fact]
    public void Reversal_history_has_company_filters_exact_amount_checks_and_same_company_original_links()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var receipt = model.FindEntityType(typeof(GoodsReceiptReversalRow))!;
        var link = model.FindEntityType(typeof(FutureSupplyConversionReversalRow))!;
        Assert.Equal(("inventory", "GoodsReceiptReversals"), (receipt.GetSchema(), receipt.GetTableName()));
        Assert.Equal(["CompanyId", "Id"], receipt.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(["CompanyId", "ReversalId", "ConversionId"], link.FindPrimaryKey()!.Properties.Select(property => property.Name));
        foreach (var type in new[] { typeof(GoodsReceiptReversalRow), typeof(FutureSupplyConversionReversalRow) })
            Assert.Single(context.Model.FindEntityType(type)!.GetDeclaredQueryFilters());
        Assert.Contains(link.GetCheckConstraints(), check => check.Name == "CK_FutureSupplyConversionReversals_Quantity"
            && check.Sql == InventoryDbContext.FutureSupplyConversionReversalQuantityCheck);
        var original = Assert.Single(link.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(FutureSupplyConversionRow));
        Assert.Equal(["CompanyId", "ConversionId"], original.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, original.DeleteBehavior);
        foreach (var property in link.GetProperties().Where(property => property.ClrType == typeof(decimal)))
            Assert.Equal((18, 3), (property.GetPrecision(), property.GetScale()));
    }

    [Fact]
    public async Task Reversal_records_of_another_company_or_an_edited_original_are_refused_before_sql()
    {
        await using var foreign = Context();
        foreign.GoodsReceiptReversals.Add(new() { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidOperationException>(() => foreign.SaveChangesAsync());

        await using var edited = Context();
        var row = new FutureSupplyConversionReversalRow { CompanyId = CompanyId, ReversalId = Guid.NewGuid(),
            ConversionId = Guid.NewGuid(), UndoneQuantity = 1m, UndoneKg = 1m, ReopenedQuantity = 1m };
        edited.Attach(row);
        row.ReopenedQuantity = 0m;
        row.OverflowReleasedQuantity = 1m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => edited.SaveChangesAsync());
    }

    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static InventoryDbContext Context()
    {
        var company = new CompanyContext(); company.Set(CompanyId);
        return new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}

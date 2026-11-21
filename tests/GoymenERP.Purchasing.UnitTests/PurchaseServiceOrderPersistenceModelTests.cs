using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseServiceOrderPersistenceModelTests
{
    [Fact]
    public void Service_history_is_company_filtered_and_the_invoice_binding_references_actual_service_acceptance_without_relaxing_the_goods_foreign_key()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var db = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(PurchaseServiceOrderRow), typeof(PurchaseServiceOrderRevisionRow), typeof(PurchaseServiceOrderAttemptRow),
            typeof(PurchaseServiceAcceptanceRow), typeof(PurchaseServiceInvoiceBindingRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("purchasing", entity.GetSchema()); Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Contains("CompanyId", entity.FindPrimaryKey()!.Properties.Select(x => x.Name));
        }
        Assert.True(model.FindEntityType(typeof(PurchaseServiceOrderRow))!.FindProperty("Version")!.IsConcurrencyToken);
        var fact = model.FindEntityType(typeof(PurchaseServiceAcceptanceRow))!;
        Assert.Equal((24, 6), (fact.FindProperty("Quantity")!.GetPrecision(), fact.FindProperty("Quantity")!.GetScale()));
        Assert.Contains(fact.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "AcceptanceId"]));
        var service = model.FindEntityType(typeof(PurchaseServiceInvoiceBindingRow))!;
        Assert.Contains(service.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(PurchaseServiceAcceptanceRow)
            && x.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "OrderId", "LineId", "AcceptanceId"]));
        Assert.Contains(model.FindEntityType(typeof(PurchaseInvoiceReceiptBindingRow))!.GetForeignKeys(),
            x => x.PrincipalEntityType.ClrType == typeof(PurchaseAcceptanceRow));
        Assert.False(db.Database.HasPendingModelChanges());
    }
    [Fact]
    public void Service_migration_is_additive_DDL_and_Down_guards_every_history_table_before_the_first_drop()
    {
        var migration = new AddPurchaseServiceOrders();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal(5, migration.UpOperations.OfType<CreateTableOperation>().Count());
        var sql = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51992", sql, StringComparison.Ordinal);
        foreach (var table in new[] { "ServiceOrders", "ServiceOrderRevisions", "ServiceOrderAttempts", "ServiceAcceptances", "ServiceInvoiceBindings" })
            Assert.Contains("[purchasing].[" + table + "]", sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.All(migration.DownOperations.Skip(1), x => Assert.IsType<DropTableOperation>(x));
    }
}

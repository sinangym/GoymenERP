using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseAdditionalCostInvoiceBindingModelTests
{
    [Fact]
    public void Expense_invoice_binding_is_a_company_filtered_immutable_monetary_fact_with_real_expense_and_invoice_foreign_keys()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var model = context.GetService<IDesignTimeModel>().Model;
        var binding = model.FindEntityType(typeof(PurchaseAdditionalCostInvoiceBindingRow))!;
        Assert.Single(binding.GetDeclaredQueryFilters()); Assert.Equal("purchasing", binding.GetSchema());
        Assert.Equal(["CompanyId", "InvoiceId", "InvoiceLineId", "CostId"], binding.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Equal((24, 6), (binding.FindProperty("NetAmount")!.GetPrecision(), binding.FindProperty("NetAmount")!.GetScale()));
        Assert.Contains(binding.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(PurchaseAdditionalCostRow)
            && x.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "CostId"]));
        Assert.Contains(binding.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(PurchaseIncomingInvoiceRow)
            && x.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "InvoiceId"]));
        Assert.DoesNotContain(binding.GetProperties(), x => x.Name is "Quantity" or "AcceptanceId" or "InventoryMovementId");
        var row = new PurchaseAdditionalCostInvoiceBindingRow { CompanyId = company.CompanyId, InvoiceId = Guid.NewGuid(),
            InvoiceLineId = Guid.NewGuid(), CostId = Guid.NewGuid(), NetAmount = 6000m };
        context.Attach(row); row.NetAmount = 1m;
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.False(context.Database.HasPendingModelChanges());
    }
    [Fact]
    public void Expense_invoice_binding_migration_is_additive_and_Down_protects_its_immutable_monetary_history_first()
    {
        var migration = new AddPurchaseAdditionalCostInvoiceBindings();
        Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()); Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51939", guard, StringComparison.Ordinal); Assert.Contains("[purchasing].[AdditionalCostInvoiceBindings]", guard, StringComparison.Ordinal);
        Assert.All(migration.DownOperations.Skip(1), x => Assert.IsType<DropTableOperation>(x));
    }
}

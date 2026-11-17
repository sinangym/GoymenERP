using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryPurchaseInvoiceCostPersistenceModelTests
{
    [Fact]
    public void The_cost_source_migration_refuses_provenance_loss_and_protects_both_immutable_tables()
    {
        var migration = new AddPurchaseInvoiceCostApplications();
        var down = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51926", down.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[PurchaseInvoiceCostApplications]", down.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[PurchaseInvoiceReceiptCostEffects]", down.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        var immutable = migration.UpOperations.OfType<SqlOperation>().ToArray();
        Assert.Equal(2, immutable.Length);
        Assert.Contains(immutable, operation => operation.Sql.Contains("THROW 51927", StringComparison.Ordinal));
        Assert.Contains(immutable, operation => operation.Sql.Contains("THROW 51928", StringComparison.Ordinal));
        Assert.All(immutable, operation => Assert.Contains("AFTER UPDATE, DELETE", operation.Sql, StringComparison.Ordinal));
        using var db = Context();
        var model = db.GetService<IDesignTimeModel>().Model;
        var source = model.FindEntityType(typeof(PurchaseInvoiceCostApplicationRow))!;
        Assert.Equal(InventoryDbContext.PurchaseInvoiceCostApplicationSourceCheck,
            source.GetCheckConstraints().Single(check => check.Name == "CK_PurchaseInvoiceCostApplications_Source").Sql);
        Assert.Equal(InventoryDbContext.PurchaseInvoiceCostApplicationEnvelopeCheck,
            source.GetCheckConstraints().Single(check => check.Name == "CK_PurchaseInvoiceCostApplications_Envelope").Sql);
        var effect = model.FindEntityType(typeof(PurchaseInvoiceReceiptCostEffectRow))!;
        Assert.Equal(InventoryDbContext.PurchaseInvoiceReceiptCostEffectSourceCheck,
            effect.GetCheckConstraints().Single(check => check.Name == "CK_PurchaseInvoiceReceiptCostEffects_Source").Sql);
        Assert.Equal(InventoryDbContext.PurchaseInvoiceReceiptCostEffectQuantityCheck,
            effect.GetCheckConstraints().Single(check => check.Name == "CK_PurchaseInvoiceReceiptCostEffects_Quantity").Sql);
    }

    [Fact]
    public void The_invoice_receipt_and_acceptance_ranges_are_company_scoped_and_unique()
    {
        using var db = Context();
        foreach (var type in new[] { typeof(PurchaseInvoiceCostApplicationRow), typeof(PurchaseInvoiceReceiptCostEffectRow) })
        {
            var entity = db.Model.FindEntityType(type)!;
            Assert.Equal("inventory", entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.All(entity.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        }
        var application = db.Model.FindEntityType(typeof(PurchaseInvoiceCostApplicationRow))!;
        Assert.Contains(application.GetIndexes(), index => index.IsUnique && index.Properties.Select(p => p.Name)
            .SequenceEqual(["CompanyId", "InvoiceId"]));
        var effect = db.Model.FindEntityType(typeof(PurchaseInvoiceReceiptCostEffectRow))!;
        Assert.Contains(effect.GetIndexes(), index => index.IsUnique && index.Properties.Select(p => p.Name)
            .SequenceEqual(["CompanyId", "AcceptanceId", "SourceEventId"]));
    }

    [Fact]
    public void A_recorded_cost_application_cannot_be_rewritten_or_deleted_even_before_SQL_is_opened()
    {
        using var db = Context();
        var company = new Guid("11000000-0000-0000-0000-000000000001");
        var row = new PurchaseInvoiceCostApplicationRow { CompanyId = company, SourceEventId = Guid.NewGuid(), ResultJson = "{}" };
        db.Attach(row); row.ResultJson = "{\"changed\":true}";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear(); db.Attach(row); db.Remove(row);
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public void An_acceptance_cost_effect_of_another_company_is_rejected_before_SQL_is_opened()
    {
        using var db = Context();
        db.PurchaseInvoiceReceiptCostEffects.Add(new() { CompanyId = Guid.NewGuid(), SourceEventId = Guid.NewGuid(), AcceptanceId = Guid.NewGuid() });
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    private static InventoryDbContext Context()
    {
        var company = new CompanyContext(); company.Set(new("11000000-0000-0000-0000-000000000001"));
        return new(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}

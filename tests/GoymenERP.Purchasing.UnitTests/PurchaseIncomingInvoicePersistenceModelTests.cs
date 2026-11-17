using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseIncomingInvoicePersistenceModelTests
{
    [Fact]
    public void Invoice_identity_history_receipt_bindings_and_terminal_attempts_are_filtered_and_restrict_foreign_keys()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(PurchaseIncomingInvoiceRow), typeof(PurchaseIncomingInvoiceRevisionRow),
                     typeof(PurchaseIncomingInvoiceAttemptRow), typeof(PurchaseInvoiceReceiptBindingRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("purchasing", entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.All(entity.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        }
        var invoice = model.FindEntityType(typeof(PurchaseIncomingInvoiceRow))!;
        Assert.Contains(invoice.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "Ettn"]));
        Assert.Equal(PurchasingDbContext.IncomingInvoiceSourceCheck,
            Assert.Single(invoice.GetCheckConstraints(), check => check.Name == "CK_IncomingInvoices_Source").Sql);
        var history = model.FindEntityType(typeof(PurchaseIncomingInvoiceRevisionRow))!;
        Assert.Equal(["CompanyId", "InvoiceId", "Version"], history.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(PurchasingDbContext.IncomingInvoiceSnapshotCheck, Assert.Single(history.GetCheckConstraints()).Sql);
        var attempt = model.FindEntityType(typeof(PurchaseIncomingInvoiceAttemptRow))!;
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempt.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(PurchasingDbContext.IncomingInvoiceAttemptCheck, Assert.Single(attempt.GetCheckConstraints()).Sql);
    }

    [Fact]
    public void Invoice_history_and_receipt_bindings_cannot_be_deleted_or_overwritten_before_database_access()
    {
        foreach (var deleted in new[] { false, true })
        {
            using var context = Context();
            var row = new PurchaseIncomingInvoiceRow { CompanyId = Company, InvoiceId = Guid.NewGuid(), SupplierPartyId = Guid.NewGuid(),
                Ettn = Guid.NewGuid(), DocumentId = Guid.NewGuid(), DocumentVersion = 1, DocumentSha256 = new string('a', 64), OfficialNumber = "SENTETIK" };
            context.Attach(row);
            if (deleted) context.Remove(row); else row.OfficialNumber = "changed";
            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        }
    }

    [Fact]
    public void Invoice_migration_creates_only_DDL_and_guards_all_history_before_any_drop()
    {
        var migration = new AddPurchaseIncomingInvoices();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal(4, migration.UpOperations.OfType<CreateTableOperation>().Count());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51980", guard.Sql, StringComparison.Ordinal);
        foreach (var table in new[] { "IncomingInvoices", "IncomingInvoiceRevisions", "IncomingInvoiceAttempts", "InvoiceReceiptBindings" })
            Assert.Contains("[purchasing].[" + table + "]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static PurchasingDbContext Context()
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(new DbContextOptionsBuilder<PurchasingDbContext>().UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}

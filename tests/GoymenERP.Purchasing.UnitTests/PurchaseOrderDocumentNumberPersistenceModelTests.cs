using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseOrderDocumentNumberPersistenceModelTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void Number_snapshot_is_optional_for_legacy_orders_unique_in_company_and_guarded_as_one_tuple()
    {
        using var context = Context();
        var order = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurchaseOrderRow))!;
        Assert.Single(order.GetDeclaredQueryFilters());
        foreach (var name in new[] { "DocumentNumber", "DocumentNumberFormatVersion", "DocumentNumberSequence", "DocumentNumberCancelledAtUtc" })
            Assert.True(order.FindProperty(name)!.IsNullable);
        Assert.Equal(64, order.FindProperty("DocumentNumber")!.GetMaxLength());
        Assert.Equal("Latin1_General_100_BIN2", order.FindProperty("DocumentNumber")!.GetCollation());
        var unique = Assert.Single(order.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "CompanyId", "DocumentNumber" }));
        Assert.True(unique.IsUnique);
        Assert.Equal("[DocumentNumber] IS NOT NULL", unique.GetFilter());
        Assert.Contains(order.GetCheckConstraints(), check => check.Name == "CK_PurchaseOrders_DocumentNumber"
            && check.Sql == PurchasingDbContext.OrderDocumentNumberCheck);
        var migration = new AddPurchaseOrderDocumentNumbers();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation or UpdateDataOperation or InsertDataOperation);
        Assert.Contains("THROW 51957", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    [Theory]
    [InlineData("DocumentNumber", "NEW-2026-000001")]
    [InlineData("DocumentNumberFormatVersion", 2L)]
    [InlineData("DocumentNumberSequence", 2L)]
    public void A_stored_number_cannot_be_replaced_before_any_database_write(string name, object value)
    {
        using var context = Context();
        var row = new PurchaseOrderRow { CompanyId = Company, Id = Guid.NewGuid(), Version = 1,
            DocumentNumber = "ALI-2026-000001", DocumentNumberFormatVersion = 1, DocumentNumberSequence = 1 };
        context.Attach(row);
        context.Entry(row).Property(name).CurrentValue = value;
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void Number_cancellation_history_cannot_be_erased_or_replaced_before_any_database_write()
    {
        using var context = Context();
        var row = new PurchaseOrderRow { CompanyId = Company, Id = Guid.NewGuid(), Version = 1,
            DocumentNumber = "ALI-2026-000001", DocumentNumberFormatVersion = 1, DocumentNumberSequence = 1,
            DocumentNumberCancelledAtUtc = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero) };
        context.Attach(row);
        row.DocumentNumberCancelledAtUtc = null;
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    private static PurchasingDbContext Context()
    {
        var company = new CompanyContext(); company.Set(Company);
        return new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True").Options, company);
    }
}

using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseInvoiceImportPersistenceModelTests
{
    [Fact]
    public void Import_reservations_are_company_filtered_append_only_and_idempotency_keys_are_company_scoped()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurchaseInvoiceImportReservationRow))!;
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.Equal(new[] { "CompanyId", "Id" }, entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "CompanyId", "IdempotencyKeyHash" }));
        Assert.Equal(PurchasingDbContext.InvoiceImportReservationCheck, Assert.Single(entity.GetCheckConstraints()).Sql);
        Assert.All(entity.GetForeignKeys(), foreign => Assert.Equal(DeleteBehavior.Restrict, foreign.DeleteBehavior));
        var row = new PurchaseInvoiceImportReservationRow { CompanyId = company.CompanyId, Id = Guid.NewGuid(), ActorId = "synthetic-actor",
            IdempotencyKeyHash = new string('a', 64), CorrelationId = "synthetic-correlation", ReservedAtUtc = DateTimeOffset.UtcNow };
        context.Attach(row); row.ActorId = "changed";
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        context.ChangeTracker.Clear(); context.Attach(row); context.Remove(row);
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void Import_reservation_migration_has_no_data_rewrite_and_down_guards_before_dropping_source_identity()
    {
        var migration = new AddPurchaseInvoiceImportReservations();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51996", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[purchasing].[InvoiceImportReservations]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
}

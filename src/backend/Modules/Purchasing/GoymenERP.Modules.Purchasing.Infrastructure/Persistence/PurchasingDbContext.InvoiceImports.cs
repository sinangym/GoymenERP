using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

internal sealed class PurchaseInvoiceImportReservationRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset ReservedAtUtc { get; set; }
}

public sealed partial class PurchasingDbContext
{
    internal const string InvoiceImportReservationCheck = "[CompanyId] <> " + EmptyGuid + " AND [Id] <> " + EmptyGuid
        + " AND LEN([ActorId]) > 0 AND LEN([IdempotencyKeyHash]) = 64 AND LEN([CorrelationId]) > 0";
    internal DbSet<PurchaseInvoiceImportReservationRow> InvoiceImportReservations => Set<PurchaseInvoiceImportReservationRow>();

    private void ConfigureInvoiceImports(ModelBuilder modelBuilder)
    {
        var reservation = modelBuilder.Entity<PurchaseInvoiceImportReservationRow>();
        reservation.ToTable("InvoiceImportReservations", Schema,
            table => table.HasCheckConstraint("CK_InvoiceImportReservations_Identity", InvoiceImportReservationCheck));
        reservation.HasKey(row => new { row.CompanyId, row.Id });
        reservation.HasIndex(row => new { row.CompanyId, row.IdempotencyKeyHash }).IsUnique();
        reservation.Property(row => row.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        reservation.Property(row => row.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        reservation.Property(row => row.CorrelationId).HasMaxLength(128);
        reservation.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        reservation.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
    }
}

using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingDbContext
{
    internal const string IncomingInvoiceSourceCheck = "[CompanyId] <> " + EmptyGuid + " AND [InvoiceId] <> " + EmptyGuid
        + " AND [SupplierPartyId] <> " + EmptyGuid + " AND [Ettn] <> " + EmptyGuid + " AND [DocumentId] <> " + EmptyGuid
        + " AND [DocumentVersion] > 0 AND LEN([OfficialNumber]) > 0 AND LEN([DocumentSha256]) = 64";
    /// <summary>WP15: the duplicate facts are all present (written since the column exists) or all absent (older records).</summary>
    internal const string IncomingInvoiceDuplicateFactsCheck = "([InvoiceDate] IS NULL AND [CurrencyId] IS NULL AND [PayableAmount] IS NULL) OR "
        + "([InvoiceDate] IS NOT NULL AND [CurrencyId] IS NOT NULL AND [CurrencyId] <> " + EmptyGuid + " AND [PayableAmount] IS NOT NULL)";
    internal const string IncomingInvoiceSnapshotCheck = "[Version] > 0 AND [Status] IN (1,2,3,4) AND ISJSON([SnapshotJson]) = 1";
    internal const string IncomingInvoiceAttemptCheck = "([SnapshotJson] IS NOT NULL AND ISJSON([SnapshotJson]) = 1 AND [RefusalJson] IS NULL) OR "
        + "([SnapshotJson] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)";
    internal const string IncomingInvoiceReceiptQuantityCheck = "[Quantity] > 0";
    internal DbSet<PurchaseIncomingInvoiceRow> IncomingInvoices => Set<PurchaseIncomingInvoiceRow>();
    internal DbSet<PurchaseIncomingInvoiceRevisionRow> IncomingInvoiceRevisions => Set<PurchaseIncomingInvoiceRevisionRow>();
    internal DbSet<PurchaseIncomingInvoiceAttemptRow> IncomingInvoiceAttempts => Set<PurchaseIncomingInvoiceAttemptRow>();
    internal DbSet<PurchaseInvoiceReceiptBindingRow> InvoiceReceiptBindings => Set<PurchaseInvoiceReceiptBindingRow>();

    private void ConfigureIncomingInvoices(ModelBuilder modelBuilder)
    {
        var invoice = modelBuilder.Entity<PurchaseIncomingInvoiceRow>();
        invoice.ToTable("IncomingInvoices", Schema, table =>
        {
            table.HasCheckConstraint("CK_IncomingInvoices_Source", IncomingInvoiceSourceCheck);
            table.HasCheckConstraint("CK_IncomingInvoices_DuplicateFacts", IncomingInvoiceDuplicateFactsCheck);
        });
        invoice.HasKey(row => new { row.CompanyId, row.InvoiceId });
        invoice.HasIndex(row => new { row.CompanyId, row.Ettn }).IsUnique();
        invoice.HasIndex(row => new { row.CompanyId, row.SupplierPartyId, row.OfficialNumber }).IsUnique();
        invoice.Property(row => row.OfficialNumber).HasMaxLength(128).UseCollation(IdentityCollation);
        invoice.Property(row => row.DocumentSha256).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        invoice.Property(row => row.PayableAmount).HasPrecision(24, 6);
        invoice.HasIndex(row => new { row.CompanyId, row.SupplierPartyId, row.InvoiceDate })
            .HasDatabaseName("IX_IncomingInvoices_DuplicateDate");
        invoice.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        invoice.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var revision = modelBuilder.Entity<PurchaseIncomingInvoiceRevisionRow>();
        revision.ToTable("IncomingInvoiceRevisions", Schema, table => table.HasCheckConstraint("CK_IncomingInvoiceRevisions_Snapshot", IncomingInvoiceSnapshotCheck));
        revision.HasKey(row => new { row.CompanyId, row.InvoiceId, row.Version });
        revision.Property(row => row.SnapshotJson).HasColumnType("nvarchar(max)");
        revision.HasOne<PurchaseIncomingInvoiceRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.InvoiceId }).OnDelete(DeleteBehavior.Restrict);
        revision.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var attempt = modelBuilder.Entity<PurchaseIncomingInvoiceAttemptRow>();
        attempt.ToTable("IncomingInvoiceAttempts", Schema, table => table.HasCheckConstraint("CK_IncomingInvoiceAttempts_Result", IncomingInvoiceAttemptCheck));
        attempt.HasKey(row => new { row.CompanyId, row.CommandName, row.IdempotencyKeyHash });
        attempt.Property(row => row.CommandName).HasMaxLength(100).UseCollation(IdentityCollation);
        attempt.Property(row => row.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(row => row.RequestHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(row => row.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        attempt.Property(row => row.CorrelationId).HasMaxLength(128);
        attempt.Property(row => row.SnapshotJson).HasColumnType("nvarchar(max)");
        attempt.Property(row => row.RefusalJson).HasColumnType("nvarchar(max)");
        attempt.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var binding = modelBuilder.Entity<PurchaseInvoiceReceiptBindingRow>();
        binding.ToTable("InvoiceReceiptBindings", Schema, table => table.HasCheckConstraint("CK_InvoiceReceiptBindings_Quantity", IncomingInvoiceReceiptQuantityCheck));
        binding.HasKey(row => new { row.CompanyId, row.InvoiceId, row.InvoiceLineId, row.AcceptanceId });
        binding.HasIndex(row => new { row.CompanyId, row.AcceptanceId });
        binding.Property(row => row.Quantity).HasPrecision(24, 6);
        binding.HasOne<PurchaseIncomingInvoiceRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.InvoiceId }).OnDelete(DeleteBehavior.Restrict);
        binding.HasOne<PurchaseOrderLineRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.OrderId, row.OrderLineId })
            .HasPrincipalKey(row => new { row.CompanyId, row.OrderId, row.LineId }).OnDelete(DeleteBehavior.Restrict);
        binding.HasOne<PurchaseAcceptanceRow>().WithMany()
            .HasForeignKey(row => new { row.CompanyId, row.OrderId, row.OrderLineId, row.AcceptanceId })
            .HasPrincipalKey(row => new { row.CompanyId, row.OrderId, row.LineId, row.AcceptanceId }).OnDelete(DeleteBehavior.Restrict);
        binding.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
    }

    private void GuardIncomingInvoiceSnapshots()
    {
        foreach (var entry in ChangeTracker.Entries<PurchaseIncomingInvoiceRevisionRow>().Where(entry => entry.State == EntityState.Added))
        {
            var row = entry.Entity;
            var snapshot = JsonSerializer.Deserialize<PurchaseIncomingInvoiceSnapshot>(row.SnapshotJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("Fatura görüntüsü okunamadı.");
            _ = snapshot.Restore();
            if (snapshot.CompanyId != row.CompanyId || snapshot.Id != row.InvoiceId || snapshot.Version != row.Version || (int)snapshot.Status != row.Status)
                throw new InvalidOperationException("Fatura görüntüsü kayıt kapsamıyla uyuşmuyor.");
        }
    }
}

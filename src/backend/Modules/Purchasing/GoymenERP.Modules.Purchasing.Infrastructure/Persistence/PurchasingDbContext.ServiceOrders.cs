using System.Text.Json;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingDbContext
{
    internal DbSet<PurchaseServiceOrderRow> ServiceOrders => Set<PurchaseServiceOrderRow>();
    internal DbSet<PurchaseServiceOrderRevisionRow> ServiceOrderRevisions => Set<PurchaseServiceOrderRevisionRow>();
    internal DbSet<PurchaseServiceOrderAttemptRow> ServiceOrderAttempts => Set<PurchaseServiceOrderAttemptRow>();
    internal DbSet<PurchaseServiceAcceptanceRow> ServiceAcceptances => Set<PurchaseServiceAcceptanceRow>();
    internal DbSet<PurchaseServiceInvoiceBindingRow> ServiceInvoiceBindings => Set<PurchaseServiceInvoiceBindingRow>();
    private void ConfigureServiceOrders(ModelBuilder builder)
    {
        var order = builder.Entity<PurchaseServiceOrderRow>();
        order.ToTable("ServiceOrders", Schema, table => table.HasCheckConstraint("CK_ServiceOrders_Identity",
            $"[CompanyId] <> {EmptyGuid} AND [OrderId] <> {EmptyGuid} AND [Version] > 0"));
        order.HasKey(x => new { x.CompanyId, x.OrderId }); order.Property(x => x.Version).IsConcurrencyToken();
        order.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        order.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        var revision = builder.Entity<PurchaseServiceOrderRevisionRow>();
        revision.ToTable("ServiceOrderRevisions", Schema, table => table.HasCheckConstraint("CK_ServiceOrderRevisions_Snapshot",
            "[Version] > 0 AND ISJSON([SnapshotJson]) = 1"));
        revision.HasKey(x => new { x.CompanyId, x.OrderId, x.Version }); revision.Property(x => x.SnapshotJson).HasColumnType("nvarchar(max)");
        revision.HasOne<PurchaseServiceOrderRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.OrderId }).OnDelete(DeleteBehavior.Restrict);
        revision.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        var attempt = builder.Entity<PurchaseServiceOrderAttemptRow>();
        attempt.ToTable("ServiceOrderAttempts", Schema, table => table.HasCheckConstraint("CK_ServiceOrderAttempts_Result",
            "([SnapshotJson] IS NOT NULL AND ISJSON([SnapshotJson]) = 1 AND [RefusalJson] IS NULL) OR "
            + "([SnapshotJson] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)"));
        attempt.HasKey(x => new { x.CompanyId, x.Command, x.KeyHash });
        attempt.Property(x => x.Command).HasMaxLength(100).UseCollation(IdentityCollation);
        attempt.Property(x => x.KeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        attempt.Property(x => x.CorrelationId).HasMaxLength(128);
        attempt.Property(x => x.SnapshotJson).HasColumnType("nvarchar(max)"); attempt.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
        attempt.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        var accepted = builder.Entity<PurchaseServiceAcceptanceRow>();
        accepted.ToTable("ServiceAcceptances", Schema, table => table.HasCheckConstraint("CK_ServiceAcceptances_Fact", "[Quantity] > 0 AND [OrderVersionAfter] >= 3"));
        accepted.HasKey(x => new { x.CompanyId, x.OrderId, x.LineId, x.AcceptanceId });
        accepted.HasIndex(x => new { x.CompanyId, x.AcceptanceId }).IsUnique();
        accepted.HasIndex(x => new { x.CompanyId, x.SupplierPartyId, x.CompanyAccountId, x.CurrencyId, x.ServiceTypeId, x.UnitId });
        accepted.Property(x => x.Quantity).HasPrecision(24, 6);
        accepted.HasOne<PurchaseServiceOrderRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.OrderId }).OnDelete(DeleteBehavior.Restrict);
        accepted.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        var binding = builder.Entity<PurchaseServiceInvoiceBindingRow>();
        binding.ToTable("ServiceInvoiceBindings", Schema, table => table.HasCheckConstraint("CK_ServiceInvoiceBindings_Quantity", "[Quantity] > 0"));
        binding.HasKey(x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.AcceptanceId });
        binding.HasIndex(x => new { x.CompanyId, x.AcceptanceId }); binding.Property(x => x.Quantity).HasPrecision(24, 6);
        binding.HasOne<PurchaseIncomingInvoiceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InvoiceId }).OnDelete(DeleteBehavior.Restrict);
        binding.HasOne<PurchaseServiceAcceptanceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.OrderId, x.LineId, x.AcceptanceId }).OnDelete(DeleteBehavior.Restrict);
        binding.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }
    private void GuardServiceOrders()
    {
        foreach (var entry in ChangeTracker.Entries<PurchaseServiceOrderRevisionRow>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted) throw new InvalidOperationException("Hizmet siparişi geçmişi değiştirilemez.");
            if (entry.State != EntityState.Added) continue;
            var value = JsonSerializer.Deserialize<PurchaseServiceOrderState>(entry.Entity.SnapshotJson, PurchaseServiceOrderStoreV1.Json)
                ?? throw new InvalidOperationException("Hizmet siparişi görüntüsü okunamadı.");
            PurchaseServiceOrder.Validate(value);
            if (value.CompanyId != entry.Entity.CompanyId || value.OrderId != entry.Entity.OrderId || value.Version != entry.Entity.Version
                || string.IsNullOrWhiteSpace(value.DocumentNumber) || value.DocumentNumber.Length > 64)
                throw new InvalidOperationException("Hizmet siparişi görüntüsü kapsamı uyuşmuyor.");
        }
        foreach (var entry in ChangeTracker.Entries<PurchaseServiceOrderAttemptRow>())
            if (entry.State is EntityState.Modified or EntityState.Deleted) throw new InvalidOperationException("Hizmet komutu geçmişi değiştirilemez.");
        foreach (var entry in ChangeTracker.Entries<PurchaseServiceAcceptanceRow>())
            if (entry.State is EntityState.Modified or EntityState.Deleted) throw new InvalidOperationException("Hizmet kabulü değiştirilemez.");
        foreach (var entry in ChangeTracker.Entries<PurchaseServiceInvoiceBindingRow>())
            if (entry.State is EntityState.Modified or EntityState.Deleted) throw new InvalidOperationException("Faturalanan hizmet payı değiştirilemez.");
    }
}
internal sealed class PurchaseServiceOrderRow : IPurchasingTenantRow
{ public Guid CompanyId { get; set; } public Guid OrderId { get; set; } public long Version { get; set; } }
internal sealed class PurchaseServiceOrderRevisionRow : IPurchasingTenantRow
{ public Guid CompanyId { get; set; } public Guid OrderId { get; set; } public long Version { get; set; } public string SnapshotJson { get; set; } = ""; }
internal sealed class PurchaseServiceOrderAttemptRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; } public string Command { get; set; } = ""; public string KeyHash { get; set; } = "";
    public string RequestHash { get; set; } = ""; public string ActorId { get; set; } = ""; public string CorrelationId { get; set; } = "";
    public string? SnapshotJson { get; set; } public string? RefusalJson { get; set; }
}
internal sealed class PurchaseServiceAcceptanceRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; } public Guid OrderId { get; set; } public Guid LineId { get; set; } public Guid AcceptanceId { get; set; }
    public decimal Quantity { get; set; } public long OrderVersionAfter { get; set; }
    public Guid SupplierPartyId { get; set; } public Guid CompanyAccountId { get; set; } public Guid CurrencyId { get; set; }
    public Guid ServiceTypeId { get; set; } public Guid UnitId { get; set; }
}
internal sealed class PurchaseServiceInvoiceBindingRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; } public Guid InvoiceId { get; set; } public Guid InvoiceLineId { get; set; }
    public Guid OrderId { get; set; } public Guid LineId { get; set; } public Guid AcceptanceId { get; set; } public decimal Quantity { get; set; }
}

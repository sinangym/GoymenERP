using Microsoft.EntityFrameworkCore;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingDbContext
{
    internal DbSet<PurchaseAdditionalCostInvoiceBindingRow> AdditionalCostInvoiceBindings => Set<PurchaseAdditionalCostInvoiceBindingRow>();
    private void ConfigureAdditionalCostInvoiceBindings(ModelBuilder builder)
    {
        var binding = builder.Entity<PurchaseAdditionalCostInvoiceBindingRow>();
        binding.ToTable("AdditionalCostInvoiceBindings", Schema, t => t.HasCheckConstraint("CK_AdditionalCostInvoiceBindings_Net",
            "[NetAmount] > 0 AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000'"));
        binding.HasKey(x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.CostId });
        binding.Property(x => x.NetAmount).HasPrecision(24, 6);
        binding.HasIndex(x => new { x.CompanyId, x.CostId });
        binding.HasOne<PurchaseAdditionalCostRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CostId }).OnDelete(DeleteBehavior.Restrict);
        binding.HasOne<PurchaseIncomingInvoiceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InvoiceId }).OnDelete(DeleteBehavior.Restrict);
        binding.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }
    private void GuardAdditionalCostInvoiceBindings()
    {
        foreach (var entry in ChangeTracker.Entries<PurchaseAdditionalCostInvoiceBindingRow>())
            if (entry.State is EntityState.Modified or EntityState.Deleted || entry.State == EntityState.Added
                && (entry.Entity.InvoiceId == Guid.Empty || entry.Entity.InvoiceLineId == Guid.Empty || entry.Entity.CostId == Guid.Empty || entry.Entity.NetAmount <= 0
                    || entry.Entity.NetAmount > PurchaseIncomingInvoice.MaximumStoredQuantity
                    || decimal.Round(entry.Entity.NetAmount, PurchaseIncomingInvoice.MaximumStoredScale) != entry.Entity.NetAmount))
                throw new InvalidOperationException("Faturalanan masraf payı değiştirilemez ve gerçek pozitif tutarla kaydedilir.");
    }
}
internal sealed class PurchaseAdditionalCostInvoiceBindingRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; } public Guid InvoiceId { get; set; } public Guid InvoiceLineId { get; set; } public Guid CostId { get; set; }
    public decimal NetAmount { get; set; }
}

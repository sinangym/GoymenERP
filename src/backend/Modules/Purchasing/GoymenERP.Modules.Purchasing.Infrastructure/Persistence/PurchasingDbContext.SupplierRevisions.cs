using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingDbContext
{
    internal const string SupplierRevisionHistoryCheck =
        "([SupplierRevisionHistoryJson] IS NULL AND [CurrentPreparedBy] IS NULL) OR "
        + "([SupplierRevisionHistoryJson] IS NOT NULL AND ISJSON([SupplierRevisionHistoryJson]) = 1 AND [CurrentPreparedBy] IS NOT NULL AND LEN([CurrentPreparedBy]) > 0)";
    internal const string InvoiceOrderBindingScopeCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [InvoiceId] <> " + EmptyGuid + " AND [InvoiceLineId] <> " + EmptyGuid
        + " AND [OrderId] <> " + EmptyGuid + " AND [OrderLineId] <> " + EmptyGuid
        + " AND [SupplierPartyId] <> " + EmptyGuid + " AND [OrderVersion] > 0";
    internal DbSet<PurchaseInvoiceOrderBindingRow> InvoiceOrderBindings => Set<PurchaseInvoiceOrderBindingRow>();

    private void ConfigureSupplierRevisions(ModelBuilder modelBuilder)
    {
        var order = modelBuilder.Entity<PurchaseOrderRow>();
        order.Property(x => x.CurrentPreparedBy).HasMaxLength(128);
        order.Property(x => x.SupplierRevisionHistoryJson).HasColumnType("nvarchar(max)");
        order.ToTable("PurchaseOrders", Schema, table => table.HasCheckConstraint("CK_PurchaseOrders_SupplierRevisionHistory",
            SupplierRevisionHistoryCheck));
        var binding = modelBuilder.Entity<PurchaseInvoiceOrderBindingRow>();
        binding.ToTable("PurchaseInvoiceOrderBindings", Schema, table => table.HasCheckConstraint("CK_PurchaseInvoiceOrderBindings_Scope",
            InvoiceOrderBindingScopeCheck));
        binding.HasKey(x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.OrderLineId });
        binding.HasIndex(x => new { x.CompanyId, x.OrderId });
        binding.HasOne<PurchaseOrderLineRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId, x.OrderLineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.OrderId, x.LineId }).OnDelete(DeleteBehavior.Restrict);
        binding.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void GuardSupplierRevisionHistory()
    {
        foreach (var entry in ChangeTracker.Entries<PurchaseOrderRow>().Where(item => item.State == EntityState.Modified))
        {
            var original = (PurchaseOrderRow)entry.OriginalValues.ToObject();
            var current = entry.Entity;
            var before = PurchaseSupplierRevisionHistory.Read(original);
            var after = PurchaseSupplierRevisionHistory.Read(current);
            if (before is not null && after is null) throw new InvalidOperationException("Cari revizyon geçmişi silinmez.");
            if (after is null)
            {
                if (current.SupplierPartyId != original.SupplierPartyId || current.CompanyAccountId != original.CompanyAccountId)
                    throw new InvalidOperationException("Cari değişikliği revizyon geçmişi gerektirir.");
                continue;
            }
            var revisionsBefore = before?.Revisions ?? [];
            var pricesBefore = before?.Prices ?? [];
            var revisionAdded = after.Revisions.Length - revisionsBefore.Length;
            var priceAdded = after.Prices.Length - pricesBefore.Length;
            if (revisionAdded is < 0 or > 1 || priceAdded is < 0 or > 1 || revisionAdded + priceAdded > 1
                || revisionsBefore.Where((item, index) => JsonSerializer.Serialize(item) != JsonSerializer.Serialize(after.Revisions[index])).Any()
                || pricesBefore.Where((item, index) => JsonSerializer.Serialize(item) != JsonSerializer.Serialize(after.Prices[index])).Any())
                throw new InvalidOperationException("Cari veya fiyat geçmişi değiştirilemez; komut tek kayıt ekler.");
            if (revisionAdded == 1)
            {
                var added = after.Revisions[^1];
                if (added.OrderVersionAfter != current.Version || current.Version != original.Version + 1
                    || original.Status != (int)PurchaseOrderStatus.Approved || current.Status != (int)PurchaseOrderStatus.Draft
                    || added.PreviousSupplierPartyId != original.SupplierPartyId || added.PreviousPreparer != (original.CurrentPreparedBy ?? original.CreatedBy)
                    || added.PreviousApproval.ApprovedBy != original.ApprovedBy || added.PreviousApproval.ApprovedAtUtc != original.ApprovedAtUtc)
                    throw new InvalidOperationException("Cari değişikliği önceki onay görüntüsüyle uyuşmuyor.");
            }
            else if (current.SupplierPartyId != original.SupplierPartyId || current.CompanyAccountId != original.CompanyAccountId
                || current.CurrentPreparedBy != original.CurrentPreparedBy)
                throw new InvalidOperationException("Cari ve hazırlayan yalnız yeni cari revizyonunda değişir.");
            if (priceAdded == 1 && (after.Prices[^1].OrderVersionAfter != current.Version || current.Version != original.Version + 1))
                throw new InvalidOperationException("Fiyat revizyonu sürümü geçersiz.");
        }
        foreach (var entry in ChangeTracker.Entries<PurchaseOrderLineRow>().Where(item => item.State == EntityState.Modified))
        {
            var before = Price((PurchaseOrderLineRow)entry.OriginalValues.ToObject());
            var after = Price(entry.Entity);
            if (before == after) continue;
            var parent = ChangeTracker.Entries<PurchaseOrderRow>().SingleOrDefault(item => item.Entity.CompanyId == entry.Entity.CompanyId
                && item.Entity.Id == entry.Entity.OrderId && item.State == EntityState.Modified)
                ?? throw new InvalidOperationException("Fiyat değişikliği sipariş revizyonunu gerektirir.");
            var history = PurchaseSupplierRevisionHistory.Read(parent.Entity) ?? throw new InvalidOperationException("Fiyat geçmişi eksik.");
            var supplier = history.Revisions.SingleOrDefault(item => item.OrderVersionAfter == parent.Entity.Version);
            var price = history.Prices.SingleOrDefault(item => item.OrderVersionAfter == parent.Entity.Version && item.LineId == entry.Entity.LineId);
            if (supplier is not null)
            {
                if (after is not null || supplier.PreviousLines.Single(item => item.LineId == entry.Entity.LineId).Price != before)
                    throw new InvalidOperationException("Cari revizyonu eski fiyatı koruyup güncel fiyatı boşaltır.");
            }
            else if (price is null || price.PreviousPrice != before || price.Price != after)
                throw new InvalidOperationException("Fiyat değişikliği geçmişindeki önce/sonra fiyatla uyuşmuyor.");
        }

        static PurchaseOrderLinePrice? Price(PurchaseOrderLineRow row) => row.UnitPrice is { } price && row.PriceUnitId is { } unit
            ? new PurchaseOrderLinePrice(price, unit, row.DiscountPercent, row.DiscountAmount, row.DeferralPercent, row.DeferralAmount) : null;
    }
}

using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>The audit sidecar written in Inventory's save: append-only and of the active company only.</summary>
internal static class InventoryAuditEventTenantGuard
{
    public static void Validate(DbContext dbContext, ICompanyContext companyContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(companyContext);

        foreach (var entry in dbContext.ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State == EntityState.Unchanged)
            {
                continue;
            }

            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Audit kayıtları değiştirilemez veya silinemez.");
            }

            if (entry.State != EntityState.Added)
            {
                continue;
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Audit kaydı CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved)
            {
                throw new InvalidOperationException("Audit kaydı yazmak için aktif şirket bağlamı zorunludur.");
            }

            if (entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Audit kaydı aktif şirket bağlamıyla eşleşmelidir.");
            }
        }
    }
}

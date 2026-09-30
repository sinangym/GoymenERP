using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence;

/// <summary>The audit sidecar written in the vault's save: append-only and of the active company only (the Logistics guard).</summary>
internal static class DocumentsAuditEventTenantGuard
{
    public static void Validate(DbContext dbContext, ICompanyContext companyContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(companyContext);
        foreach (var entry in dbContext.ChangeTracker.Entries<AuditEvent>())
        {
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

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Audit kaydı aktif şirket bağlamıyla eşleşmelidir.");
            }
        }
    }
}

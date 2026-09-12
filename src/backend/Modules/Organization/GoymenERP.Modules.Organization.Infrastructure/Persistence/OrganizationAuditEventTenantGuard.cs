using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

internal static class OrganizationAuditEventTenantGuard
{
    public static void Validate(
        DbContext dbContext,
        ICompanyContext companyContext)
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
                throw new InvalidOperationException(
                    "Audit kayıtları değiştirilemez veya silinemez.");
            }

            if (entry.State != EntityState.Added)
            {
                continue;
            }

            if (!entry.Entity.CompanyId.HasValue || entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "Audit kaydı CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved)
            {
                throw new InvalidOperationException(
                    "Audit kaydı yazmak için aktif şirket bağlamı zorunludur.");
            }

            if (entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException(
                    "Audit kaydı aktif şirket bağlamıyla eşleşmelidir.");
            }
        }
    }
}

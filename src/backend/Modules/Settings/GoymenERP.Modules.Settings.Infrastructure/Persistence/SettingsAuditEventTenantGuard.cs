using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal static class SettingsAuditEventTenantGuard
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

            if (entry.State == EntityState.Added
                && (!entry.Entity.CompanyId.HasValue
                    || entry.Entity.CompanyId == Guid.Empty
                    || !companyContext.IsResolved
                    || entry.Entity.CompanyId != companyContext.CompanyId))
            {
                throw new InvalidOperationException(
                    "Settings audit kaydı aktif şirket bağlamıyla eşleşmelidir.");
            }
        }
    }
}

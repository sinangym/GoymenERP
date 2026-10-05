using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

internal static class WorkAuditEventTenantGuard
{
    public static void Validate(DbContext dbContext, ICompanyContext companyContext)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Audit records are append-only.");
            }

            if (entry.State == EntityState.Added
                && (!companyContext.IsResolved
                    || entry.Entity.CompanyId != companyContext.CompanyId))
            {
                throw new InvalidOperationException("Audit record company must match the active company.");
            }
        }
    }
}

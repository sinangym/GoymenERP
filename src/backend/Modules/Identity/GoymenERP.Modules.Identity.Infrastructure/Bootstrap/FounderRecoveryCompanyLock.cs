using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;

namespace GoymenERP.Modules.Identity.Infrastructure.Bootstrap;

/// <summary>
/// The KIM-D17/rule 7 company lock taken by <c>recover-founder</c> step 3 on the caller's open transaction, for every
/// company in <c>CompanyId</c> order. A seam so the command's ordering can be tested without SQL Server; production
/// always uses <see cref="SqlFounderRecoveryCompanyLock"/>.
/// </summary>
internal interface IFounderRecoveryCompanyLock
{
    /// <summary><see langword="true"/> when the lock is held; <see langword="false"/> when the company is busy.</summary>
    Task<bool> TryAcquireAsync(Guid companyId, CancellationToken cancellationToken);
}

/// <summary>The same <c>identity.admin:{companyId}</c> application lock as the ID-3 access administration.</summary>
internal sealed class SqlFounderRecoveryCompanyLock(IdentityDbContext dbContext) : IFounderRecoveryCompanyLock
{
    public Task<bool> TryAcquireAsync(Guid companyId, CancellationToken cancellationToken) =>
        IdentityAdminLock.TryAcquireAsync(dbContext, companyId, cancellationToken);
}

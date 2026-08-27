using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

internal sealed class ExternalIdentityResolver(IdentityDbContext dbContext)
    : IExternalIdentityResolver
{
    public async Task<ResolvedIdentity?> ResolveAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var identity = await dbContext.ExternalIdentities
            .AsNoTracking()
            .Where(x => x.Issuer == issuer && x.Subject == subject)
            .Join(
                dbContext.Users.AsNoTracking().Where(x => x.IsActive),
                external => external.UserId,
                user => user.Id,
                (external, user) => new
                {
                    user.Id,
                    user.DisplayName,
                    user.Email,
                })
            .SingleOrDefaultAsync(cancellationToken);

        if (identity is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var companyIds = await dbContext.RoleAssignments
            .AsNoTracking()
            .Where(x => x.UserId == identity.Id
                && x.Status == RoleAssignmentStatus.Active
                && x.ValidFromUtc <= now
                && (!x.ValidUntilUtc.HasValue || x.ValidUntilUtc > now)
                && dbContext.Roles.Any(role => role.Id == x.RoleId && role.IsActive)
                && x.Scopes.Any())
            .Select(x => x.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new ResolvedIdentity(
            identity.Id,
            identity.DisplayName,
            identity.Email,
            companyIds.ToHashSet());
    }
}

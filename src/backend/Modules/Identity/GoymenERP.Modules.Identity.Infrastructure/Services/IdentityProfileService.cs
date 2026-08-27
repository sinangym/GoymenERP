using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

internal sealed class IdentityProfileService(
    IdentityDbContext dbContext,
    ICurrentUser currentUser)
    : IIdentityProfileService
{
    public async Task<IdentityProfile?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(currentUser.UserId, out var userId))
        {
            return null;
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == userId && x.IsActive)
            .Select(x => new { x.Id, x.DisplayName, x.Email })
            .SingleOrDefaultAsync(cancellationToken);
        return user is null
            ? null
            : new IdentityProfile(
                user.Id,
                user.DisplayName,
                user.Email,
                currentUser.AllowedCompanyIds.OrderBy(id => id).ToArray());
    }
}

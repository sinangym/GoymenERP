using System.Globalization;
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
        if (user is null)
        {
            return null;
        }

        // The flag is the one the session fence reads (LocalAccountSessionValidator); no local credential, no flag.
        var credential = await dbContext.LocalCredentials
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.MustChangePassword, x.Version })
            .SingleOrDefaultAsync(cancellationToken);
        return new IdentityProfile(
            user.Id,
            user.DisplayName,
            user.Email,
            currentUser.AllowedCompanyIds.OrderBy(id => id).ToArray(),
            MustChangePassword: credential?.MustChangePassword ?? false,
            LocalCredentialVersion: credential?.Version.ToString(CultureInfo.InvariantCulture));
    }
}

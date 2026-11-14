using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

/// <summary>
/// The relational login store. Each login write is one conditional <c>UPDATE</c> whose <c>WHERE</c> repeats the
/// values read at the start of the attempt, so a concurrent password change, stamp renewal, temporary password or
/// lock makes the write affect no row. Neither write touches <c>Version</c>.
/// </summary>
internal sealed class SqlLocalCredentialStore(IdentityDbContext dbContext) : ILocalCredentialStore
{
    public Task<LocalCredentialLoginState?> FindForLoginAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        dbContext.LocalCredentials
            .AsNoTracking()
            .Where(credential => credential.NormalizedUserName == normalizedUserName)
            .Join(
                dbContext.Users.AsNoTracking(),
                credential => credential.UserId,
                user => user.Id,
                (credential, user) => new LocalCredentialLoginState(
                    credential.UserId,
                    credential.PasswordHash,
                    credential.MustChangePassword,
                    credential.FailedAccessCount,
                    credential.LockoutEndUtc,
                    credential.TemporaryPasswordIssuedAtUtc,
                    credential.SecurityStamp,
                    user.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> TryRecordFailedAccessAsync(
        LocalCredentialLoginState read,
        LocalCredentialLockoutPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(policy);
        var maxFailedAttempts = policy.MaxFailedAttempts;
        var lockoutEndUtc = nowUtc + policy.Duration;

        // The same transition as LocalCredentialLockout.AfterFailedAccess, evaluated by SQL Server on the stored
        // counter: every SET reads the pre-update row, so two parallel failures both count.
        var affected = await Unchanged(read)
            .Where(credential => credential.LockoutEndUtc == null || credential.LockoutEndUtc <= nowUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        credential => credential.LockoutEndUtc,
                        credential => credential.FailedAccessCount + 1 >= maxFailedAttempts
                            ? lockoutEndUtc
                            : credential.LockoutEndUtc)
                    .SetProperty(
                        credential => credential.FailedAccessCount,
                        credential => credential.FailedAccessCount + 1 >= maxFailedAttempts
                            ? 0
                            : credential.FailedAccessCount + 1),
                cancellationToken);
        return affected == 1;
    }

    public async Task<bool> TryRecordSuccessfulAccessAsync(
        LocalCredentialLoginState read,
        string? rehashedPasswordHash,
        int? rehashedFormatVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        if ((rehashedPasswordHash is null) != (rehashedFormatVersion is null))
        {
            throw new ArgumentException("Yeniden özet ve biçim sürümü birlikte verilmelidir.");
        }

        var unchanged = Unchanged(read);
        var affected = rehashedPasswordHash is null
            ? await unchanged.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(credential => credential.FailedAccessCount, 0)
                    .SetProperty(credential => credential.LockoutEndUtc, (DateTimeOffset?)null),
                cancellationToken)
            : await unchanged.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(credential => credential.FailedAccessCount, 0)
                    .SetProperty(credential => credential.LockoutEndUtc, (DateTimeOffset?)null)
                    .SetProperty(credential => credential.PasswordHash, rehashedPasswordHash)
                    .SetProperty(credential => credential.HashFormatVersion, rehashedFormatVersion!.Value),
                cancellationToken);
        return affected == 1;
    }

    public Task<LocalCredentialSessionState?> FindForSessionAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.LocalCredentials
            .AsNoTracking()
            .Where(credential => credential.UserId == userId)
            .Join(
                dbContext.Users.AsNoTracking(),
                credential => credential.UserId,
                user => user.Id,
                (credential, user) => new LocalCredentialSessionState(
                    credential.SecurityStamp,
                    user.IsActive,
                    credential.MustChangePassword,
                    credential.TemporaryPasswordIssuedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    private IQueryable<LocalCredential> Unchanged(LocalCredentialLoginState read)
    {
        var userId = read.UserId;
        var passwordHash = read.PasswordHash;
        var securityStamp = read.SecurityStamp;
        var temporaryPasswordIssuedAtUtc = read.TemporaryPasswordIssuedAtUtc;
        return dbContext.LocalCredentials.Where(credential =>
            credential.UserId == userId
            && credential.PasswordHash == passwordHash
            && credential.SecurityStamp == securityStamp
            && credential.TemporaryPasswordIssuedAtUtc == temporaryPasswordIssuedAtUtc);
    }
}

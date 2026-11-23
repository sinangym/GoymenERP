using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

/// <summary>
/// The relational store of credential commands (ID-2 password change). The credential write is one conditional
/// <c>UPDATE</c> that repeats the version, stamp and hash read before it, and it commits together with the
/// idempotency record; nothing else is written on any refusal.
/// </summary>
internal sealed class SqlLocalCredentialCommandStore(IdentityDbContext dbContext) : ILocalCredentialCommandStore
{
    private const int SqlUniqueIndexViolation = 2601;
    private const int SqlUniqueConstraintViolation = 2627;

    public Task<LocalCredentialCommandState?> FindForCommandAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.LocalCredentials
            .AsNoTracking()
            .Where(credential => credential.UserId == userId)
            .Join(
                dbContext.Users.AsNoTracking(),
                credential => credential.UserId,
                user => user.Id,
                (credential, user) => new LocalCredentialCommandState(
                    credential.UserId,
                    credential.UserName,
                    credential.PasswordHash,
                    credential.SecurityStamp,
                    credential.Version,
                    user.IsActive,
                    credential.TemporaryPasswordIssuedAtUtc,
                    credential.TemporaryPasswordConsumedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<LocalCredentialCommandRecord?> FindCommandAsync(
        Guid userId,
        string operation,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        dbContext.LocalCredentialCommands
            .AsNoTracking()
            .Where(command => command.UserId == userId
                && command.Operation == operation
                && command.IdempotencyKey == idempotencyKey)
            .Select(command => new LocalCredentialCommandRecord(command.RequestFingerprint, command.ResultVersion))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> TryChangePasswordAsync(
        LocalCredentialCommandState read,
        string newPasswordHash,
        int hashFormatVersion,
        Guid newSecurityStamp,
        DateTimeOffset changedAtUtc,
        LocalCredentialCommand record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);
        if (newSecurityStamp == Guid.Empty || newSecurityStamp == read.SecurityStamp)
        {
            throw new ArgumentException("Yeni güvenlik damgası boş olamaz ve eskisinden farklı olmalıdır.", nameof(newSecurityStamp));
        }

        if (record.UserId != read.UserId || record.ResultVersion != read.Version + 1)
        {
            throw new ArgumentException("Komut kaydı okunan kimlik bilgisiyle eşleşmiyor.", nameof(record));
        }

        var userId = read.UserId;
        var version = read.Version;
        var stamp = read.SecurityStamp;
        var hash = read.PasswordHash;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var affected = await dbContext.LocalCredentials
            .Where(credential => credential.UserId == userId
                && credential.Version == version
                && credential.SecurityStamp == stamp
                && credential.PasswordHash == hash
                && (credential.TemporaryPasswordIssuedAtUtc == null
                    || credential.TemporaryPasswordConsumedAtUtc != null))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(credential => credential.PasswordHash, newPasswordHash)
                    .SetProperty(credential => credential.HashFormatVersion, hashFormatVersion)
                    .SetProperty(credential => credential.MustChangePassword, false)
                    .SetProperty(credential => credential.PasswordChangedAtUtc, changedAtUtc)
                    .SetProperty(credential => credential.TemporaryPasswordIssuedAtUtc, (DateTimeOffset?)null)
                    .SetProperty(credential => credential.TemporaryPasswordExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(credential => credential.TemporaryPasswordConsumedAtUtc, (DateTimeOffset?)null)
                    .SetProperty(credential => credential.FailedAccessCount, 0)
                    .SetProperty(credential => credential.LockoutEndUtc, (DateTimeOffset?)null)
                    .SetProperty(credential => credential.SecurityStamp, newSecurityStamp)
                    .SetProperty(credential => credential.Version, credential => credential.Version + 1),
                cancellationToken);
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        dbContext.LocalCredentialCommands.Add(record);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.Entry(record).State = EntityState.Detached;
            await transaction.RollbackAsync(cancellationToken);
            throw new LocalCredentialCommandKeyTakenException(exception);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: SqlUniqueIndexViolation or SqlUniqueConstraintViolation };
}

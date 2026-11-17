using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

internal sealed class SqlLocalTwoFactorLoginV1(IdentityDbContext database, ILocalTwoFactorProtectionV1 protection, TimeProvider clock)
    : ILocalTwoFactorLoginV1
{
    public async Task<bool> TryCompleteAsync(LocalCredentialLoginState read, string? code, Func<Task<bool>> completeCredential, CancellationToken cancellationToken)
    {
        if (database.ChangeTracker.HasChanges() || database.Database.CurrentTransaction is not null)
            throw new LocalTwoFactorUnavailableException();
        try { return await CompleteCoreAsync(read, code, completeCredential, cancellationToken); }
        catch (Exception error) when (error is CryptographicException or JsonException or InvalidOperationException or DbException or DbUpdateException)
        { throw new LocalTwoFactorUnavailableException(); }
        finally { database.ChangeTracker.Clear(); }
    }

    private async Task<bool> CompleteCoreAsync(LocalCredentialLoginState read, string? code, Func<Task<bool>> completeCredential, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var fresh = await database.LocalCredentials.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == read.UserId, cancellationToken);
        var secret = await database.LocalTwoFactorSecrets.SingleOrDefaultAsync(row => row.UserId == read.UserId, cancellationToken);
        if (fresh is null || !fresh.TwoFactorEnabled || fresh.SecurityStamp != read.SecurityStamp || fresh.PasswordHash != read.PasswordHash
            || GoymenERP.Modules.Identity.Domain.Credentials.LocalCredentialLockout.IsLockedOut(fresh.LockoutEndUtc, clock.GetUtcNow())
            || fresh.TemporaryPasswordIssuedAtUtc is not null && !GoymenERP.Modules.Identity.Domain.Credentials.LocalTemporaryPasswordRules.IsUsable(
                fresh.TemporaryPasswordExpiresAtUtc, fresh.TemporaryPasswordConsumedAtUtc, clock.GetUtcNow())
            || !await database.Users.AsNoTracking().AnyAsync(user => user.Id == read.UserId && user.IsActive, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken); database.ChangeTracker.Clear(); return false;
        }
        if (secret is not { IsActive: true }) throw new LocalTwoFactorUnavailableException();
        if (!LocalTwoFactorProofVerifierV1.Consume(secret, code, protection, clock.GetUtcNow())) return false;
        if (!await completeCredential())
        {
            await transaction.RollbackAsync(cancellationToken); database.ChangeTracker.Clear(); return false;
        }
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken); database.ChangeTracker.Clear(); return false;
        }
    }
}

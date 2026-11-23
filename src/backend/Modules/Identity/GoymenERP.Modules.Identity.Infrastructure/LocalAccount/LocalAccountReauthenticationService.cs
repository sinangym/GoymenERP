using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

/// <summary>
/// YOL-D27 password re-entry over the existing credential stores (no new SQL). Every path runs one password
/// verification, so timing does not tell an unknown, locked or inactive account apart. A two-factor account is
/// re-authenticated by password, as YOL-D27 states; a temporary or must-change password never verifies.
/// </summary>
internal sealed class LocalAccountReauthenticationService(
    ILocalCredentialCommandStore commandStore,
    ILocalCredentialStore store,
    ILocalPasswordHasher passwordHasher,
    TimeProvider timeProvider,
    IOptions<LocalAccountOptions> options)
    : ILocalAccountReauthenticationServiceV1
{
    public async Task<LocalAccountReauthenticationStatusV1> VerifyAsync(Guid userId, string? password, CancellationToken cancellationToken)
    {
        var supplied = password ?? string.Empty;
        var account = userId == Guid.Empty || supplied.Length == 0
            ? null
            : await commandStore.FindForCommandAsync(userId, cancellationToken);
        var normalizedUserName = account is null ? null : LocalUserName.TryNormalize(account.UserName);
        var state = normalizedUserName is null ? null : await store.FindForLoginAsync(normalizedUserName, cancellationToken);
        if (state is null || state.UserId != userId)
        {
            passwordHasher.VerifyAgainstDummy(supplied);
            return LocalAccountReauthenticationStatusV1.Refused;
        }

        var nowUtc = timeProvider.GetUtcNow();
        if (LocalCredentialLockout.IsLockedOut(state.LockoutEndUtc, nowUtc) || !state.UserIsActive
            || state.MustChangePassword || state.TemporaryPasswordIssuedAtUtc is not null)
        {
            _ = passwordHasher.Verify(state.PasswordHash, supplied);
            return LocalAccountReauthenticationStatusV1.Refused;
        }

        var verification = passwordHasher.Verify(state.PasswordHash, supplied);
        if (verification == LocalPasswordVerification.Failed)
        {
            _ = await store.TryRecordFailedAccessAsync(state, options.Value.ToLockoutPolicy(), nowUtc, cancellationToken);
            return LocalAccountReauthenticationStatusV1.Refused;
        }

        var rehashed = verification == LocalPasswordVerification.SuccessRehashNeeded ? passwordHasher.Hash(supplied) : null;
        var recorded = await store.TryRecordSuccessfulAccessAsync(
            state, rehashed, rehashed is null ? null : passwordHasher.FormatVersion, cancellationToken);
        return recorded ? LocalAccountReauthenticationStatusV1.Verified : LocalAccountReauthenticationStatusV1.Refused;
    }
}

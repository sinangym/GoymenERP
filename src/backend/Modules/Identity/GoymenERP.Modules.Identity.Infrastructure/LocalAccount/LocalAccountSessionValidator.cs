using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

/// <summary>
/// The per-request session check (spec "Durum geçişleri": the session carries the security stamp and it is
/// re-read from the database on every request). A renewed stamp, a deactivated user or an outstanding temporary
/// password ends the session. A forced password change (ID-2) keeps the session but fences it: only the password
/// change and logout are reachable.
/// </summary>
internal sealed class LocalAccountSessionValidator(ILocalCredentialStore store) : ILocalAccountSessionValidator
{
    public async Task<bool> IsCurrentAsync(Guid userId, Guid securityStamp, CancellationToken cancellationToken) =>
        await GetSessionStateAsync(userId, securityStamp, cancellationToken) == LocalAccountSessionState.Current;

    public async Task<LocalAccountSessionState> GetSessionStateAsync(
        Guid userId,
        Guid securityStamp,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || securityStamp == Guid.Empty)
        {
            return LocalAccountSessionState.NotCurrent;
        }

        var state = await store.FindForSessionAsync(userId, cancellationToken);
        if (state is null
            || state.SecurityStamp != securityStamp
            || !state.UserIsActive
            || LocalTemporaryPasswordRules.BlocksSession(
                state.TemporaryPasswordIssuedAtUtc,
                state.TemporaryPasswordConsumedAtUtc))
        {
            return LocalAccountSessionState.NotCurrent;
        }

        return state.MustChangePassword
            ? LocalAccountSessionState.PasswordChangeRequired
            : LocalAccountSessionState.Current;
    }
}

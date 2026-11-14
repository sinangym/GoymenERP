using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

/// <summary>
/// The per-request session check (spec "Durum geçişleri": the session carries the security stamp and it is
/// re-read from the database on every request). A renewed stamp, a deactivated user, a forced password change or
/// a temporary password ends the session.
/// </summary>
internal sealed class LocalAccountSessionValidator(ILocalCredentialStore store) : ILocalAccountSessionValidator
{
    public async Task<bool> IsCurrentAsync(Guid userId, Guid securityStamp, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || securityStamp == Guid.Empty)
        {
            return false;
        }

        var state = await store.FindForSessionAsync(userId, cancellationToken);
        return state is not null
            && state.SecurityStamp == securityStamp
            && state.UserIsActive
            && !state.MustChangePassword
            && state.TemporaryPasswordIssuedAtUtc is null;
    }
}

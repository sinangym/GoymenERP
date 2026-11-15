using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

/// <summary>
/// KIM-D30 logout: renews the user's security stamp with one conditional write, so every open cookie session of the
/// user fails <see cref="LocalAccountSessionValidator"/> on its next request and answers the generic 401. No session
/// row exists to delete; the stamp is the only server-side session state.
/// </summary>
internal sealed class LocalAccountLogoutService(ILocalCredentialStore store) : ILocalAccountLogoutService
{
    public async Task<LocalAccountLogoutResult> LogoutAsync(
        Guid userId,
        Guid sessionSecurityStamp,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || sessionSecurityStamp == Guid.Empty)
        {
            return LocalAccountLogoutResult.AlreadyEnded;
        }

        var renewed = await store.TryRenewSecurityStampAsync(
            userId,
            sessionSecurityStamp,
            NewStamp(sessionSecurityStamp),
            cancellationToken);
        return renewed ? LocalAccountLogoutResult.StampRenewed : LocalAccountLogoutResult.AlreadyEnded;
    }

    private static Guid NewStamp(Guid current)
    {
        Guid next;
        do
        {
            next = Guid.NewGuid();
        }
        while (next == current);

        return next;
    }
}

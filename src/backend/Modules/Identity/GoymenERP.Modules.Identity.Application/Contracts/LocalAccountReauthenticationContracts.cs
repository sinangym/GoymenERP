namespace GoymenERP.Modules.Identity.Application.Contracts;

public enum LocalAccountReauthenticationStatusV1
{
    Verified = 1,

    /// <summary>One generic refusal: wrong password, unknown, inactive, locked or session-fenced account.</summary>
    Refused = 2,
}

/// <summary>
/// YOL-D27 step-up: the signed-in user types their password again at an approval. The check is the ID-1 local account
/// password verification with the same KS-8 lockout counter and policy as login: a wrong password counts as a failed
/// attempt and can lock the account; while locked even the right password is refused. A success resets the counter
/// like a login (and may rehash). It opens no session and changes no stamp. The password is never stored or logged.
/// </summary>
public interface ILocalAccountReauthenticationServiceV1
{
    Task<LocalAccountReauthenticationStatusV1> VerifyAsync(Guid userId, string? password, CancellationToken cancellationToken);
}

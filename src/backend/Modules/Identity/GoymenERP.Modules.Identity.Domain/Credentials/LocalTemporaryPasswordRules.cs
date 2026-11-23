namespace GoymenERP.Modules.Identity.Domain.Credentials;

/// <summary>
/// Pure rules of the KIM-D23/KIM-D43 temporary password. Expiry is decided only by the expiry stored when the password
/// was issued; the configured lifetime is applied at issue time and never re-read at login.
/// </summary>
public static class LocalTemporaryPasswordRules
{
    /// <summary>
    /// A temporary password can open a session while it has a stored expiry that lies strictly in the future and it
    /// has not been consumed. A missing expiry is unusable (fail-closed).
    /// </summary>
    public static bool IsUsable(
        DateTimeOffset? expiresAtUtc,
        DateTimeOffset? consumedAtUtc,
        DateTimeOffset nowUtc) =>
        consumedAtUtc is null && expiresAtUtc is { } expiresAt && nowUtc < expiresAt;

    /// <summary>
    /// An issued temporary password still blocks every session until it is consumed by a login; after that the
    /// session is fenced by <c>MustChangePassword</c> until the forced password change clears the temporary fields.
    /// </summary>
    public static bool BlocksSession(DateTimeOffset? issuedAtUtc, DateTimeOffset? consumedAtUtc) =>
        issuedAtUtc is not null && consumedAtUtc is null;
}

using GoymenERP.Modules.Identity.Domain.Credentials;

namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>
/// Versioned password hashing (KIM-D12 option A: the framework <c>PasswordHasher</c>, PBKDF2). The comparison is
/// constant time inside the hasher. Implementations never log or keep the password.
/// </summary>
public interface ILocalPasswordHasher
{
    /// <summary>The format version written next to every new hash (<c>LocalCredentials.HashFormatVersion</c>).</summary>
    int FormatVersion { get; }

    string Hash(string password);

    LocalPasswordVerification Verify(string passwordHash, string password);

    /// <summary>
    /// Runs one verification against a fixed throw-away hash so an unknown user name costs the same time as a
    /// known one. The result is always ignored by the caller.
    /// </summary>
    void VerifyAgainstDummy(string password);
}

public enum LocalPasswordVerification
{
    Failed = 0,
    Success = 1,
    SuccessRehashNeeded = 2,
}

/// <summary>What login reads once at its start; the conditional writes compare against these values.</summary>
public sealed record LocalCredentialLoginState(
    Guid UserId,
    string PasswordHash,
    bool MustChangePassword,
    int FailedAccessCount,
    DateTimeOffset? LockoutEndUtc,
    DateTimeOffset? TemporaryPasswordIssuedAtUtc,
    Guid SecurityStamp,
    bool UserIsActive);

/// <summary>
/// Storage port of the login path. Login writes are single conditional updates that never raise
/// <c>Version</c>; when the row changed after the read (another hash, stamp or temporary password, or a lock set
/// in between) nothing is written and the result is <see langword="false"/>.
/// </summary>
public interface ILocalCredentialStore
{
    Task<LocalCredentialLoginState?> FindForLoginAsync(string normalizedUserName, CancellationToken cancellationToken);

    Task<bool> TryRecordFailedAccessAsync(
        LocalCredentialLoginState read,
        LocalCredentialLockoutPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<bool> TryRecordSuccessfulAccessAsync(
        LocalCredentialLoginState read,
        string? rehashedPasswordHash,
        int? rehashedFormatVersion,
        CancellationToken cancellationToken);

    /// <summary>The per-request session check: the stored stamp and whether the account may still hold a session.</summary>
    Task<LocalCredentialSessionState?> FindForSessionAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// KIM-D30 logout: one conditional write that replaces the stamp and raises <c>Version</c> by one, only while
    /// the stored stamp is still <paramref name="readSecurityStamp"/>. <see langword="false"/> means the stamp was
    /// already renewed (another logout, a password change) and nothing was written; the session is over either way.
    /// </summary>
    Task<bool> TryRenewSecurityStampAsync(
        Guid userId,
        Guid readSecurityStamp,
        Guid newSecurityStamp,
        CancellationToken cancellationToken);
}

public sealed record LocalCredentialSessionState(
    Guid SecurityStamp,
    bool UserIsActive,
    bool MustChangePassword,
    DateTimeOffset? TemporaryPasswordIssuedAtUtc);

/// <summary>ID-1 local account login (<c>POST /api/v1/identity/session/login</c>). It takes no idempotency key.</summary>
public interface ILocalAccountLoginService
{
    Task<LocalAccountLoginResult> LoginAsync(string? userName, string? password, CancellationToken cancellationToken);
}

/// <summary>
/// The login decision. A refusal carries no detail for the caller: every refusal is the same 401 so the response
/// never shows whether the user exists, is locked or inactive.
/// </summary>
public sealed record LocalAccountLoginResult
{
    private LocalAccountLoginResult(Guid? userId, Guid? securityStamp)
    {
        UserId = userId;
        SecurityStamp = securityStamp;
    }

    public static LocalAccountLoginResult Refused { get; } = new(null, null);

    public bool Succeeded => UserId is not null;

    public Guid? UserId { get; }

    public Guid? SecurityStamp { get; }

    public static LocalAccountLoginResult Success(Guid userId, Guid securityStamp) => new(userId, securityStamp);
}

/// <summary>Validates a local account session on every request against the stored security stamp.</summary>
public interface ILocalAccountSessionValidator
{
    /// <summary><see langword="true"/> only for <see cref="LocalAccountSessionState.Current"/>.</summary>
    Task<bool> IsCurrentAsync(Guid userId, Guid securityStamp, CancellationToken cancellationToken);

    /// <summary>
    /// ID-2: a session of an account with <c>MustChangePassword=true</c> stays open but fenced
    /// (<see cref="LocalAccountSessionState.PasswordChangeRequired"/>); everything else that is not current ends.
    /// </summary>
    Task<LocalAccountSessionState> GetSessionStateAsync(Guid userId, Guid securityStamp, CancellationToken cancellationToken);
}

public enum LocalAccountSessionState
{
    /// <summary>Unknown user, other stamp, inactive account or an outstanding temporary password: the session ends.</summary>
    NotCurrent = 0,

    /// <summary>The session is valid and unrestricted.</summary>
    Current = 1,

    /// <summary>
    /// The session is valid but the account must change its password first (spec ID-2): only the password change,
    /// logout and the endpoints the client needs to reach them are allowed; everything else is refused with 403
    /// <see cref="IdentitySessionProblemCodesV1.PasswordChangeRequired"/>.
    /// </summary>
    PasswordChangeRequired = 2,
}

/// <summary>
/// KIM-D30 logout (<c>POST /api/v1/identity/session/logout</c>): the user's security stamp is renewed, so every open
/// cookie session of that user fails the per-request stamp check on its next request. There is no server-side
/// session table; the decision does not open a "this session only" option.
/// </summary>
public interface ILocalAccountLogoutService
{
    /// <summary>
    /// Renews the stamp when <paramref name="sessionSecurityStamp"/> is still the stored one. The caller signs the
    /// cookie out whatever the result; a stale or unknown session simply has nothing left to end.
    /// </summary>
    Task<LocalAccountLogoutResult> LogoutAsync(Guid userId, Guid sessionSecurityStamp, CancellationToken cancellationToken);
}

public enum LocalAccountLogoutResult
{
    /// <summary>The stamp was renewed; every session of the user ends on its next request.</summary>
    StampRenewed = 1,

    /// <summary>The stored stamp had already changed (or the credential is gone); nothing was written.</summary>
    AlreadyEnded = 2,
}

/// <summary>
/// <c>POST /api/v1/identity/session/login</c> body. Both fields are required in the document; an empty, missing or
/// unusable value is still answered with the same refusal as a wrong password. The password is never logged.
/// </summary>
public sealed record LocalAccountLoginRequestV1
{
    public required string UserName { get; init; }

    public required string Password { get; init; }

    /// <summary>The password stays out of logs and debugger output.</summary>
    public override string ToString() => nameof(LocalAccountLoginRequestV1);
}

public static class IdentitySessionProblemCodesV1
{
    /// <summary>HTTP 429: the login rate limit window is used up (<c>Identity:LocalAccount:RateLimit:Login</c>).</summary>
    public const string LoginRateLimited = "login_rate_limited";

    /// <summary>
    /// HTTP 400 (KIM-D31): the antiforgery request header is missing or does not validate on the login request or on
    /// a write (POST/PUT/PATCH/DELETE) authenticated by the local account cookie. Bearer requests never get it.
    /// </summary>
    public const string AntiforgeryTokenInvalid = "antiforgery_token_invalid";

    /// <summary>HTTP 403 (spec ID-2, KS-7): the account must change its password before any other request.</summary>
    public const string PasswordChangeRequired = "identity.password_change_required";

    /// <summary>HTTP 400: the new password breaks the configured policy; rule codes are under <c>violations</c>.</summary>
    public const string PasswordPolicyViolation = "password_policy_violation";

    /// <summary>HTTP 400: the current password does not verify. It does not count towards the lockout.</summary>
    public const string CurrentPasswordInvalid = "current_password_invalid";

    /// <summary>HTTP 400: the body is missing, malformed or has an unusable field (named in <c>field</c>).</summary>
    public const string RequestInvalid = "request_invalid";

    /// <summary>HTTP 400: the <c>Idempotency-Key</c> header is missing, repeated, empty or too long.</summary>
    public const string IdempotencyKeyInvalid = "idempotency_key_invalid";

    /// <summary>HTTP 409: the same <c>Idempotency-Key</c> was already used with another request.</summary>
    public const string IdempotencyConflict = "idempotency_conflict";

    /// <summary>HTTP 409: <c>expectedVersion</c> is not the stored credential version.</summary>
    public const string VersionConflict = "version_conflict";
}

/// <summary>
/// <c>GET /api/v1/identity/session/antiforgery</c> response (KIM-D31). The request token goes back in the
/// antiforgery request header of the next write; the paired cookie is set on the same response.
/// </summary>
public sealed record LocalAccountAntiforgeryTokenV1(string RequestToken);

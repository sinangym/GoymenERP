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
    Task<bool> IsCurrentAsync(Guid userId, Guid securityStamp, CancellationToken cancellationToken);
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
}

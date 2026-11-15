using GoymenERP.Modules.Identity.Domain.Credentials;

namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>
/// <c>POST /api/v1/identity/session/password</c> body (spec ID-2). <see cref="ExpectedVersion"/> is the decimal
/// text of <c>localCredentialVersion</c> from <c>GET /api/v1/identity/me</c>. Passwords are never logged.
/// </summary>
public sealed record LocalAccountPasswordChangeRequestV1
{
    public required string CurrentPassword { get; init; }

    public required string NewPassword { get; init; }

    public required string ExpectedVersion { get; init; }

    /// <summary>The passwords stay out of logs and debugger output.</summary>
    public override string ToString() => nameof(LocalAccountPasswordChangeRequestV1);
}

/// <summary>The password change command as the endpoint hands it to the service.</summary>
public sealed record LocalAccountPasswordChangeCommand(
    Guid UserId,
    Guid SessionSecurityStamp,
    string IdempotencyKey,
    long ExpectedVersion,
    string CurrentPassword,
    string NewPassword)
{
    /// <summary>The passwords stay out of logs and debugger output.</summary>
    public override string ToString() => nameof(LocalAccountPasswordChangeCommand);
}

public enum LocalAccountPasswordChangeStatus
{
    /// <summary>The password was changed; the caller re-issues its session with <see cref="LocalAccountPasswordChangeResult.NewSecurityStamp"/>.</summary>
    Changed = 1,

    /// <summary>The same key and request already succeeded; nothing was written again.</summary>
    Replayed = 2,

    PolicyViolation = 3,

    CurrentPasswordInvalid = 4,

    VersionConflict = 5,

    IdempotencyConflict = 6,

    /// <summary>The session's stamp is no longer the stored one (or the credential is gone): the session ends (401).</summary>
    SessionNotCurrent = 7,
}

public sealed record LocalAccountPasswordChangeResult(
    LocalAccountPasswordChangeStatus Status,
    Guid? NewSecurityStamp,
    long? CurrentVersion,
    IReadOnlyList<string> PolicyViolations)
{
    public static LocalAccountPasswordChangeResult Of(LocalAccountPasswordChangeStatus status) => new(status, null, null, []);
}

/// <summary>ID-2 password change, including the forced first change after <c>bootstrap-founder</c> or a reset.</summary>
public interface ILocalAccountPasswordChangeService
{
    Task<LocalAccountPasswordChangeResult> ChangeAsync(LocalAccountPasswordChangeCommand command, CancellationToken cancellationToken);
}

/// <summary>What the password change reads before it writes; the conditional write compares against these values.</summary>
public sealed record LocalCredentialCommandState(
    Guid UserId,
    string UserName,
    string PasswordHash,
    Guid SecurityStamp,
    long Version,
    bool UserIsActive,
    DateTimeOffset? TemporaryPasswordIssuedAtUtc);

/// <summary>A recorded credential command, read back for an idempotent replay.</summary>
public sealed record LocalCredentialCommandRecord(byte[] RequestFingerprint, long ResultVersion);

/// <summary>
/// Storage port of credential commands. Separate from <see cref="ILocalCredentialStore"/> (the login path) because a
/// command raises <c>Version</c> and writes an idempotency record in the same transaction.
/// </summary>
public interface ILocalCredentialCommandStore
{
    Task<LocalCredentialCommandState?> FindForCommandAsync(Guid userId, CancellationToken cancellationToken);

    Task<LocalCredentialCommandRecord?> FindCommandAsync(
        Guid userId,
        string operation,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// One transaction: a conditional UPDATE of the credential (same version, stamp and hash as read) plus the
    /// idempotency record. <see langword="false"/> means the row changed after the read and nothing was written.
    /// A concurrent request with the same key surfaces as <see cref="LocalCredentialCommandKeyTakenException"/>.
    /// </summary>
    Task<bool> TryChangePasswordAsync(
        LocalCredentialCommandState read,
        string newPasswordHash,
        int hashFormatVersion,
        Guid newSecurityStamp,
        DateTimeOffset changedAtUtc,
        LocalCredentialCommand record,
        CancellationToken cancellationToken);
}

/// <summary>The idempotency record's unique key was taken by a concurrent request; the caller reads it back.</summary>
public sealed class LocalCredentialCommandKeyTakenException(Exception innerException)
    : Exception("Tekrar anahtarı eşzamanlı bir istekle kullanıldı.", innerException);

/// <summary>Reads the company names used as blocklist terms (KIM-D4: company name derivatives).</summary>
public interface ILocalPasswordContextTermSource
{
    Task<IReadOnlyList<string>> GetCompanyTermsAsync(CancellationToken cancellationToken);
}

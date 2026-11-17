namespace GoymenERP.Modules.Identity.Application.Contracts;

public sealed class LocalTwoFactorSessionNotCurrentException : Exception { }
public sealed class LocalTwoFactorUnavailableException : Exception { }

public interface ILocalTwoFactorProtectionV1
{
    string Protect(Guid userId, string purpose, string plaintext);
    string Unprotect(Guid userId, string purpose, string protectedText);
}
public interface ILocalTwoFactorLoginV1
{
    /// <summary>Verify and consume one factor plus the successful credential update in one SQL transaction.</summary>
    Task<bool> TryCompleteAsync(LocalCredentialLoginState state, string? code, Func<Task<bool>> completeCredential, CancellationToken cancellationToken);
}
public sealed record LocalTwoFactorStateV1(string CredentialVersion, bool Enabled, int RecoveryCodesRemaining);
public sealed record LocalTwoFactorProofV1(string ExpectedVersion, string CurrentPassword, string? Code, string? EnrollmentToken)
{
    public override string ToString() => nameof(LocalTwoFactorProofV1);
}
public sealed record LocalTwoFactorEnrollmentV1(string CredentialVersion, string SecretKey, string AuthenticatorUri,
    IReadOnlyList<string> RecoveryCodes, string EnrollmentToken)
{
    public override string ToString() => nameof(LocalTwoFactorEnrollmentV1);
}
public enum LocalTwoFactorCommandStatusV1 { Applied = 1, Replayed = 2, SessionNotCurrent = 3, VersionConflict = 4, InvalidProof = 5, InvalidRequest = 6, StateConflict = 7, IdempotencyConflict = 8 }
public sealed record LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1 Status, string? Version = null,
    Guid? SecurityStamp = null, bool Enabled = false, IReadOnlyList<string>? RecoveryCodes = null, string? OriginalCorrelationId = null)
{
    public override string ToString() => nameof(LocalTwoFactorCommandResultV1);
}
public interface ILocalTwoFactorServiceV1
{
    Task<LocalTwoFactorStateV1?> ReadAsync(Guid userId, Guid stamp, CancellationToken ct);
    Task<LocalTwoFactorEnrollmentV1?> BeginEnrollmentAsync(Guid userId, Guid stamp, LocalTwoFactorProofV1 request, CancellationToken ct);
    Task<LocalTwoFactorCommandResultV1> ChangeAsync(Guid userId, Guid stamp, string action, LocalTwoFactorProofV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken ct);
}

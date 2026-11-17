namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>KIM-D27, human answer relayed at #1189/6951606530. Server terminal only; no web endpoint or role grant.</summary>
public interface ILocalTwoFactorOperatorRecoveryConsoleV1
{
    bool IsInteractive { get; }
    string? ReadTargetUserName();
    string? ReadIdempotencyKey();
    string? ReadReason();
    LocalTwoFactorRecoveryOperatorDeclarationV1? ReadOperatorDeclaration();
    bool Confirm(LocalTwoFactorOperatorRecoveryConfirmationV1 confirmation);
}

public sealed record LocalTwoFactorRecoveryOperatorDeclarationV1(string OperatingSystemUser, string MachineName)
{
    public override string ToString() => nameof(LocalTwoFactorRecoveryOperatorDeclarationV1);
}

/// <summary>Confirmed identities and versions, never a password, seed, code, protected secret or security stamp.</summary>
public sealed record LocalTwoFactorOperatorRecoveryConfirmationV1(Guid TargetUserId, string TargetUserName, bool TargetIsActive,
    long UserVersion, long CredentialVersion, long SecretVersion, bool TwoFactorEnabled, bool IsRecordedReplay,
    string IdempotencyKey, string Reason, string? DatabaseServer, string? DatabaseCatalog,
    LocalTwoFactorRecoveryOperatorDeclarationV1 Operator)
{
    public override string ToString() => nameof(LocalTwoFactorOperatorRecoveryConfirmationV1);
}

public enum LocalTwoFactorOperatorRecoveryStatusV1
{
    Recovered = 1,
    AlreadyDisabled = 2,
    ConcurrentChange = 3,
    InvalidInvocation = 4,
    NotInteractive = 5,
    InvalidRequest = 6,
    TargetNotFound = 7,
    NotConfirmed = 8,
    IdempotencyConflict = 9,
    SourceUnavailable = 10,
    AuditUnavailable = 11,
    SecurityEventUnavailable = 12,
}

/// <summary>The immutable result of this attempt. On replay it describes the ORIGINAL outcome, not current 2FA state.</summary>
public sealed record LocalTwoFactorOperatorRecoveryResultV1(LocalTwoFactorOperatorRecoveryStatusV1 Status, Guid? TargetUserId = null,
    long? RecordedCredentialVersion = null, bool IsReplay = false, string? OriginalCorrelationId = null, Guid? AttemptId = null)
{
    public bool Completed => Status == LocalTwoFactorOperatorRecoveryStatusV1.Recovered;
}

public interface ILocalTwoFactorOperatorRecoveryServiceV1
{
    /// <param name="invocationValid">Only the exact two nonsecret command words are valid. No argv contents reach this service.</param>
    Task<LocalTwoFactorOperatorRecoveryResultV1> RunAsync(bool invocationValid, ILocalTwoFactorOperatorRecoveryConsoleV1 console,
        CancellationToken cancellationToken = default);
}

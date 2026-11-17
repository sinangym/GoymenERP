namespace GoymenERP.Modules.Identity.Domain.Credentials;

public enum LocalTwoFactorOperatorRecoveryOutcome { Recovered = 1, AlreadyDisabled = 2, ConcurrentChange = 3 }

/// <summary>
/// Immutable server-operator command history for a GLOBAL local credential, including a target with no company
/// membership. The globally unique key binds its target and operator; it never supplies a role or company scope.
/// No password, authenticator, recovery-code hash or security stamp is stored here.
/// </summary>
public sealed class LocalTwoFactorOperatorRecoveryAttempt
{
    public const int HashLength = 64;
    public const int ReasonMaximumLength = 1000;
    public const int CorrelationMaximumLength = 128;
    private LocalTwoFactorOperatorRecoveryAttempt() { }

    public LocalTwoFactorOperatorRecoveryAttempt(Guid id, Guid targetUserId, string keyHash, string requestHash,
        string targetInputHash, string operatorHash, string reason, long confirmedUserVersion, long confirmedCredentialVersion,
        long confirmedSecretVersion, bool confirmedTwoFactorEnabled, LocalTwoFactorOperatorRecoveryOutcome outcome,
        long resultCredentialVersion, long resultSecretVersion, DateTimeOffset recordedAtUtc, string originalCorrelationId)
    {
        if (id == Guid.Empty || targetUserId == Guid.Empty || !Enum.IsDefined(outcome)) throw new ArgumentException("A versioned recovery target and closed outcome are required.");
        RequireHash(keyHash); RequireHash(requestHash); RequireHash(targetInputHash); RequireHash(operatorHash);
        RequireCanonical(reason, ReasonMaximumLength); RequireCanonical(originalCorrelationId, CorrelationMaximumLength);
        if (confirmedUserVersion < 1 || confirmedCredentialVersion < 1 || confirmedSecretVersion < 0
            || resultCredentialVersion < confirmedCredentialVersion || resultSecretVersion < 0 || recordedAtUtc == default || recordedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Recovery versions and UTC evidence are invalid.");
        if (outcome == LocalTwoFactorOperatorRecoveryOutcome.Recovered
            && (!confirmedTwoFactorEnabled || confirmedSecretVersion < 1 || confirmedCredentialVersion == long.MaxValue
                || confirmedSecretVersion == long.MaxValue || resultCredentialVersion != confirmedCredentialVersion + 1
                || resultSecretVersion != confirmedSecretVersion + 1))
            throw new ArgumentException("A successful recovery disables one confirmed factor generation.");
        if (outcome == LocalTwoFactorOperatorRecoveryOutcome.AlreadyDisabled
            && (confirmedTwoFactorEnabled || resultCredentialVersion != confirmedCredentialVersion || resultSecretVersion != confirmedSecretVersion))
            throw new ArgumentException("An already-disabled refusal changes no credential or factor version.");
        Id = id; TargetUserId = targetUserId; KeyHash = keyHash; RequestHash = requestHash; TargetInputHash = targetInputHash;
        OperatorHash = operatorHash; Reason = reason; ConfirmedUserVersion = confirmedUserVersion;
        ConfirmedCredentialVersion = confirmedCredentialVersion; ConfirmedSecretVersion = confirmedSecretVersion;
        ConfirmedTwoFactorEnabled = confirmedTwoFactorEnabled; Outcome = outcome; ResultCredentialVersion = resultCredentialVersion;
        ResultSecretVersion = resultSecretVersion; RecordedAtUtc = recordedAtUtc; OriginalCorrelationId = originalCorrelationId;
    }
    public Guid Id { get; private set; }
    public Guid TargetUserId { get; private set; }
    public string KeyHash { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string TargetInputHash { get; private set; } = string.Empty;
    public string OperatorHash { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public long ConfirmedUserVersion { get; private set; }
    public long ConfirmedCredentialVersion { get; private set; }
    public long ConfirmedSecretVersion { get; private set; }
    public bool ConfirmedTwoFactorEnabled { get; private set; }
    public LocalTwoFactorOperatorRecoveryOutcome Outcome { get; private set; }
    public long ResultCredentialVersion { get; private set; }
    public long ResultSecretVersion { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }
    public string OriginalCorrelationId { get; private set; } = string.Empty;
    public static void RequireHash(string hash)
    {
        if (hash is null || hash.Length != HashLength || hash.Any(value => value is not (>= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new ArgumentException("A canonical SHA-256 fingerprint is required.");
    }
    public static void RequireCanonical(string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Trim() != value || value.Any(char.IsControl))
            throw new ArgumentException("A bounded canonical recovery value is required.");
    }
    public override string ToString() => nameof(LocalTwoFactorOperatorRecoveryAttempt);
}

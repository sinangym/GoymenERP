using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Audit.Domain.Events;

public enum LocalTwoFactorOperatorRecoveryAuditOutcome { Recovered = 1, AlreadyDisabled = 2, ConcurrentChange = 3 }
public sealed record LocalTwoFactorRecoveryOperatorAudit(string OperatingSystemUser, string MachineName)
{ public override string ToString() => nameof(LocalTwoFactorRecoveryOperatorAudit); }
public sealed record LocalTwoFactorRecoveryVersionAudit(long UserVersion, long CredentialVersion, long SecretVersion, bool TwoFactorEnabled)
{
    public void Validate()
    {
        if (UserVersion < 1 || CredentialVersion < 1 || SecretVersion < 0 || TwoFactorEnabled && SecretVersion < 1)
            throw new ArgumentException("A local credential recovery snapshot is invalid.");
    }
}
public sealed record LocalTwoFactorOperatorRecoveryAuditData(Guid AttemptId, Guid TargetUserId,
    LocalTwoFactorRecoveryOperatorAudit Operator, LocalTwoFactorRecoveryVersionAudit Confirmed,
    LocalTwoFactorRecoveryVersionAudit Before, LocalTwoFactorRecoveryVersionAudit After,
    LocalTwoFactorOperatorRecoveryAuditOutcome Outcome, string Reason, DateTimeOffset OccurredAtUtc,
    string CorrelationId, string? CausationId, string IdempotencyKeyHash)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Operator); ArgumentNullException.ThrowIfNull(Confirmed);
        ArgumentNullException.ThrowIfNull(Before); ArgumentNullException.ThrowIfNull(After);
        Confirmed.Validate(); Before.Validate(); After.Validate();
        if (AttemptId == Guid.Empty || TargetUserId == Guid.Empty || !Enum.IsDefined(Outcome) || OccurredAtUtc == default || OccurredAtUtc.Offset != TimeSpan.Zero
            || Before.UserVersion != After.UserVersion)
            throw new ArgumentException("A closed platform credential recovery audit is required.");
        var valid = Outcome switch
        {
            LocalTwoFactorOperatorRecoveryAuditOutcome.Recovered => Confirmed == Before && Before.TwoFactorEnabled && !After.TwoFactorEnabled
                && Before.CredentialVersion < long.MaxValue && Before.SecretVersion < long.MaxValue
                && After.CredentialVersion == Before.CredentialVersion + 1 && After.SecretVersion == Before.SecretVersion + 1,
            LocalTwoFactorOperatorRecoveryAuditOutcome.AlreadyDisabled => Confirmed == Before && Before == After && !Before.TwoFactorEnabled,
            LocalTwoFactorOperatorRecoveryAuditOutcome.ConcurrentChange => Confirmed != Before && Before == After,
            _ => false,
        };
        if (!valid) throw new ArgumentException("Recovery audit outcome does not match the actual version transition.");
        Canonical(Operator.OperatingSystemUser, 256); Canonical(Operator.MachineName, 256);
        Canonical(Reason, 1000); Canonical(CorrelationId, 128); if (CausationId is not null) Canonical(CausationId, 128);
        if (IdempotencyKeyHash is null || IdempotencyKeyHash.Length != 64
            || IdempotencyKeyHash.Any(c => c is not (>= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new ArgumentException("A canonical recovery key hash is required.");
    }
    public string ComputeIntegrity()
    {
        Validate();
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this, Json)));
    }
    private static void Canonical(string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Trim() != value || value.Any(char.IsControl))
            throw new ArgumentException("Canonical bounded recovery evidence is required.");
    }
    public override string ToString() => nameof(LocalTwoFactorOperatorRecoveryAuditData);
}

public sealed partial class AuditEvent
{
    public const string PlatformCredentialRecoverySubject = "PlatformCredentialRecovery";
    public const string OperatorTwoFactorRecoveryActor = "identity.recover-two-factor";
    public const string OperatorTwoFactorRecoveryEvent = "identity.local-credentials.operator-two-factor-recovered.v1";
    public const string OperatorTwoFactorRecoveryRefusedEvent = "identity.local-credentials.operator-two-factor-recovery-refused.v1";
    public const string OperatorTwoFactorRecoveryDeclaration = "IdentityTwoFactorOperatorRecoveryV1";
    private sealed record OperatorRecoveryApproval(string Kind, LocalTwoFactorRecoveryOperatorAudit Operator,
        LocalTwoFactorRecoveryVersionAudit Confirmed, LocalTwoFactorOperatorRecoveryAuditOutcome Result);

    // Separate canonical V3 subject; normal V1 permission-bearing constructors and V2 Product provenance are unchanged.
    private AuditEvent(LocalTwoFactorOperatorRecoveryAuditData data)
    {
        data.Validate();
        Id = data.AttemptId; SubjectKind = PlatformCredentialRecoverySubject; CompanyId = null; OriginCompanyId = null; AuthorityRevision = null;
        OccurredAtUtc = data.OccurredAtUtc; EntityType = "Identity.LocalCredential"; EntityId = data.TargetUserId.ToString("D");
        var succeeded = data.Outcome == LocalTwoFactorOperatorRecoveryAuditOutcome.Recovered;
        EventType = succeeded ? OperatorTwoFactorRecoveryEvent : OperatorTwoFactorRecoveryRefusedEvent;
        Operation = "RecoverTwoFactor"; EntityVersionBefore = data.Before.CredentialVersion; EntityVersionAfter = data.After.CredentialVersion;
        ActorType = "System"; ActorId = OperatorTwoFactorRecoveryActor; PermissionCode = null; RoleCodesJson = "[]";
        BeforeJson = JsonSerializer.Serialize(data.Before, LocalTwoFactorOperatorRecoveryAuditData.Json);
        AfterJson = JsonSerializer.Serialize(data.After, LocalTwoFactorOperatorRecoveryAuditData.Json);
        ApprovalContextJson = JsonSerializer.Serialize(new OperatorRecoveryApproval(OperatorTwoFactorRecoveryDeclaration,
            data.Operator, data.Confirmed, data.Outcome), LocalTwoFactorOperatorRecoveryAuditData.Json);
        Reason = data.Reason; AiInvolvement = "None"; AiContextJson = null; SourceChannel = "System";
        ClientIpAddress = null; UserAgent = null; CorrelationId = data.CorrelationId; CausationId = data.CausationId;
        IdempotencyKeyHash = data.IdempotencyKeyHash; Outcome = succeeded ? "Succeeded" : "Refused"; SchemaVersion = 3;
        IntegrityHash = data.ComputeIntegrity();
    }
    public static AuditEvent CreateLocalTwoFactorOperatorRecovery(LocalTwoFactorOperatorRecoveryAuditData data) => new(data);
    public LocalTwoFactorOperatorRecoveryAuditData ToLocalTwoFactorOperatorRecoveryIntegrityData()
    {
        if (SubjectKind != PlatformCredentialRecoverySubject || CompanyId is not null || OriginCompanyId is not null || AuthorityRevision is not null
            || SchemaVersion != 3 || EntityType != "Identity.LocalCredential" || Operation != "RecoverTwoFactor"
            || !Guid.TryParseExact(EntityId, "D", out var target) || target == Guid.Empty || EntityId != target.ToString("D")
            || ActorType != "System" || ActorId != OperatorTwoFactorRecoveryActor || PermissionCode is not null || RoleCodesJson != "[]"
            || AiInvolvement != "None" || AiContextJson is not null || SourceChannel != "System" || ClientIpAddress is not null || UserAgent is not null)
            throw new InvalidOperationException("The canonical platform recovery subject is invalid.");
        var before = JsonSerializer.Deserialize<LocalTwoFactorRecoveryVersionAudit>(BeforeJson, LocalTwoFactorOperatorRecoveryAuditData.Json)
            ?? throw new InvalidOperationException("Recovery before evidence is missing.");
        var after = JsonSerializer.Deserialize<LocalTwoFactorRecoveryVersionAudit>(AfterJson, LocalTwoFactorOperatorRecoveryAuditData.Json)
            ?? throw new InvalidOperationException("Recovery after evidence is missing.");
        var approval = JsonSerializer.Deserialize<OperatorRecoveryApproval>(ApprovalContextJson ?? "", LocalTwoFactorOperatorRecoveryAuditData.Json)
            ?? throw new InvalidOperationException("Recovery operator evidence is missing.");
        var succeeded = approval.Result == LocalTwoFactorOperatorRecoveryAuditOutcome.Recovered;
        if (approval.Kind != OperatorTwoFactorRecoveryDeclaration || EntityVersionBefore != before.CredentialVersion || EntityVersionAfter != after.CredentialVersion
            || EventType != (succeeded ? OperatorTwoFactorRecoveryEvent : OperatorTwoFactorRecoveryRefusedEvent) || Outcome != (succeeded ? "Succeeded" : "Refused"))
            throw new InvalidOperationException("The recovery audit projection does not match its envelope.");
        var data = new LocalTwoFactorOperatorRecoveryAuditData(Id, target, approval.Operator, approval.Confirmed, before, after, approval.Result,
            Reason, OccurredAtUtc, CorrelationId, CausationId, IdempotencyKeyHash);
        if (!string.Equals(data.ComputeIntegrity(), IntegrityHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The canonical recovery integrity hash does not match.");
        return data;
    }
}

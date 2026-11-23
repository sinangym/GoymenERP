using System.Globalization;

namespace GoymenERP.Modules.AiKnowledge.Domain;

/// <summary>
/// WP-A4 (AI-D32 "onaylı karar dizini", DALGA_PLANI_v2 A.4): the AI knowledge sources that ship inside the API publish
/// output. Each kind is one versioned package file; its version is the SHA-256 of the packaged bytes.
/// </summary>
public enum AiKnowledgeSourceKindV1
{
    DecisionIndex = 1,
    ReleaseKnowledge = 2,
}

public static class AiKnowledgeSourceKindsV1
{
    public const string DecisionIndex = "decision-index";
    public const string ReleaseKnowledge = "release-knowledge";

    public static IReadOnlyList<AiKnowledgeSourceKindV1> All { get; } =
        [AiKnowledgeSourceKindV1.DecisionIndex, AiKnowledgeSourceKindV1.ReleaseKnowledge];

    public static string Wire(AiKnowledgeSourceKindV1 kind) => kind switch
    {
        AiKnowledgeSourceKindV1.DecisionIndex => DecisionIndex,
        AiKnowledgeSourceKindV1.ReleaseKnowledge => ReleaseKnowledge,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParse(string? value, out AiKnowledgeSourceKindV1 kind)
    {
        switch (value)
        {
            case DecisionIndex:
                kind = AiKnowledgeSourceKindV1.DecisionIndex;
                return true;
            case ReleaseKnowledge:
                kind = AiKnowledgeSourceKindV1.ReleaseKnowledge;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}

public enum AiKnowledgeApprovalStatusV1
{
    Approved = 1,
    Withdrawn = 2,
}

/// <summary>
/// One human approval of one packaged knowledge version for one company. Approved (version 1) → Withdrawn (version 2) is
/// the only transition; an approval is never edited or deleted, and a withdrawn approval never comes back (a new approval
/// is a new record). A changed package has a new SHA-256 and therefore needs a new approval: an approval never moves to
/// other bytes (the same rule as AI-D34 a: an approval binds to the concrete thing that was seen).
/// </summary>
public sealed class AiKnowledgeApprovalV1
{
    public const int ReasonMaximumLength = 500;

    private AiKnowledgeApprovalV1(Guid approvedByUserId, AiKnowledgeApprovalStatusV1 status, long version)
    {
        ApprovedByUserId = approvedByUserId;
        Status = status;
        Version = version;
    }

    public Guid ApprovedByUserId { get; }
    public AiKnowledgeApprovalStatusV1 Status { get; private set; }
    public long Version { get; private set; }
    public Guid? WithdrawnByUserId { get; private set; }
    public DateTimeOffset? WithdrawnAtUtc { get; private set; }
    public string? WithdrawReason { get; private set; }

    public static AiKnowledgeApprovalV1 Approve(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("An approval needs a person.", nameof(actorUserId));
        return new AiKnowledgeApprovalV1(actorUserId, AiKnowledgeApprovalStatusV1.Approved, 1);
    }

    public static AiKnowledgeApprovalV1 Restore(Guid approvedByUserId, AiKnowledgeApprovalStatusV1 status, long version) =>
        new(approvedByUserId, status, version);

    public void Withdraw(long expectedVersion, Guid actorUserId, string reason, DateTimeOffset now)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("A withdrawal needs a person.", nameof(actorUserId));
        if (expectedVersion != Version) throw new AiKnowledgeRuleException(AiKnowledgeProblemCodesV1.VersionConflict);
        if (Status != AiKnowledgeApprovalStatusV1.Approved) throw new AiKnowledgeRuleException(AiKnowledgeProblemCodesV1.ApprovalNotActive);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > ReasonMaximumLength)
            throw new ArgumentException("A withdrawal needs a reason.", nameof(reason));
        Status = AiKnowledgeApprovalStatusV1.Withdrawn;
        Version = checked(Version + 1);
        WithdrawnByUserId = actorUserId;
        WithdrawnAtUtc = now;
        WithdrawReason = reason;
    }

    public static string WireStatus(AiKnowledgeApprovalStatusV1 status) => status switch
    {
        AiKnowledgeApprovalStatusV1.Approved => "onaylandi",
        AiKnowledgeApprovalStatusV1.Withdrawn => "geri_alindi",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string WireVersion(long version) => version.ToString(CultureInfo.InvariantCulture);
}

public sealed class AiKnowledgeRuleException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>The module's own permission name (spelled exactly as <c>ErpPermissions</c> registers it). Registered, not granted.</summary>
public static class AiKnowledgePermissionManifestV1
{
    public const int Version = 1;
    public const string Approve = "ai.knowledge.approve";

    public static IReadOnlySet<string> Names { get; } = new HashSet<string>([Approve], StringComparer.Ordinal);
}

public static class AiKnowledgeCommandsV1
{
    public const string ApprovePackage = "ai-knowledge.package.approve.v1";
    public const string WithdrawApproval = "ai-knowledge.approval.withdraw.v1";
}

public static class AiKnowledgeProblemCodesV1
{
    public const string ValidationFailed = "validation_failed";
    public const string CommandMetadataInvalid = "ai_knowledge_command_metadata_invalid";
    public const string PermissionUnregistered = "permission_unregistered";
    public const string PackageUnavailable = "ai_knowledge_package_unavailable";
    public const string PackageChanged = "ai_knowledge_package_changed";
    public const string AlreadyApproved = "ai_knowledge_already_approved";
    public const string ApprovalNotActive = "ai_knowledge_approval_not_active";
    public const string VersionConflict = "version_conflict";
    public const string SameKeyPayloadMismatch = "same_key_payload_mismatch";
    public const string ConcurrentChange = "concurrent_change";
}

public sealed class AiKnowledgeException(int status, string code, bool isReplay = false, string? originalCorrelationId = null)
    : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}

public sealed record AiKnowledgeCommandMetadataV1(string IdempotencyKey, string CorrelationId, string? CausationId);

// ---- Ports -------------------------------------------------------------------------------------------------------------

/// <summary>What the host has packaged for a kind: the SHA-256 of the packaged bytes and a few facts for the approver.</summary>
public sealed record AiKnowledgePackageInfoV1(
    AiKnowledgeSourceKindV1 Kind,
    string PackageSha256,
    string SourceSetSha256,
    int ItemCount,
    int ExcludedOrConflictCount,
    int MaskedItemCount);

/// <summary>
/// Host port: the knowledge packages that are in this build's publish output. Implemented by the API (the package files
/// are API content); null for a kind means "not packaged or unreadable" (fail-closed).
/// </summary>
public interface IAiKnowledgePackageCatalogV1
{
    AiKnowledgePackageInfoV1? Find(AiKnowledgeSourceKindV1 kind);
}

public enum AiKnowledgeApprovalStateV1
{
    Approved = 1,
    NotApproved = 2,
    Unavailable = 3,
}

/// <summary>
/// Read port for the AI reading tools: is this exact package version approved for this company now? No authority (the
/// tool runtime has already fenced the caller); answers <see cref="AiKnowledgeApprovalStateV1.Unavailable"/> instead of
/// guessing when the store cannot answer.
/// </summary>
public interface IAiKnowledgeApprovalReaderV1
{
    Task<AiKnowledgeApprovalStateV1> GetStateAsync(Guid companyId, AiKnowledgeSourceKindV1 kind, string packageSha256,
        CancellationToken cancellationToken = default);
}

// ---- Service contract --------------------------------------------------------------------------------------------------

public sealed record ApproveAiKnowledgePackageRequestV1(string? SourceKind, string? PackageSha256);

public sealed record WithdrawAiKnowledgeApprovalRequestV1(string? ExpectedVersion, string? Reason);

public sealed record AiKnowledgeApprovalViewV1(
    Guid Id,
    Guid CompanyId,
    string SourceKind,
    string PackageSha256,
    string Status,
    string Version,
    Guid ApprovedByUserId,
    DateTimeOffset ApprovedAtUtc,
    Guid? WithdrawnByUserId,
    DateTimeOffset? WithdrawnAtUtc,
    string? WithdrawReason);

public sealed record AiKnowledgeApprovalResponseV1(AiKnowledgeApprovalViewV1 Approval, bool IsReplay, string OriginalCorrelationId);

public sealed record AiKnowledgePackageStatusV1(
    string SourceKind,
    bool Packaged,
    string? PackageSha256,
    string? SourceSetSha256,
    int? ItemCount,
    int? ExcludedOrConflictCount,
    int? MaskedItemCount,
    AiKnowledgeApprovalViewV1? ActiveApproval);

public sealed record AiKnowledgePackageStatusPageV1(Guid CompanyId, IReadOnlyList<AiKnowledgePackageStatusV1> Packages);

public interface IAiKnowledgeApprovalServiceV1
{
    Task<AiKnowledgePackageStatusPageV1> ListPackagesAsync(CancellationToken cancellationToken = default);

    Task<AiKnowledgeApprovalResponseV1> ApproveAsync(ApproveAiKnowledgePackageRequestV1 request, AiKnowledgeCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);

    Task<AiKnowledgeApprovalResponseV1> WithdrawAsync(Guid approvalId, WithdrawAiKnowledgeApprovalRequestV1 request,
        AiKnowledgeCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

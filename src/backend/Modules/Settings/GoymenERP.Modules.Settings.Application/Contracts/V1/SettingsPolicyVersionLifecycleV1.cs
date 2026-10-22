using System.Collections.Immutable;

namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Approved generic action identities. Declarations do not register permissions, classify a
/// policy family as critical or grant an actor access.
/// </summary>
public static class SettingsPolicyActionsV1
{
    public const string Prepare = "settings.policies.prepare";
    public const string Approve = "settings.policies.approve";
    public const string Publish = "settings.policies.publish";
    public const string PublishCritical = "settings.policies.publish-critical";
}

public enum SettingsPolicyVersionStatusV1
{
    Draft = 1,
    Approved = 2,
    Published = 3,
}

/// <summary>
/// Server-derived command metadata. The business date is supplied explicitly by the
/// authoritative caller and represents its Europe/Istanbul calendar date.
/// </summary>
public sealed record SettingsPolicyCommandContextV1(
    Guid CompanyId,
    Guid PolicyId,
    Guid PolicyVersionId,
    string ActorId,
    long ExpectedStateVersion,
    string IdempotencyKey,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    DateOnly BusinessDateEuropeIstanbul);

/// <summary>
/// One opaque, consumer-validated exact-scope or value field. The generic kernel stores the
/// supplied representation without assigning units, defaults or family semantics.
/// </summary>
public sealed record SettingsPolicySnapshotEntryV1(string Key, string Value);

public sealed record SettingsPolicySnapshotInputV1(
    string FamilyId,
    string SchemaId,
    string ScopeIdentity,
    IReadOnlyCollection<SettingsPolicySnapshotEntryV1> ExactScope,
    Guid ValueSnapshotId,
    IReadOnlyCollection<SettingsPolicySnapshotEntryV1> Values,
    DateOnly EffectiveFrom);

public sealed record SettingsPolicyValueRevisionV1(
    Guid ValueSnapshotId,
    IReadOnlyCollection<SettingsPolicySnapshotEntryV1> Values,
    DateOnly EffectiveFrom);

public sealed record PrepareSettingsPolicyVersionV1(
    SettingsPolicySnapshotInputV1 Snapshot,
    SettingsPolicyCommandContextV1 Context);

/// <summary>
/// Draft revision uses the generic prepare authority declaration. Runtime authorization and
/// persistence belong to a later command adapter.
/// </summary>
public sealed record ReviseSettingsPolicyVersionV1(
    SettingsPolicyValueRevisionV1 Revision,
    SettingsPolicyCommandContextV1 Context);

public sealed record ApproveSettingsPolicyVersionV1(SettingsPolicyCommandContextV1 Context);

public sealed record PublishSettingsPolicyVersionV1(SettingsPolicyCommandContextV1 Context);

public sealed record PrepareNextSettingsPolicyVersionV1(
    Guid PolicyVersionId,
    SettingsPolicyValueRevisionV1 Revision,
    SettingsPolicyCommandContextV1 Context);

/// <summary>
/// Immutable metadata and exact value snapshot for one policy business version. StateVersion is
/// the optimistic command version and remains separate from PolicyVersion.
/// </summary>
public sealed class SettingsPolicyVersionLifecycleV1
{
    internal SettingsPolicyVersionLifecycleV1(
        Guid companyId,
        Guid policyId,
        Guid policyVersionId,
        string familyId,
        string schemaId,
        string scopeIdentity,
        ImmutableArray<SettingsPolicySnapshotEntryV1> exactScope,
        long policyVersion,
        long stateVersion,
        SettingsPolicyVersionStatusV1 status,
        Guid valueSnapshotId,
        ImmutableArray<SettingsPolicySnapshotEntryV1> values,
        DateOnly effectiveFrom,
        string preparedBy,
        DateTimeOffset preparedAtUtc,
        DateOnly preparedBusinessDate,
        string preparationReason,
        string? approvedBy,
        DateTimeOffset? approvedAtUtc,
        DateOnly? approvedBusinessDate,
        string? approvalReason,
        string? publishedBy,
        DateTimeOffset? publishedAtUtc,
        DateOnly? publishedBusinessDate,
        string? publicationReason)
    {
        CompanyId = companyId;
        PolicyId = policyId;
        PolicyVersionId = policyVersionId;
        FamilyId = familyId;
        SchemaId = schemaId;
        ScopeIdentity = scopeIdentity;
        ExactScope = exactScope;
        PolicyVersion = policyVersion;
        StateVersion = stateVersion;
        Status = status;
        ValueSnapshotId = valueSnapshotId;
        Values = values;
        EffectiveFrom = effectiveFrom;
        PreparedBy = preparedBy;
        PreparedAtUtc = preparedAtUtc;
        PreparedBusinessDate = preparedBusinessDate;
        PreparationReason = preparationReason;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = approvedAtUtc;
        ApprovedBusinessDate = approvedBusinessDate;
        ApprovalReason = approvalReason;
        PublishedBy = publishedBy;
        PublishedAtUtc = publishedAtUtc;
        PublishedBusinessDate = publishedBusinessDate;
        PublicationReason = publicationReason;
    }

    public Guid CompanyId { get; }
    public Guid PolicyId { get; }
    public Guid PolicyVersionId { get; }
    public string FamilyId { get; }
    public string SchemaId { get; }
    public string ScopeIdentity { get; }
    public ImmutableArray<SettingsPolicySnapshotEntryV1> ExactScope { get; }
    public long PolicyVersion { get; }
    public long StateVersion { get; }
    public SettingsPolicyVersionStatusV1 Status { get; }
    public Guid ValueSnapshotId { get; }
    public ImmutableArray<SettingsPolicySnapshotEntryV1> Values { get; }
    public DateOnly EffectiveFrom { get; }
    public string PreparedBy { get; }
    public DateTimeOffset PreparedAtUtc { get; }
    public DateOnly PreparedBusinessDate { get; }
    public string PreparationReason { get; }
    public string? ApprovedBy { get; }
    public DateTimeOffset? ApprovedAtUtc { get; }
    public DateOnly? ApprovedBusinessDate { get; }
    public string? ApprovalReason { get; }
    public string? PublishedBy { get; }
    public DateTimeOffset? PublishedAtUtc { get; }
    public DateOnly? PublishedBusinessDate { get; }
    public string? PublicationReason { get; }
}

public static class SettingsPolicyVersionLifecycleCodesV1
{
    public const string Invalid = "settings_policy_lifecycle_invalid";
    public const string VersionConflict = "settings_policy_lifecycle_version_conflict";
    public const string InvalidTransition = "settings_policy_lifecycle_transition_invalid";
    public const string MakerSeparationRequired = "settings_policy_lifecycle_maker_separation_required";
    public const string EffectiveDateInPast = "settings_policy_lifecycle_effective_date_in_past";
}

public sealed class SettingsPolicyVersionLifecycleException(
    string code,
    long? expectedStateVersion = null,
    long? currentStateVersion = null) : InvalidOperationException(code)
{
    public string Code { get; } = code;
    public long? ExpectedStateVersion { get; } = expectedStateVersion;
    public long? CurrentStateVersion { get; } = currentStateVersion;
}

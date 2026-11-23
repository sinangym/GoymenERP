using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.Ai.Commands;

/// <summary>An untrusted request to have the model propose one owner command. The hint is untrusted like model output.</summary>
public sealed record AiCommandProposalRequestV1(string? Question, string? ToolId, string? ToolVersion, string? ArgumentsJson);

public enum AiCommandStatusV1
{
    Disabled = 1,
    Unavailable = 2,
    Refused = 3,
    Invalid = 4,
    NotFound = 5,
    Previewed = 6,
    Approved = 7,
    /// <summary>YOL-D27: a different authorized person approved with password re-entry; the preparer may execute.</summary>
    SecondApproved = 12,
    Executed = 8,
    Rejected = 9,
    /// <summary>The approval no longer binds what would run (record/input changed, preview expired, authority moved).</summary>
    Invalidated = 10,
    /// <summary>The owner outcome is not known yet; a resend only looks the frozen request up, it never re-executes.</summary>
    Unknown = 11,
}

/// <summary>Closed, content-free refusal codes of the pipeline (ledger events and results carry only these).</summary>
public static class AiCommandReasonCodesV1
{
    public const string PipelineDisabled = "pipeline_disabled";
    public const string PipelineUnavailable = "pipeline_unavailable";
    public const string SessionInvalid = "session_invalid";
    public const string CompanyContextMismatch = "company_context_mismatch";
    public const string PermissionMissing = "permission_missing";
    public const string ToolNotAllowed = "tool_not_allowed";
    public const string ClosedCommand = "closed_command";
    public const string DataClassNotAllowed = "data_class_not_allowed";
    public const string InvalidProposal = "invalid_proposal";
    public const string OwnerPreviewRefused = "owner_preview_refused";
    public const string PreviewHashMismatch = "preview_hash_mismatch";
    public const string PreviewExpired = "preview_expired";
    public const string PreviewStale = "preview_stale";
    public const string AuthorityChanged = "authority_changed";
    public const string ApprovalRequired = "approval_required";
    public const string ApprovalMismatch = "approval_mismatch";
    public const string StepUpRequired = "step_up_required";
    public const string SamePerson = "same_person";
    public const string SecondApprovalRequired = "second_approval_required";
    public const string NotActor = "not_actor";
    public const string Terminal = "terminal";
    public const string Concurrent = "concurrent_change";
    public const string OwnerRefused = "owner_refused";
    public const string OwnerFailed = "owner_failed";
    public const string ProviderFailed = "provider_failed";
    public const string AuditUnavailable = "audit_unavailable";
}

/// <summary>What the user sees and approves: the complete canonical command, its difference and the digest that binds them.</summary>
public sealed record AiCommandPreviewViewV1(
    Guid PreviewId,
    string PreviewHash,
    string ToolId,
    string ToolVersion,
    string TargetKind,
    Guid TargetId,
    string TargetVersion,
    string CanonicalCommandJson,
    string DifferenceJson,
    DateTimeOffset ExpiresAtUtc,
    bool RequiresSecondApproval,
    bool RequiresStepUp);

public sealed record AiCommandPipelineResultV1(
    AiCommandStatusV1 Status,
    string? ReasonCode = null,
    AiCommandPreviewViewV1? Preview = null,
    Guid? ApprovalId = null,
    string? ResultVersion = null,
    bool IsReplay = false)
{
    public static AiCommandPipelineResultV1 Of(AiCommandStatusV1 status, string? reasonCode = null) => new(status, reasonCode);
}

public enum AiCommandStateV1
{
    Previewed = 1,
    Approved = 2,
    Executing = 3,
    Executed = 4,
    Refused = 5,
    Rejected = 6,
    Invalidated = 7,
    Unknown = 8,
    SecondApproved = 9,
}

/// <summary>
/// One AI-prepared command, owned by the pipeline ledger. <see cref="Context"/> is the server context frozen at preview
/// time; the owner request for execution and for result lookup is rebuilt from these stored values only, so a resend
/// reproduces exactly the same request identity and idempotency key (<c>ai:&lt;previewId&gt;</c>).
/// </summary>
public sealed record AiCommandRecordV1(
    Guid PreviewId,
    AiServerContextV1 Context,
    string ToolId,
    string ToolVersion,
    string CanonicalArgumentsJson,
    AiPreviewV1 Preview,
    string PreviewIdentityJson,
    string PreviewHash,
    DateTimeOffset ExpiresAtUtc,
    AiCommandStateV1 State,
    long Version,
    Guid? ApprovalId = null,
    string? ApprovedBy = null,
    DateTimeOffset? ApprovedAtUtc = null,
    string? ResultVersion = null,
    string? ReasonCode = null,
    Guid? SecondApprovalId = null,
    string? SecondApprovedBy = null,
    DateTimeOffset? SecondApprovedAtUtc = null)
{
    public Guid CompanyId => Context.CompanyId;

    public string ActorId => Context.ActorId;
}

/// <summary>Proposal, preview, approval, second approval, execution, rejection, invalidation and refusal are separate events.</summary>
public enum AiCommandEventKindV1
{
    Proposed = 1,
    Previewed = 2,
    Approved = 3,
    SecondApproved = 4,
    Executed = 5,
    Rejected = 6,
    Invalidated = 7,
    Refused = 8,
    OutcomeUnknown = 9,
    ExecutionStarted = 10,
}

/// <summary>
/// Content-free audit row: identities, digest and closed codes only. No question, argument, command, difference,
/// source title or provider output is recorded here.
/// </summary>
public sealed record AiCommandLedgerEventV1(
    Guid EventId,
    Guid CompanyId,
    string ActorId,
    AiCommandEventKindV1 Kind,
    Guid? PreviewId,
    string? ToolId,
    string? ToolVersion,
    string? PreviewHash,
    Guid? ApprovalId,
    string? ReasonCode,
    string? ResultVersion,
    string CorrelationId,
    string? CausationId,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// The pipeline's own durable ledger. Every read is company-scoped: a preview of another company is simply absent.
/// <see cref="TryReplaceAsync"/> is compare-and-set on <see cref="AiCommandRecordV1.Version"/> and appends its event in
/// the same atomic step; a false return means a concurrent change won and nothing was written.
/// </summary>
public interface IAiCommandLedgerV1
{
    bool IsAvailable { get; }

    Task<bool> TryAddAsync(AiCommandRecordV1 record, IReadOnlyList<AiCommandLedgerEventV1> events, CancellationToken cancellationToken = default);

    Task<AiCommandRecordV1?> GetAsync(Guid companyId, Guid previewId, CancellationToken cancellationToken = default);

    Task<bool> TryReplaceAsync(AiCommandRecordV1 next, long expectedVersion, AiCommandLedgerEventV1 auditEvent, CancellationToken cancellationToken = default);

    /// <summary>A refusal that changes no state. Returns false when it could not be recorded.</summary>
    Task<bool> AppendAsync(AiCommandLedgerEventV1 auditEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default ledger: no durable store is configured in this package, so the pipeline answers unavailable (fail-closed).
/// A durable SQL ledger is required before any command family is opened (C.2).
/// </summary>
public sealed class UnconfiguredAiCommandLedger : IAiCommandLedgerV1
{
    public bool IsAvailable => false;

    public Task<bool> TryAddAsync(AiCommandRecordV1 record, IReadOnlyList<AiCommandLedgerEventV1> events, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<AiCommandRecordV1?> GetAsync(Guid companyId, Guid previewId, CancellationToken cancellationToken = default) =>
        Task.FromResult<AiCommandRecordV1?>(null);

    public Task<bool> TryReplaceAsync(AiCommandRecordV1 next, long expectedVersion, AiCommandLedgerEventV1 auditEvent, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> AppendAsync(AiCommandLedgerEventV1 auditEvent, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

/// <summary>What a step-up verification is bound to: this actor, session, company and exact preview digest.</summary>
public sealed record AiStepUpChallengeV1(Guid CompanyId, string ActorId, string SessionId, Guid PreviewId, string PreviewHash);

/// <summary>
/// Decision 156 / YOL-D27: the second approver types their password again at the approval. The password travels only
/// as this call's argument (never in a record, event or log); a failure is a refusal and counts toward the login lockout.
/// </summary>
public interface IAiStepUpVerifierV1
{
    Task<bool> IsVerifiedAsync(AiStepUpChallengeV1 challenge, string? password, CancellationToken cancellationToken = default);
}

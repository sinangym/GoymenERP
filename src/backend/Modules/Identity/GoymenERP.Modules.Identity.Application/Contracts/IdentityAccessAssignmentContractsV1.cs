namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>
/// ID-3 role assignment, revocation and the Critical role request (spec section ID-3; KIM-D1, KIM-D5, KIM-D15,
/// KIM-D17, KIM-D19; BS-6). Every command runs in the server-resolved company, company-wide only, with one
/// Idempotency-Key, the expected version as decimal text where a row exists, correlation/causation, one audit event
/// per change and, for a grant or a revocation, one <see cref="IdentityPermissionChangedV1"/> outbox message per
/// permission of the role plus one <c>identity.permission.changed</c> security event. Nothing here grants a role to
/// anyone by itself: who holds <see cref="IdentityPermissionManifestV1.RolesManage"/> and
/// <see cref="IdentityPermissionManifestV1.AccessApprove"/> stays a human gate.
/// </summary>
public interface IIdentityAccessAssignmentService
{
    /// <summary>
    /// One-step assignment of a non-Critical role (201). A Critical role is refused with
    /// <see cref="IdentityAccessProblemCodesV1.SecondApprovalRequired"/>, except the active bootstrap founder's own
    /// direct assignment (KIM-D5 path a), which closes an <c>AccessGrantRequest</c> as <c>FounderDirect</c>.
    /// </summary>
    Task<IdentityAccessCommandResultV1> AssignAsync(
        AssignIdentityRoleRequestV1 request,
        IdentityAccessCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);

    /// <summary>One-step revocation of an active assignment, a Critical role too (KIM-D15); the last Admin stays (KIM-D17).</summary>
    Task<IdentityAccessCommandResultV1> RevokeAsync(
        Guid assignmentId,
        RevokeIdentityRoleAssignmentRequestV1 request,
        IdentityAccessCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a pending request for a Critical role (201); a non-Critical role is assigned directly instead.</summary>
    Task<IdentityAccessCommandResultV1> CreateGrantRequestAsync(
        CreateAccessGrantRequestRequestV1 request,
        IdentityAccessCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);

    /// <summary>The second person's approval (KIM-D1): creates the assignment with the approver recorded.</summary>
    Task<IdentityAccessCommandResultV1> ApproveAsync(
        Guid requestId,
        ApproveAccessGrantRequestV1 request,
        IdentityAccessCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);

    /// <summary>The second person's reject with a canonical reason code; nothing is assigned.</summary>
    Task<IdentityAccessCommandResultV1> RejectAsync(
        Guid requestId,
        RejectAccessGrantRequestV1 request,
        IdentityAccessCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);

    /// <summary>The requester's own withdrawal of a pending request; nothing is assigned.</summary>
    Task<IdentityAccessCommandResultV1> WithdrawAsync(
        Guid requestId,
        WithdrawAccessGrantRequestV1 request,
        IdentityAccessCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The pending requests of the context company, oldest first. Null when the caller holds neither
    /// <see cref="IdentityPermissionManifestV1.AccessApprove"/> nor <see cref="IdentityPermissionManifestV1.AssignmentsRead"/>
    /// company-wide.
    /// </summary>
    Task<IReadOnlyList<IdentityAccessGrantRequestView>?> GetPendingGrantRequestsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Direct assignment. A null start means "now"; an end needs a start and must be after it.</summary>
public sealed record AssignIdentityRoleRequestV1(
    Guid UserId,
    Guid RoleId,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    string? Reason);

/// <summary>A Critical role request; the same shape as the direct assignment.</summary>
public sealed record CreateAccessGrantRequestRequestV1(
    Guid UserId,
    Guid RoleId,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    string? Reason);

public sealed record RevokeIdentityRoleAssignmentRequestV1(string ExpectedVersion, string? Reason);

public sealed record ApproveAccessGrantRequestV1(string ExpectedVersion);

/// <summary>The reject reason is a canonical lower-case code, never free text.</summary>
public sealed record RejectAccessGrantRequestV1(string ExpectedVersion, string ReasonCode);

public sealed record WithdrawAccessGrantRequestV1(string ExpectedVersion);

/// <summary>The write envelope the endpoint builds from the server-resolved request context.</summary>
public sealed record IdentityAccessCommandMetadataV1(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string IdempotencyKey);

/// <summary>The terminal answer of a command; the same answer replays for the same Idempotency-Key.</summary>
public sealed record IdentityAccessCommandAnswerV1(
    int StatusCode,
    string? Code,
    string Title,
    string Detail,
    IdentityAccessCommandOutcomeV1? Result = null);

/// <summary>
/// The changed record: an assignment (<c>Id</c> = assignment) or a request (<c>Id</c> = request, with the
/// assignment its approval created). <c>Version</c> is decimal text.
/// </summary>
public sealed record IdentityAccessCommandOutcomeV1(Guid Id, string Version, string Status, Guid? RoleAssignmentId);

public sealed record IdentityAccessCommandResultV1(
    IdentityAccessCommandAnswerV1 Answer,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>One pending Critical role request, as the approval queue shows it.</summary>
public sealed record IdentityAccessGrantRequestView(
    Guid RequestId,
    IdentityAccessPerson RequestedBy,
    IdentityAccessPerson Target,
    Guid RoleId,
    string RoleCode,
    string RoleName,
    string Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    string? Reason,
    string Version);

/// <summary>The versioned command names (idempotency scope and refused-attempt command name).</summary>
public static class IdentityAccessCommandsV1
{
    public const string Assign = "identity.role-assignment.assign.v1";

    public const string Revoke = "identity.role-assignment.revoke.v1";

    public const string CreateGrantRequest = "identity.access-grant-request.create.v1";

    public const string Approve = "identity.access-grant-request.approve.v1";

    public const string Reject = "identity.access-grant-request.reject.v1";

    public const string Withdraw = "identity.access-grant-request.withdraw.v1";
}

/// <summary>The problem codes of the ID-3 commands (spec section ID-3 suggested codes, kept as written there).</summary>
public static class IdentityAccessProblemCodesV1
{
    public const string RequestInvalid = "identity.access.request_invalid";

    public const string IdempotencyKeyInvalid = "identity.access.idempotency_key_invalid";

    public const string IdempotencyConflict = "identity.access.idempotency_conflict";

    public const string VersionConflict = "identity.access.version_conflict";

    public const string SecondApprovalRequired = "identity.assignment.second_approval_required";

    public const string DuplicateActiveAssignment = "identity.assignment.duplicate_active";

    public const string AssignmentNotActive = "identity.assignment.not_active";

    public const string RoleNotAssignable = "identity.assignment.role_not_assignable";

    public const string UserNotAssignable = "identity.assignment.user_not_assignable";

    public const string LastAdminRequired = "identity.last_admin_required";

    public const string DuplicatePendingRequest = "identity.grant_request.duplicate_pending";

    public const string SelfApprovalRefused = "identity.grant_request.self_approval_refused";

    public const string RequestNotPending = "identity.grant_request.not_pending";

    public const string ValidityElapsed = "identity.grant_request.validity_elapsed";

    public const string RoleNotCritical = "identity.grant_request.role_not_critical";

    public const string NotRequester = "identity.grant_request.not_requester";
}

/// <summary>
/// <c>identity.permission.changed.v1</c> (outbox; consumer platform/Audit): one message per permission of the role
/// whose assignment was granted or revoked, in the same transaction as the change. <c>RequestId</c> names the
/// Critical role request an approval or a founder-direct assignment closed.
/// </summary>
public sealed record IdentityPermissionChangedV1(
    Guid CompanyId,
    Guid UserId,
    string Permission,
    string Change,
    Guid ActorUserId,
    Guid? RequestId,
    DateTimeOffset OccurredAt)
{
    public const string EventType = "identity.permission.changed.v1";

    public const int SchemaVersion = 1;

    public const string Granted = "Granted";

    public const string Revoked = "Revoked";
}

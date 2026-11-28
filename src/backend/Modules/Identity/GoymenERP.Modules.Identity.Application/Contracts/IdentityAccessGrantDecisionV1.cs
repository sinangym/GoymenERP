namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>Approval-purpose access only; it conveys neither role management nor a general Identity read grant.</summary>
public interface IIdentityAccessGrantDecisionV1
{
    Task<IdentityAccessGrantDecisionDetailV1?> ReadDecisionAsync(Guid requestId,
        IdentityAccessCommandMetadataV1 context, Func<CancellationToken, Task> consumerFence,
        CancellationToken cancellationToken = default);
    Task<IdentityAccessGrantDecisionReceiptV1?> ReadDecisionReceiptAsync(Guid requestId, bool approve,
        string expectedVersion, string? reasonCode, IdentityAccessCommandMetadataV1 context,
        Func<CancellationToken, Task> consumerFence, CancellationToken cancellationToken = default);
    Task<IdentityAccessCommandResultV1> DecidePurposeAsync(Guid requestId, bool approve, string expectedVersion,
        string? reasonCode, string expectedImpactDigest, IdentityAccessCommandMetadataV1 metadata,
        Func<CancellationToken, Task> consumerFence, CancellationToken cancellationToken = default);
}

public sealed record IdentityAccessGrantDecisionDetailV1(
    IdentityAccessGrantRequestView Request, bool RequiresSecondApproval, bool TargetActive, bool RoleActive,
    IReadOnlyList<string> CurrentPermissions, bool CurrentRoleIsCritical, string SourceFactsDigest,
    string CurrentImpactDigest, string PreviewDigest);

/// <summary>Original native answer and retained request facts. Historical applied impact stays unavailable without an owner Audit proof reader.</summary>
public sealed record IdentityAccessGrantDecisionReceiptV1(
    Guid CompanyId, Guid RequestId, Guid TargetUserId, Guid RoleId, Guid RequestedBy,
    DateTimeOffset RequestedAtUtc, string ExpectedVersion, string ResultVersion,
    string CommandName, string KeyHash, string PayloadHash, string ActorId,
    string OriginalCorrelationId, DateTimeOffset CompletedAtUtc, string SourceFactsDigest,
    IdentityAccessCommandAnswerV1 OriginalAnswer, string OriginalAnswerDigest)
{
    public string? AppliedImpactDigest { get; init; }
    public int? AppliedPermissionCount { get; init; }
}

using GoymenERP.Modules.Work.Domain;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Work.Infrastructure.Delegations;

public sealed class WorkDelegationOptionsV1
{
    public const string SectionName = "Work:Delegations";
    public bool Enabled { get; set; }
}

/// <summary>Null delegator means the authenticated actor; null start means the server's current time. No grant is created.</summary>
public sealed record CreateWorkDelegationV1(string Kind, Guid DelegateUserId, DateTimeOffset EndsAtUtc,
    string Reason, Guid? DelegatorUserId = null, DateTimeOffset? StartsAtUtc = null);
public sealed record RevokeWorkDelegationV1(string ExpectedVersion, string Reason);
public sealed record WorkDelegationViewV1(Guid Id, Guid CompanyId, string Kind, string ApproverPermission,
    Guid DelegatorUserId, Guid DelegateUserId, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc,
    string Version, Guid RecordedByUserId, DateTimeOffset RecordedAtUtc, string Reason,
    Guid? RevokedByUserId, DateTimeOffset? RevokedAtUtc, string? RevocationReason);
public sealed record WorkDelegationCommandResultV1(WorkDelegationViewV1 Delegation, bool Replayed, string OriginalCorrelationId);
public sealed record WorkDelegationTableV1(Guid CompanyId, IReadOnlyList<WorkDelegationViewV1> Items, int Page, bool HasMore);

public interface IWorkDelegationReaderV1
{
    Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 ownerItem,
        CancellationToken cancellationToken = default);
}

public interface IWorkDelegationServiceV1
{
    Task<WorkDelegationTableV1> ListAsync(bool all = false, int page = 0, CancellationToken cancellationToken = default);
    Task<WorkDelegationCommandResultV1> CreateAsync(CreateWorkDelegationV1 request, WorkCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);
    Task<WorkDelegationCommandResultV1> RevokeAsync(Guid id, RevokeWorkDelegationV1 request, WorkCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);
}

public sealed class WorkDelegationException(int status, string code, bool isReplay = false, string? originalCorrelationId = null)
    : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}

public static class WorkDelegationCodesV1
{
    public const string Disabled = "work_delegation_disabled";
    public const string Invalid = "work_delegation_invalid";
    public const string MissingPermission = "work_delegation_existing_permission_required";
    public const string ScopeMismatch = "work_delegation_scope_not_covered";
    public const string Chain = "work_delegation_chain_or_overlap";
    public const string Concurrent = "work_delegation_concurrent_change";
    public const string Version = "work_delegation_version_conflict";
    public const string KeyMismatch = "work_delegation_key_payload_mismatch";
    public const string Metadata = "work_delegation_metadata_invalid";
}

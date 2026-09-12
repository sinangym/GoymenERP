namespace GoymenERP.BuildingBlocks.Security;

// Explicit allowlist, not a tenant-filter exception or a client-selectable group ID.
public enum GroupSharedMasterKind
{
    Product = 1,
}

public enum GroupSharedMasterOperation
{
    Read = 1,
    DraftCreate = 2,
    DraftBasicEdit = 3,
}

// This is a trusted server service result, never an HTTP request contract.
// A granted result must come from the separately reviewed Identity authority provider.
// Its data shape cannot prove that a company assignment is global authority.
public sealed record GroupSharedMasterAuthorizationSnapshot(
    bool IsGranted,
    GroupSharedMasterKind Subject,
    GroupSharedMasterOperation Operation,
    Guid LocalActorId,
    Guid OriginCompanyId,
    string AuthorityRevision)
{
    public static GroupSharedMasterAuthorizationSnapshot Denied(
        GroupSharedMasterKind subject, GroupSharedMasterOperation operation) =>
        new(false, subject, operation, Guid.Empty, Guid.Empty, string.Empty);
}

public interface IGroupSharedMasterAuthorizationService
{
    // Resolve actor, origin and global eligibility from authoritative server state.
    // Call on every request, including before looking up a persisted command result.
    Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(
        GroupSharedMasterKind subject,
        GroupSharedMasterOperation operation,
        CancellationToken cancellationToken = default);
}

// No production registration or grant is added by this preparation contract.
// This adapter makes a missing reviewed global eligibility provider explicitly fail closed.
public sealed class UnconfiguredGroupSharedMasterAuthorizationService
    : IGroupSharedMasterAuthorizationService
{
    public Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(
        GroupSharedMasterKind subject, GroupSharedMasterOperation operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GroupSharedMasterAuthorizationSnapshot.Denied(subject, operation));
    }
}

public sealed record GroupSharedMasterCommandContext
{
    private GroupSharedMasterCommandContext(GroupSharedMasterAuthorizationSnapshot snapshot)
    {
        Subject = snapshot.Subject;
        Operation = snapshot.Operation;
        LocalActorId = snapshot.LocalActorId;
        OriginCompanyId = snapshot.OriginCompanyId;
        AuthorityRevision = snapshot.AuthorityRevision;
    }

    public GroupSharedMasterKind Subject { get; }
    public GroupSharedMasterOperation Operation { get; }
    public Guid LocalActorId { get; }
    // Verified request causation only. This is NOT an owner CompanyId on Product.
    public Guid OriginCompanyId { get; }
    public string AuthorityRevision { get; }

    public static GroupSharedMasterCommandContext FromServerDecision(
        GroupSharedMasterAuthorizationSnapshot snapshot,
        GroupSharedMasterOperation expectedOperation)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsGranted || snapshot.Subject != GroupSharedMasterKind.Product
            || expectedOperation is not (GroupSharedMasterOperation.DraftCreate
                or GroupSharedMasterOperation.DraftBasicEdit)
            || snapshot.Operation != expectedOperation
            || snapshot.LocalActorId == Guid.Empty || snapshot.OriginCompanyId == Guid.Empty
            || string.IsNullOrWhiteSpace(snapshot.AuthorityRevision)
            || snapshot.AuthorityRevision != snapshot.AuthorityRevision.Trim()
            || snapshot.AuthorityRevision.Any(char.IsControl))
            throw new UnauthorizedAccessException("Reviewed global master authority and a server origin are required.");

        return new(snapshot);
    }
}

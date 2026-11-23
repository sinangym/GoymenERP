using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Explicit release of the unused customer-goods hold only; never a physical movement.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReleaseInventorySubcontractAllocationV1(
    [property: JsonRequired] string ExpectedAllocationVersion,
    [property: JsonRequired] string Reason);

public sealed record InventorySubcontractAllocationReleaseCandidateV1(
    InventoryPhysicalAllocationViewV1 Allocation, string LabelCode, string LabelVersion,
    Guid CustomerPartyId, Guid CustomerAccountId, Guid ManufacturingOrderId, Guid OutputFactId);
public sealed record InventorySubcontractAllocationReleaseCandidatesV1(
    IReadOnlyList<InventorySubcontractAllocationReleaseCandidateV1> Items, string? NextCursor);
public sealed record InventorySubcontractAllocationReleaseViewV1(
    InventoryPhysicalAllocationViewV1 Allocation, string LabelVersion, string ReleasedKg, string PreviouslyConsumedKg,
    string ReleaseReason, DateTimeOffset ReleasedAtUtc);
public sealed record InventorySubcontractAllocationReleaseResultV1(
    InventorySubcontractAllocationReleaseViewV1 Release, bool IsReplay, string OriginalCorrelationId);

public static class InventorySubcontractAllocationReleaseV1
{
    public const string Command = "inventory.subcontract-allocation.release-unused.v1";
    public const string Event = "inventory.subcontract-allocation.unused-released.v1";
    public const string Unavailable = "inventory_subcontract_allocation_release_unavailable";
}

public interface IInventorySubcontractAllocationReleaseStoreV1
{
    Task<InventorySubcontractAllocationReleaseCandidatesV1> ListAsync(Guid? after,
        Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default);
    Task<InventorySubcontractAllocationReleaseResultV1> ReleaseAsync(Guid allocationId,
        ReleaseInventorySubcontractAllocationV1 request, InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default);
}

public interface IInventorySubcontractAllocationReleaseServiceV1
{
    Task<InventorySubcontractAllocationReleaseCandidatesV1> ListAsync(string? cursor, CancellationToken cancellationToken = default);
    Task<InventorySubcontractAllocationReleaseResultV1> ReleaseAsync(Guid allocationId,
        ReleaseInventorySubcontractAllocationV1 request, InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

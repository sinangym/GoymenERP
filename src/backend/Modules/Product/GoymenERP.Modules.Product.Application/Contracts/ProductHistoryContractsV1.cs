namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// U-01 Product 360 change history (#181 U-01/U-02 spec §5.4, §9 item 6; CASE-PRD-U01-016).
/// A read-only projection of the canonical Audit owner's Product events, newest first.
/// It carries no actor identity, client address, user agent, correlation or idempotency
/// data: only what changed, when, the version transition and the recorded reason.
/// </summary>
public sealed record ProductHistoryView(Guid ProductId, IReadOnlyList<ProductHistoryEntryView> Entries, bool Truncated);

/// <param name="Operation">Exactly <c>DraftCreate</c>, <c>DraftBasicEdit</c>, <c>Activate</c>, <c>Deactivate</c> or <c>Reactivate</c>.</param>
/// <param name="VersionBefore">Lossless decimal string; null only for <c>DraftCreate</c>.</param>
/// <param name="ChangedFields">
/// Stable keys from <see cref="ProductHistoryFields"/>, in that order. Empty for
/// <c>DraftCreate</c>; an edit that changed none of them has an empty list too.
/// </param>
public sealed record ProductHistoryEntryView(Guid EventId, string Operation, DateTimeOffset OccurredAtUtc,
    string? VersionBefore, string VersionAfter, string Reason, IReadOnlyList<string> ChangedFields);

/// <summary>One canonical audit event as the Audit owner stored it; supplied by the host adapter.</summary>
public sealed record ProductAuditRecordV1(Guid EventId, Guid ProductId, string Operation,
    DateTimeOffset OccurredAtUtc, long? VersionBefore, long VersionAfter, string Reason,
    string BeforeJson, string AfterJson);

/// <summary>
/// Read port over the Audit owner. Implementations must authorize the global Product read
/// before reading and re-verify it before releasing rows, return newest first, and never
/// synthesize an event. Product does not read audit tables itself.
/// </summary>
public interface IProductAuditHistorySourceV1
{
    Task<IReadOnlyList<ProductAuditRecordV1>> ReadNewestAsync(Guid productId, int limit,
        CancellationToken cancellationToken);
}

public static class ProductHistoryApiV1
{
    public const string PathSuffix = "/history";
    public const int PageSize = 50;
    public const string DraftCreate = "DraftCreate";
    public const string DraftBasicEdit = "DraftBasicEdit";
    public const string Activate = "Activate";
    public const string Deactivate = "Deactivate";
    public const string Reactivate = "Reactivate";
}

/// <summary>The only change keys a history entry may carry, in display order.</summary>
public static class ProductHistoryFields
{
    public static readonly IReadOnlyList<string> All =
    [
        "name", "classId", "familyId", "lifecycle",
        "roles.purchasable", "roles.sellable", "roles.productionInput", "roles.productionOutput",
        "references.templateId", "references.templateVersion", "references.attributes",
        "references.units.stock", "references.units.purchase", "references.units.sales",
        "references.units.price", "references.units.production", "references.units.package",
        "references.trackingPolicyId",
    ];
}

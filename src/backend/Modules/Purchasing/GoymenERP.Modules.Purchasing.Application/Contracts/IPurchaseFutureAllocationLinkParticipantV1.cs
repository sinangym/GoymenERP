using System.Data;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// F-017 §3.5 F17-4 (ALS-D83/ALS-D85): the release reason codes Purchasing sends to the Inventory owner. The values are the
/// Inventory release port's own codes (F-017 STOK §3.3); Purchasing chooses which one, never a quantity.
/// </summary>
public static class PurchaseFutureAllocationLinkReasonsV1
{
    /// <summary>ALS-D83: an authorized purchasing user drops one link; the purchase quantities do not change.</summary>
    public const string LinkDropped = "purchasing-link-dropped";

    /// <summary>ALS-D85: the order's remainder is cancelled together with the allocations the actor confirmed.</summary>
    public const string SourceCancelled = "source-cancelled";

    /// <summary>F-017 §3.6: the line's remainder is closed together with the allocations the actor confirmed.</summary>
    public const string LineClosed = "purchase-line-closed";
}

/// <summary>One active future allocation on a purchase general line, as Purchasing shows it (quantity in the purchase line unit).</summary>
public sealed record PurchaseFutureAllocationLinkV1(
    Guid AllocationId,
    long Version,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    Guid UnitId,
    string AllocatedQuantity,
    string RemainingQuantity,
    DateTimeOffset CreatedAtUtc);

/// <summary>An allocation the actor saw (and confirmed) at its version; ALS-D85: the set is compared exactly before any write.</summary>
public sealed record PurchaseFutureAllocationSeenV1(Guid AllocationId, long ExpectedAllocationVersion);

/// <summary>One release asked by Purchasing inside its own transaction; the whole remaining quantity is released.</summary>
public sealed record PurchaseFutureAllocationLinkReleaseV1(
    Guid PurchaseOrderId,
    Guid AllocationId,
    long ExpectedAllocationVersion,
    string ReasonCode,
    string ReasonText,
    PurchasingWriteEnvelope Envelope);

/// <summary>
/// F-017 §3.5 F17-4: the host's bridge to the Inventory release port (<c>IInventoryFutureAllocationReleasePortV1</c>). Purchasing
/// calls it only inside its own transaction, after it took every general line lock of the order (ascending); the owner re-takes the
/// purchase line lock re-entrantly, checks the expected version and writes allocation + ledger + audit + outbox in the caller's
/// transaction. Purchasing never writes the owner's tables. A failure is <see cref="PurchaseFutureAllocationLinkException"/>.
/// </summary>
public interface IPurchaseFutureAllocationLinkParticipantV1
{
    Task<IReadOnlyList<PurchaseFutureAllocationLinkV1>> ListActiveAsync(IDbTransaction transaction, Guid purchaseOrderId,
        IReadOnlyList<Guid> purchaseLineIds, CancellationToken cancellationToken = default);

    Task<PurchaseFutureAllocationLinkV1> ReleaseAsync(IDbTransaction transaction, PurchaseFutureAllocationLinkReleaseV1 request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A refusal on the F17-4 path. <see cref="Terminal"/> refusals are the key's recorded answer (a stale allocation version, an
/// allocation no longer releasable); every other one (busy line, the owner unavailable, the confirmed set changed) is retried.
/// </summary>
public sealed class PurchaseFutureAllocationLinkException(int status, string code, bool terminal, Exception? inner = null)
    : Exception($"{status} {code}", inner)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public bool Terminal { get; } = terminal;
}

using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// KLN-D22 reverse transfer contract. An accepted transfer is never cancelled; it is undone by a NEW transfer in the
/// opposite direction (from the original destination location and custodian back to the original source location and
/// custodian). The new transfer carries a mandatory reason and a mandatory link to the original transfer
/// (<c>OriginalTransferId</c> on the route) and to each accepted original line it brings back
/// (<see cref="ReverseInventoryTransferLineV1.OriginalLineId"/>). The reverse transfer is then dispatched and accepted
/// through the ordinary transfer commands, so stock moves only through those commands. Host routing and permission
/// registration are separate contributions.
/// </summary>
public static class InventoryTransferReverseApiV1
{
    public const string ReversePathTemplate = "/api/v1/inventory/transfers/{transferId}/reverse";

    public const string ReversalFactsPathTemplate = "/api/v1/inventory/transfers/{transferId}/reversal-facts";

    public const int MaximumReasonLength = 500;

    /// <summary>The canonical reason, or null when it is empty, too long or carries a control character.</summary>
    public static string? NormalizeReason(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) || normalized.Length > MaximumReasonLength
            || normalized.Any(char.IsControl)
            ? null
            : normalized;
    }
}

/// <summary>
/// One line of a reverse transfer. <see cref="OriginalLineId"/> names an accepted line of the original transfer; the
/// label is that line's label (no free label can be chosen). <see cref="ExpectedLabelVersion"/> and
/// <see cref="PlannedKg"/> follow the ordinary transfer plan line rules.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReverseInventoryTransferLineV1(
    [property: JsonRequired] Guid LineId,
    [property: JsonRequired] Guid OriginalLineId,
    [property: JsonRequired] string ExpectedLabelVersion,
    [property: JsonRequired] string PlannedKg);

/// <summary>
/// KLN-D22 request: plan a new transfer that brings accepted lines of the original transfer back to its source.
/// <see cref="ReverseTransferId"/> is the identity of the new transfer; <see cref="Reason"/> is mandatory.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReverseInventoryTransferV1(
    [property: JsonRequired] Guid ReverseTransferId,
    [property: JsonRequired] DateTimeOffset PlannedAtUtc,
    [property: JsonRequired] string Reason,
    [property: JsonRequired] IReadOnlyList<ReverseInventoryTransferLineV1> Lines);

public sealed record InventoryTransferReversalLineViewV1(Guid LineId, Guid OriginalLineId);

/// <summary>The immutable KLN-D22 link of one reverse transfer to its original transfer.</summary>
public sealed record InventoryTransferReversalLinkViewV1(
    Guid ReverseTransferId,
    Guid OriginalTransferId,
    string Reason,
    DateTimeOffset RecordedAtUtc,
    string RecordedBy,
    IReadOnlyList<InventoryTransferReversalLineViewV1> Lines);

/// <summary>A reverse transfer that undoes (part of) the read transfer, with its current transfer status.</summary>
public sealed record InventoryTransferReversedByViewV1(
    InventoryTransferReversalLinkViewV1 Reversal,
    string ReverseTransferStatus);

/// <summary>
/// The KLN-D22 facts of one transfer: <see cref="ReversalOf"/> is set when the transfer is itself a reverse transfer;
/// <see cref="ReversedBy"/> lists the reverse transfers opened against it, oldest first.
/// </summary>
public sealed record InventoryTransferReversalFactsViewV1(
    Guid TransferId,
    Guid CompanyId,
    InventoryTransferReversalLinkViewV1? ReversalOf,
    IReadOnlyList<InventoryTransferReversedByViewV1> ReversedBy);

/// <summary>The HTTP body of a KLN-D22 command answer: the new reverse transfer and its link.</summary>
public sealed record InventoryTransferReverseResponseV1(
    InventoryTransferViewV1 Transfer,
    InventoryTransferReversalLinkViewV1 Reversal);

public sealed record InventoryTransferReverseResultV1(
    InventoryTransferViewV1 Transfer,
    InventoryTransferReversalLinkViewV1 Reversal,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// The KLN-D22 refusals beyond <see cref="InventoryTransferCodesV1"/>. Every refusal here is a 409 except
/// <see cref="ReasonRequired"/> (422).
/// </summary>
public static class InventoryTransferReverseCodesV1
{
    public const string ReasonRequired = "inventory_transfer_reverse_reason_required";

    /// <summary>The original transfer has no accepted line yet (planned, in transit or dispatch-cancelled).</summary>
    public const string OriginalNotAccepted = "inventory_transfer_reverse_original_not_accepted";

    /// <summary>A named original line is not accepted (still in transit, written off or never dispatched).</summary>
    public const string LineNotAccepted = "inventory_transfer_reverse_line_not_accepted";

    /// <summary>A named original line is already being brought back by a dispatched, not cancelled reverse transfer.</summary>
    public const string LineAlreadyReversed = "inventory_transfer_reverse_line_already_reversed";

    /// <summary>A named original line carries a receipt excess still waiting for its KLN-D24 decision.</summary>
    public const string OveragePending = "inventory_transfer_reverse_overage_pending";
}

/// <summary>
/// Inventory's transactional KLN-D22 write boundary. The central command adapter supplies a freshly authorized
/// company/actor envelope and has already checked that the original source is an active destination.
/// </summary>
public interface IInventoryTransferReverseWriterV1
{
    Task<InventoryTransferReverseResultV1?> ReplayReverseAsync(
        Guid originalTransferId,
        ReverseInventoryTransferV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferReverseResultV1> ReverseAsync(
        Guid originalTransferId,
        ReverseInventoryTransferV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);
}

public interface IInventoryTransferReverseReaderV1
{
    Task<InventoryTransferReversalFactsViewV1?> ReadReversalFactsAsync(
        Guid transferId,
        CancellationToken cancellationToken = default);
}

public interface IInventoryTransferReverseCommandV1
{
    Task<InventoryTransferReverseResultV1> ReverseAsync(
        Guid originalTransferId,
        ReverseInventoryTransferV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);
}

public interface IInventoryTransferReverseQueryV1
{
    Task<InventoryTransferReversalFactsViewV1> GetReversalFactsAsync(
        Guid transferId,
        CancellationToken cancellationToken = default);
}

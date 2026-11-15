using System.Text.Json.Serialization;
using GoymenERP.Modules.Inventory.Domain.Transfers;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// B-22b dispatch-cancel request (KLN-D20). The caller names the transfer version it saw, the business moment of the
/// cancellation and a reason; the reverse movement, the window decision (KLN-D25/KLN-D27) and the owner confirmations
/// are server-side. The reason is trimmed, 1-500 characters, and carries no control characters.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelInventoryTransferDispatchV1(
    [property: JsonRequired] string ExpectedTransferVersion,
    [property: JsonRequired] DateTimeOffset CancelledAtUtc,
    [property: JsonRequired] string Reason)
{
    public const int MaximumReasonLength = 500;

    /// <summary>The canonical reason, or null when it is empty, too long or carries a control character.</summary>
    public static string? NormalizeReason(string? reason)
    {
        var normalized = reason?.Trim();
        return string.IsNullOrEmpty(normalized) || normalized.Length > MaximumReasonLength
            || normalized.Any(char.IsControl)
            ? null
            : normalized;
    }
}

/// <summary>
/// What Inventory stored for one transfer that the cancellation window needs. The closure, once persisted, is final
/// (KLN-D25 (7)(g), KLN-D27 (5)): a later reading never reopens it.
/// </summary>
public sealed record InventoryTransferDispatchCancelContextV1(
    Guid TransferId,
    Guid CompanyId,
    long Version,
    InventoryTransferStatus Status,
    Guid SourceLocationId,
    DateTimeOffset? DispatchedAtUtc,
    InventoryTransferSourceScaleSetting? StoredScaleSettingAtDispatch,
    InventoryTransferCancelClosureBasis? ClosureBasis,
    DateTimeOffset? ClosedAtUtc);

/// <summary>
/// The source warehouse "kantardan geçer" setting as its owner reports it (KLN-D25 (7)(a), LOJ-D11). An undefined
/// setting is reported as <see cref="Off"/> (the default); <see cref="Unreadable"/> is a read error.
/// </summary>
public enum InventoryTransferScaleSettingV1
{
    Off = 1,
    On = 2,
    Unreadable = 3,
}

/// <summary>
/// Settings-owned read of the per-warehouse "bu depodan çıkan transferler kantardan geçer" setting. Inventory never
/// stores or chooses the value; it only reads it at dispatch (stored on the dispatch) and at cancellation. The owner
/// keeps the setting off until the Logistics exit read port is live (KLN-D25 (7)(a)). A missing registration or any
/// failure is read as <see cref="InventoryTransferScaleSettingV1.Unreadable"/> by the caller. Registration and the real
/// producer are Settings integration work.
/// </summary>
public interface IInventoryTransferSourceScaleSettingPortV1
{
    Task<InventoryTransferScaleSettingV1> ReadAsync(
        Guid companyId,
        Guid sourceLocationId,
        CancellationToken cancellationToken = default);
}

public enum InventoryTransferEDispatchLinkV1
{
    NotLinked = 1,
    Linked = 2,
    Unreadable = 3,
}

public enum InventoryTransferDepartureFactStatusV1
{
    NotOccurred = 1,
    Occurred = 2,
    Unreadable = 3,
}

/// <summary>One owner fact. <see cref="OccurredAtUtc"/> is set (UTC) only when the fact occurred.</summary>
public sealed record InventoryTransferDepartureFactV1(
    InventoryTransferDepartureFactStatusV1 Status,
    DateTimeOffset? OccurredAtUtc);

/// <summary>
/// The departure-side facts of one dispatch as their owners report them now: the e-İrsaliye link and its GİB send
/// (KLN-D25 (6)), and the Logistics exit weighing and vehicle exit (LOJ-D10, KLN-D25 (7)(b)).
/// </summary>
public sealed record InventoryTransferDispatchCancelFactsV1(
    InventoryTransferEDispatchLinkV1 EDispatchLink,
    DateTimeOffset? EDispatchLinkedAtUtc,
    InventoryTransferDepartureFactV1 EDispatchSentToGib,
    InventoryTransferDepartureFactV1 ExitWeighing,
    InventoryTransferDepartureFactV1 VehicleExit);

/// <summary>The exact dispatch a cancel gate read or confirmation is about.</summary>
public sealed class InventoryTransferDispatchCancelGateQueryV1
{
    public InventoryTransferDispatchCancelGateQueryV1(
        Guid companyId,
        Guid transferId,
        Guid sourceLocationId,
        DateTimeOffset dispatchedAtUtc)
    {
        CompanyId = RequireId(companyId, nameof(companyId));
        TransferId = RequireId(transferId, nameof(transferId));
        SourceLocationId = RequireId(sourceLocationId, nameof(sourceLocationId));
        DispatchedAtUtc = dispatchedAtUtc.Offset == TimeSpan.Zero
            ? dispatchedAtUtc
            : throw new ArgumentException("The dispatch moment must be UTC.", nameof(dispatchedAtUtc));
    }

    public Guid CompanyId { get; }
    public Guid TransferId { get; }
    public Guid SourceLocationId { get; }
    public DateTimeOffset DispatchedAtUtc { get; }

    private static Guid RequireId(Guid value, string parameterName) =>
        value != Guid.Empty ? value : throw new ArgumentException("A cancel gate identity is required.", parameterName);
}

/// <summary>
/// The owners' versioned answer to a confirmation request. A required confirmation is valid only with a positive
/// version; the facts are the owners' reading at the confirmation moment and are re-evaluated by Inventory.
/// </summary>
public sealed record InventoryTransferDispatchCancelConfirmationV1(
    InventoryTransferDispatchCancelFactsV1 Facts,
    long? LogisticsNoExitConfirmationVersion,
    long? EDispatchCancelConfirmationVersion);

/// <summary>
/// The Logistics/e-İrsaliye side of a dispatch cancellation (KLN-D25 (6)(f), (7)(e); KLN-D27 (8)).
/// <see cref="ReadAsync"/> is a fresh read (no cache, no event-derived value). <see cref="ConfirmAsync"/> asks the owner
/// to confirm, for the requested parts only: Logistics that the dispatch has no exit weighing or vehicle exit and that
/// it will refuse any later one; e-İrsaliye that the draft was cancelled or that the send of a cancelled dispatch will
/// be refused. A part the owner cannot confirm comes back with a null version. Compensation when only one part could
/// be confirmed is the owners' lane (KLN-D27 (8), Seçilmeyenler). Inventory never writes Logistics or e-İrsaliye
/// tables. Registration and the real producer are Logistics integration work; without a registration the cancel
/// command answers 503.
/// </summary>
public interface IInventoryTransferDispatchCancelGatePortV1
{
    Task<InventoryTransferDispatchCancelFactsV1> ReadAsync(
        InventoryTransferDispatchCancelGateQueryV1 query,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferDispatchCancelConfirmationV1> ConfirmAsync(
        InventoryTransferDispatchCancelGateQueryV1 query,
        bool requireLogisticsNoExit,
        bool requireEDispatchCancel,
        CancellationToken cancellationToken = default);
}

/// <summary>Maps the cross-module V1 readings onto the pure domain window rule; unknown values fail closed.</summary>
public static class InventoryTransferDispatchCancelMapping
{
    public static InventoryTransferSourceScaleSetting ToDomain(InventoryTransferScaleSettingV1 value) => value switch
    {
        InventoryTransferScaleSettingV1.Off => InventoryTransferSourceScaleSetting.Off,
        InventoryTransferScaleSettingV1.On => InventoryTransferSourceScaleSetting.On,
        _ => InventoryTransferSourceScaleSetting.Unreadable,
    };

    public static InventoryTransferDepartureFacts ToDomain(InventoryTransferDispatchCancelFactsV1? facts)
    {
        if (facts is null)
        {
            return new(InventoryTransferEDispatchLink.Unreadable, null, InventoryTransferDepartureFact.Unreadable,
                InventoryTransferDepartureFact.Unreadable, InventoryTransferDepartureFact.Unreadable);
        }

        var link = facts.EDispatchLink switch
        {
            InventoryTransferEDispatchLinkV1.NotLinked => InventoryTransferEDispatchLink.NotLinked,
            InventoryTransferEDispatchLinkV1.Linked => InventoryTransferEDispatchLink.Linked,
            _ => InventoryTransferEDispatchLink.Unreadable,
        };
        return new(link, facts.EDispatchLinkedAtUtc, ToDomain(facts.EDispatchSentToGib), ToDomain(facts.ExitWeighing),
            ToDomain(facts.VehicleExit));
    }

    private static InventoryTransferDepartureFact ToDomain(InventoryTransferDepartureFactV1? fact) => fact switch
    {
        { Status: InventoryTransferDepartureFactStatusV1.NotOccurred } =>
            new(InventoryTransferDepartureFactStatus.NotOccurred, fact.OccurredAtUtc),
        { Status: InventoryTransferDepartureFactStatusV1.Occurred } =>
            new(InventoryTransferDepartureFactStatus.Occurred, fact.OccurredAtUtc),
        _ => InventoryTransferDepartureFact.Unreadable,
    };
}

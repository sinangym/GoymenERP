using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Modules.Logistics.Domain.Returns;

public enum CustomerReturnStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

public static class CustomerReturnRuleCodesV1
{
    public const string NotPending = "logistics_customer_return_not_pending";
    public const string OwnDecision = "logistics_customer_return_own_decision";
    public const string QuantityExceedsDelivery = "logistics_customer_return_quantity_exceeds_delivery";
}

public sealed class CustomerReturnRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed class CustomerReturnVersionConflictException(long expectedVersion, long currentVersion)
    : InvalidOperationException($"Beklenen iade surumu {expectedVersion}, guncel surum {currentVersion}.")
{
    public long ExpectedVersion { get; } = expectedVersion;
    public long CurrentVersion { get; } = currentVersion;
}

/// <summary>
/// One immutable request to take back part of one recorded delivery. The request never rewrites the
/// shipment, delivery, dispatch note or invoice. Approval only records the identity of the separate
/// Inventory compensating movement produced by the owner participant.
/// </summary>
public sealed class CustomerReturn
{
    private CustomerReturn(Guid id, Guid companyId, Guid loadingInstructionId, Guid loadingInstructionLineId,
        Guid sourceDeliveryId, Guid sourceShipmentId, Guid sourceOrderId, Guid sourceOrderLineId, Guid unitId,
        Quantity quantity, Quantity sourceDeliveredQuantity, string reason, string requestedBy,
        DateTimeOffset occurredAtUtc, DateTimeOffset requestedAtUtc)
    {
        Id = Required(id, nameof(id));
        CompanyId = Required(companyId, nameof(companyId));
        LoadingInstructionId = Required(loadingInstructionId, nameof(loadingInstructionId));
        LoadingInstructionLineId = Required(loadingInstructionLineId, nameof(loadingInstructionLineId));
        SourceDeliveryId = Required(sourceDeliveryId, nameof(sourceDeliveryId));
        SourceShipmentId = Required(sourceShipmentId, nameof(sourceShipmentId));
        SourceOrderId = Required(sourceOrderId, nameof(sourceOrderId));
        SourceOrderLineId = Required(sourceOrderLineId, nameof(sourceOrderLineId));
        UnitId = Required(unitId, nameof(unitId));
        quantity.RequireSameUnit(sourceDeliveredQuantity);
        if (quantity.IsZero) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (quantity.IsGreaterThan(sourceDeliveredQuantity))
        {
            throw new CustomerReturnRuleException(CustomerReturnRuleCodesV1.QuantityExceedsDelivery,
                "Iade miktari kaynak teslimat miktarini asamaz.");
        }

        Quantity = quantity;
        SourceDeliveredQuantity = sourceDeliveredQuantity;
        Reason = Required(reason, nameof(reason));
        RequestedBy = Required(requestedBy, nameof(requestedBy));
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        RequestedAtUtc = requestedAtUtc.ToUniversalTime();
        Status = CustomerReturnStatus.Pending;
        Version = 1;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public Guid LoadingInstructionId { get; }
    public Guid LoadingInstructionLineId { get; }
    public Guid SourceDeliveryId { get; }
    public Guid SourceShipmentId { get; }
    public Guid SourceOrderId { get; }
    public Guid SourceOrderLineId { get; }
    public Guid UnitId { get; }
    public Quantity Quantity { get; }
    public Quantity SourceDeliveredQuantity { get; }
    public string Reason { get; }
    public string RequestedBy { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public DateTimeOffset RequestedAtUtc { get; }
    public CustomerReturnStatus Status { get; private set; }
    public long Version { get; private set; }
    public string? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionNote { get; private set; }
    public Guid? InventoryMovementId { get; private set; }
    public Guid? OriginalLabelId { get; private set; }
    public Guid? OriginalShipmentMovementId { get; private set; }

    public static CustomerReturn Request(Guid id, Guid companyId, Guid loadingInstructionId,
        Guid loadingInstructionLineId, Guid sourceDeliveryId, Guid sourceShipmentId, Guid sourceOrderId,
        Guid sourceOrderLineId, Guid unitId, Quantity quantity, Quantity sourceDeliveredQuantity,
        string reason, string requestedBy, DateTimeOffset occurredAtUtc, DateTimeOffset requestedAtUtc) =>
        new(id, companyId, loadingInstructionId, loadingInstructionLineId, sourceDeliveryId, sourceShipmentId,
            sourceOrderId, sourceOrderLineId, unitId, quantity, sourceDeliveredQuantity, reason, requestedBy,
            occurredAtUtc, requestedAtUtc);

    public void RequireDecision(long expectedVersion, string actorId)
    {
        RequireVersion(expectedVersion);
        if (Status != CustomerReturnStatus.Pending)
        {
            throw new CustomerReturnRuleException(CustomerReturnRuleCodesV1.NotPending,
                "Yalniz bekleyen iade istegi karara baglanabilir.");
        }

        var actor = Required(actorId, nameof(actorId));
        if (string.Equals(RequestedBy, actor, StringComparison.Ordinal))
        {
            throw new CustomerReturnRuleException(CustomerReturnRuleCodesV1.OwnDecision,
                "Fiziksel iadeyi hazirlayan kisi kendi iadesini onaylayamaz veya reddedemez.");
        }
    }

    public void Approve(long expectedVersion, string actorId, Guid inventoryMovementId, Guid originalLabelId,
        Guid originalShipmentMovementId, DateTimeOffset decidedAtUtc, string? decisionNote = null)
    {
        RequireDecision(expectedVersion, actorId);
        InventoryMovementId = Required(inventoryMovementId, nameof(inventoryMovementId));
        OriginalLabelId = Required(originalLabelId, nameof(originalLabelId));
        OriginalShipmentMovementId = Required(originalShipmentMovementId, nameof(originalShipmentMovementId));
        Status = CustomerReturnStatus.Approved;
        Decide(actorId, decidedAtUtc, decisionNote);
    }

    public void Reject(long expectedVersion, string actorId, DateTimeOffset decidedAtUtc, string? decisionNote = null)
    {
        RequireDecision(expectedVersion, actorId);
        Status = CustomerReturnStatus.Rejected;
        Decide(actorId, decidedAtUtc, decisionNote);
    }

    private void Decide(string actorId, DateTimeOffset decidedAtUtc, string? decisionNote)
    {
        DecidedBy = actorId.Trim();
        DecidedAtUtc = decidedAtUtc.ToUniversalTime();
        DecisionNote = string.IsNullOrWhiteSpace(decisionNote) ? null : decisionNote.Trim();
        Version++;
    }

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version) throw new CustomerReturnVersionConflictException(expectedVersion, Version);
    }

    private static Guid Required(Guid value, string name) => value == Guid.Empty
        ? throw new ArgumentException("Kimlik bos olamaz.", name)
        : value;

    private static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("Deger bos olamaz.", name)
        : value.Trim();
}

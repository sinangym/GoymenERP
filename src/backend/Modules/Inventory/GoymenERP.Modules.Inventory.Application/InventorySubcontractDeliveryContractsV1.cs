using System.Globalization;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// Price-free facts composed from the real Sales and Manufacturing owners, never an HTTP request body.
/// The host retains those owners' transaction/authority fences. Inventory verifies the actual label,
/// its customer ownership and company custody in the borrowed transaction. Account is not owner identity.
/// </summary>
public sealed record InventorySubcontractDeliverySourceV1(Guid CompanyId, Guid CustomerPartyId, Guid CustomerAccountId,
    Guid SalesOrderId, Guid SalesOrderLineId, long SalesOrderVersion, int SalesLineRevision,
    Guid ProductId, string ProductVersion, Guid ManufacturingOrderId, long ManufacturingOrderVersion,
    Guid ManufacturingPlanId, long ManufacturingPlanVersion, Guid OutputFactId, Guid LabelId,
    string OutputNetKg, string SourceVersion, long ManufacturingSalesLinkRevision, Guid SalesServiceProductId);

public sealed record InventorySubcontractReservationLineV1(Guid AllocationId, Guid LabelId,
    string ExpectedLabelVersion, string QuantityKg, string Reason, InventorySubcontractDeliverySourceV1 Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] InventorySubcontractPlanExcessProofV1? PlanExcess = null);
/// <summary>Trusted host's same-transaction Logistics approval snapshot, never a public request or extra authority.</summary>
public sealed record InventorySubcontractPlanExcessProofV1(Guid CompanyId, Guid ExceptionId, Guid InstructionId, Guid InstructionLineId,
    long ApprovedVersion, string ApprovedQuantityKg, string ReservedQuantityKg, string SourceVersion);
public sealed record InventorySubcontractAdditionalAllocationV1(Guid AllocationId, string ExpectedVersion, string QuantityKg);
public sealed record InventorySubcontractReservationRequestV1(Guid CompanyId, Guid ReservationId,
    IReadOnlyList<InventorySubcontractReservationLineV1> Lines);
public sealed record InventorySubcontractReservationResultV1(Guid CompanyId, Guid ReservationId,
    IReadOnlyList<InventoryPhysicalAllocationViewV1> Allocations, bool IsReplay, string OriginalCorrelationId);

/// <summary>
/// One subcontract delivery line. <c>PieceCount</c> (KLN-D119, AK-2b) is the number of pieces leaving a piece-tracking
/// customer label: required for such a label and refused on a kg-only label (400); a partial delivery leaves at least one
/// piece, a whole delivery takes every piece. Hosts before AK-4/AK-7 omit it and kg-only labels deliver as before.
/// </summary>
public sealed record InventorySubcontractShipmentLineV1(Guid ShipmentId, Guid LoadingInstructionLineId,
    Guid AllocationId, string ExpectedAllocationVersion, Guid LabelId, string ExpectedLabelVersion,
    Guid MovementId, string QuantityKg, InventorySubcontractDeliverySourceV1 Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<InventorySubcontractAdditionalAllocationV1>? AdditionalAllocations = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PieceCount = null);
public sealed record InventorySubcontractShipmentRequestV1(Guid CompanyId, Guid LoadingInstructionId,
    Guid CustomerPartyId, DateTimeOffset OccurredAtUtc, IReadOnlyList<InventorySubcontractShipmentLineV1> Lines);
public sealed record InventorySubcontractShipmentLineResultV1(Guid ShipmentId, Guid LoadingInstructionLineId,
    Guid AllocationId, string AllocationVersion, Guid LabelId, string LabelVersion, Guid MovementId, Guid OutboxMessageId,
    string QuantityKg, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PieceCount = null);
public sealed record InventorySubcontractShipmentResultV1(Guid CompanyId, Guid LoadingInstructionId,
    IReadOnlyList<InventorySubcontractShipmentLineResultV1> Lines, bool IsReplay, string OriginalCorrelationId);

/// <summary>One whole attempt on the host's existing connection/transaction. Never commits borrowed handles.</summary>
public interface IInventorySubcontractDeliveryParticipantV1 : IAsyncDisposable
{
    /// <summary>Price-free actual owner facts. Does not start a command or consume its one-attempt flag.</summary>
    Task<InventorySubcontractLabelReadV1?> ReadLabelAsync(Guid labelId, CancellationToken cancellationToken = default);
    Task<InventorySubcontractReservationResultV1> ReserveAsync(InventorySubcontractReservationRequestV1 request,
        InventoryCommandMetadata metadata, Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default);
    Task<InventorySubcontractShipmentResultV1> ShipAsync(InventorySubcontractShipmentRequestV1 request,
        InventoryCommandMetadata metadata, Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default);
}

public sealed record InventorySubcontractLabelReadV1(Guid CompanyId, Guid LabelId, string LabelCode, string LabelVersion,
    Guid ProductId, string ProductVersion, bool IsReleasable,
    IReadOnlyList<InventorySubcontractLabelPositionV1> Positions, IReadOnlyList<InventoryPhysicalAllocationViewV1> Allocations,
    string SourceVersion);
public sealed record InventorySubcontractLabelPositionV1(string OwnerKind, Guid OwnerId, string CustodianKind, Guid CustodianId,
    Guid LocationId, string Block, string PhysicalKg, string ReservedKg, string AvailableKg);

public static class InventorySubcontractDeliveryContractV1
{
    public const string SourceVersion = "inventory.subcontract-delivery-source.v1";
    public const string ReservationCommand = "inventory.subcontract-delivery.reserve.v1";
    public const string ShipmentCommand = "inventory.subcontract-delivery.ship.v1";
    public const string MovementSourceKind = "logistics.subcontract-delivery";
    public const string EventType = "inventory.subcontract-delivery.recorded.v1";
    public const string Invalid = "inventory_subcontract_delivery_invalid";
    public const string SourceMismatch = "inventory_subcontract_delivery_source_mismatch";
    public const string OwnerMismatch = "inventory_subcontract_delivery_owner_mismatch";
    public const string Unavailable = "inventory_subcontract_delivery_unavailable";

    public static Kilograms Quantity(string? value)
    {
        if (value is null || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var kg)
            || kg <= 0m || kg > 999999999999999.999m || value != kg.ToString("0.###", CultureInfo.InvariantCulture))
            throw Refused(400, Invalid);
        return Kilograms.Positive(kg);
    }

    public static long Version(string? value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1
            || value != version.ToString(CultureInfo.InvariantCulture)) throw Refused(400, Invalid);
        return version;
    }

    public static void Source(InventorySubcontractDeliverySourceV1? source, Guid companyId, Guid labelId)
    {
        if (source is null || source.CompanyId != companyId || companyId == Guid.Empty || source.LabelId != labelId
            || labelId == Guid.Empty || source.CustomerPartyId == Guid.Empty || source.CustomerAccountId == Guid.Empty
            || source.SalesOrderId == Guid.Empty || source.SalesOrderLineId == Guid.Empty || source.SalesOrderVersion < 1
            || source.SalesLineRevision < 0 || source.ProductId == Guid.Empty || !Text(source.ProductVersion, 128)
            || source.ManufacturingOrderId == Guid.Empty || source.ManufacturingOrderVersion < 1
            || source.ManufacturingPlanId == Guid.Empty || source.ManufacturingPlanVersion < 1 || source.OutputFactId == Guid.Empty
            || source.ManufacturingSalesLinkRevision < 1 || source.SalesServiceProductId == Guid.Empty
            || source.SourceVersion != SourceVersion) throw Refused(503, SourceMismatch);
        try { _ = Quantity(source.OutputNetKg); }
        catch (InventoryDefiniteShipmentException error) { throw Refused(503, SourceMismatch, error); }
    }

    public static bool Text(string? value, int limit) => value is { Length: > 0 } && value.Length <= limit
        && value == value.Trim() && !value.Any(char.IsControl);
    public static InventoryDefiniteShipmentException Refused(int status, string code, Exception? inner = null) =>
        InventoryDefiniteShipmentException.RefusedSubcontract(status, code, inner);
}

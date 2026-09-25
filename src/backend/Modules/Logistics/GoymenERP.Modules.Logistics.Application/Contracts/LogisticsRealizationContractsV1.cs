using System.Globalization;
using System.Text.Json;

namespace GoymenERP.Modules.Logistics.Application.Contracts;

// The browser supplies identities/version preconditions, never stock position, company, approval or product facts.
public sealed record RecordShipmentRequest(
    string ExpectedVersion, Guid ShipmentId, Guid AllocationId, string ExpectedAllocationVersion,
    string ExpectedLabelVersion, Guid MovementId, string Quantity, DateTimeOffset OccurredAtUtc);

public sealed record RecordDeliveryRequest(
    string ExpectedVersion, Guid DeliveryId, Guid SourceShipmentId, string Quantity, DateTimeOffset OccurredAtUtc);

public interface ILogisticsRealizationServiceV1
{
    Task<LoadingInstructionResponse> RecordShipmentAsync(Guid instructionId, Guid lineId, RecordShipmentRequest request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<LoadingInstructionResponse> RecordDeliveryAsync(Guid instructionId, Guid lineId, RecordDeliveryRequest request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default);
}

public static class LogisticsRealizationApiV1
{
    public const string ShipmentsPath = LogisticsApiV1.InstructionDetailPath + "/lines/{lineId}/shipments";
    public const string DeliveriesPath = LogisticsApiV1.InstructionDetailPath + "/lines/{lineId}/deliveries";
}

public static class LogisticsRealizationCodesV1
{
    public const string Unavailable = "logistics_realization_unavailable";
    public const string AllocationSourceMismatch = "logistics_allocation_source_mismatch";
    public const string ShipmentExceedsLoaded = "logistics_shipment_exceeds_loaded";
    public const string DeliveryExceedsShipment = "logistics_delivery_exceeds_shipment";
    public const string ShipmentSourceInvalid = "logistics_shipment_source_invalid";
}

/// <summary>
/// API-host-owned atomic boundary. Use the SAME scoped Logistics store/context and a borrowed Inventory
/// participant on the same SQL connection/transaction. Operation stages writes, beforeCommit performs the
/// last fresh authority check, then the host alone commits. Any exception (including cancellation, a stock
/// refusal or lost authority) rolls back BOTH modules and clears tracked entities/pending audit drafts before
/// returning control. No post-commit authority check. Never retry the callback implicitly.
/// ExecuteDeliveryAsync is Logistics-only (also used to durably record a terminal refusal); it must not read,
/// enlist or mutate Inventory. No production connection, DB provisioning or grant is supplied by this contract.
/// </summary>
public interface ILogisticsRealizationTransactionRunnerV1
{
    Task<T> ExecuteShipmentAsync<T>(Guid companyId,
        Func<ILogisticsShipmentStockV1, CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default);

    Task<T> ExecuteDeliveryAsync<T>(Guid companyId, Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Host adapter over existing Inventory owner reads and IInventoryDefiniteShipmentParticipantV1.
/// FindAllocationAsync must use the public authorized Sales-order-line allocation read, retain its independent
/// inventory.physical-allocation.write authority check, verify demand kind, and select exactly one allocation.
/// A full search window without a match is NOT a proven not-found: fail closed as unavailable. No raw table read.
/// StageAsync forwards the closed Inventory refusal status/code as LogisticsInstructionServiceException;
/// a staged result is not a commit. Delivery never invokes this port.
/// </summary>
public interface ILogisticsShipmentStockV1
{
    Task<LogisticsShipmentAllocationV1?> FindAllocationAsync(Guid salesOrderId, Guid salesOrderLineId, Guid allocationId,
        CancellationToken cancellationToken = default);

    Task<LogisticsShipmentStockResultV1> StageAsync(LogisticsShipmentStockRequestV1 request, LogisticsWriteEnvelope envelope,
        CancellationToken cancellationToken = default);
}

public sealed record LogisticsShipmentAllocationV1(
    Guid CompanyId, Guid AllocationId, Guid LabelId, Guid SalesOrderId, Guid SalesOrderLineId, Guid ProductId);

public sealed record LogisticsShipmentStockRequestV1(
    Guid CompanyId, Guid ShipmentId, Guid LoadingInstructionId, Guid LoadingInstructionLineId,
    Guid AllocationId, string ExpectedAllocationVersion, Guid LabelId, string ExpectedLabelVersion,
    Guid MovementId, string QuantityKg, DateTimeOffset OccurredAtUtc);

public sealed record LogisticsShipmentStockResultV1(
    Guid CompanyId, Guid ShipmentId, Guid AllocationId, Guid LabelId, Guid MovementId, Guid OutboxMessageId,
    bool IsReplay, string OriginalCorrelationId);

public static class LogisticsRealizationJson
{
    public static RecordShipmentRequest ReadShipment(string json)
    {
        var request = JsonSerializer.Deserialize<RecordShipmentRequest>(json, LogisticsJson.Options)
            ?? throw new JsonException("Request body is required.");
        Validate(request);
        return request;
    }

    public static RecordDeliveryRequest ReadDelivery(string json)
    {
        var request = JsonSerializer.Deserialize<RecordDeliveryRequest>(json, LogisticsJson.Options)
            ?? throw new JsonException("Request body is required.");
        Validate(request);
        return request;
    }

    public static void Validate(RecordShipmentRequest request)
    {
        RequireVersion(request.ExpectedVersion);
        RequireVersion(request.ExpectedAllocationVersion);
        RequireVersion(request.ExpectedLabelVersion);
        if (request.ShipmentId == Guid.Empty || request.AllocationId == Guid.Empty || request.MovementId == Guid.Empty)
        {
            throw new JsonException("Shipment identities are required.");
        }

        RequireQuantityAndTime(request.Quantity, request.OccurredAtUtc);
    }

    public static void Validate(RecordDeliveryRequest request)
    {
        RequireVersion(request.ExpectedVersion);
        if (request.DeliveryId == Guid.Empty || request.SourceShipmentId == Guid.Empty)
        {
            throw new JsonException("Delivery and source shipment identities are required.");
        }

        RequireQuantityAndTime(request.Quantity, request.OccurredAtUtc);
    }

    private static void RequireVersion(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 19 || text[0] is < '1' or > '9'
            || text.Any(character => character is < '0' or > '9')
            || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new JsonException("Version must be canonical positive integer text.");
        }
    }

    private static void RequireQuantityAndTime(string text, DateTimeOffset occurredAtUtc)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 128 || text[0] == '.' || text[^1] == '.'
            || text.Count(character => character == '.') > 1
            || text.Any(character => character != '.' && character is not (>= '0' and <= '9'))
            || !text.Any(character => character is >= '1' and <= '9')
            || occurredAtUtc == default || occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new JsonException("Exact positive decimal quantity and UTC event time are required.");
        }
    }
}

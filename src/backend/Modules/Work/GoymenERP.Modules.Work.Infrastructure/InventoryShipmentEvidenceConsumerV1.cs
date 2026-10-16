using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Modules.Work.Infrastructure;

/// <summary>
/// Fail-closed admission for the first Inventory event accepted by Work. This type only validates
/// the immutable source envelope and projects a read-only desired-state observation. Durable inbox,
/// aggregate high-water/gap handling and producer completion remain persistence-owned concerns.
/// </summary>
public static class InventoryShipmentEvidenceConsumerV1
{
    public const string ConsumerName = "work.shipment-evidence.v1";
    public const string EventType = "inventory.definite-shipment.recorded.v1";
    public const int SchemaVersion = 1;
    public const string SourceOperation = "inventory.definite-shipment.record.v1";
    public const string AggregateType = "Inventory.StockAllocation";
    public const string ScopePrefix = "inventory.stock-allocation:";

    private static readonly string[] PayloadPropertyNames =
    [
        "companyId",
        "shipmentId",
        "loadingInstructionId",
        "loadingInstructionLineId",
        "allocationId",
        "allocationVersion",
        "labelId",
        "labelVersion",
        "movementId",
        "quantityKg",
        "occurredAtUtc",
        "correlationId",
        "causationId"
    ];

    private static readonly HashSet<string> PayloadProperties =
        new(PayloadPropertyNames, StringComparer.Ordinal);

    public static InventoryShipmentEvidenceAdmissionV1 Admit(
        InventoryShipmentEvidenceEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateEnvelope(envelope);
        var payload = ParsePayload(envelope.PayloadJson);
        ValidatePayloadMatchesEnvelope(payload, envelope);

        var observation = new WorkDesiredStateObservationV2(
            envelope.CompanyId,
            EventType,
            string.Concat(ScopePrefix, envelope.AggregateId.ToString("D", CultureInfo.InvariantCulture)),
            envelope.Sequence,
            WorkSourceAvailabilityV2.Available,
            WorkDesiredStateV2.Satisfied,
            envelope.MessageId,
            envelope.PayloadSha256);

        // Exercise the existing Work contract now, before any persistence-owned processing begins.
        _ = WorkDesiredStateProtocolV2.Reconcile(null, observation);
        return new(envelope, payload, observation);
    }

    public static string ComputePayloadSha256(string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)))
            .ToLowerInvariant();
    }

    private static void ValidateEnvelope(InventoryShipmentEvidenceEnvelopeV1 envelope)
    {
        if (envelope.MessageId == Guid.Empty || envelope.CompanyId == Guid.Empty
            || envelope.AggregateId == Guid.Empty || envelope.AggregateVersion <= 0
            || envelope.Sequence <= 0 || envelope.AggregateVersion != envelope.Sequence)
        {
            throw ContractError("The source identity, aggregate version and sequence are required.");
        }

        if (!string.Equals(envelope.EventType, EventType, StringComparison.Ordinal)
            || envelope.SchemaVersion != SchemaVersion
            || !string.Equals(envelope.SourceOperation, SourceOperation, StringComparison.Ordinal)
            || !string.Equals(envelope.AggregateType, AggregateType, StringComparison.Ordinal))
        {
            throw ContractError("The event is outside the registered Inventory source contract.");
        }

        if (envelope.OccurredAtUtc.Offset != TimeSpan.Zero
            || !HasCanonicalText(envelope.CorrelationId)
            || envelope.CausationId is not null && !HasCanonicalText(envelope.CausationId)
            || string.IsNullOrWhiteSpace(envelope.PayloadJson)
            || !IsLowerHexSha256(envelope.PayloadSha256)
            || !string.Equals(ComputePayloadSha256(envelope.PayloadJson), envelope.PayloadSha256,
                StringComparison.Ordinal))
        {
            throw ContractError("The event time, correlation facts or payload integrity is invalid.");
        }
    }

    private static InventoryShipmentEvidencePayloadV1 ParsePayload(string payloadJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException exception)
        {
            throw ContractError("The event payload is not valid JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw ContractError("The event payload must be an object.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!PayloadProperties.Contains(property.Name) || !seen.Add(property.Name))
                {
                    throw ContractError("The event payload property set is not the registered schema.");
                }
            }

            if (seen.Count != PayloadPropertyNames.Length)
            {
                throw ContractError("The event payload property set is not the registered schema.");
            }

            var companyId = RequiredGuid(root, "companyId");
            var shipmentId = RequiredGuid(root, "shipmentId");
            var loadingInstructionId = RequiredGuid(root, "loadingInstructionId");
            var loadingInstructionLineId = RequiredGuid(root, "loadingInstructionLineId");
            var allocationId = RequiredGuid(root, "allocationId");
            var allocationVersion = RequiredPositiveInt64Text(root, "allocationVersion");
            var labelId = RequiredGuid(root, "labelId");
            var labelVersion = RequiredPositiveInt64Text(root, "labelVersion");
            var movementId = RequiredGuid(root, "movementId");
            var quantityKg = RequiredCanonicalText(root, "quantityKg");
            var occurredAtUtc = RequiredUtcDateTimeOffset(root, "occurredAtUtc");
            var correlationId = RequiredCanonicalText(root, "correlationId");
            var causationId = OptionalCanonicalText(root, "causationId");

            return new(companyId, shipmentId, loadingInstructionId, loadingInstructionLineId,
                allocationId, allocationVersion, labelId, labelVersion, movementId, quantityKg,
                occurredAtUtc, correlationId, causationId);
        }
    }

    private static void ValidatePayloadMatchesEnvelope(
        InventoryShipmentEvidencePayloadV1 payload,
        InventoryShipmentEvidenceEnvelopeV1 envelope)
    {
        if (payload.CompanyId != envelope.CompanyId
            || payload.AllocationId != envelope.AggregateId
            || payload.AllocationVersion != envelope.AggregateVersion
            || payload.OccurredAtUtc != envelope.OccurredAtUtc
            || !string.Equals(payload.CorrelationId, envelope.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(payload.CausationId, envelope.CausationId, StringComparison.Ordinal))
        {
            throw ContractError("The payload does not match its immutable source envelope.");
        }
    }

    private static Guid RequiredGuid(JsonElement root, string propertyName)
    {
        var element = root.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.String || !element.TryGetGuid(out var value)
            || value == Guid.Empty)
        {
            throw ContractError($"Payload property '{propertyName}' must be a non-empty GUID string.");
        }

        return value;
    }

    private static long RequiredPositiveInt64Text(JsonElement root, string propertyName)
    {
        var text = RequiredCanonicalText(root, propertyName);
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value <= 0 || !string.Equals(value.ToString(CultureInfo.InvariantCulture), text,
                StringComparison.Ordinal))
        {
            throw ContractError($"Payload property '{propertyName}' must be a canonical positive Int64 string.");
        }

        return value;
    }

    private static DateTimeOffset RequiredUtcDateTimeOffset(JsonElement root, string propertyName)
    {
        var element = root.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.String || !element.TryGetDateTimeOffset(out var value)
            || value.Offset != TimeSpan.Zero)
        {
            throw ContractError($"Payload property '{propertyName}' must be a UTC date-time string.");
        }

        return value;
    }

    private static string RequiredCanonicalText(JsonElement root, string propertyName)
    {
        var element = root.GetProperty(propertyName);
        var value = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        if (!HasCanonicalText(value))
        {
            throw ContractError($"Payload property '{propertyName}' must be canonical non-empty text.");
        }

        return value!;
    }

    private static string? OptionalCanonicalText(JsonElement root, string propertyName)
    {
        var element = root.GetProperty(propertyName);
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String || !HasCanonicalText(element.GetString()))
        {
            throw ContractError($"Payload property '{propertyName}' must be null or canonical non-empty text.");
        }

        return element.GetString();
    }

    private static bool HasCanonicalText(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static InventoryShipmentEvidenceContractException ContractError(
        string message,
        Exception? innerException = null) => new(message, innerException);
}

public sealed record InventoryShipmentEvidenceEnvelopeV1(
    Guid MessageId,
    Guid CompanyId,
    string EventType,
    int SchemaVersion,
    string SourceOperation,
    string AggregateType,
    Guid AggregateId,
    long AggregateVersion,
    long Sequence,
    string PayloadJson,
    string PayloadSha256,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId);

public sealed record InventoryShipmentEvidencePayloadV1(
    Guid CompanyId,
    Guid ShipmentId,
    Guid LoadingInstructionId,
    Guid LoadingInstructionLineId,
    Guid AllocationId,
    long AllocationVersion,
    Guid LabelId,
    long LabelVersion,
    Guid MovementId,
    string QuantityKg,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId);

public sealed record InventoryShipmentEvidenceAdmissionV1(
    InventoryShipmentEvidenceEnvelopeV1 Envelope,
    InventoryShipmentEvidencePayloadV1 Payload,
    WorkDesiredStateObservationV2 Observation);

public sealed class InventoryShipmentEvidenceContractException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

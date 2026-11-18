using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>In-app recipients only. Immutable producer sales targets are never enriched from later allocations.</summary>
internal sealed class PurchaseSupplyRevivedNotificationConsumerV1(IWorkNotificationDeliveryV1 delivery, string expectedSource)
    : IWorkInboxConsumerV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly string[] Fields = ["companyId", "purchaseOrderId", "purchaseOrderLineId", "orderVersion",
        "netOpenedQuantity", "receiptAdjustmentId", "batchId", "salesTargets"];
    private static readonly string[] TargetFields = ["salesOrderId", "salesOrderLineId"];

    public async Task HandleAsync(WorkInboxEventV1 message, CancellationToken cancellationToken)
    {
        PurchasingOrderLineRevivedPayloadV1 value;
        try
        {
            using var json = JsonDocument.Parse(message.PayloadJson);
            RequireFields(json.RootElement, Fields);
            var targets = RequiredJsonField(json.RootElement, "salesTargets");
            if (targets.ValueKind != JsonValueKind.Array) throw new JsonException();
            foreach (var target in targets.EnumerateArray()) RequireFields(target, TargetFields);
            value = JsonSerializer.Deserialize<PurchasingOrderLineRevivedPayloadV1>(message.PayloadJson, Json) ?? throw new JsonException();
            if (message.SourceName != expectedSource || message.EventType != PurchasingOrderLineRevivedEventV1.EventType
                || message.Version != 1 || message.EventId == Guid.Empty || message.CompanyId == Guid.Empty
                || value.CompanyId != message.CompanyId || value.PurchaseOrderId == Guid.Empty || value.PurchaseOrderLineId == Guid.Empty
                || value.OrderVersion < 1 || value.ReceiptAdjustmentId == Guid.Empty || value.BatchId == Guid.Empty
                || (value.ReceiptAdjustmentId is null) == (value.BatchId is null)
                || !PositiveQuantity(value.NetOpenedQuantity) || value.SalesTargets is null
                || value.SalesTargets.Any(target => target is null || target.SalesOrderId == Guid.Empty || target.SalesOrderLineId == Guid.Empty)
                || value.SalesTargets.Distinct().Count() != value.SalesTargets.Count
                || string.IsNullOrWhiteSpace(message.CorrelationId) || message.CorrelationId.Length > 128 || message.CausationId?.Length > 128)
                throw new JsonException();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        { throw new OutboxPermanentFailureException("notification_revival_contract_invalid", "The purchase revival event contract or target evidence is invalid."); }
        await delivery.DeliverAsync(new(message.CompanyId, message.SourceName, message.EventId, message.EventType, message.Version,
            message.PayloadJson, WorkNotificationKindV1.PurchaseSupplyRevived, value.PurchaseOrderLineId,
            value.OrderVersion.ToString(CultureInfo.InvariantCulture), "purchase-supply-revived", "Kabul düzeltmesi sonrası tedarik yeniden açıldı.",
            message.OccurredAtUtc, message.CorrelationId, message.CausationId,
            value.SalesTargets.Select(target => new WorkNotificationTargetV1(target.SalesOrderId, target.SalesOrderLineId)).ToArray(),
            new(value.PurchaseOrderId, value.PurchaseOrderLineId)), cancellationToken);
    }
    private static void RequireFields(JsonElement value, IReadOnlyCollection<string> fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException();
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != fields.Count || !names.ToHashSet(StringComparer.Ordinal).SetEquals(fields)) throw new JsonException();
    }
    private static bool PositiveQuantity(string? text) => text is { Length: > 0 and <= 32 }
        && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
        && number > 0 && number < 1_000_000_000_000_000m && decimal.Round(number, 6) == number
        && number.ToString("0.######", CultureInfo.InvariantCulture) == text;
    // Required JSON lookup retains missing-key failure and never uses runtime reflection.
    private static JsonElement RequiredJsonField(JsonElement value, string name) =>
        value.TryGetProperty(name, out var field) ? field : throw new KeyNotFoundException();

}

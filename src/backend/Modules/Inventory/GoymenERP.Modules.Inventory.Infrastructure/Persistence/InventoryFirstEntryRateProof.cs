using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Decode only owner-admitted immutable receipt FX; no price or current-rate fallback.</summary>
internal static class InventoryFirstEntryRateProof
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static bool IsValid(InventoryFirstEntryRateV1? value, Guid companyId, Guid acceptanceId, Guid? orderId = null) =>
        InventoryFirstEntryRateAdmissionV1.IsValid(value, companyId, acceptanceId, orderId);

    internal static InventoryFirstEntryRateV1? Read(string? json, Guid companyId, Guid acceptanceId)
    {
        try
        {
            var value = json is null ? null : JsonSerializer.Deserialize<InventoryFirstEntryRateV1>(json, Json);
            return IsValid(value, companyId, acceptanceId) ? value : null;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Application;

public enum InventoryFirstEntryRateStateV1 { Rated = 1, NotRequired = 2, Unavailable = 3 }

/// <summary>FNS-D103: an immutable receipt-time snapshot, independent from the supplier invoice's stock-cost FX.
/// Only an approved order rate, or an actual entry-day TCMB answer when no order rate is specified, can create Rated.
/// SourceVersion/EvidenceJson retain the actual owner answer. NotRequired never invents a same-currency FX of one.</summary>
public sealed record InventoryFirstEntryRateV1(Guid CompanyId, Guid AcceptanceId, Guid PurchaseOrderId,
    DateOnly EntryDay, InventoryFirstEntryRateStateV1 State, Guid? SourceCurrencyId, Guid? TargetCurrencyId,
    decimal? Rate, InventoryExactCostRatioV1? RateRatio, string SourceKind, string SourceVersion,
    string EvidenceJson, string? Reason, long? PurchaseOrderVersion = null);

public static class InventoryFirstEntryRateSourcesV1
{
    public const string ApprovedPurchaseOrder = "purchasing.approved-order-rate.v1";
    public const string EntryDayTcmb = "settings.tcmb-entry-day-rate.v1";
    public const string FunctionalCurrency = "inventory.first-entry-functional-currency.v1";
    public const string Unavailable = "inventory.first-entry-rate-unavailable.v1";
}

/// <summary>Strict receipt-owned envelope admission, shared by persistence and the exact public read contract.</summary>
public static class InventoryFirstEntryRateAdmissionV1
{
    /// <summary>Binds every immutable outer fact as well as the exact owner evidence; no rate/currency/version can drift
    /// while retaining a previously recorded source version. This hash grants no authority to an external caller.</summary>
    public static string SourceVersionOf(InventoryFirstEntryRateV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var envelope = JsonSerializer.Serialize(new { value.CompanyId, value.AcceptanceId, value.PurchaseOrderId,
            value.EntryDay, value.State, value.SourceCurrencyId, value.TargetCurrencyId, value.Rate, value.RateRatio,
            value.SourceKind, value.EvidenceJson, value.Reason, value.PurchaseOrderVersion }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return value.SourceKind + "/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope))).ToLowerInvariant();
    }

    public static bool IsValid(InventoryFirstEntryRateV1? value, Guid companyId, Guid acceptanceId, Guid? orderId = null)
    {
        if (value is null || value.CompanyId != companyId || value.AcceptanceId != acceptanceId || companyId == Guid.Empty
            || acceptanceId == Guid.Empty || value.PurchaseOrderId == Guid.Empty || orderId is { } order && order != value.PurchaseOrderId
            || value.SourceCurrencyId == Guid.Empty || value.TargetCurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira
            || string.IsNullOrWhiteSpace(value.SourceVersion) || value.SourceVersion.Length > 160)
            return false;
        try
        {
            using var evidence = JsonDocument.Parse(value.EvidenceJson);
            var root = evidence.RootElement;
            if (root.ValueKind != JsonValueKind.Object || RequiredJsonField(root, "companyId").GetGuid() != companyId
                || RequiredJsonField(root, "acceptanceId").GetGuid() != acceptanceId
                || RequiredJsonField(root, "purchaseOrderId").GetGuid() != value.PurchaseOrderId
                || RequiredJsonField(root, "entryDay").GetString() != value.EntryDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                || value.SourceVersion != SourceVersionOf(value)) return false;
            return value.State switch
            {
                InventoryFirstEntryRateStateV1.Rated => value.SourceCurrencyId is not null
                    && value.SourceCurrencyId != value.TargetCurrencyId && value.PurchaseOrderVersion is > 0
                    && value.Reason is null && (value.SourceKind is InventoryFirstEntryRateSourcesV1.ApprovedPurchaseOrder or InventoryFirstEntryRateSourcesV1.EntryDayTcmb)
                    && InventoryExactCostRatioV1.Resolve(value.Rate, value.RateRatio).Sign > 0,
                InventoryFirstEntryRateStateV1.NotRequired => value.SourceCurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira
                    && value.PurchaseOrderVersion is > 0 && value.Rate is null && value.RateRatio is null && value.Reason is null
                    && value.SourceKind == InventoryFirstEntryRateSourcesV1.FunctionalCurrency,
                InventoryFirstEntryRateStateV1.Unavailable => value.Rate is null && value.RateRatio is null
                    && !string.IsNullOrWhiteSpace(value.Reason) && value.SourceKind == InventoryFirstEntryRateSourcesV1.Unavailable,
                _ => false,
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException
            or FormatException or KeyNotFoundException or OverflowException) { return false; }
    }

    // Required JSON lookup retains missing-key failure and never uses runtime reflection.
    private static JsonElement RequiredJsonField(JsonElement value, string name) =>
        value.TryGetProperty(name, out var field) ? field : throw new KeyNotFoundException();

}

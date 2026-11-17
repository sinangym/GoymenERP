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

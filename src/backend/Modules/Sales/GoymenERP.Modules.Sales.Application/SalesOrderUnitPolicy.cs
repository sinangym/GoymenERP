using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-D443 / AK-4 (SAT-D434/SAT-D435): the units a sales line may be ordered in. Besides kilograms only the Settings standard piece
/// identity in the count dimension is approved; metre and every other non-kilogram dimension are refused as unsupported (golden
/// ADET_KG_FORMUL_GOLDEN_V1 K07/M05/M15 "bu trende gösterilmez"), never treated as a piece or mapped to a guessed unit. The same
/// gate as Purchasing (<c>PurchaseUnitVerifier</c>) and Logistics (<c>LoadingUnitVerifier</c>).
/// </summary>
internal static class SalesOrderUnitPolicy
{
    /// <summary>The Settings dimension code of the standard piece identity (ApprovedUnitProfileCatalog, AK-1).</summary>
    internal const string PieceDimensionCode = "count";

    internal static bool IsApproved(UnitProfileV1 profile) =>
        profile.IsKilogram
        || (profile.UnitId == SettingsUnitIdentitiesV1.Piece && string.Equals(profile.DimensionCode, PieceDimensionCode, StringComparison.Ordinal));
}

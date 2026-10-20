using System.Text.Json.Serialization;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// Serializable snapshot of the accepted stock-item quantity contract for one existing Product
/// kilogram receipt. Purchase quantity is commercial and is never an alternate physical quantity.
/// </summary>
public sealed record InventoryGoodsReceiptQuantityV2
{
    public InventoryGoodsReceiptQuantityV2(
        Guid companyId,
        InventoryStockItemKindV2 itemKind,
        Guid itemId,
        string sourceKind,
        Guid sourceId,
        string sourceVersion,
        decimal value,
        Guid unitId,
        Guid profileId,
        long profileVersion,
        StockQuantityDimensionV2 dimension,
        int scale)
    {
        if (itemKind != InventoryStockItemKindV2.Product
            || unitId != SettingsUnitIdentitiesV1.Kilogram
            || dimension != StockQuantityDimensionV2.Mass
            || scale is < 0 or > Kilograms.MaximumDecimals)
        {
            throw Unavailable();
        }

        InventoryStockItemQuantityV2 accepted;
        try
        {
            accepted = new InventoryStockItemQuantityV2(
                companyId,
                InventoryStockItemV2.Product(itemId),
                new InventoryStockQuantitySourceV2(sourceKind, sourceId, sourceVersion),
                StockQuantityV2.Positive(value, unitId, profileId, profileVersion, dimension, scale),
                alternateQuantity: null);
        }
        catch (Exception exception) when (exception is StockQuantityRuleExceptionV2
            or InventoryStockItemQuantityContractExceptionV2)
        {
            throw InventoryGoodsReceiptException.Refused(
                503, InventoryGoodsReceiptCodesV1.QuantityProfileUnavailable);
        }

        CompanyId = accepted.CompanyId;
        ItemKind = accepted.Item.Kind;
        ItemId = accepted.Item.Id;
        SourceKind = accepted.Source.Kind;
        SourceId = accepted.Source.Id;
        SourceVersion = accepted.Source.Version;
        Value = accepted.BaseQuantity.Value;
        UnitId = accepted.BaseQuantity.UnitId;
        ProfileId = accepted.BaseQuantity.ProfileId;
        ProfileVersion = accepted.BaseQuantity.ProfileVersion;
        Dimension = accepted.BaseQuantity.Dimension;
        Scale = accepted.BaseQuantity.Scale;
    }

    public Guid CompanyId { get; }
    public InventoryStockItemKindV2 ItemKind { get; }
    public Guid ItemId { get; }
    public string SourceKind { get; }
    public Guid SourceId { get; }
    public string SourceVersion { get; }
    public decimal Value { get; }
    public Guid UnitId { get; }
    public Guid ProfileId { get; }
    public long ProfileVersion { get; }
    public StockQuantityDimensionV2 Dimension { get; }
    public int Scale { get; }

    /// <summary>This Product receipt has no accepted alternate conversion or density fact.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public InventoryAlternateStockQuantityV2? AlternateQuantity => null;

    [JsonIgnore]
    public InventoryStockItemQuantityV2 Contract => new(
        CompanyId,
        InventoryStockItemV2.Product(ItemId),
        new InventoryStockQuantitySourceV2(SourceKind, SourceId, SourceVersion),
        StockQuantityV2.Positive(Value, UnitId, ProfileId, ProfileVersion, Dimension, Scale),
        AlternateQuantity);

    public static InventoryGoodsReceiptQuantityV2 Create(
        InventoryGoodsReceiptRequestV1 request,
        Kilograms quantity,
        InventoryKilogramProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (profile is null
            || profile.CompanyId != request.CompanyId
            || profile.UnitId != SettingsUnitIdentitiesV1.Kilogram
            || profile.ProfileId == Guid.Empty
            || profile.ProfileVersion <= 0
            || profile.Scale is < 0 or > Kilograms.MaximumDecimals)
        {
            throw InventoryGoodsReceiptException.Refused(
                503, InventoryGoodsReceiptCodesV1.QuantityProfileUnavailable);
        }

        if (request.CompanyId == Guid.Empty || request.ProductId == Guid.Empty
            || request.AcceptanceId == Guid.Empty)
        {
            throw InventoryGoodsReceiptException.Refused(400, InventoryGoodsReceiptCodesV1.InvalidRequest);
        }

        try
        {
            var accepted = new InventoryStockItemQuantityV2(
                request.CompanyId,
                InventoryStockItemV2.Product(request.ProductId),
                new InventoryStockQuantitySourceV2(
                    InventoryGoodsReceiptContractV1.SourceKind,
                    request.AcceptanceId,
                    request.PurchaseOrderVersionAfter),
                StockQuantityV2.Positive(
                    quantity.Value,
                    profile.UnitId,
                    profile.ProfileId,
                    profile.ProfileVersion,
                    StockQuantityDimensionV2.Mass,
                    profile.Scale),
                alternateQuantity: null);
            return new InventoryGoodsReceiptQuantityV2(
                accepted.CompanyId,
                accepted.Item.Kind,
                accepted.Item.Id,
                accepted.Source.Kind,
                accepted.Source.Id,
                accepted.Source.Version,
                accepted.BaseQuantity.Value,
                accepted.BaseQuantity.UnitId,
                accepted.BaseQuantity.ProfileId,
                accepted.BaseQuantity.ProfileVersion,
                accepted.BaseQuantity.Dimension,
                accepted.BaseQuantity.Scale);
        }
        catch (StockQuantityRuleExceptionV2 exception)
            when (exception.Code is StockQuantityRuleCodesV2.QuantityNotPositive
                or StockQuantityRuleCodesV2.ScaleExceeded)
        {
            throw InventoryGoodsReceiptException.Refused(400, InventoryGoodsReceiptCodesV1.InvalidQuantity);
        }
        catch (Exception exception) when (exception is StockQuantityRuleExceptionV2
            or InventoryStockItemQuantityContractExceptionV2)
        {
            throw InventoryGoodsReceiptException.Refused(
                503, InventoryGoodsReceiptCodesV1.QuantityProfileUnavailable);
        }
    }

    /// <summary>
    /// Binds a non-legacy stored snapshot back to the authoritative receipt request and outer result.
    /// The stored Settings profile identity/version stays immutable and is validated structurally;
    /// this method never reads Settings or replaces the committed tuple with current data.
    /// </summary>
    public static void BindStored(
        InventoryGoodsReceiptQuantityV2? snapshot,
        InventoryGoodsReceiptRequestV1 request,
        InventoryGoodsReceiptResultV1 result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        if (snapshot is null) return;

        try
        {
            _ = snapshot.Contract;
        }
        catch (Exception exception) when (exception is StockQuantityRuleExceptionV2
            or InventoryStockItemQuantityContractExceptionV2)
        {
            throw Unavailable();
        }

        if (request.CompanyId == Guid.Empty
            || request.ProductId == Guid.Empty
            || request.AcceptanceId == Guid.Empty
            || result.CompanyId != request.CompanyId
            || result.AcceptanceId != request.AcceptanceId
            || result.PurchaseOrderId != request.PurchaseOrderId
            || result.PurchaseOrderLineId != request.PurchaseOrderLineId
            || result.ProductId != request.ProductId
            || !string.Equals(result.ProductVersion, request.ExpectedProductVersion, StringComparison.Ordinal)
            || result.PurchaseUnitId != request.PurchaseUnitId
            || snapshot.CompanyId != request.CompanyId
            || snapshot.CompanyId != result.CompanyId
            || snapshot.ItemKind != InventoryStockItemKindV2.Product
            || snapshot.ItemId != request.ProductId
            || snapshot.ItemId != result.ProductId
            || !string.Equals(snapshot.SourceKind, InventoryGoodsReceiptContractV1.SourceKind,
                StringComparison.Ordinal)
            || snapshot.SourceId != request.AcceptanceId
            || snapshot.SourceId != result.AcceptanceId
            || !string.Equals(snapshot.SourceVersion, request.PurchaseOrderVersionAfter,
                StringComparison.Ordinal)
            || !string.Equals(snapshot.SourceVersion, result.PurchaseOrderVersion,
                StringComparison.Ordinal)
            || !string.Equals(result.QuantityKg, request.QuantityKg, StringComparison.Ordinal)
            || !string.Equals(snapshot.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                request.QuantityKg, StringComparison.Ordinal)
            || snapshot.UnitId != SettingsUnitIdentitiesV1.Kilogram
            || snapshot.ProfileId == Guid.Empty
            || snapshot.ProfileVersion <= 0
            || snapshot.Dimension != StockQuantityDimensionV2.Mass
            || snapshot.Scale is < 0 or > Kilograms.MaximumDecimals
            || snapshot.AlternateQuantity is not null)
        {
            throw Unavailable();
        }
    }

    private static InventoryGoodsReceiptException Unavailable() =>
        InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
}

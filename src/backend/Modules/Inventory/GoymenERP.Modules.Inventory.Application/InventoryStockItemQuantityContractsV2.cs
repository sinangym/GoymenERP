using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Modules.Inventory.Application;

public enum InventoryStockItemKindV2
{
    Product = 1,
    Consumable = 2,
}

/// <summary>
/// One physical stock subject. Construction is closed over Product and Consumable, so a Service can
/// neither be represented nor accidentally sent through the Inventory quantity contract.
/// </summary>
public sealed record InventoryStockItemV2
{
    private InventoryStockItemV2(InventoryStockItemKindV2 kind, Guid id)
    {
        Kind = kind;
        Id = id;
    }

    public InventoryStockItemKindV2 Kind { get; }
    public Guid Id { get; }

    public static InventoryStockItemV2 Product(Guid productId) =>
        Create(InventoryStockItemKindV2.Product, productId);

    public static InventoryStockItemV2 Consumable(Guid consumableId) =>
        Create(InventoryStockItemKindV2.Consumable, consumableId);

    private static InventoryStockItemV2 Create(InventoryStockItemKindV2 kind, Guid id)
    {
        if (id == Guid.Empty)
        {
            throw InventoryStockItemQuantityContractExceptionV2.Refused(
                InventoryStockItemQuantityCodesV2.InvalidItem,
                "Stok kalemi kimliği boş olamaz.");
        }

        return new(kind, id);
    }
}

/// <summary>An immutable, versioned source for a quantity fact or an alternate conversion.</summary>
public sealed record InventoryStockQuantitySourceV2
{
    private const int MaximumKindLength = 128;
    private const int MaximumVersionLength = 64;

    public InventoryStockQuantitySourceV2(string kind, Guid id, string version)
    {
        RequireToken(kind, MaximumKindLength);
        RequireToken(version, MaximumVersionLength);
        if (id == Guid.Empty)
        {
            throw InventoryStockItemQuantityContractExceptionV2.Refused(
                InventoryStockItemQuantityCodesV2.InvalidSource,
                "Miktar kaynağı kimliği boş olamaz.");
        }

        Kind = kind;
        Id = id;
        Version = version;
    }

    public string Kind { get; }
    public Guid Id { get; }
    public string Version { get; }

    private static void RequireToken(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw InventoryStockItemQuantityContractExceptionV2.Refused(
                InventoryStockItemQuantityCodesV2.InvalidSource,
                "Miktar kaynağı türü ve sürümü kanonik olmalıdır.");
        }
    }
}

/// <summary>
/// An optional quantity in another unit/dimension. Its value is present only with the exact versioned
/// source that produced it. When no accepted conversion or density exists, the whole value is null.
/// </summary>
public sealed record InventoryAlternateStockQuantityV2(
    StockQuantityV2 Quantity,
    InventoryStockQuantitySourceV2 ConversionSource);

/// <summary>
/// Company-scoped quantity facts for one Product or Consumable. This is a contract foundation only;
/// it does not select a catalogue entry, conversion rule, movement, ledger or persistence policy.
/// </summary>
public sealed record InventoryStockItemQuantityV2
{
    public InventoryStockItemQuantityV2(
        Guid companyId,
        InventoryStockItemV2 item,
        InventoryStockQuantitySourceV2 source,
        StockQuantityV2 baseQuantity,
        InventoryAlternateStockQuantityV2? alternateQuantity)
    {
        if (companyId == Guid.Empty)
        {
            throw InventoryStockItemQuantityContractExceptionV2.Refused(
                InventoryStockItemQuantityCodesV2.InvalidCompany,
                "Şirket kimliği boş olamaz.");
        }

        Item = item ?? throw InventoryStockItemQuantityContractExceptionV2.Refused(
            InventoryStockItemQuantityCodesV2.InvalidItem,
            "Stok kalemi açıkça verilmelidir.");
        Source = source ?? throw InventoryStockItemQuantityContractExceptionV2.Refused(
            InventoryStockItemQuantityCodesV2.InvalidSource,
            "Temel miktarın sürümlü kaynağı açıkça verilmelidir.");

        if (baseQuantity == default)
        {
            throw InventoryStockItemQuantityContractExceptionV2.Refused(
                InventoryStockItemQuantityCodesV2.InvalidQuantity,
                "Temel miktar açıkça verilmelidir.");
        }

        if (alternateQuantity is not null
            && (alternateQuantity.Quantity == default || alternateQuantity.ConversionSource is null))
        {
            throw InventoryStockItemQuantityContractExceptionV2.Refused(
                InventoryStockItemQuantityCodesV2.InvalidAlternateQuantity,
                "Alternatif miktar yalnız sürümlü dönüşüm kaynağıyla verilebilir.");
        }

        CompanyId = companyId;
        BaseQuantity = baseQuantity;
        AlternateQuantity = alternateQuantity;
    }

    public Guid CompanyId { get; }
    public InventoryStockItemV2 Item { get; }
    public InventoryStockQuantitySourceV2 Source { get; }
    public StockQuantityV2 BaseQuantity { get; }
    public InventoryAlternateStockQuantityV2? AlternateQuantity { get; }
}

public static class InventoryStockItemQuantityCodesV2
{
    public const string InvalidCompany = "inventory_stock_item_quantity_company_invalid";
    public const string InvalidItem = "inventory_stock_item_quantity_item_invalid";
    public const string InvalidSource = "inventory_stock_item_quantity_source_invalid";
    public const string InvalidQuantity = "inventory_stock_item_quantity_base_invalid";
    public const string InvalidAlternateQuantity = "inventory_stock_item_quantity_alternate_invalid";
}

public sealed class InventoryStockItemQuantityContractExceptionV2 : Exception
{
    private InventoryStockItemQuantityContractExceptionV2(string code, string message) : base(message) =>
        Code = code;

    public string Code { get; }

    public static InventoryStockItemQuantityContractExceptionV2 Refused(string code, string message) =>
        new(code, message);
}

using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryStockItemQuantityContractsV2Tests
{
    private static readonly Guid CompanyId = Guid.Parse("96000000-0000-4000-8000-000000000001");
    private static readonly Guid ProductId = Guid.Parse("96000000-0000-4000-8000-000000000002");
    private static readonly Guid ConsumableId = Guid.Parse("96000000-0000-4000-8000-000000000003");
    private static readonly Guid UnitId = Guid.Parse("96000000-0000-4000-8000-000000000004");
    private static readonly Guid ProfileId = Guid.Parse("96000000-0000-4000-8000-000000000005");
    private static readonly Guid SourceId = Guid.Parse("96000000-0000-4000-8000-000000000006");

    [Fact]
    public void Product_and_consumable_are_the_only_constructible_stock_items()
    {
        var product = InventoryStockItemV2.Product(ProductId);
        var consumable = InventoryStockItemV2.Consumable(ConsumableId);

        Assert.Equal((InventoryStockItemKindV2.Product, ProductId), (product.Kind, product.Id));
        Assert.Equal((InventoryStockItemKindV2.Consumable, ConsumableId), (consumable.Kind, consumable.Id));
        Assert.DoesNotContain(typeof(InventoryStockItemV2).GetConstructors(), constructor => constructor.IsPublic);
        Assert.Equal(new[] { InventoryStockItemKindV2.Product, InventoryStockItemKindV2.Consumable },
            Enum.GetValues<InventoryStockItemKindV2>());
    }

    [Fact]
    public void Empty_product_or_consumable_identity_is_rejected()
    {
        Assert.Equal(InventoryStockItemQuantityCodesV2.InvalidItem,
            Assert.Throws<InventoryStockItemQuantityContractExceptionV2>(
                () => InventoryStockItemV2.Product(Guid.Empty)).Code);
        Assert.Equal(InventoryStockItemQuantityCodesV2.InvalidItem,
            Assert.Throws<InventoryStockItemQuantityContractExceptionV2>(
                () => InventoryStockItemV2.Consumable(Guid.Empty)).Code);
    }

    [Theory]
    [InlineData(InventoryStockItemKindV2.Product)]
    [InlineData(InventoryStockItemKindV2.Consumable)]
    public void Contract_preserves_company_item_source_and_canonical_base_quantity(
        InventoryStockItemKindV2 kind)
    {
        var item = kind == InventoryStockItemKindV2.Product
            ? InventoryStockItemV2.Product(ProductId)
            : InventoryStockItemV2.Consumable(ConsumableId);
        var source = Source();
        var quantity = BaseQuantity();

        var contract = new InventoryStockItemQuantityV2(CompanyId, item, source, quantity, null);

        Assert.Equal(CompanyId, contract.CompanyId);
        Assert.Same(item, contract.Item);
        Assert.Same(source, contract.Source);
        Assert.Equal(quantity, contract.BaseQuantity);
        Assert.Null(contract.AlternateQuantity);
    }

    [Fact]
    public void Missing_conversion_or_density_is_represented_only_by_null_alternate()
    {
        var contract = Contract(alternate: null);

        Assert.Null(contract.AlternateQuantity);
        Assert.NotEqual(0m, contract.BaseQuantity.Value);
    }

    [Fact]
    public void Alternate_quantity_requires_and_preserves_a_versioned_conversion_source()
    {
        var conversion = new InventoryStockQuantitySourceV2(
            "settings.unit-conversion", Guid.NewGuid(), "conversion-v4");
        var alternate = new InventoryAlternateStockQuantityV2(
            StockQuantityV2.Positive(25.5m, Guid.NewGuid(), Guid.NewGuid(), 8,
                StockQuantityDimensionV2.Mass, 3),
            conversion);

        var contract = Contract(alternate);

        Assert.Same(alternate, contract.AlternateQuantity);
        Assert.Same(conversion, contract.AlternateQuantity!.ConversionSource);
        Assert.Equal("conversion-v4", contract.AlternateQuantity.ConversionSource.Version);
    }

    [Fact]
    public void Default_or_unsourced_alternate_quantity_is_rejected()
    {
        var alternate = new InventoryAlternateStockQuantityV2(default, null!);

        var error = Assert.Throws<InventoryStockItemQuantityContractExceptionV2>(() => Contract(alternate));
        Assert.Equal(InventoryStockItemQuantityCodesV2.InvalidAlternateQuantity, error.Code);
    }

    [Fact]
    public void Company_item_source_and_base_quantity_are_all_required()
    {
        AssertCode(InventoryStockItemQuantityCodesV2.InvalidCompany,
            () => new InventoryStockItemQuantityV2(
                Guid.Empty, InventoryStockItemV2.Product(ProductId), Source(), BaseQuantity(), null));
        AssertCode(InventoryStockItemQuantityCodesV2.InvalidItem,
            () => new InventoryStockItemQuantityV2(CompanyId, null!, Source(), BaseQuantity(), null));
        AssertCode(InventoryStockItemQuantityCodesV2.InvalidSource,
            () => new InventoryStockItemQuantityV2(
                CompanyId, InventoryStockItemV2.Product(ProductId), null!, BaseQuantity(), null));
        AssertCode(InventoryStockItemQuantityCodesV2.InvalidQuantity,
            () => new InventoryStockItemQuantityV2(
                CompanyId, InventoryStockItemV2.Product(ProductId), Source(), default, null));
    }

    [Theory]
    [InlineData("", "v1")]
    [InlineData(" settings.profile", "v1")]
    [InlineData("settings.profile", "")]
    [InlineData("settings.profile", "v1 ")]
    public void Source_kind_and_version_are_canonical(string kind, string version)
    {
        var error = Assert.Throws<InventoryStockItemQuantityContractExceptionV2>(() =>
            new InventoryStockQuantitySourceV2(kind, SourceId, version));

        Assert.Equal(InventoryStockItemQuantityCodesV2.InvalidSource, error.Code);
    }

    [Fact]
    public void Source_identity_is_required()
    {
        var error = Assert.Throws<InventoryStockItemQuantityContractExceptionV2>(() =>
            new InventoryStockQuantitySourceV2("settings.stock-profile", Guid.Empty, "v1"));

        Assert.Equal(InventoryStockItemQuantityCodesV2.InvalidSource, error.Code);
    }

    [Fact]
    public void Kilogram_v1_stays_compatible_and_separate_from_the_new_dimension_contract()
    {
        var legacy = Kilograms.Positive(10.125m);
        var current = StockQuantityV2.Positive(10.125m, UnitId, ProfileId, 2,
            StockQuantityDimensionV2.Mass, 3);

        Assert.Equal(legacy.Value, current.Value);
        Assert.Equal("10.125", legacy.ToString());
        Assert.Equal("10.125", current.ToString());
    }

    private static InventoryStockItemQuantityV2 Contract(InventoryAlternateStockQuantityV2? alternate) =>
        new(CompanyId, InventoryStockItemV2.Product(ProductId), Source(), BaseQuantity(), alternate);

    private static InventoryStockQuantitySourceV2 Source() =>
        new("inventory.stock-item-source", SourceId, "source-v3");

    private static StockQuantityV2 BaseQuantity() =>
        StockQuantityV2.Positive(100.25m, UnitId, ProfileId, 3, StockQuantityDimensionV2.Volume, 2);

    private static void AssertCode(string code, Action action) =>
        Assert.Equal(code, Assert.Throws<InventoryStockItemQuantityContractExceptionV2>(action).Code);
}

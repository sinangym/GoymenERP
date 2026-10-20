using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryGoodsReceiptQuantityV2Tests
{
    private static readonly Guid CompanyId = Guid.Parse("97000000-0000-4000-8000-000000000001");
    private static readonly Guid ProductId = Guid.Parse("97000000-0000-4000-8000-000000000002");
    private static readonly Guid AcceptanceId = Guid.Parse("97000000-0000-4000-8000-000000000003");
    private static readonly Guid ProfileId = Guid.Parse("97000000-0000-4000-8000-000000000004");

    [Fact]
    public void Canonical_kilograms_keep_the_exact_settings_profile_and_goods_receipt_source()
    {
        var quantity = InventoryGoodsReceiptQuantityV2.Create(
            Request(), Kilograms.Positive(12.125m), Profile());

        Assert.Equal(CompanyId, quantity.CompanyId);
        Assert.Equal((InventoryStockItemKindV2.Product, ProductId),
            (quantity.Item.Kind, quantity.Item.Id));
        Assert.Equal((InventoryGoodsReceiptContractV1.SourceKind, AcceptanceId, "7"),
            (quantity.Source.Kind, quantity.Source.Id, quantity.Source.Version));
        Assert.Equal((12.125m, SettingsUnitIdentitiesV1.Kilogram, ProfileId, 4L,
                StockQuantityDimensionV2.Mass, 3),
            (quantity.BaseQuantity.Value, quantity.BaseQuantity.UnitId,
                quantity.BaseQuantity.ProfileId, quantity.BaseQuantity.ProfileVersion,
                quantity.BaseQuantity.Dimension, quantity.BaseQuantity.Scale));
        Assert.Null(quantity.AlternateQuantity);
    }

    [Fact]
    public void Purchase_order_version_after_is_the_exact_quantity_source_version()
    {
        var quantity = InventoryGoodsReceiptQuantityV2.Create(
            Request() with { PurchaseOrderVersionAfter = "19" },
            Kilograms.Positive(1m),
            Profile());

        Assert.Equal("19", quantity.Source.Version);
    }

    [Fact]
    public void Missing_profile_fails_closed_without_a_default_unit_or_scale()
    {
        AssertRefused(503, InventoryGoodsReceiptCodesV1.QuantityProfileUnavailable,
            () => InventoryGoodsReceiptQuantityV2.Create(Request(), Kilograms.Positive(1m), null));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("unit")]
    [InlineData("profile")]
    [InlineData("version")]
    [InlineData("scale-negative")]
    [InlineData("scale-wide")]
    public void Invalid_or_non_kilogram_profile_fails_closed(string fault)
    {
        var profile = fault switch
        {
            "company" => Profile() with { CompanyId = Guid.NewGuid() },
            "unit" => Profile() with { UnitId = Guid.NewGuid() },
            "profile" => Profile() with { ProfileId = Guid.Empty },
            "version" => Profile() with { ProfileVersion = 0 },
            "scale-negative" => Profile() with { Scale = -1 },
            _ => Profile() with { Scale = 4 },
        };

        AssertRefused(503, InventoryGoodsReceiptCodesV1.QuantityProfileUnavailable,
            () => InventoryGoodsReceiptQuantityV2.Create(Request(), Kilograms.Positive(1m), profile));
    }

    [Fact]
    public void Profile_scale_mismatch_is_an_invalid_quantity_not_a_rounded_value()
    {
        AssertRefused(400, InventoryGoodsReceiptCodesV1.InvalidQuantity,
            () => InventoryGoodsReceiptQuantityV2.Create(
                Request(), Kilograms.Positive(1.125m), Profile() with { Scale = 2 }));
    }

    [Fact]
    public void Existing_product_receipt_cannot_substitute_service_or_consumable()
    {
        var quantity = InventoryGoodsReceiptQuantityV2.Create(
            Request(), Kilograms.Positive(1m), Profile());

        Assert.Equal(InventoryStockItemKindV2.Product, quantity.Item.Kind);
        Assert.Equal(ProductId, quantity.Item.Id);
        Assert.NotEqual(InventoryStockItemKindV2.Consumable, quantity.Item.Kind);
        Assert.DoesNotContain(Enum.GetNames<InventoryStockItemKindV2>(), name => name == "Service");
    }

    private static InventoryKilogramProfile Profile() => new(
        CompanyId, SettingsUnitIdentitiesV1.Kilogram, ProfileId, 4, 3);

    private static InventoryGoodsReceiptRequestV1 Request() => new(
        CompanyId,
        AcceptanceId,
        Guid.Parse("97000000-0000-4000-8000-000000000005"),
        Guid.Parse("97000000-0000-4000-8000-000000000006"),
        "7",
        ProductId,
        "product-v1",
        SettingsUnitIdentitiesV1.Kilogram,
        Guid.Parse("97000000-0000-4000-8000-000000000007"),
        Guid.Parse("97000000-0000-4000-8000-000000000008"),
        "LBL-P051B-001",
        null,
        null,
        [],
        StockPartyKind.Company,
        CompanyId,
        StockPartyKind.Company,
        CompanyId,
        Guid.Parse("97000000-0000-4000-8000-000000000009"),
        BlockState.Free,
        Guid.Parse("97000000-0000-4000-8000-000000000010"),
        "12.125",
        new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));

    private static void AssertRefused(int status, string code, Action action)
    {
        var error = Assert.Throws<InventoryGoodsReceiptException>(action);
        Assert.Equal((status, code), (error.Status, error.Code));
    }
}

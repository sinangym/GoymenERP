using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseGoodsReceiptComparisonContractTests
{
    private static readonly Guid CompanyId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid AcceptanceId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid OrderId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static readonly Guid LineId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
    private static readonly Guid ProductId = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
    private static readonly Guid UnitId = Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff");
    private static readonly Guid ProfileId = Guid.Parse("11111111-2222-4333-8444-555555555555");

    [Fact]
    public void Same_kilogram_unit_exposes_exact_signed_difference_without_tolerance()
    {
        var result = Create(Receipt(), storedUnitIsKilogram: true);

        Assert.Equal(PurchaseGoodsReceiptQuantityComparisonStatusesV1.SameKilogramUnit, result.ComparisonStatus);
        Assert.Equal("100.25", result.AcceptedQuantityKg);
        Assert.Equal("1.5", result.MeasuredMinusAcceptedKg);
        Assert.Equal("100.25", result.AcceptedQuantity);
        Assert.Equal("101.75", result.MeasuredQuantityKg);
    }

    [Fact]
    public void Non_kilogram_basis_preserves_sources_and_exposes_no_derived_quantities()
    {
        var result = Create(Receipt() with { AcceptedQuantity = "4" }, storedUnitIsKilogram: false, storedAcceptedQuantity: "4");

        Assert.Equal(PurchaseGoodsReceiptQuantityComparisonStatusesV1.Unavailable, result.ComparisonStatus);
        Assert.Null(result.AcceptedQuantityKg);
        Assert.Null(result.MeasuredMinusAcceptedKg);
        Assert.Equal("4", result.AcceptedQuantity);
        Assert.Equal("101.75", result.MeasuredQuantityKg);
    }

    [Fact]
    public void Legacy_explicit_null_snapshot_preserves_the_existing_flat_measurement_comparison()
    {
        var result = Create(Receipt() with { StockItemQuantity = null }, storedUnitIsKilogram: true);

        Assert.Equal(PurchaseGoodsReceiptQuantityComparisonStatusesV1.SameKilogramUnit, result.ComparisonStatus);
        Assert.Equal("101.75", result.MeasuredQuantityKg);
        Assert.Equal("1.5", result.MeasuredMinusAcceptedKg);
    }

    [Theory]
    [InlineData("flat-value")]
    [InlineData("company")]
    [InlineData("subject-kind")]
    [InlineData("subject-id")]
    [InlineData("source-kind")]
    [InlineData("source-id")]
    [InlineData("source-version")]
    [InlineData("value")]
    [InlineData("unit")]
    [InlineData("profile")]
    [InlineData("profile-version")]
    [InlineData("dimension")]
    [InlineData("scale")]
    [InlineData("alternate")]
    public void Non_null_snapshot_tampering_is_refused(string fault)
    {
        var receipt = Receipt();
        var quantity = Assert.IsType<PurchaseGoodsReceiptQuantityV2>(receipt.StockItemQuantity);
        receipt = fault switch
        {
            "flat-value" => receipt with { MeasuredQuantityKg = "101.5" },
            "company" => WithQuantity(receipt, quantity with { CompanyId = Guid.NewGuid() }),
            "subject-kind" => WithQuantity(receipt, quantity with
            {
                Subject = quantity.Subject with { Kind = "Service" }
            }),
            "subject-id" => WithQuantity(receipt, quantity with
            {
                Subject = quantity.Subject with { Id = Guid.NewGuid() }
            }),
            "source-kind" => WithQuantity(receipt, quantity with
            {
                Source = quantity.Source with { Kind = "other" }
            }),
            "source-id" => WithQuantity(receipt, quantity with
            {
                Source = quantity.Source with { Id = Guid.NewGuid() }
            }),
            "source-version" => WithQuantity(receipt, quantity with
            {
                Source = quantity.Source with { Version = "6" }
            }),
            "value" => WithQuantity(receipt, quantity with
            {
                BaseQuantity = quantity.BaseQuantity with { Value = "101.5" }
            }),
            "unit" => WithQuantity(receipt, quantity with
            {
                BaseQuantity = quantity.BaseQuantity with { UnitId = Guid.NewGuid() }
            }),
            "profile" => WithQuantity(receipt, quantity with
            {
                BaseQuantity = quantity.BaseQuantity with { ProfileId = Guid.Empty }
            }),
            "profile-version" => WithQuantity(receipt, quantity with
            {
                BaseQuantity = quantity.BaseQuantity with { ProfileVersion = "01" }
            }),
            "dimension" => WithQuantity(receipt, quantity with
            {
                BaseQuantity = quantity.BaseQuantity with { Dimension = "Volume" }
            }),
            "scale" => WithQuantity(receipt, quantity with
            {
                BaseQuantity = quantity.BaseQuantity with { Scale = 4 }
            }),
            "alternate" => WithQuantity(receipt, quantity with
            {
                AlternateQuantity = new(quantity.BaseQuantity, quantity.Source)
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        Assert.Throws<ArgumentException>(() => Create(receipt, storedUnitIsKilogram: true));
    }

    [Fact]
    public void Mismatched_owner_unit_is_refused_instead_of_comparing()
    {
        var otherUnit = Guid.Parse("11111111-1111-4111-8111-111111111111");

        Assert.Throws<ArgumentException>(() => Create(Receipt() with { UnitId = otherUnit }, storedUnitIsKilogram: true));
    }

    [Fact]
    public void Receipt_version_ahead_of_stored_order_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Create(Receipt() with { PurchaseOrderVersion = "9" }, storedUnitIsKilogram: true));
    }

    private static PurchaseGoodsReceiptQuantityComparisonV1 Create(
        PurchaseGoodsReceiptViewV1 receipt,
        bool storedUnitIsKilogram,
        string storedAcceptedQuantity = "100.25") =>
        PurchaseGoodsReceiptQuantityComparisonFactoryV1.Create(
            receipt,
            CompanyId,
            OrderId,
            AcceptanceId,
            LineId,
            ProductId,
            "3",
            UnitId,
            storedAcceptedQuantity,
            storedUnitIsKilogram,
            "8");

    private static PurchaseGoodsReceiptViewV1 Receipt() =>
        new(
            CompanyId,
            AcceptanceId,
            OrderId,
            LineId,
            "7",
            ProductId,
            "3",
            UnitId,
            "100.25",
            "40",
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            Guid.Parse("33333333-3333-4333-8333-333333333333"),
            "LBL-001",
            "2",
            Guid.Parse("44444444-4444-4444-8444-444444444444"),
            Guid.Parse("55555555-5555-4555-8555-555555555555"),
            "101.75",
            "Free",
            new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero),
            Quantity());

    private static PurchaseGoodsReceiptViewV1 WithQuantity(
        PurchaseGoodsReceiptViewV1 receipt,
        PurchaseGoodsReceiptQuantityV2 quantity) => receipt with { StockItemQuantity = quantity };

    private static PurchaseGoodsReceiptQuantityV2 Quantity() => new(
        CompanyId,
        new(PurchaseGoodsReceiptApiV1.QuantitySubjectKind, ProductId),
        new(PurchaseGoodsReceiptApiV1.QuantitySourceKind, AcceptanceId, "7"),
        new("101.75", SettingsUnitIdentitiesV1.Kilogram, ProfileId, "5",
            PurchaseGoodsReceiptApiV1.QuantityDimension, 2),
        null);
}

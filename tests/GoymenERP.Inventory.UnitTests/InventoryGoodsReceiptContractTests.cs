using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryGoodsReceiptContractTests
{
    private static readonly Guid Company = Guid.Parse("91000000-0000-4000-8000-000000000001");

    [Theory]
    [InlineData(BlockState.Free)]
    [InlineData(BlockState.Blocked)]
    public void Explicit_initial_block_is_preserved_without_a_default(BlockState block)
    {
        var request = Request() with { InitialBlock = block };
        var quantity = InventoryGoodsReceiptContractV1.Validate(request, Metadata(), Company);
        Assert.Equal(12.345m, quantity.Value);
    }

    [Fact]
    public void Another_company_is_the_neutral_invalid_reference()
    {
        var error = Assert.Throws<InventoryGoodsReceiptException>(() =>
            InventoryGoodsReceiptContractV1.Validate(Request() with { CompanyId = Guid.NewGuid() }, Metadata(), Company));
        Assert.Equal(404, error.Status);
        Assert.Equal(InventoryGoodsReceiptCodesV1.InvalidReference, error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    public void Post_acceptance_purchase_order_version_is_exact_and_canonical(string version)
    {
        var error = Assert.Throws<InventoryGoodsReceiptException>(() =>
            InventoryGoodsReceiptContractV1.Validate(Request() with { PurchaseOrderVersionAfter = version },
                Metadata(), Company));
        Assert.Equal(400, error.Status);
        Assert.Equal(InventoryGoodsReceiptCodesV1.InvalidVersion, error.Code);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("12.3450")]
    [InlineData("12,345")]
    [InlineData("12.3456")]
    public void Kilograms_are_positive_canonical_and_never_rounded(string quantity)
    {
        var error = Assert.Throws<InventoryGoodsReceiptException>(() =>
            InventoryGoodsReceiptContractV1.Validate(Request() with { QuantityKg = quantity }, Metadata(), Company));
        Assert.Equal(400, error.Status);
        Assert.Equal(InventoryGoodsReceiptCodesV1.InvalidQuantity, error.Code);
    }

    [Theory]
    [InlineData(" product-v1")]
    [InlineData("product-v1 ")]
    [InlineData("")]
    public void Product_version_is_explicit_and_canonical(string version)
    {
        var error = Assert.Throws<InventoryGoodsReceiptException>(() =>
            InventoryGoodsReceiptContractV1.Validate(Request() with { ExpectedProductVersion = version },
                Metadata(), Company));
        Assert.Equal(InventoryGoodsReceiptCodesV1.InvalidRequest, error.Code);
    }

    [Fact]
    public void Physical_position_and_purchase_references_are_all_required()
    {
        var examples = new[]
        {
            Request() with { PurchaseUnitId = Guid.Empty },
            Request() with { OwnerId = Guid.Empty },
            Request() with { CustodianId = Guid.Empty },
            Request() with { LocationId = Guid.Empty },
            Request() with { InitialBlock = (BlockState)0 },
        };
        foreach (var request in examples)
        {
            Assert.Equal(InventoryGoodsReceiptCodesV1.InvalidRequest,
                Assert.Throws<InventoryGoodsReceiptException>(() =>
                    InventoryGoodsReceiptContractV1.Validate(request, Metadata(), Company)).Code);
        }
    }

    [Fact]
    public void Required_document_tokens_are_not_trimmed_or_inferred()
    {
        var error = Assert.Throws<InventoryGoodsReceiptException>(() =>
            InventoryGoodsReceiptContractV1.Validate(Request() with { RequiredDocuments = [" MTC"] },
                Metadata(), Company));
        Assert.Equal(InventoryGoodsReceiptCodesV1.InvalidRequest, error.Code);
    }

    [Fact]
    public void Metadata_is_server_canonical()
    {
        var error = Assert.Throws<InventoryGoodsReceiptException>(() =>
            InventoryGoodsReceiptContractV1.Validate(Request(), Metadata() with { IdempotencyKey = " key" }, Company));
        Assert.Equal(InventoryGoodsReceiptCodesV1.InvalidRequest, error.Code);
    }

    [Fact]
    public void Replay_lookup_requires_the_original_command_identity_shape()
    {
        var missingAcceptance = Assert.Throws<InventoryGoodsReceiptException>(() =>
            InventoryGoodsReceiptContractV1.ValidateReplayLookup(Guid.Empty, Metadata(), Company));
        Assert.Equal((400, InventoryGoodsReceiptCodesV1.InvalidRequest),
            (missingAcceptance.Status, missingAcceptance.Code));

        var malformedKey = Assert.Throws<InventoryGoodsReceiptException>(() =>
            InventoryGoodsReceiptContractV1.ValidateReplayLookup(
                Request().AcceptanceId, Metadata() with { IdempotencyKey = " receipt-key-1" }, Company));
        Assert.Equal((400, InventoryGoodsReceiptCodesV1.InvalidRequest),
            (malformedKey.Status, malformedKey.Code));
    }

    [Fact]
    public void Refusal_contract_is_closed()
    {
        Assert.True(InventoryGoodsReceiptCodesV1.IsDefined(409,
            InventoryGoodsReceiptCodesV1.IdempotencyConflict));
        Assert.False(InventoryGoodsReceiptCodesV1.IsDefined(422, "invented"));
        Assert.Throws<InvalidOperationException>(() => InventoryGoodsReceiptException.Refused(409, "invented"));
    }

    private static InventoryGoodsReceiptRequestV1 Request() => new(
        Company,
        Guid.Parse("91000000-0000-4000-8000-000000000002"),
        Guid.Parse("91000000-0000-4000-8000-000000000003"),
        Guid.Parse("91000000-0000-4000-8000-000000000004"),
        "7",
        Guid.Parse("91000000-0000-4000-8000-000000000005"),
        "product-v1",
        Guid.Parse("91000000-0000-4000-8000-000000000006"),
        Guid.Parse("91000000-0000-4000-8000-000000000007"),
        Guid.Parse("91000000-0000-4000-8000-000000000008"),
        "LBL-RECEIPT-001",
        "HEAT-1",
        "COIL-1",
        ["MTC"],
        StockPartyKind.Company,
        Company,
        StockPartyKind.Company,
        Company,
        Guid.Parse("91000000-0000-4000-8000-000000000009"),
        BlockState.Blocked,
        Guid.Parse("91000000-0000-4000-8000-000000000010"),
        "12.345",
        new DateTimeOffset(2026, 9, 24, 10, 30, 0, TimeSpan.Zero));

    private static InventoryCommandMetadata Metadata() => new(
        "inventory-receipt-user",
        "purchasing.goods-receipt.record",
        ["PURCHASING-USER"],
        "corr-receipt-1",
        "cause-receipt-1",
        "receipt-key-1",
        "api");
}

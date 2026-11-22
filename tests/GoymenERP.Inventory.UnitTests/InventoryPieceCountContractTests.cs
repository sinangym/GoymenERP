using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// KLN-D119 (AK-2b): the optional piece count on the remaining participant contracts. A negative count is the request's
/// quantity fault; an omitted count is not serialized, so every caller before AK-3/AK-4/AK-7 hashes and replays exactly as
/// before.
/// </summary>
public sealed class InventoryPieceCountContractTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset At = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_supplier_return_exit_carries_a_non_negative_piece_count_and_omits_it_when_absent()
    {
        var input = InventorySupplierReturnExitContractV1.Validate(SupplierReturn() with { PieceCount = 3 }, Metadata(), Company);
        var refused = Assert.Throws<InventorySupplierReturnExitException>(() =>
            InventorySupplierReturnExitContractV1.Validate(SupplierReturn() with { PieceCount = -1 }, Metadata(), Company));

        Assert.Equal(3, input.Pieces!.Value.Value);
        Assert.Null(InventorySupplierReturnExitContractV1.Validate(SupplierReturn(), Metadata(), Company).Pieces);
        Assert.Equal((400, InventorySupplierReturnExitCodesV1.InvalidQuantity), (refused.Status, refused.Code));
        Assert.DoesNotContain("PieceCount", JsonSerializer.Serialize(SupplierReturn()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_customer_return_refuses_a_negative_piece_count_and_omits_an_absent_one()
    {
        var refused = Assert.Throws<InventoryCustomerReturnException>(() =>
            InventoryCustomerReturnContractV1.Validate(CustomerReturn() with { PieceCount = -1 }, Metadata(), Company));

        Assert.Equal((400, InventoryCustomerReturnCodesV1.InvalidQuantity), (refused.Status, refused.Code));
        Assert.Equal(12.125m, InventoryCustomerReturnContractV1.Validate(CustomerReturn() with { PieceCount = 2 },
            Metadata(), Company).Value);
        Assert.DoesNotContain("PieceCount", JsonSerializer.Serialize(CustomerReturn()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_manufacturing_output_starts_with_at_least_one_piece_and_an_issue_refuses_a_negative_count()
    {
        var output = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(Output() with { PieceCount = 0 }, Metadata(), Company));
        var issue = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV1.Validate(Issue() with { PieceCount = -1 }, Metadata(), Company));

        Assert.Equal(InventoryManufacturingRealizationCodesV1.InvalidRequest, output.Code);
        Assert.Equal(InventoryManufacturingRealizationCodesV1.InvalidRequest, issue.Code);
        Assert.Equal(1m, InventoryManufacturingRealizationContractV1.Validate(Output() with { PieceCount = 1 }, Metadata(),
            Company).Value);
        Assert.DoesNotContain("PieceCount", JsonSerializer.Serialize(Issue()), StringComparison.Ordinal);
    }

    [Fact]
    public void The_V2_output_request_hands_its_piece_count_to_the_V1_core()
    {
        var v2 = new InventoryManufacturingOutputRequestV2(Company, Guid.NewGuid(), Guid.NewGuid(), "2", Guid.NewGuid(), "3",
            "Main", Guid.NewGuid(), "v1", Guid.NewGuid(), Guid.NewGuid(), "L-1", null, null, [], StockPartyKind.Company,
            Company, StockPartyKind.Company, Company, Guid.NewGuid(), BlockState.Free, Guid.NewGuid(), "1", At,
            PieceCount: 6);

        Assert.Equal(6, v2.ToV1().PieceCount);
    }

    private static InventorySupplierReturnExitRequestV1 SupplierReturn() => new(Company,
        Guid.Parse("72000000-0000-0000-0000-000000000001"), Guid.Parse("72000000-0000-0000-0000-000000000002"),
        Guid.Parse("72000000-0000-0000-0000-000000000003"), Guid.Parse("72000000-0000-0000-0000-000000000004"),
        BlockState.Free, "12.125", "41.5", At);

    private static InventoryCustomerReturnRequestV1 CustomerReturn() => new(Company,
        Guid.Parse("71000000-0000-0000-0000-000000000001"), Guid.Parse("71000000-0000-0000-0000-000000000002"),
        Guid.Parse("71000000-0000-0000-0000-000000000003"), Guid.Parse("71000000-0000-0000-0000-000000000004"),
        "12.125", At);

    private static InventoryManufacturingInputIssueRequestV1 Issue() => new(Company,
        Guid.Parse("73000000-0000-0000-0000-000000000001"), Guid.Parse("73000000-0000-0000-0000-000000000002"), "2",
        Guid.Parse("73000000-0000-0000-0000-000000000003"), "3", Guid.Parse("73000000-0000-0000-0000-000000000004"), "1", At);

    private static InventoryManufacturingOutputRequestV1 Output() => new(Company,
        Guid.Parse("74000000-0000-0000-0000-000000000001"), Guid.Parse("74000000-0000-0000-0000-000000000002"), "2",
        Guid.Parse("74000000-0000-0000-0000-000000000003"), "3", "Main", Guid.Parse("74000000-0000-0000-0000-000000000004"),
        "v1", Guid.Parse("74000000-0000-0000-0000-000000000005"), Guid.Parse("74000000-0000-0000-0000-000000000006"), "L-1",
        null, null, [], StockPartyKind.Company, Company, StockPartyKind.Company, Company,
        Guid.Parse("74000000-0000-0000-0000-000000000007"), BlockState.Free,
        Guid.Parse("74000000-0000-0000-0000-000000000008"), "1", At);

    private static InventoryCommandMetadata Metadata() => new(
        "actor-1", "purchasing.supplier-returns.decide", ["purchasing-user"],
        "corr-1", "cause-1", "key-1", "api");
}

using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryGoodsReceiptReversalContractTests
{
    [Theory]
    [InlineData(InventoryStoreCodes.ConcurrentChange, 409, InventoryGoodsReceiptReversalCodesV1.ConcurrentChange)]
    [InlineData(InventoryStoreCodes.MovementIdConflict, 409, InventoryGoodsReceiptReversalCodesV1.IdempotencyConflict)]
    [InlineData(InventoryStoreCodes.DuplicateSource, 409, InventoryGoodsReceiptReversalCodesV1.IdempotencyConflict)]
    [InlineData("settings_provider_unavailable", 503, InventoryGoodsReceiptReversalCodesV1.QuantityProfileUnavailable)]
    [InlineData("unrecognized_owner_fault", 503, InventoryGoodsReceiptReversalCodesV1.Unavailable)]
    public void Store_failures_keep_the_closed_status_code_contract_instead_of_escaping_as_server_errors(string source, int status, string code)
    {
        var refused = InventoryGoodsReceiptReversalParticipant.StoreRefusal(source);
        Assert.Equal((status, code), (refused.Status, refused.Code));
        Assert.True(InventoryGoodsReceiptReversalCodesV1.IsDefined(status, code));
    }

    [Fact]
    public void The_pieces_of_a_partial_hold_release_join_the_request_hash_only_when_sent()
    {
        // KLN-D120: the pieces that leave with a partial piece-holding release are the request's PieceCount. They change
        // the hash when sent, and a kilogram-only request keeps the hash it had before pieces existed.
        var kilograms = new InventoryGoodsReceiptReversalRequestV1(Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("10000000-0000-0000-0000-000000000002"), Guid.Parse("10000000-0000-0000-0000-000000000003"),
            Guid.Parse("10000000-0000-0000-0000-000000000004"), Guid.Parse("10000000-0000-0000-0000-000000000005"),
            InventoryGoodsReceiptReversalKindV1.AcceptanceUndo, GoymenERP.Modules.Inventory.Domain.Stock.BlockState.Free, "2", null,
            "SENTETIK duzeltme", new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero));
        var withoutPieces = InventoryGoodsReceiptReversalParticipant.RequestHash(kilograms);
        var two = InventoryGoodsReceiptReversalParticipant.RequestHash(kilograms with { PieceCount = 2 });
        var three = InventoryGoodsReceiptReversalParticipant.RequestHash(kilograms with { PieceCount = 3 });

        Assert.Equal(withoutPieces, InventoryGoodsReceiptReversalParticipant.RequestHash(kilograms with { PieceCount = null }));
        Assert.Equal(3, new[] { withoutPieces, two, three }.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void An_unknown_or_mismatched_status_code_pair_cannot_be_constructed_as_a_typed_reversal_refusal()
    {
        Assert.Throws<ArgumentException>(() => new InventoryGoodsReceiptReversalException(200, InventoryGoodsReceiptReversalCodesV1.Unavailable));
        Assert.Throws<ArgumentException>(() => new InventoryGoodsReceiptReversalException(409, "unknown"));
    }
}

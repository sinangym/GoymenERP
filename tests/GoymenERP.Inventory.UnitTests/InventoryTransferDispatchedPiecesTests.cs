using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// KLN-D119 (AK-2b): the transfer line view names the dispatched pieces of a piece-tracking line, so the receiver knows the
/// receipt must carry its received pieces; a kg-only line and an undispatched line carry none (the member is absent).
/// </summary>
public sealed class InventoryTransferDispatchedPiecesTests
{
    private static readonly Guid PieceLine = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid KilogramLine = Guid.Parse("10000000-0000-4000-8000-000000000002");

    [Fact]
    public void A_piece_line_sums_its_buckets_pieces_and_a_kilogram_line_has_none()
    {
        var pieces = InventoryStore.DispatchedPieces(
        [
            Bucket(PieceLine, 1, 4),
            Bucket(PieceLine, 2, 3),
            Bucket(KilogramLine, 1, null),
            Bucket(KilogramLine, 2, null),
        ]);

        Assert.Equal(7, pieces[PieceLine]);
        Assert.False(pieces.ContainsKey(KilogramLine));
    }

    [Fact]
    public void A_line_whose_buckets_do_not_all_carry_pieces_is_not_reported_as_a_piece_line()
    {
        var pieces = InventoryStore.DispatchedPieces([Bucket(PieceLine, 1, 4), Bucket(PieceLine, 2, null)]);

        Assert.Empty(pieces);
    }

    [Fact]
    public void The_line_view_writes_the_dispatched_pieces_only_when_present()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var kilogramLine = new InventoryTransferLineViewV1(KilogramLine, PieceLine, "3", "10", "10", "0", "InTransit");
        var pieceLine = kilogramLine with { DispatchedPieceCount = 7 };

        using var kilogram = JsonDocument.Parse(JsonSerializer.Serialize(kilogramLine, options));
        using var piece = JsonDocument.Parse(JsonSerializer.Serialize(pieceLine, options));

        Assert.False(kilogram.RootElement.TryGetProperty("dispatchedPieceCount", out _));
        Assert.Equal(7, piece.RootElement.GetProperty("dispatchedPieceCount").GetInt32());
    }

    private static InventoryTransferBucketRow Bucket(Guid line, int sequence, int? pieces) => new()
    {
        LineId = line,
        Sequence = sequence,
        QuantityKg = 1m,
        PieceCount = pieces,
    };
}

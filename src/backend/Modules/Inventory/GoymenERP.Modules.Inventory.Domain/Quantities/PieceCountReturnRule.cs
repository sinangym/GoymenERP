namespace GoymenERP.Modules.Inventory.Domain.Quantities;

/// <summary>
/// KLN-D119 (AK-2b): the piece rule of a movement that brings back part of an earlier traced exit — a customer return of a
/// definite shipment line, an unused production input returned to its issued label. The exit's pieces decide:
/// <list type="bullet">
/// <item>an exit that carried pieces is returned with pieces, one that did not never is (no backfill);</item>
/// <item>all returns of the exit never bring back more pieces than left
/// (<see cref="InventoryRuleCodes.NegativePieceCount"/>: the outstanding pieces would go below zero);</item>
/// <item>the outstanding kilograms and pieces run out together: the last kilograms bring back the last pieces, and pieces
/// never all come back while kilograms are still outstanding.</item>
/// </list>
/// A return of kilograms with zero pieces (part of a piece's weight) is valid while both stay outstanding.
/// </summary>
public static class PieceCountReturnRule
{
    public static PieceCount? Returned(
        Kilograms exitedKg,
        PieceCount? exitedPieces,
        Kilograms alreadyReturnedKg,
        PieceCount alreadyReturnedPieces,
        Kilograms returnedKg,
        int? requestedPieces)
    {
        if (exitedPieces is null && requestedPieces is not null)
        {
            throw new InventoryRuleException(InventoryRuleCodes.PieceCountNotTracked,
                "Adetsiz çıkışın iadesi adetle kaydedilmez.");
        }

        if (exitedPieces is not { } exited)
        {
            return null;
        }

        if (requestedPieces is not { } requested)
        {
            throw new InventoryRuleException(InventoryRuleCodes.PieceCountRequired,
                "Adetli çıkışın iadesi iade edilen adetle kaydedilir.");
        }

        var pieces = PieceCount.Of(requested);
        var outstandingPieces = exited.Value - alreadyReturnedPieces.Value - pieces.Value;
        if (outstandingPieces < 0)
        {
            throw new InventoryRuleException(InventoryRuleCodes.NegativePieceCount,
                "İade edilen adet çıkan adetten fazla olamaz.");
        }

        var outstandingKg = exitedKg.Value - alreadyReturnedKg.Value - returnedKg.Value;
        if ((outstandingPieces == 0) != (outstandingKg == 0m))
        {
            throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount,
                "İadede kalan adet ile kalan kilogram birlikte sıfıra iner.");
        }

        return pieces;
    }
}

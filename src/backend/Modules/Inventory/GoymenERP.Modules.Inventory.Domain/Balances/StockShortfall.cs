using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Domain.Balances;

/// <summary>
/// The movement line a negative-stock refusal stopped, and the bucket it would have taken below zero (owner decision
/// of 2026-11-13, SAT-D318: no operation takes physical stock below zero, and the refusal says which operation and
/// where). It travels on <see cref="InventoryRuleException.Shortfall"/> beside the unchanged
/// <see cref="InventoryRuleCodes.NegativePhysicalStock"/> code: the code stays the stable reason, this only names the
/// failing line.
/// <para>
/// Identities are the ones the position holds: its label, the label's product and the stored location. The lot is not
/// part of a position, so it is not repeated here. <see cref="Available"/> is the bucket's balance just before the
/// failing line, including earlier lines of the same movement on that bucket. Building one records nothing; a durable
/// record of the refused attempt is a separate, later slice.
/// </para>
/// </summary>
public sealed record StockShortfall
{
    public StockShortfall(StockMovementKind movementKind, StockSourceReference source, StockPosition position,
        Kilograms requested, Kilograms available)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(position);
        if (!Enum.IsDefined(movementKind))
        {
            throw new ArgumentOutOfRangeException(nameof(movementKind), "The movement kind is not defined.");
        }

        if (requested.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(requested), "A refused line always asks for a positive mass.");
        }

        if (available >= requested)
        {
            throw new ArgumentOutOfRangeException(nameof(available),
                "A shortfall exists only while the bucket holds less than the line asks for.");
        }

        MovementKind = movementKind;
        Source = source;
        Position = position;
        Requested = requested;
        Available = available;
    }

    /// <summary>The kind of the refused movement (an issue, a scrap, a transfer leg, ...).</summary>
    public StockMovementKind MovementKind { get; }

    /// <summary>The causing module's source, and its line when the movement answers one line.</summary>
    public StockSourceReference Source { get; }

    /// <summary>The bucket the failing line would have taken below zero.</summary>
    public StockPosition Position { get; }

    /// <summary>What the failing line asked to take out of <see cref="Position"/>.</summary>
    public Kilograms Requested { get; }

    /// <summary>What <see cref="Position"/> held just before the failing line; always less than <see cref="Requested"/>.</summary>
    public Kilograms Available { get; }
}

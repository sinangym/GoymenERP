using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Domain.Allocations;

/// <summary>The lifecycle stored for one physical-stock allocation.</summary>
public enum StockAllocationStatus
{
    Active = 1,
    Consumed = 2,
    Released = 3,
}

/// <summary>
/// A persisted allocation of one physical label at one exact stock position. Allocation is not a
/// stock movement (DEC-STK-003): consuming it changes only the allocation here; the caller must
/// stage the corresponding physical movement in the same owner transaction.
///
/// This predecessor deliberately has no public create policy. It restores already-authorized
/// allocations and enforces their version, position and remaining quantity while LOG-5's approved
/// outbox schema is still unresolved.
/// </summary>
public sealed class StockAllocation
{
    private StockAllocation(Guid id, StockPosition position, Kilograms allocated, Kilograms remaining,
        long version, StockAllocationStatus status, PieceCount? allocatedPieces, PieceCount? remainingPieces)
    {
        if (id == Guid.Empty || version < 1 || !Enum.IsDefined(status)
            || allocated.Value <= 0m || remaining.Value < 0m || remaining.Value > allocated.Value
            || status == StockAllocationStatus.Active && remaining.Value == 0m
            || status is StockAllocationStatus.Consumed or StockAllocationStatus.Released && remaining.Value != 0m
            // KLN-D119: an allocation holds pieces from its creation or never; the held pieces never exceed the allocated ones.
            || allocatedPieces.HasValue != remainingPieces.HasValue
            || allocatedPieces is { } all && (all.Value <= 0 || remainingPieces!.Value.Value > all.Value))
        {
            throw new ArgumentException("Stored stock allocation is inconsistent.");
        }

        Id = id;
        Position = position ?? throw new ArgumentNullException(nameof(position));
        Allocated = allocated;
        Remaining = remaining;
        Version = version;
        Status = status;
        AllocatedPieces = allocatedPieces;
        RemainingPieces = remainingPieces;
    }

    public Guid Id { get; }

    public Guid CompanyId => Position.CompanyId;

    public Guid LabelId => Position.LabelId;

    public StockPosition Position { get; }

    public Kilograms Allocated { get; private set; }

    public Kilograms Remaining { get; private set; }

    /// <summary>
    /// KLN-D119 / SAT-D434 (AK-4b): the pieces this allocation reserved on a piece-tracking label, next to its kilograms;
    /// null on an allocation without pieces (a kg-only label, or one created before AK-4b: no backfill).
    /// </summary>
    public PieceCount? AllocatedPieces { get; private set; }

    /// <summary>KLN-D119: the pieces still reserved; zero once consumed or released; null when no pieces are held.</summary>
    public PieceCount? RemainingPieces { get; private set; }

    public bool TracksPieces => AllocatedPieces is not null;

    public long Version { get; private set; }

    public StockAllocationStatus Status { get; private set; }

    public static StockAllocation Restore(Guid id, StockPosition position, Kilograms allocated,
        Kilograms remaining, long version, StockAllocationStatus status,
        PieceCount? allocatedPieces = null, PieceCount? remainingPieces = null) =>
        new(id, position, allocated, remaining, version, status, allocatedPieces, remainingPieces);

    /// <summary>
    /// Creates one allocation over an exact free physical position. <paramref name="pieces"/> (KLN-D119) is the number of
    /// pieces reserved from a piece-tracking label (at least one); null reserves kilograms only.
    /// </summary>
    public static StockAllocation Create(Guid id, StockPosition position, Kilograms quantity, PieceCount? pieces = null) =>
        pieces is { Value: 0 }
            ? throw new StockAllocationException(StockAllocationFailure.PiecesInvalid)
            : new(id, position, quantity, quantity, 1, StockAllocationStatus.Active, pieces, pieces);

    /// <summary>
    /// KLN-D119: the pieces of one Free piece-tracking position that no active allocation holds. An active allocation
    /// without pieces holds none of them; a stored state whose held pieces exceed the position leaves nothing.
    /// </summary>
    public static PieceCount UnreservedPieces(PieceCount freePhysical, PieceCount activelyAllocated) =>
        activelyAllocated.Value >= freePhysical.Value ? PieceCount.Zero : PieceCount.Of(freePhysical.Value - activelyAllocated.Value);

    /// <summary>
    /// KLN-D119: the piece side of a new reservation of <paramref name="quantity"/> kg (a create, a move's target or the
    /// whole new remainder of a resize) against one Free position. A piece-tracking position requires the pieces, a
    /// kg-only position refuses them (no backfill); the pieces must fit the unreserved ones, and — while every other
    /// active allocation of the position holds pieces too — the unreserved kilograms and pieces run out together.
    /// </summary>
    public static PieceCount? ReservablePieces(Kilograms quantity, int? requestedPieces, Kilograms unreservedKg,
        PieceCount? positionPieces, PieceCount activelyAllocatedPieces, bool everyActiveAllocationHoldsPieces)
    {
        if (positionPieces is not { } physical)
        {
            return requestedPieces is null ? null : throw new StockAllocationException(StockAllocationFailure.PiecesNotTracked);
        }

        if (requestedPieces is not { } requested)
        {
            throw new StockAllocationException(StockAllocationFailure.PiecesRequired);
        }

        if (requested <= 0)
        {
            throw new StockAllocationException(StockAllocationFailure.PiecesInvalid);
        }

        var unreserved = UnreservedPieces(physical, activelyAllocatedPieces);
        if (requested > unreserved.Value)
        {
            throw new StockAllocationException(StockAllocationFailure.PiecesInsufficient);
        }

        if (everyActiveAllocationHoldsPieces && (requested == unreserved.Value) != (quantity.Value == unreservedKg.Value))
        {
            throw new StockAllocationException(StockAllocationFailure.PiecesInvalid);
        }

        return PieceCount.Of(requested);
    }

    /// <summary>
    /// The kilograms of one Free physical position that no active allocation holds, the only part another
    /// consumer (a Manufacturing input issue) may take. Master v3 §7.6 / INV-003:
    /// <c>Serbest Fiziki = Fiziki Mevcut − Aktif Satış Tahsisleri − …</c>; DEC-STK-004: an allocation never
    /// exceeds free stock, so consuming its kilograms elsewhere would leave it unbacked. A reservation changes
    /// only through its own explicit release (DEC-STK-005: no silent release). A stored state whose active
    /// remainders already exceed the position is not repaired here: nothing of it is unreserved.
    /// </summary>
    public static Kilograms Unreserved(Kilograms freePhysical, Kilograms activelyAllocated) =>
        activelyAllocated >= freePhysical ? Kilograms.Zero : Kilograms.Of(freePhysical.Value - activelyAllocated.Value);

    /// <summary>
    /// Consumes exactly the supplied kg; partiality is chosen by the owner request, not here. KLN-D119: an allocation
    /// that holds pieces is consumed with pieces (at least one, never more than it holds) and its kilograms and pieces
    /// run out together; an allocation without pieces ignores <paramref name="pieces"/> exactly as before (the label's
    /// own piece rule still decides the stock movement).
    /// </summary>
    public void Consume(long expectedVersion, Guid labelId, Kilograms quantity, PieceCount? pieces = null)
    {
        if (expectedVersion != Version)
        {
            throw new StockAllocationException(StockAllocationFailure.VersionConflict);
        }

        if (labelId != LabelId)
        {
            throw new StockAllocationException(StockAllocationFailure.LabelMismatch);
        }

        if (Status != StockAllocationStatus.Active)
        {
            throw new StockAllocationException(StockAllocationFailure.StateConflict);
        }

        if (quantity.Value <= 0m || quantity.Value > Remaining.Value)
        {
            throw new StockAllocationException(StockAllocationFailure.Insufficient);
        }

        if (RemainingPieces is { } held)
        {
            if (pieces is not { } taken)
            {
                throw new StockAllocationException(StockAllocationFailure.PiecesRequired);
            }

            if (taken.Value > held.Value)
            {
                throw new StockAllocationException(StockAllocationFailure.PiecesInsufficient);
            }

            if (taken.Value <= 0 || (taken.Value == held.Value) != (quantity.Value == Remaining.Value))
            {
                throw new StockAllocationException(StockAllocationFailure.PiecesInvalid);
            }

            RemainingPieces = PieceCount.Of(held.Value - taken.Value);
        }

        Remaining = Kilograms.Of(Remaining.Value - quantity.Value);
        Version = checked(Version + 1);
        if (Remaining.Value == 0m)
        {
            Status = StockAllocationStatus.Consumed;
        }
    }

    /// <summary>
    /// INV-AL-1 change: re-states the kilograms this active allocation still reserves at its own position, in
    /// place, so its identity (and any reference bound to it) survives. The consumed part is kept exactly:
    /// <c>Allocated − Remaining</c> before equals it after. An increase is a new reservation part, so the caller
    /// must first prove the position's unreserved free kilograms cover it (DEC-STK-004); a decrease frees kilograms
    /// the same way a release does. Changing to zero is a release, not a change; the same quantity is no change.
    /// Returns the signed difference (new − old). KLN-D119: an allocation that holds pieces is re-stated with the pieces
    /// it holds afterwards (at least one); one without pieces never gains them (no backfill).
    /// </summary>
    public decimal Resize(long expectedVersion, Kilograms remaining, PieceCount? pieces = null)
    {
        if (expectedVersion != Version)
        {
            throw new StockAllocationException(StockAllocationFailure.VersionConflict);
        }

        if (Status != StockAllocationStatus.Active)
        {
            throw new StockAllocationException(StockAllocationFailure.StateConflict);
        }

        if (remaining.Value <= 0m)
        {
            throw new StockAllocationException(StockAllocationFailure.Insufficient);
        }

        if (TracksPieces != pieces.HasValue)
        {
            throw new StockAllocationException(TracksPieces
                ? StockAllocationFailure.PiecesRequired : StockAllocationFailure.PiecesNotTracked);
        }

        if (pieces is { Value: <= 0 })
        {
            throw new StockAllocationException(StockAllocationFailure.PiecesInvalid);
        }

        if (remaining.Value == Remaining.Value && pieces == RemainingPieces)
        {
            throw new StockAllocationException(StockAllocationFailure.Unchanged);
        }

        if (pieces is { } held)
        {
            // KLN-D119: the consumed pieces are kept exactly, like the consumed kilograms.
            var consumedPieces = AllocatedPieces!.Value.Value - RemainingPieces!.Value.Value;
            AllocatedPieces = PieceCount.Of(checked(consumedPieces + held.Value));
            RemainingPieces = held;
        }

        var consumed = Allocated.Value - Remaining.Value;
        var difference = remaining.Value - Remaining.Value;
        Allocated = Kilograms.Positive(consumed + remaining.Value);
        Remaining = remaining;
        Version = checked(Version + 1);
        return difference;
    }

    /// <summary>
    /// Releases every unconsumed kilogram. Releasing is not a stock movement: the physical balance stays
    /// unchanged and the released quantity becomes free again when active allocations are deducted.
    /// </summary>
    public Kilograms Release(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new StockAllocationException(StockAllocationFailure.VersionConflict);
        }

        if (Status != StockAllocationStatus.Active)
        {
            throw new StockAllocationException(StockAllocationFailure.StateConflict);
        }

        var released = Remaining;
        Remaining = Kilograms.Zero;
        // KLN-D119: the released pieces are free on the label again, exactly like the kilograms.
        if (TracksPieces)
        {
            RemainingPieces = PieceCount.Zero;
        }

        Version = checked(Version + 1);
        Status = StockAllocationStatus.Released;
        return released;
    }
}

public enum StockAllocationFailure
{
    VersionConflict,
    StateConflict,
    LabelMismatch,
    Insufficient,

    /// <summary>A change that would leave the allocation exactly as it is.</summary>
    Unchanged,

    /// <summary>KLN-D119: the position or allocation holds pieces and the request does not say how many (fail-closed).</summary>
    PiecesRequired,

    /// <summary>KLN-D119: a piece count on a kg-only position or allocation (no backfill).</summary>
    PiecesNotTracked,

    /// <summary>KLN-D119: more pieces than the position has unreserved, or than the allocation still holds.</summary>
    PiecesInsufficient,

    /// <summary>KLN-D119: a count below one, or pieces and kilograms that would not run out together.</summary>
    PiecesInvalid,
}

public sealed class StockAllocationException(StockAllocationFailure failure) : Exception(failure.ToString())
{
    public StockAllocationFailure Failure { get; } = failure;
}

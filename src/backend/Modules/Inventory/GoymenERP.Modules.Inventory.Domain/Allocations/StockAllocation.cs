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
        long version, StockAllocationStatus status)
    {
        if (id == Guid.Empty || version < 1 || !Enum.IsDefined(status)
            || allocated.Value <= 0m || remaining.Value < 0m || remaining.Value > allocated.Value
            || status == StockAllocationStatus.Active && remaining.Value == 0m
            || status is StockAllocationStatus.Consumed or StockAllocationStatus.Released && remaining.Value != 0m)
        {
            throw new ArgumentException("Stored stock allocation is inconsistent.");
        }

        Id = id;
        Position = position ?? throw new ArgumentNullException(nameof(position));
        Allocated = allocated;
        Remaining = remaining;
        Version = version;
        Status = status;
    }

    public Guid Id { get; }

    public Guid CompanyId => Position.CompanyId;

    public Guid LabelId => Position.LabelId;

    public StockPosition Position { get; }

    public Kilograms Allocated { get; private set; }

    public Kilograms Remaining { get; private set; }

    public long Version { get; private set; }

    public StockAllocationStatus Status { get; private set; }

    public static StockAllocation Restore(Guid id, StockPosition position, Kilograms allocated,
        Kilograms remaining, long version, StockAllocationStatus status) =>
        new(id, position, allocated, remaining, version, status);

    /// <summary>Creates one allocation over an exact free physical position.</summary>
    public static StockAllocation Create(Guid id, StockPosition position, Kilograms quantity) =>
        new(id, position, quantity, quantity, 1, StockAllocationStatus.Active);

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

    /// <summary>Consumes exactly the supplied kg; partiality is chosen by the owner request, not here.</summary>
    public void Consume(long expectedVersion, Guid labelId, Kilograms quantity)
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
    /// Returns the signed difference (new − old).
    /// </summary>
    public decimal Resize(long expectedVersion, Kilograms remaining)
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

        if (remaining.Value == Remaining.Value)
        {
            throw new StockAllocationException(StockAllocationFailure.Unchanged);
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
}

public sealed class StockAllocationException(StockAllocationFailure failure) : Exception(failure.ToString())
{
    public StockAllocationFailure Failure { get; } = failure;
}

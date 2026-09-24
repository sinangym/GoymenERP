using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Domain.Allocations;

/// <summary>The lifecycle stored for one physical-stock allocation.</summary>
public enum StockAllocationStatus
{
    Active = 1,
    Consumed = 2,
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
            || status == StockAllocationStatus.Consumed && remaining.Value != 0m)
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

    public Kilograms Allocated { get; }

    public Kilograms Remaining { get; private set; }

    public long Version { get; private set; }

    public StockAllocationStatus Status { get; private set; }

    public static StockAllocation Restore(Guid id, StockPosition position, Kilograms allocated,
        Kilograms remaining, long version, StockAllocationStatus status) =>
        new(id, position, allocated, remaining, version, status);

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
}

public enum StockAllocationFailure
{
    VersionConflict,
    StateConflict,
    LabelMismatch,
    Insufficient,
}

public sealed class StockAllocationException(StockAllocationFailure failure) : Exception(failure.ToString())
{
    public StockAllocationFailure Failure { get; } = failure;
}

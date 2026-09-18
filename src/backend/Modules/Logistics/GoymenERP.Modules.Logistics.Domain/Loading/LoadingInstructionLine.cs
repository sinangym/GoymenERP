using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Modules.Logistics.Domain.Loading;

/// <summary>
/// One product line of a loading instruction with its four separate shares (§7.4): instructed,
/// loaded, shipped, delivered. Each later share is a sub-share of the one before it, so the
/// differences (remaining to load, loaded-not-shipped, shipped-not-delivered) are always
/// non-negative and nothing is counted twice.
/// </summary>
public sealed class LoadingInstructionLine
{
    internal LoadingInstructionLine(Guid id, Guid productId, Guid? allocationId, Quantity instructed)
    {
        Id = id;
        ProductId = productId;
        AllocationId = allocationId;
        InstructedQuantity = instructed;
        LoadedQuantity = Quantity.Zero(instructed.Unit);
        ShippedQuantity = Quantity.Zero(instructed.Unit);
        DeliveredQuantity = Quantity.Zero(instructed.Unit);
    }

    public Guid Id { get; }
    public Guid ProductId { get; }

    /// <summary>The Inventory allocation this loading draws from, identity only; null when loading from free stock.</summary>
    public Guid? AllocationId { get; }

    public Quantity InstructedQuantity { get; }
    public Quantity LoadedQuantity { get; private set; }
    public Quantity ShippedQuantity { get; private set; }
    public Quantity DeliveredQuantity { get; private set; }
    public bool IsBlocked { get; private set; }

    public Quantity RemainingToLoad => InstructedQuantity.Subtract(LoadedQuantity);

    /// <summary>Loaded but not yet definitely shipped (§13: the 200 kg that stays visible).</summary>
    public Quantity LoadedNotShipped => LoadedQuantity.Subtract(ShippedQuantity);

    public Quantity ShippedNotDelivered => ShippedQuantity.Subtract(DeliveredQuantity);

    internal void SetBlocked(bool blocked) => IsBlocked = blocked;

    internal void AddLoaded(Quantity loaded)
    {
        var next = LoadedQuantity.Add(loaded);
        if (next.IsGreaterThan(InstructedQuantity))
        {
            throw new InvalidOperationException("Yüklenen pay talimatı aşamaz.");
        }

        LoadedQuantity = next;
    }

    internal void AddShipped(Quantity shipped)
    {
        var next = ShippedQuantity.Add(shipped);
        if (next.IsGreaterThan(LoadedQuantity))
        {
            throw new InvalidOperationException("Sevk edilen pay yüklenen payı aşamaz.");
        }

        ShippedQuantity = next;
    }

    internal void AddDelivered(Quantity delivered)
    {
        var next = DeliveredQuantity.Add(delivered);
        if (next.IsGreaterThan(ShippedQuantity))
        {
            throw new InvalidOperationException("Teslim edilen pay sevk edilen payı aşamaz.");
        }

        DeliveredQuantity = next;
    }
}

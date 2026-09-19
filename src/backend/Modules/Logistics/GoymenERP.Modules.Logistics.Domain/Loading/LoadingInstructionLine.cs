using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Modules.Logistics.Domain.Loading;

/// <summary>
/// One product line of a loading instruction with its four separate shares (§7.4): instructed,
/// loaded, shipped, delivered. Each later share is a sub-share of the one before it, so the
/// differences (remaining to load, loaded-not-shipped, shipped-not-delivered) are always
/// non-negative and nothing is counted twice.
/// <para>
/// Every share bound is decided by <see cref="LoadingInstruction"/>; the checks in this class are a
/// second, defensive guard and report <see cref="LogisticsRuleCodes.InternalInvariant"/> so they
/// are never mistaken for the aggregate's rule.
/// </para>
/// </summary>
public sealed class LoadingInstructionLine
{
    internal LoadingInstructionLine(Guid id, SourceOrderLineRef source, Guid productId, Guid unitId, Guid? allocationId, Quantity instructed)
    {
        Id = id;
        Source = source;
        ProductId = productId;
        UnitId = unitId;
        AllocationId = allocationId;
        InstructedQuantity = instructed;
        LoadedQuantity = Quantity.Zero(instructed.Unit);
        ShippedQuantity = Quantity.Zero(instructed.Unit);
        DeliveredQuantity = Quantity.Zero(instructed.Unit);
    }

    public Guid Id { get; }

    /// <summary>The sales order line this loading serves: order, line and revision (Sales identities only; §5 source identity; LOG-4 D4).</summary>
    public SourceOrderLineRef Source { get; }

    /// <summary>The sales order line's id (the binding's line member, kept for readers of LOG-1..3).</summary>
    public Guid SourceOrderLineId => Source.LineId;

    public Guid ProductId { get; }

    /// <summary>The Settings unit this line is in (identity only).</summary>
    public Guid UnitId { get; }

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
            throw new LogisticsRuleException(LogisticsRuleCodes.InternalInvariant, "İç tutarsızlık: yüklenen pay talimatı aşıyor.");
        }

        LoadedQuantity = next;
    }

    internal void AddShipped(Quantity shipped)
    {
        var next = ShippedQuantity.Add(shipped);
        if (next.IsGreaterThan(LoadedQuantity))
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.InternalInvariant, "İç tutarsızlık: sevk edilen pay yüklenen payı aşıyor.");
        }

        ShippedQuantity = next;
    }

    internal void AddDelivered(Quantity delivered)
    {
        var next = DeliveredQuantity.Add(delivered);
        if (next.IsGreaterThan(ShippedQuantity))
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.InternalInvariant, "İç tutarsızlık: teslim edilen pay sevk edilen payı aşıyor.");
        }

        DeliveredQuantity = next;
    }
}

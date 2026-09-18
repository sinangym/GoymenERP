using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Domain.Balances;

/// <summary>One bucket's current physical quantity.</summary>
public sealed record StockBalance(StockPosition Position, Kilograms Quantity);

/// <summary>
/// Stock balances kept together with the movements that produced them (§7.1: the movement
/// is the physical trace; the balance is the fast read and the race guard, and it must
/// reconcile with the movements).
///
/// Rules held here:
/// <list type="bullet">
/// <item>No physical balance ever goes below zero. A movement that would do so is refused
/// WHOLE: its lines are checked together first, so nothing is partly applied.</item>
/// <item>The same movement is applied at most once.</item>
/// <item>Balances are only ever changed by applying a movement; there is no "set quantity".</item>
/// <item><see cref="Reconcile"/> compares stored balances with the movements, bucket by
/// bucket.</item>
/// </list>
/// This is the in-memory domain rule. The persistent race guard (SQL rowversion/lock) is
/// INV-2's work and is not claimed here.
/// </summary>
public sealed class StockLedger
{
    private readonly Dictionary<StockPosition, decimal> balances = [];
    private readonly List<StockMovement> applied = [];
    private readonly HashSet<Guid> appliedIds = [];

    public IReadOnlyList<StockMovement> AppliedMovements => applied.AsReadOnly();

    /// <summary>Every non-zero bucket.</summary>
    public IReadOnlyList<StockBalance> Balances =>
        [.. balances.Where(entry => entry.Value != 0m).Select(entry => new StockBalance(entry.Key, Kilograms.Of(entry.Value)))];

    public Kilograms QuantityAt(StockPosition position) =>
        balances.TryGetValue(position, out var quantity) ? Kilograms.Of(quantity) : Kilograms.Zero;

    /// <summary>All physical stock of a product in a company, blocked or not.</summary>
    public Kilograms PhysicalTotal(Guid companyId, Guid productId) =>
        Kilograms.Of(balances
            .Where(entry => entry.Key.CompanyId == companyId && entry.Key.ProductId == productId)
            .Sum(entry => entry.Value));

    /// <summary>The free part only: blocked stock is physically there but not free.</summary>
    public Kilograms FreeTotal(Guid companyId, Guid productId) =>
        Kilograms.Of(balances
            .Where(entry => entry.Key.CompanyId == companyId && entry.Key.ProductId == productId
                && entry.Key.Block == BlockState.Free)
            .Sum(entry => entry.Value));

    public void Apply(StockMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        if (appliedIds.Contains(movement.Id))
        {
            throw new InventoryRuleException(
                InventoryRuleCodes.DuplicateMovement, "Aynı hareket ikinci kez uygulanamaz.");
        }

        // Check every line against the resulting balance BEFORE changing anything: a movement
        // either applies whole or not at all.
        var next = new Dictionary<StockPosition, decimal>();
        foreach (var line in movement.Lines)
        {
            var current = next.TryGetValue(line.Position, out var pending)
                ? pending
                : balances.GetValueOrDefault(line.Position);
            var delta = line.Direction == StockDirection.In ? line.Quantity.Value : -line.Quantity.Value;
            var result = current + delta;
            if (result < 0m)
            {
                throw new InventoryRuleException(
                    InventoryRuleCodes.NegativePhysicalStock,
                    "Hareket fiziksel stoğu sıfırın altına düşürür; negatif stok yoktur.");
            }

            next[line.Position] = result;
        }

        foreach (var (position, quantity) in next)
        {
            balances[position] = quantity;
        }

        appliedIds.Add(movement.Id);
        applied.Add(movement);
    }

    /// <summary>
    /// Compare stored balances with what the movements add up to (§7.1: the balance must
    /// reconcile with the physical trace). This is what a persistent store runs to prove a
    /// stored balance was never written except through its movements. Empty means they agree
    /// exactly; otherwise every disagreeing bucket is listed with both values — a bucket
    /// missing on either side counts as zero there.
    /// </summary>
    public static IReadOnlyList<StockReconciliationDifference> Reconcile(
        IEnumerable<StockBalance> storedBalances, IEnumerable<StockMovement> movements)
    {
        ArgumentNullException.ThrowIfNull(storedBalances);
        ArgumentNullException.ThrowIfNull(movements);
        var stored = new Dictionary<StockPosition, decimal>();
        foreach (var balance in storedBalances)
        {
            stored[balance.Position] = stored.GetValueOrDefault(balance.Position) + balance.Quantity.Value;
        }

        var rebuilt = new Dictionary<StockPosition, decimal>();
        foreach (var line in movements.SelectMany(movement => movement.Lines))
        {
            var delta = line.Direction == StockDirection.In ? line.Quantity.Value : -line.Quantity.Value;
            rebuilt[line.Position] = rebuilt.GetValueOrDefault(line.Position) + delta;
        }

        return [.. stored.Keys.Union(rebuilt.Keys)
            .Select(position => new StockReconciliationDifference(
                position, stored.GetValueOrDefault(position), rebuilt.GetValueOrDefault(position)))
            .Where(difference => difference.Balance != difference.FromMovements)];
    }
}

/// <summary>A bucket whose balance does not match what its movements add up to.</summary>
public sealed record StockReconciliationDifference(StockPosition Position, decimal Balance, decimal FromMovements);

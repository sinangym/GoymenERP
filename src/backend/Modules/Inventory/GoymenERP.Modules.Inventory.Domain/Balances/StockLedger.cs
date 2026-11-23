using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Domain.Balances;

/// <summary>
/// One bucket's current physical quantity. <paramref name="Pieces"/> (KLN-D119, AK-2) is the bucket's piece count on a
/// piece-tracking label and null on a label without one.
/// </summary>
public sealed record StockBalance(StockPosition Position, Kilograms Quantity, PieceCount? Pieces = null);

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
/// <item>One source (or source line) causes at most one movement of each kind, and a source is
/// answered either whole or line by line for a kind, never both (ORG #281 P3-1, #293 ORG P3-1).</item>
/// <item>A label is one physical piece: at any time all of its non-zero buckets share one
/// company, owner, custodian, location and product; only the block state may differ (a
/// partly blocked coil). Moving it is a transfer (T13), a second piece is a split (T13).</item>
/// <item>A receipt never names a label that already holds stock: a piece arrives once (ORG #281
/// P3-1). Once all its stock has left, it may be received again.</item>
/// <item>Balances are only ever changed by applying a movement; there is no "set quantity".
/// <see cref="FromStoredBalances"/> seeds a ledger from balances that were themselves written
/// only through movements, so a store can apply the next movement against them.</item>
/// <item><see cref="Reconcile"/> compares stored balances with the movements, bucket by
/// bucket.</item>
/// <item>KLN-D119 (AK-2): a label either tracks pieces in every stocked bucket or in none. A movement line on a
/// piece-tracking label must carry its pieces, a piece-less label holding stock never receives one (no backfill), a
/// piece count never goes below zero, and a piece-tracking bucket's pieces and kilograms are both zero or both
/// positive. An empty label may be stocked again with or without pieces.</item>
/// </list>
/// This is the in-memory domain rule. The persistent race guard is the store's (INV-2: a
/// concurrency token on every touched label, and SQL unique indexes).
/// </summary>
public sealed class StockLedger
{
    private readonly Dictionary<StockPosition, decimal> balances = [];
    private readonly Dictionary<StockPosition, int> pieces = [];
    private readonly List<StockMovement> applied = [];
    private readonly HashSet<Guid> appliedIds = [];

    public IReadOnlyList<StockMovement> AppliedMovements => applied.AsReadOnly();

    /// <summary>
    /// A ledger holding exactly these stored balances and no applied movements. The balances
    /// must themselves be a valid state: one bucket per position, and every label in one
    /// physical place (a <see cref="Kilograms"/> value is never negative).
    /// </summary>
    public static StockLedger FromStoredBalances(IEnumerable<StockBalance> storedBalances)
    {
        ArgumentNullException.ThrowIfNull(storedBalances);
        var ledger = new StockLedger();
        foreach (var balance in storedBalances)
        {
            ArgumentNullException.ThrowIfNull(balance);
            if (!ledger.balances.TryAdd(balance.Position, balance.Quantity.Value))
            {
                throw new InventoryRuleException(
                    InventoryRuleCodes.InvalidMovement, "Aynı pozisyon için iki kayıtlı bakiye olamaz.");
            }

            if (balance.Pieces is { } count)
            {
                ledger.pieces[balance.Position] = count.Value;
            }
        }

        foreach (var label in ledger.balances.Where(entry => entry.Value != 0m).GroupBy(entry => entry.Key.LabelId))
        {
            var tracked = label.Count(entry => ledger.pieces.ContainsKey(entry.Key));
            if (tracked != 0 && tracked != label.Count())
            {
                throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount,
                    "Kayıtlı bakiyelerde bir etiket bazı kovalarda adet taşıyor, bazılarında taşımıyor.");
            }
        }

        if (ledger.pieces.Any(entry => (entry.Value == 0) != (ledger.balances[entry.Key] == 0m)))
        {
            throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount,
                "Kayıtlı bakiyede adet ile kilogram birlikte sıfır ya da birlikte pozitif olmalıdır.");
        }

        var split = ledger.balances.Where(entry => entry.Value != 0m)
            .GroupBy(entry => entry.Key.LabelId)
            .Any(label => label.Select(entry => entry.Key.PhysicalPlace).Distinct().Count() > 1);
        if (split)
        {
            throw new InventoryRuleException(
                InventoryRuleCodes.LabelInTwoPositions, "Kayıtlı bakiyelerde bir etiket iki fiziksel yerde görünüyor.");
        }

        return ledger;
    }

    /// <summary>Every non-zero bucket.</summary>
    public IReadOnlyList<StockBalance> Balances =>
        [.. balances.Where(entry => entry.Value != 0m)
            .Select(entry => new StockBalance(entry.Key, Kilograms.Of(entry.Value), PiecesAt(entry.Key)))];

    public Kilograms QuantityAt(StockPosition position) =>
        balances.TryGetValue(position, out var quantity) ? Kilograms.Of(quantity) : Kilograms.Zero;

    /// <summary>KLN-D119: the bucket's piece count, or null when its label does not track pieces.</summary>
    public PieceCount? PiecesAt(StockPosition position) =>
        pieces.TryGetValue(position, out var count) ? PieceCount.Of(count) : null;

    /// <summary>KLN-D119: whether the label holds stock with a piece count.</summary>
    public bool TracksPieces(Guid labelId) =>
        balances.Any(entry => entry.Key.LabelId == labelId && entry.Value != 0m && pieces.ContainsKey(entry.Key));

    /// <summary>
    /// All physical stock of a product in a company for ONE owner, blocked or not. The owner
    /// is required: a customer's goods kept by us are never part of our own stock (§5, §7.1).
    /// </summary>
    public Kilograms PhysicalTotal(Guid companyId, StockOwner owner, Guid productId) =>
        Kilograms.Of(balances
            .Where(entry => entry.Key.CompanyId == companyId && entry.Key.Owner == owner
                && entry.Key.ProductId == productId)
            .Sum(entry => entry.Value));

    /// <summary>The free part only, for one owner: blocked stock is physically there but not free.</summary>
    public Kilograms FreeTotal(Guid companyId, StockOwner owner, Guid productId) =>
        Kilograms.Of(balances
            .Where(entry => entry.Key.CompanyId == companyId && entry.Key.Owner == owner
                && entry.Key.ProductId == productId && entry.Key.Block == BlockState.Free)
            .Sum(entry => entry.Value));

    public void Apply(StockMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        if (appliedIds.Contains(movement.Id))
        {
            throw new InventoryRuleException(
                InventoryRuleCodes.DuplicateMovement, "Aynı hareket ikinci kez uygulanamaz.");
        }

        RequireSourceUnused(movement);

        // Check every line against the resulting balance BEFORE changing anything: a movement
        // either applies whole or not at all.
        var next = new Dictionary<StockPosition, decimal>();
        for (var lineIndex = 0; lineIndex < movement.Lines.Count; lineIndex++)
        {
            var line = movement.Lines[lineIndex];
            var current = next.TryGetValue(line.Position, out var pending)
                ? pending
                : balances.GetValueOrDefault(line.Position);
            var delta = line.Direction == StockDirection.In ? line.Quantity.Value : -line.Quantity.Value;
            var result = current + delta;
            if (result < 0m)
            {
                // SAT-D318: the refusal names the failing line and what its bucket held just before it.
                throw new InventoryRuleException(
                    new StockShortfall(movement.Kind, movement.Source, line.Position, line.Quantity,
                        Kilograms.Of(current), lineIndex + 1),
                    "Hareket fiziksel stoğu sıfırın altına düşürür; negatif stok yoktur.");
            }

            next[line.Position] = result;
        }

        RequireOnePhysicalPlacePerLabel(next);
        if (movement.Kind == StockMovementKind.Receipt)
        {
            RequireNoStockBeforeReceipt(movement);
        }

        var (nextPieces, untracked) = NextPieces(movement, next);

        foreach (var (position, quantity) in next)
        {
            balances[position] = quantity;
        }

        foreach (var position in untracked)
        {
            pieces.Remove(position);
        }

        foreach (var (position, count) in nextPieces)
        {
            pieces[position] = count;
        }

        appliedIds.Add(movement.Id);
        applied.Add(movement);
    }

    /// <summary>
    /// A source answers a kind once per line, or once whole; never both. Checked against the
    /// movements this ledger applied; the store holds the same rule against stored movements.
    /// </summary>
    private void RequireSourceUnused(StockMovement movement)
    {
        foreach (var earlier in applied.Where(candidate => candidate.Kind == movement.Kind
            && candidate.Source.IsSameSource(movement.Source)))
        {
            if (earlier.Source.Grain != movement.Source.Grain)
            {
                throw new InventoryRuleException(
                    InventoryRuleCodes.SourceGrainMismatch,
                    "Bu kaynak bu türde başka ayrıntı düzeyinde (bütün/satır) karşılandı; aynı miktar iki kez sayılmaz.");
            }

            if (earlier.Source.LineId == movement.Source.LineId)
            {
                throw new InventoryRuleException(
                    InventoryRuleCodes.DuplicateSource, "Bu kaynak bu türde zaten bir hareket doğurdu.");
            }
        }
    }

    /// <summary>
    /// KLN-D119: the piece counts after the movement, checked before anything changes. Returns the new count of every touched
    /// bucket of a piece-tracking label and the buckets whose stale count is dropped (an empty label stocked again without
    /// pieces).
    /// </summary>
    private (Dictionary<StockPosition, int> Next, List<StockPosition> Untracked) NextPieces(StockMovement movement,
        IReadOnlyDictionary<StockPosition, decimal> nextKilograms)
    {
        var next = new Dictionary<StockPosition, int>();
        var untracked = new List<StockPosition>();
        foreach (var label in movement.Lines.GroupBy(line => line.Position.LabelId))
        {
            var withPieces = label.Count(line => line.Pieces is not null);
            if (withPieces != 0 && withPieces != label.Count())
            {
                throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount,
                    "Bir hareketteki aynı etiketin satırları adedi birlikte taşır ya da hiçbiri taşımaz.");
            }

            var holdsStock = balances.Any(entry => entry.Key.LabelId == label.Key && entry.Value != 0m);
            var tracked = TracksPieces(label.Key);
            if (tracked && withPieces == 0)
            {
                throw new InventoryRuleException(InventoryRuleCodes.PieceCountRequired,
                    "Etiket adet taşıyor; hareketin adedi bilinmeden kayıt yapılmaz.");
            }

            if (holdsStock && !tracked && withPieces != 0)
            {
                throw new InventoryRuleException(InventoryRuleCodes.PieceCountNotTracked,
                    "Adetsiz kayıtlı etikete sonradan adet eklenmez.");
            }

            if (withPieces == 0)
            {
                untracked.AddRange(pieces.Keys.Where(position => position.LabelId == label.Key));
                continue;
            }

            foreach (var line in label)
            {
                var current = next.TryGetValue(line.Position, out var pending)
                    ? pending
                    : holdsStock ? pieces.GetValueOrDefault(line.Position) : 0;
                var delta = line.Direction == StockDirection.In ? line.Pieces!.Value.Value : -line.Pieces!.Value.Value;
                var result = checked(current + delta);
                if (result < 0)
                {
                    throw new InventoryRuleException(InventoryRuleCodes.NegativePieceCount,
                        "Hareket etiketin adedini sıfırın altına düşürür; negatif adet yoktur.");
                }

                next[line.Position] = result;
            }

            if (!holdsStock)
            {
                // A stale count of an emptied label never mixes into its new stock.
                untracked.AddRange(pieces.Keys.Where(position => position.LabelId == label.Key && !next.ContainsKey(position)));
            }
        }

        foreach (var (position, count) in next)
        {
            if ((count == 0) != (nextKilograms[position] == 0m))
            {
                throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount,
                    "Adetli etikette adet ile kilogram birlikte sıfıra iner ya da birlikte pozitif kalır.");
            }
        }

        return (next, untracked);
    }

    /// <summary>A received label must hold no stock before the receipt, in any bucket.</summary>
    private void RequireNoStockBeforeReceipt(StockMovement movement)
    {
        foreach (var labelId in movement.Lines.Select(line => line.Position.LabelId).Distinct())
        {
            if (balances.Any(entry => entry.Key.LabelId == labelId && entry.Value != 0m))
            {
                throw new InventoryRuleException(
                    InventoryRuleCodes.LabelAlreadyInStock,
                    "Stok taşıyan bir etiket yeniden giriş alamaz; bir parça bir kez gelir.");
            }
        }
    }

    /// <summary>
    /// After the movement, every touched label may hold stock in one physical place only; its
    /// buckets may differ by block state alone. Checked before anything changes.
    /// </summary>
    private void RequireOnePhysicalPlacePerLabel(IReadOnlyDictionary<StockPosition, decimal> next)
    {
        foreach (var labelId in next.Keys.Select(position => position.LabelId).Distinct())
        {
            var places = balances
                .Where(entry => entry.Key.LabelId == labelId && !next.ContainsKey(entry.Key))
                .Concat(next.Where(entry => entry.Key.LabelId == labelId))
                .Where(entry => entry.Value != 0m)
                .Select(entry => entry.Key.PhysicalPlace)
                .Distinct()
                .Count();
            if (places > 1)
            {
                throw new InventoryRuleException(
                    InventoryRuleCodes.LabelInTwoPositions,
                    "Bir etiket aynı anda tek fiziksel yerde bulunur; yer değişimi transfer, ikinci parça bölme işidir.");
            }
        }
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
        var seen = new HashSet<Guid>();
        var trace = movements.ToArray();
        if (trace.Any(movement => movement is null || !seen.Add(movement.Id)))
        {
            // A movement read twice would double its effect and look like a missing balance.
            throw new InventoryRuleException(
                InventoryRuleCodes.DuplicateMovement, "Uzlaştırmaya aynı hareket iki kez verilemez.");
        }

        foreach (var line in trace.SelectMany(movement => movement.Lines))
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

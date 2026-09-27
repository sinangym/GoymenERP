using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Domain.Counts;

/// <summary>
/// ST-05 / AT-037: one physical count of one stocked piece at its one position (DEC-STK-007).
/// <list type="bullet">
/// <item>The book quantity is the position's stored balance the counter saw; the difference is
/// <c>counted − book</c>, signed, exact to the kilogram scale (Ö2).</item>
/// <item>A difference always carries a reason (DEC-STK-007). The reason is free text: a closed reason list is
/// not decided (README ST-05, blocker INV-R21-REASON-LIST-01).</item>
/// <item>A count changes no stock. Only its approved <see cref="Adjustment"/> does, as one new
/// <see cref="StockMovementKind.CountAdjustment"/> movement whose source is the count; the count and every earlier
/// movement stay as they are (history is never deleted).</item>
/// </list>
/// Who may approve, and whether a small difference may be self-approved, is not decided here (INV-R20-R21-01 Q2);
/// the store holds the fail-closed rule.
/// </summary>
public sealed class StockCount
{
    /// <summary>The source kind of the adjustment movement: the count document is its source.</summary>
    public const string SourceKind = "inventory.stock-count";

    public const int MaximumReasonLength = 1000;

    public StockCount(Guid id, Guid companyId, StockPosition position, Kilograms book, Kilograms counted,
        DateTimeOffset countedAtUtc, string reason)
    {
        Id = id == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Sayım kimliği zorunludur.")
            : id;
        CompanyId = companyId == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "İşlem şirketi zorunludur.")
            : companyId;
        ArgumentNullException.ThrowIfNull(position);
        if (position.CompanyId != companyId)
        {
            throw Invalid("Sayılan pozisyon sayımın şirketinde olmalıdır.");
        }

        if (countedAtUtc.Offset != TimeSpan.Zero)
        {
            throw Invalid("Sayım zamanı UTC olmalıdır.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaximumReasonLength)
        {
            throw Invalid($"Sayım gerekçesi zorunludur (en fazla {MaximumReasonLength} karakter).");
        }

        Position = position;
        Book = book;
        Counted = counted;
        CountedAtUtc = countedAtUtc;
        Reason = reason.Trim();
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    /// <summary>The counted position: owner, custodian, location, label, product and block state as stored.</summary>
    public StockPosition Position { get; }

    public Kilograms Book { get; }

    public Kilograms Counted { get; }

    public DateTimeOffset CountedAtUtc { get; }

    public string Reason { get; }

    /// <summary><c>counted − book</c>: positive is a surplus, negative a shortage, zero no difference.</summary>
    public decimal Difference => Counted.Value - Book.Value;

    public bool HasDifference => Difference != 0m;

    /// <summary>
    /// The one movement that posts this count's difference: a single line on the counted position, In for a
    /// surplus and Out for a shortage, of exactly the difference's size. A count without a difference has none.
    /// </summary>
    public StockMovement Adjustment(Guid movementId, DateTimeOffset occurredAtUtc)
    {
        if (!HasDifference)
        {
            throw Invalid("Farkı olmayan sayımın düzeltme hareketi yoktur.");
        }

        var direction = Difference > 0m ? StockDirection.In : StockDirection.Out;
        return new StockMovement(movementId, CompanyId, StockMovementKind.CountAdjustment, occurredAtUtc,
            new StockSourceReference(SourceKind, Id),
            [new StockMovementLine(Position, direction, Kilograms.Positive(Math.Abs(Difference)))]);
    }

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidStockCount, message);
}

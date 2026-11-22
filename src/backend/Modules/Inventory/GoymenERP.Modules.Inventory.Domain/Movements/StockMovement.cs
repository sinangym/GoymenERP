using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Domain.Movements;

/// <summary>The physical movement kinds currently recorded by Inventory.</summary>
public enum StockMovementKind
{
    /// <summary>Physical arrival: every line adds stock.</summary>
    Receipt = 1,

    /// <summary>
    /// Physical departure: every line removes FREE stock. Blocked stock cannot leave until it
    /// is released (§7.1: its realisability is blocked); scrapping blocked material is a
    /// movement kind of its own (T13).
    /// </summary>
    Issue = 2,

    /// <summary>Free → Blocked on the same position. Physical total unchanged.</summary>
    Block = 3,

    /// <summary>Blocked → Free on the same position. Physical total unchanged.</summary>
    Release = 4,

    /// <summary>
    /// Unused production input returned to the same existing label and physical position.
    /// Unlike a receipt, this deliberately adds stock to an already registered piece.
    /// </summary>
    Return = 5,

    /// <summary>
    /// ST-05 / AT-037: the approved difference of one stock count (DEC-STK-007), posted on the counted
    /// position of an already registered piece. Exactly one line: In for a surplus, Out for a shortage,
    /// free or blocked alike (a count is physical). The count it answers stays as the history; a wrong
    /// adjustment is corrected by a new count, never by editing this one.
    /// </summary>
    CountAdjustment = 6,

    /// <summary>
    /// ST-02 scrap write-off (hurda/fire çıkışı): the approved scrap of part or all of one stocked piece, posted on
    /// its one stored position. Exactly one Out line, free or blocked alike: this is the movement kind of its own
    /// that scrapping blocked material needs (T13), so a blocked piece leaves without first being released. The
    /// scrap document it answers stays as the history; a wrong write-off is corrected by a new count, never by
    /// editing this one.
    /// </summary>
    Scrap = 7,

    /// <summary>
    /// DEC-STK-008 first leg: every line removes one dispatched whole-label bucket from its stored physical place.
    /// Free and Blocked buckets are both preserved by the transfer aggregate; this is relocation, not fulfilment.
    /// </summary>
    TransferDispatch = 8,

    /// <summary>
    /// DEC-STK-008 second leg: every line restores one dispatched whole-label bucket at the accepted destination,
    /// with the same owner, product, label, block and kilograms and the new stored location/custodian.
    /// </summary>
    TransferAccept = 9,

    /// <summary>
    /// F-009 D1 (URT-D28/URT-D31): Manufacturing's output reversal (çıktı ters hareketi). Exactly one Out line from
    /// the Free bucket of the position the reversed output receipt first stocked, never more than that position's
    /// free unallocated kg. The original <see cref="Receipt"/> stays as the history; this is the new, linked
    /// movement that corrects it (T13), with the source kind <c>manufacturing.output-reversal</c>. Blocked kg
    /// cannot be reversed while blocked: the block is lifted by Inventory's own command first.
    /// </summary>
    ManufacturingOutputReversal = 10,

    /// <summary>
    /// KLN-D20 (B-22b): the reverse of a whole <see cref="TransferDispatch"/> before any acceptance. Every line is an
    /// In line that restores one dispatched bucket at its exact source position; the dispatch movement stays as history.
    /// </summary>
    TransferDispatchCancellation = 11,

    /// <summary>
    /// KLN-D21 (B-22c): the automatic fire of a transfer receipt shortage. Exactly one Out line, free or blocked alike,
    /// on the destination bucket the same receipt's <see cref="TransferAccept"/> just stocked; the missing kilograms
    /// are the dispatched kilograms minus the receiver's entered kilograms. It is not the two-step <see cref="Scrap"/>.
    /// </summary>
    TransferReceiptShortage = 12,

    /// <summary>
    /// KLN-D24 (B-22c): the approved excess of a transfer receipt. Exactly one In line on the received destination
    /// bucket, written only after a person other than the receiver approved it.
    /// </summary>
    TransferReceiptOverage = 13,

    /// <summary>
    /// KLN-D23 (c) (B-22c): the approved transit loss of one pending transfer label. Its kilograms already left the
    /// source at dispatch, so every line pair is an In and then an Out of the same kilograms on the same source
    /// position: the loss is recorded as a movement fact without changing any balance.
    /// </summary>
    TransferTransitLoss = 14,

    /// <summary>
    /// B-19 / SAT-D306: the exit of a supplier return. Exactly one Out line, free or blocked alike, on the position the
    /// original goods receipt stocked; it is traced to that receipt line and never exceeds its received kilograms.
    /// </summary>
    SupplierReturnExit = 15,

    /// <summary>ALS-D86: a linked physical undo of one original Purchasing acceptance; the original receipt remains immutable.</summary>
    PurchaseAcceptanceReversal = 16,
}

public enum StockDirection
{
    In = 1,
    Out = 2,
}

/// <summary>
/// Whether a movement answers a whole source or one of its lines. For one source and one movement
/// kind the first movement fixes it: a source answered whole never also gets line movements, and
/// the other way round, so the same quantity cannot be counted twice (#293 ORG P3-1).
/// </summary>
public enum StockSourceGrain
{
    Whole = 1,
    Line = 2,
}

/// <summary>
/// What caused the movement, by the causing module's identity (§5: one owner, one source), and
/// optionally which line of that source. One source (or source line) causes at most one movement
/// of each kind (ORG #281 P3-1), and one source is answered at one <see cref="StockSourceGrain"/>
/// per kind (#293 ORG P3-1); the ledger holds both, persistence holds them in SQL.
/// </summary>
public sealed record StockSourceReference
{
    /// <summary>The longest source kind token this module stores.</summary>
    public const int MaximumKindLength = 64;

    public StockSourceReference(string kind, Guid id, Guid? lineId = null)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Kaynak türü zorunludur.");
        }

        Kind = kind.Trim().Length <= MaximumKindLength
            ? kind.Trim()
            : throw new InventoryRuleException(InventoryRuleCodes.InvalidMovement,
                $"Kaynak türü en fazla {MaximumKindLength} karakterdir.");
        Id = id == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Kaynak kimliği zorunludur.")
            : id;
        LineId = lineId == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Kaynak satırı verildiyse boş olamaz.")
            : lineId;
    }

    /// <summary>A stable machine token of the source kind, e.g. <c>purchase.receipt</c>.</summary>
    public string Kind { get; }

    public Guid Id { get; }

    /// <summary>The source's own line, when the movement answers one line of a multi-line source.</summary>
    public Guid? LineId { get; }

    public StockSourceGrain Grain => LineId is null ? StockSourceGrain.Whole : StockSourceGrain.Line;

    /// <summary>
    /// The same source, whatever the line. The kind token compares ignoring case, as the database's
    /// default collation does in the source indexes (README, INV-3).
    /// </summary>
    public bool IsSameSource(StockSourceReference other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Id == other.Id && string.Equals(Kind, other.Kind, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>One line: a positive kilogram amount into or out of one physical position.</summary>
/// <summary>
/// One movement line. <paramref name="Pieces"/> (KLN-D119, AK-2) is the piece count that moves with the kilograms on a
/// piece-tracking label; null on a label without a piece count. The ledger refuses a null on a piece-tracking label and a
/// value on a piece-less label that holds stock.
/// </summary>
public sealed record StockMovementLine(StockPosition Position, StockDirection Direction, Kilograms Quantity,
    PieceCount? Pieces = null);

/// <summary>
/// An immutable physical trace (§7.1). Once built it cannot change: there are no setters,
/// and the line list is a private copy. A wrong movement is corrected by a new, linked
/// movement later (T13), never by editing this one.
///
/// Cost is carried as <see cref="CostResult"/>; the default is <see cref="CostResult.NotCalculated"/>,
/// never zero (R6).
/// </summary>
public sealed class StockMovement
{
    private readonly StockMovementLine[] lines;

    public StockMovement(Guid id, Guid companyId, StockMovementKind kind, DateTimeOffset occurredAtUtc,
        StockSourceReference source, IEnumerable<StockMovementLine> lines, CostResult? cost = null)
    {
        Id = id == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Hareket kimliği zorunludur.")
            : id;
        CompanyId = companyId == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "İşlem şirketi zorunludur.")
            : companyId;
        if (!Enum.IsDefined(kind))
        {
            throw Invalid("Hareket türü tanımsız.");
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw Invalid("Hareket zamanı UTC olmalıdır.");
        }

        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lines);
        Kind = kind;
        OccurredAtUtc = occurredAtUtc;
        Source = source;
        Cost = cost ?? CostResult.NotCalculated;
        this.lines = [.. lines];
        Validate();
    }

    public Guid Id { get; }

    /// <summary>The transaction company. Every line's position is recorded under it.</summary>
    public Guid CompanyId { get; }

    public StockMovementKind Kind { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public StockSourceReference Source { get; }

    public CostResult Cost { get; }

    public IReadOnlyList<StockMovementLine> Lines => Array.AsReadOnly(lines);

    private void Validate()
    {
        if (lines.Length == 0)
        {
            throw Invalid("Hareketin en az bir satırı olmalıdır.");
        }

        foreach (var line in lines)
        {
            if (line is null || line.Position is null)
            {
                throw Invalid("Hareket satırı eksik.");
            }

            if (line.Position.CompanyId != CompanyId)
            {
                throw Invalid("Bir hareketin bütün satırları hareketin işlem şirketinde olmalıdır.");
            }

            if (line.Quantity.Value <= 0m)
            {
                throw new InventoryRuleException(
                    InventoryRuleCodes.QuantityNotPositive, "Hareket miktarı sıfırdan büyük olmalıdır.");
            }
        }

        switch (Kind)
        {
            case StockMovementKind.Receipt:
                RequireAll(StockDirection.In, "Giriş hareketinin bütün satırları giriş olmalıdır.");
                break;
            case StockMovementKind.Issue:
                RequireAll(StockDirection.Out, "Çıkış hareketinin bütün satırları çıkış olmalıdır.");
                if (lines.Any(line => line.Position.Block == BlockState.Blocked))
                {
                    throw Invalid("Bloklu stok çıkış hareketiyle çıkamaz; önce blokaj kaldırılmalıdır.");
                }

                break;
            case StockMovementKind.Return:
                RequireAll(StockDirection.In, "İade hareketinin bütün satırları giriş olmalıdır.");
                break;
            case StockMovementKind.Block:
                RequireBlockPair(BlockState.Free, BlockState.Blocked);
                break;
            case StockMovementKind.Release:
                RequireBlockPair(BlockState.Blocked, BlockState.Free);
                break;
            case StockMovementKind.CountAdjustment:
                if (lines.Length != 1)
                {
                    throw Invalid("Sayım farkı hareketi tek satırdır: sayılan pozisyona giriş ya da çıkış.");
                }

                break;
            case StockMovementKind.Scrap:
                if (lines.Length != 1 || lines[0].Direction != StockDirection.Out)
                {
                    throw Invalid("Hurda hareketi tek satırdır: hurdaya ayrılan pozisyondan çıkış.");
                }

                break;
            case StockMovementKind.TransferDispatch:
                RequireAll(StockDirection.Out, "Transfer çıkışının bütün satırları çıkış olmalıdır.");
                break;
            case StockMovementKind.TransferAccept:
                RequireAll(StockDirection.In, "Transfer kabulünün bütün satırları giriş olmalıdır.");
                break;
            case StockMovementKind.TransferDispatchCancellation:
                RequireAll(StockDirection.In, "Transfer çıkış iptalinin bütün satırları giriş olmalıdır.");
                break;
            case StockMovementKind.TransferReceiptShortage:
                if (lines.Length != 1 || lines[0].Direction != StockDirection.Out)
                {
                    throw Invalid("Transfer eksik teslim firesi tek satırdır: teslim alınan pozisyondan çıkış.");
                }

                break;
            case StockMovementKind.TransferReceiptOverage:
                if (lines.Length != 1 || lines[0].Direction != StockDirection.In)
                {
                    throw Invalid("Transfer fazla teslim hareketi tek satırdır: teslim alınan pozisyona giriş.");
                }

                break;
            case StockMovementKind.TransferTransitLoss:
                RequireNetZeroPairs();
                break;
            case StockMovementKind.PurchaseAcceptanceReversal:
            case StockMovementKind.SupplierReturnExit:
                if (lines.Length != 1 || lines[0].Direction != StockDirection.Out)
                {
                    throw Invalid("Tedarikçi iadesi çıkışı tek satırdır: iade edilen pozisyondan çıkış.");
                }

                break;
            case StockMovementKind.ManufacturingOutputReversal:
                if (lines.Length != 1 || lines[0].Direction != StockDirection.Out)
                {
                    throw Invalid("Üretim çıktısı ters hareketi tek satırdır: çıktının pozisyonundan çıkış.");
                }

                if (lines[0].Position.Block == BlockState.Blocked)
                {
                    throw Invalid("Bloklu stok üretim çıktısı ters hareketiyle çıkamaz; önce blokaj kaldırılmalıdır.");
                }

                break;
        }
    }

    private void RequireAll(StockDirection direction, string message)
    {
        if (lines.Any(line => line.Direction != direction))
        {
            throw Invalid(message);
        }
    }

    /// <summary>
    /// A transit loss is In/Out pairs, each pair an In immediately followed by an Out of the same kilograms on the
    /// same position, so it records kilograms without creating, losing or relocating any balance.
    /// </summary>
    private void RequireNetZeroPairs()
    {
        if (lines.Length % 2 != 0)
        {
            throw Invalid("Transfer yolda kayıp hareketi aynı pozisyonda giriş-çıkış çiftleridir.");
        }

        for (var index = 0; index < lines.Length; index += 2)
        {
            var into = lines[index];
            var outOf = lines[index + 1];
            if (into.Direction != StockDirection.In || outOf.Direction != StockDirection.Out
                || into.Position != outOf.Position || into.Quantity != outOf.Quantity || into.Pieces != outOf.Pieces)
            {
                throw Invalid("Transfer yolda kayıp hareketi aynı pozisyonda giriş-çıkış çiftleridir.");
            }
        }
    }

    /// <summary>
    /// A block change is exactly one pair on the SAME position, only the block state moving:
    /// out of <paramref name="from"/>, into <paramref name="to"/>, the same kilograms. It can
    /// therefore never create, lose or relocate physical stock.
    /// </summary>
    private void RequireBlockPair(BlockState from, BlockState to)
    {
        var outs = lines.Where(line => line.Direction == StockDirection.Out).ToArray();
        var ins = lines.Where(line => line.Direction == StockDirection.In).ToArray();
        if (lines.Length != 2 || outs.Length != 1 || ins.Length != 1)
        {
            throw Invalid("Blokaj değişikliği tam olarak bir çıkış ve bir giriş satırıdır.");
        }

        var outLine = outs[0];
        var inLine = ins[0];
        if (outLine.Position.Block != from || inLine.Position.Block != to
            || outLine.Position.WithBlock(to) != inLine.Position
            || outLine.Quantity != inLine.Quantity || outLine.Pieces != inLine.Pieces)
        {
            throw Invalid("Blokaj değişikliği aynı fiziksel pozisyonda, aynı miktarla yalnız blokaj durumunu değiştirir.");
        }
    }

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidMovement, message);
}

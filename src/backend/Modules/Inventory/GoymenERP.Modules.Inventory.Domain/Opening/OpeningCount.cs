using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Domain.Opening;

/// <summary>
/// One counted piece of an opening count (T07, decision A, #66 5738113623 §2): its new label, the lot
/// the label belongs to (DEC-PRD-003; heat and coil null = unknown) and the kilograms counted.
/// </summary>
public sealed record OpeningCountLine(Guid Id, MaterialLot Lot, MaterialLabel Label, Kilograms Quantity);

/// <summary>
/// A company's opening stock as physically counted at one of its locations (INV-OPEN-1). The count
/// document IS the source of the opening: every line becomes exactly one receipt whose source is that
/// count line, so no opening quantity exists without a counted piece, and the book equals the count at
/// the moment of opening. It is not an adjustment: a later difference between book and a new count is
/// the count-difference process (ST-05) with its own second approval.
/// <para>
/// Kept: company, location, the counted-on date and the reference of the physical count sheet. No
/// personal name is stored (the recording actor is in the audit, ADR-009). Owner and custodian are the
/// company itself: a customer's or another party's goods are not opened here (DEC-STK-001, fail-closed).
/// Opening stock is free stock; nothing is blocked or allocated by it.
/// </para>
/// </summary>
public sealed class OpeningCount
{
    /// <summary>The source kind of an opening receipt: the count document.</summary>
    public const string SourceKind = "inventory.opening-count";

    /// <summary>The longest count-sheet reference this module stores.</summary>
    public const int MaximumSheetReferenceLength = 128;

    private readonly OpeningCountLine[] lines;

    public OpeningCount(Guid id, Guid companyId, Guid locationId, DateOnly countedOn, string sheetReference,
        IEnumerable<OpeningCountLine> lines)
    {
        Id = StockIds.Require(id, "Açılış sayımı");
        CompanyId = StockIds.Require(companyId, "Sayımın şirketi");
        LocationId = StockIds.Require(locationId, "Sayım konumu");
        if (countedOn == default)
        {
            throw Invalid("Sayım tarihi zorunludur.");
        }

        CountedOn = countedOn;
        SheetReference = RequireSheetReference(sheetReference);
        ArgumentNullException.ThrowIfNull(lines);
        this.lines = [.. lines];
        Validate();
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public Guid LocationId { get; }

    public DateOnly CountedOn { get; }

    /// <summary>The reference of the physical (paper or scanned) count sheet.</summary>
    public string SheetReference { get; }

    public IReadOnlyList<OpeningCountLine> Lines => lines;

    /// <summary>The stock position of a counted piece: this company owns and keeps it, at the counted location, free.</summary>
    public StockPosition PositionOf(OpeningCountLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var self = new StockOwner(StockPartyKind.Company, CompanyId);
        return new StockPosition(CompanyId, self, new StockCustodian(StockPartyKind.Company, CompanyId), LocationId,
            line.Label, BlockState.Free);
    }

    /// <summary>
    /// The one receipt of a line: the movement id is the line id, the source is (count, line), the time is
    /// the counted-on date (UTC midnight) and the cost is not calculated here.
    /// </summary>
    public StockMovement ReceiptOf(OpeningCountLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new StockMovement(line.Id, CompanyId, StockMovementKind.Receipt,
            new DateTimeOffset(CountedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new StockSourceReference(SourceKind, Id, line.Id),
            [new StockMovementLine(PositionOf(line), StockDirection.In, line.Quantity)]);
    }

    private void Validate()
    {
        if (lines.Length == 0)
        {
            throw Invalid("Açılış sayımı en az bir sayılmış parça içerir.");
        }

        foreach (var line in lines)
        {
            if (line is null || line.Lot is null || line.Label is null)
            {
                throw Invalid("Sayım satırı lot ve etiket içerir.");
            }

            _ = StockIds.Require(line.Id, "Sayım satırı");
            if (line.Quantity.Value <= 0m)
            {
                throw new InventoryRuleException(InventoryRuleCodes.QuantityNotPositive,
                    "Sayılan miktar sıfırdan büyük olmalıdır.");
            }

            // One company, one product per lot, the label named by its own lot (DEC-PRD-003).
            if (line.Lot.CompanyId != CompanyId || line.Label.CompanyId != CompanyId)
            {
                throw new InventoryRuleException(InventoryRuleCodes.LabelCompanyMismatch,
                    "Sayılan parça ve lotu sayımın şirketine aittir.");
            }

            if (line.Label.LotId != line.Lot.Id || line.Label.ProductId != line.Lot.ProductId)
            {
                throw Invalid("Etiket kendi lotunu ve lotun ürününü adlandırır.");
            }
        }

        if (lines.Select(line => line.Id).Distinct().Count() != lines.Length
            || lines.Select(line => line.Label.Id).Distinct().Count() != lines.Length
            || lines.Select(line => line.Label.Code).Distinct(StringComparer.Ordinal).Count() != lines.Length)
        {
            throw Invalid("Bir parça bir sayımda bir kez sayılır: satır, etiket ve etiket kodu tekildir.");
        }

        // A lot named twice is the same lot twice: the same facts.
        if (lines.GroupBy(line => line.Lot.Id).Any(group => group.Select(line => (line.Lot.ProductId,
                line.Lot.HeatNumber, line.Lot.CoilNumber)).Distinct().Count() != 1))
        {
            throw Invalid("Aynı lot bir sayımda farklı bilgilerle verilemez.");
        }
    }

    private static string RequireSheetReference(string sheetReference)
    {
        if (string.IsNullOrWhiteSpace(sheetReference))
        {
            throw Invalid("Sayım belgesinin referansı zorunludur.");
        }

        var trimmed = sheetReference.Trim();
        if (trimmed.Length > MaximumSheetReferenceLength || trimmed.Any(char.IsControl))
        {
            throw Invalid("Sayım belgesinin referansı en fazla 128 karakterdir ve kontrol karakteri içermez.");
        }

        return trimmed;
    }

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidOpeningCount, message);
}

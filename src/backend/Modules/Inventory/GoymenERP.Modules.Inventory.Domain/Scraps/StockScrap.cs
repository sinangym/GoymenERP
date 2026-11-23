using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Domain.Scraps;

/// <summary>
/// ST-02 scrap write-off (hurda/fire çıkışı): part or all of one stocked piece, at its one stored position, leaves
/// stock as scrap.
/// <list type="bullet">
/// <item>The book quantity is the position's stored balance the recorder saw; the scrap is positive, never more than
/// the book, exact to the kilogram scale (Ö2). What remains is <c>book − scrap</c>.</item>
/// <item>A scrap always carries a reason, as a count difference does (DEC-STK-007: a stock difference needs a reason
/// and an authority; history is never deleted). The reason is free text: no closed reason list is decided.</item>
/// <item>Recording changes no stock. Only its approved <see cref="WriteOff"/> does, as one new
/// <see cref="StockMovementKind.Scrap"/> movement whose source is the scrap document; the document and every earlier
/// movement stay as they are.</item>
/// </list>
/// Who may approve, and whether a small scrap may be self-approved, is not decided; the store holds the fail-closed
/// rule (another actor decides every scrap). Whether scrapped kg becomes a sellable scrap label (DEC-PRDCT-007) is not
/// decided either: this is a write-off, the kg leave stock.
/// </summary>
public sealed class StockScrap
{
    /// <summary>The source kind of the scrap movement: the scrap document is its source.</summary>
    public const string SourceKind = "inventory.stock-scrap";

    public const int MaximumReasonLength = 1000;

    public StockScrap(Guid id, Guid companyId, StockPosition position, Kilograms book, Kilograms scrap,
        DateTimeOffset scrappedAtUtc, string reason, PieceCount? bookPieces = null, PieceCount? scrapPieces = null)
    {
        Id = id == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Hurda kimliği zorunludur.")
            : id;
        CompanyId = companyId == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "İşlem şirketi zorunludur.")
            : companyId;
        ArgumentNullException.ThrowIfNull(position);
        if (position.CompanyId != companyId)
        {
            throw Invalid("Hurdaya ayrılan pozisyon işlemin şirketinde olmalıdır.");
        }

        if (scrappedAtUtc.Offset != TimeSpan.Zero)
        {
            throw Invalid("Hurda zamanı UTC olmalıdır.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaximumReasonLength)
        {
            throw Invalid($"Hurda gerekçesi zorunludur (en fazla {MaximumReasonLength} karakter).");
        }

        if (book.Value <= 0m)
        {
            throw Invalid("Stoğu olmayan parça hurdaya ayrılamaz.");
        }

        if (scrap.Value <= 0m || scrap.Value > book.Value)
        {
            throw Invalid("Hurda miktarı sıfırdan büyük ve parçanın defter miktarından fazla olmamalıdır.");
        }

        RequirePieces(book, scrap, bookPieces, scrapPieces);
        Position = position;
        Book = book;
        Scrap = scrap;
        BookPieces = bookPieces;
        ScrapPieces = scrapPieces;
        ScrappedAtUtc = scrappedAtUtc;
        Reason = reason.Trim();
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    /// <summary>The scrapped position: owner, custodian, location, label, product and block state as stored.</summary>
    public StockPosition Position { get; }

    public Kilograms Book { get; }

    public Kilograms Scrap { get; }

    /// <summary>KLN-D119 (AK-2b): the stored pieces the recorder saw on a piece-tracking label; null on a kg-only label.</summary>
    public PieceCount? BookPieces { get; }

    /// <summary>KLN-D119 (AK-2b): the scrapped pieces; given exactly when the label tracks pieces.</summary>
    public PieceCount? ScrapPieces { get; }

    public DateTimeOffset ScrappedAtUtc { get; }

    public string Reason { get; }

    /// <summary><c>book − scrap</c>: what stays on the piece; zero when the whole piece is scrapped.</summary>
    public Kilograms Remaining => Kilograms.Of(Book.Value - Scrap.Value);

    /// <summary>KLN-D119: the pieces left at the position after the scrap; null when the position does not count pieces.</summary>
    public PieceCount? RemainingPieces => BookPieces is { } book && ScrapPieces is { } scrap
        ? PieceCount.Of(book.Value - scrap.Value)
        : null;

    /// <summary>The one movement that posts this scrap: a single Out line on the scrapped position, exactly the scrap.</summary>
    public StockMovement WriteOff(Guid movementId, DateTimeOffset occurredAtUtc) =>
        new(movementId, CompanyId, StockMovementKind.Scrap, occurredAtUtc,
            new StockSourceReference(SourceKind, Id),
            [new StockMovementLine(Position, StockDirection.Out, Scrap, ScrapPieces)]);

    /// <summary>
    /// KLN-D119 (AK-2b): a piece-tracking label is scrapped with its pieces, a kg-only label never is (no backfill). The
    /// scrapped pieces never exceed the book pieces; the whole kilograms take every piece, and a partial scrap leaves at
    /// least one piece on the label (pieces and kilograms stay both positive). Zero scrapped pieces is a kg-only fire on a
    /// piece-tracking label (part of a piece's weight lost).
    /// </summary>
    private static void RequirePieces(Kilograms book, Kilograms scrap, PieceCount? bookPieces, PieceCount? scrapPieces)
    {
        if ((bookPieces is null) != (scrapPieces is null))
        {
            throw new InventoryRuleException(bookPieces is null ? InventoryRuleCodes.PieceCountNotTracked
                : InventoryRuleCodes.PieceCountRequired, "Adetli etiket adetle, adetsiz etiket adetsiz hurdaya ayrılır.");
        }

        if (bookPieces is not { } bookCount || scrapPieces is not { } scrapCount)
        {
            return;
        }

        var remainingPieces = bookCount.Value - scrapCount.Value;
        if (remainingPieces < 0 || (remainingPieces == 0) != (scrap == book))
        {
            throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount,
                "Hurdada kalan adet ile kalan kilogram birlikte sıfır ya da birlikte pozitif olmalıdır.");
        }
    }

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidStockScrap, message);
}

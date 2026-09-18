namespace GoymenERP.Modules.Inventory.Domain.Labels;

/// <summary>
/// Ö3 (Sinan's decision of 2026-09-18): whether the documents this piece needs are in.
/// A missing mandatory document never hides the physical arrival — the stock is recorded —
/// but the piece is not free for production or shipment until they are.
/// </summary>
public enum LabelDocumentStatus
{
    /// <summary>No document is required, or every required one is attached.</summary>
    Complete = 1,

    /// <summary>At least one required document is missing: "belge/kalite bekliyor".</summary>
    PendingDocuments = 2,
}

/// <summary>One print of a label. A reprint is another print of the SAME physical identity.</summary>
public sealed record LabelPrint(int Sequence, DateTimeOffset PrintedAtUtc, string PrintedBy, string? Reason);

/// <summary>
/// The physical identity of a piece of material (§7.1). It is not the product card: the
/// product is referenced by identity, and this label is the thing on the coil.
///
/// <list type="bullet">
/// <item>Lot/label trace is the mandatory minimum (Ö3): a label always carries its product
/// and lot identity.</item>
/// <item>Reprinting keeps the SAME identity and only adds an audited print — it is not a
/// new piece and not a stock movement. Split/merge (a NEW identity) is T13's work.</item>
/// <item>Heat, coil and MTC are recorded only when real; nothing here generates or
/// defaults them.</item>
/// </list>
/// </summary>
public sealed class MaterialLabel
{
    private readonly HashSet<string> requiredDocuments;
    private readonly HashSet<string> attachedDocuments = new(StringComparer.Ordinal);
    private readonly List<LabelPrint> prints = [];

    public MaterialLabel(Guid id, Guid productId, Guid lotId, IEnumerable<string>? requiredDocuments = null,
        string? heatNumber = null, string? coilNumber = null)
    {
        Id = RequireId(id, "Etiket");
        ProductId = RequireId(productId, "Ürün");
        LotId = RequireId(lotId, "Lot");
        this.requiredDocuments = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in requiredDocuments ?? [])
        {
            this.requiredDocuments.Add(RequireToken(document));
        }

        HeatNumber = Optional(heatNumber);
        CoilNumber = Optional(coilNumber);
    }

    /// <summary>The physical identity. It never changes, whatever is printed.</summary>
    public Guid Id { get; }

    public Guid ProductId { get; }

    public Guid LotId { get; }

    /// <summary>Only when a real heat number is known; never generated.</summary>
    public string? HeatNumber { get; }

    /// <summary>Only when a real coil number is known; never generated.</summary>
    public string? CoilNumber { get; }

    public IReadOnlySet<string> RequiredDocuments => requiredDocuments;

    public IReadOnlySet<string> AttachedDocuments => attachedDocuments;

    public IReadOnlyList<LabelPrint> Prints => prints.AsReadOnly();

    public LabelDocumentStatus DocumentStatus =>
        requiredDocuments.IsSubsetOf(attachedDocuments) ? LabelDocumentStatus.Complete : LabelDocumentStatus.PendingDocuments;

    /// <summary>
    /// Free for production or shipment only with its documents in. Physical presence is a
    /// different question and is not affected (the stock is counted either way).
    /// </summary>
    public bool IsReleasableForProductionOrShipment => DocumentStatus == LabelDocumentStatus.Complete;

    /// <summary>Record that a required (or extra) document is now attached.</summary>
    public void AttachDocument(string documentKind) => attachedDocuments.Add(RequireToken(documentKind));

    /// <summary>The first print of this label.</summary>
    public LabelPrint Print(DateTimeOffset printedAtUtc, string printedBy)
    {
        if (prints.Count > 0)
        {
            throw Invalid("Etiket zaten basıldı; tekrar basmak yeniden baskıdır ve gerekçe ister.");
        }

        return AddPrint(printedAtUtc, printedBy, reason: null);
    }

    /// <summary>
    /// Another print of the same physical identity, with its reason on the audit trail. It
    /// creates no new label and no stock movement.
    /// </summary>
    public LabelPrint Reprint(DateTimeOffset printedAtUtc, string printedBy, string reason)
    {
        if (prints.Count == 0)
        {
            throw Invalid("Basılmamış etiket yeniden basılamaz.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw Invalid("Yeniden baskı gerekçe ister.");
        }

        return AddPrint(printedAtUtc, printedBy, reason.Trim());
    }

    private LabelPrint AddPrint(DateTimeOffset printedAtUtc, string printedBy, string? reason)
    {
        if (printedAtUtc.Offset != TimeSpan.Zero)
        {
            throw Invalid("Baskı zamanı UTC olmalıdır.");
        }

        if (string.IsNullOrWhiteSpace(printedBy))
        {
            throw Invalid("Baskıyı yapan aktör zorunludur.");
        }

        var print = new LabelPrint(prints.Count + 1, printedAtUtc, printedBy.Trim(), reason);
        prints.Add(print);
        return print;
    }

    private static Guid RequireId(Guid id, string what) =>
        id == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, $"{what} kimliği zorunludur.")
            : id;

    private static string RequireToken(string? value) =>
        string.IsNullOrWhiteSpace(value) ? throw Invalid("Belge türü boş olamaz.") : value.Trim();

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidLabelOperation, message);
}

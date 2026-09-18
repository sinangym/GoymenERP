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

/// <summary>One attached document: which kind, when and by whom it was attached.</summary>
public sealed record LabelDocumentAttachment(string Kind, DateTimeOffset AttachedAtUtc, string AttachedBy);

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
    private readonly List<LabelDocumentAttachment> attachments = [];
    private readonly List<LabelPrint> prints = [];

    /// <summary>The longest label code this module stores.</summary>
    public const int MaximumCodeLength = 64;

    public MaterialLabel(Guid id, Guid companyId, string code, Guid productId, Guid lotId,
        IEnumerable<string>? requiredDocuments = null, string? heatNumber = null, string? coilNumber = null)
    {
        Id = RequireId(id, "Etiket");
        CompanyId = RequireId(companyId, "Etiketin şirketi");
        Code = RequireCode(code);
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

    /// <summary>
    /// The company that holds this piece's stock (ADR-INV-001 A). It never changes: a transfer
    /// to another company closes this label and the receiving company records a NEW label with a
    /// new <see cref="Id"/> (T13).
    /// </summary>
    public Guid CompanyId { get; }

    /// <summary>
    /// The code printed on the piece, unique within <see cref="CompanyId"/> only (no group-wide
    /// uniqueness, so no code can reveal another company's stock). Its format is not chosen here:
    /// it is whatever the printing or receiving process gives, trimmed.
    /// </summary>
    public string Code { get; }

    public Guid ProductId { get; }

    public Guid LotId { get; }

    /// <summary>Only when a real heat number is known; never generated.</summary>
    public string? HeatNumber { get; }

    /// <summary>Only when a real coil number is known; never generated.</summary>
    public string? CoilNumber { get; }

    public IReadOnlySet<string> RequiredDocuments => requiredDocuments;

    /// <summary>The document kinds attached so far.</summary>
    public IReadOnlySet<string> AttachedDocuments =>
        attachments.Select(attachment => attachment.Kind).ToHashSet(StringComparer.Ordinal);

    /// <summary>Every attachment, in order, with who and when (Ö3: leaving "waiting" is an event).</summary>
    public IReadOnlyList<LabelDocumentAttachment> DocumentAttachments => attachments.AsReadOnly();

    public IReadOnlyList<LabelPrint> Prints => prints.AsReadOnly();

    public LabelDocumentStatus DocumentStatus =>
        requiredDocuments.IsSubsetOf(AttachedDocuments) ? LabelDocumentStatus.Complete : LabelDocumentStatus.PendingDocuments;

    /// <summary>
    /// Free for production or shipment only with its documents in. Physical presence is a
    /// different question and is not affected (the stock is counted either way). In this
    /// slice this is a FLAG: no movement is refused by it yet — enforcing it belongs to
    /// allocation (T11) and shipment, which are not built.
    /// </summary>
    public bool IsReleasableForProductionOrShipment => DocumentStatus == LabelDocumentStatus.Complete;

    /// <summary>Record that a required (or extra) document is now attached, by whom and when.</summary>
    public LabelDocumentAttachment AttachDocument(string documentKind, DateTimeOffset attachedAtUtc, string attachedBy)
    {
        if (attachedAtUtc.Offset != TimeSpan.Zero)
        {
            throw Invalid("Belge ekleme zamanı UTC olmalıdır.");
        }

        if (string.IsNullOrWhiteSpace(attachedBy))
        {
            throw Invalid("Belgeyi ekleyen aktör zorunludur.");
        }

        var attachment = new LabelDocumentAttachment(RequireToken(documentKind), attachedAtUtc, attachedBy.Trim());
        attachments.Add(attachment);
        return attachment;
    }

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

    /// <summary>
    /// Rebuilds a stored label with its prints and attachments in their stored order. Every
    /// rule of a new label holds again: a stored row that breaks one is refused, not repaired.
    /// </summary>
    public static MaterialLabel Restore(Guid id, Guid companyId, string code, Guid productId, Guid lotId,
        IEnumerable<string> requiredDocuments, string? heatNumber, string? coilNumber,
        IEnumerable<LabelPrint> prints, IEnumerable<LabelDocumentAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(prints);
        ArgumentNullException.ThrowIfNull(attachments);
        var label = new MaterialLabel(id, companyId, code, productId, lotId, requiredDocuments, heatNumber, coilNumber);
        foreach (var print in prints)
        {
            ArgumentNullException.ThrowIfNull(print);
            var restored = label.prints.Count == 0
                ? label.Print(print.PrintedAtUtc, print.PrintedBy)
                : label.Reprint(print.PrintedAtUtc, print.PrintedBy, print.Reason ?? string.Empty);
            if (restored != print)
            {
                throw Invalid("Kayıtlı baskı sırası etiketin baskı kurallarıyla uyuşmuyor.");
            }
        }

        foreach (var attachment in attachments)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            label.AttachDocument(attachment.Kind, attachment.AttachedAtUtc, attachment.AttachedBy);
        }

        return label;
    }

    private static string RequireCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Etiket kodu zorunludur.");
        }

        var code = value.Trim();
        return code.Length <= MaximumCodeLength
            ? code
            : throw Invalid($"Etiket kodu en fazla {MaximumCodeLength} karakterdir.");
    }

    private static string RequireToken(string? value) =>
        string.IsNullOrWhiteSpace(value) ? throw Invalid("Belge türü boş olamaz.") : value.Trim();

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidLabelOperation, message);
}

namespace GoymenERP.Modules.Purchasing.Domain.Requests;

/// <summary>
/// A1-2 (plan API-02; Üretim note A-2, Sales note S-A1, ALS-D16): the module whose need opened a request line, beside manual
/// entry. Manual is not a value here: a manual line has no source (<see cref="PurchaseRequestLine.Source"/> is null), so a
/// line has exactly one way to say "manual". The numbers are stored; they never change meaning.
/// </summary>
public enum PurchaseRequestSourceKind
{
    /// <summary>A stock need (Inventory). No producer exists yet (ALS-D58, ALS-D61); the identity is carried as given.</summary>
    Stock = 1,

    /// <summary>
    /// A sales order line's need (Sales; S-A1/MOD-03). A1-2b (SAT-D219, SAT-D220): CLOSED for new lines unless (A4b) the Application layer has read
    /// <c>ISalesOrderLineReferencePortV1</c> through the host adapter and got Approved (every other answer is refused); the Sales
    /// linked-supply read adaptor is wired in the same delivery; a line recorded before A1-2b keeps this value and stays readable.
    /// </summary>
    Sales = 2,

    /// <summary>A production plan input's need (Manufacturing; Üretim U5-c calls the create route with it, ALS-D16).</summary>
    Production = 3,
}

/// <summary>
/// A1-2 (plan API-02): where a request line's need came from — the source module's kind and the source record's identity
/// (its document and the line in it, e.g. a sales order and its line, a production plan and its input line). The identity is
/// an OPAQUE reference: Purchasing neither reads the source module's tables nor checks that the record exists (no port is
/// published for it yet; ALS-D58 forbids new ports in deferred modules), so the reference is stored as given and its
/// validation is a follow-up of the producing module's port. A source is set when the line is created and never moves.
/// </summary>
public sealed record PurchaseRequestLineSource
{
    private PurchaseRequestLineSource(PurchaseRequestSourceKind kind, Guid documentId, Guid documentLineId)
    {
        Kind = kind;
        DocumentId = documentId;
        DocumentLineId = documentLineId;
    }

    public PurchaseRequestSourceKind Kind { get; }

    /// <summary>The source record (sales order, production plan, stock need); never empty.</summary>
    public Guid DocumentId { get; }

    /// <summary>The line inside the source record; never empty.</summary>
    public Guid DocumentLineId { get; }

    /// <summary>A defined kind and two non-empty identities; anything else is refused (fail-closed), never defaulted.</summary>
    public static PurchaseRequestLineSource Of(PurchaseRequestSourceKind kind, Guid documentId, Guid documentLineId) =>
        Enum.IsDefined(kind)
            ? new PurchaseRequestLineSource(kind, Identity.Require(documentId, nameof(documentId)), Identity.Require(documentLineId, nameof(documentLineId)))
            : throw new ArgumentOutOfRangeException(nameof(kind), "Talep satırı kaynağı tanımlı değil.");

    /// <summary>
    /// A1-2b (SAT-D219, SAT-D220; fail-closed): whether a NEW line may record this kind. <see cref="PurchaseRequestSourceKind.Sales"/>
    /// may not: Sales requires the line to be read through its reference port (only an Approved sales order line is linked) and
    /// the linked-supply read adaptor to exist from the first recorded link, and neither exists until MOD-03. Stock and
    /// Production are unaffected. A stored line is read back through <see cref="Of"/>, which this never narrows.
    /// </summary>
    public static bool IsOpenForNewLines(PurchaseRequestSourceKind kind) =>
        kind is PurchaseRequestSourceKind.Stock or PurchaseRequestSourceKind.Production;

    /// <summary>
    /// A1-2b: the source of a line being created now — <see cref="Of"/> plus <see cref="IsOpenForNewLines"/>; a defined kind
    /// that is closed is a <see cref="PurchasingRuleException"/> (<see cref="PurchasingRuleCodes.RequestSourceSalesUnavailable"/>).
    /// A4b (SAT-D220): the Application layer passes <paramref name="salesLineVerified"/> = true only after Sales's approved-line port
    /// answered Approved for that very line; Sales is otherwise still closed. The rule lives only in this factory (used by the create path); <see cref="PurchaseRequest.AddLine"/> accepts any defined
    /// source, because stored lines are rebuilt through it with <see cref="Of"/>.
    /// </summary>
    public static PurchaseRequestLineSource ForNewLine(PurchaseRequestSourceKind kind, Guid documentId, Guid documentLineId,
        bool salesLineVerified = false) =>
        Enum.IsDefined(kind) && !IsOpenForNewLines(kind) && !(kind == PurchaseRequestSourceKind.Sales && salesLineVerified)
            ? throw new PurchasingRuleException(PurchasingRuleCodes.RequestSourceSalesUnavailable,
                "Satış kaynaklı talep satırı, Satış sipariş satırı doğrulaması ve bağlı tedarik okuma bağdaştırıcısı (MOD-03) gelene kadar kaydedilemez.")
            : Of(kind, documentId, documentLineId);
}

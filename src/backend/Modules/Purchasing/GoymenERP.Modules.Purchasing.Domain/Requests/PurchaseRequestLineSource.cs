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

    /// <summary>A sales order line's need (Sales; S-A1/MOD-03 reads it back through a later adaptor).</summary>
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
}

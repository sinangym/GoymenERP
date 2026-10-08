namespace GoymenERP.Modules.Documents.Application.Contracts;

/// <summary>
/// The owner-declared link kinds (DEC-DOC-001 "Kayıt Bağlantısı"). A kind exists only when its owner's port is
/// registered; an unknown kind is a 400, never a guess. The first kind is the Cari party (AT-065's first screen).
/// </summary>
public static class DocumentOwnerLinkKindsV1
{
    /// <summary>A Cari party with an account in the context company (Counterparty owner).</summary>
    public const string CounterpartyParty = "counterparty.party";

    /// <summary>An Inventory material label in the context company (Inventory owner).</summary>
    public const string InventoryMaterialLabel = "inventory.material-label";

    /// <summary>A Purchasing purchase order readable in the context company (Purchasing owner).</summary>
    public const string PurchasingPurchaseOrder = "purchasing.purchase-order";
}

/// <summary>
/// One owner module's answer for its link kind: whether the record exists IN THE CONTEXT COMPANY and the current actor
/// may READ it, measured by the owner's own authorized read at the moment of asking. The vault reads no owner table.
/// Anything but <see cref="DocumentOwnerAccessV1.Readable"/> is answered to the client as 404 (non-disclosure: a record
/// of another company, a record that does not exist and a record the actor may not read look the same).
/// </summary>
public interface IDocumentOwnerLinkPortV1
{
    /// <summary>The link kind this port answers (one of <see cref="DocumentOwnerLinkKindsV1"/>).</summary>
    string Kind { get; }

    Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default);
}

public enum DocumentOwnerAccessV1
{
    Readable = 1,
    NotFound = 2,
    Denied = 3,
    Conflict = 4,
    Unavailable = 5,
}

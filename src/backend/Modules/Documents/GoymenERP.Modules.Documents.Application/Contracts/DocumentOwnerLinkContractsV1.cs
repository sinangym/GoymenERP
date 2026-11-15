using GoymenERP.Modules.Documents.Domain;

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

    /// <summary>A Sales order readable in the context company (Sales owner).</summary>
    public const string SalesOrder = "sales.order";

    /// <summary>
    /// A Finance official sales invoice record, or its reserved id before the record exists, in the context company
    /// (Finance owner; #66 6860586175: the PDF is uploaded to the reserved id first).
    /// </summary>
    public const string FinanceOfficialSalesInvoice = "finance.official-sales-invoice";

    /// <summary>A Manufacturing production plan readable in the context company (Manufacturing owner; B-26).</summary>
    public const string ManufacturingPlan = "manufacturing.plan";

    /// <summary>A Manufacturing production order readable in the context company (Manufacturing owner; B-26).</summary>
    public const string ManufacturingOrder = "manufacturing.order";

    /// <summary>A Finance manual receipt (tahsilat) source readable in the context company (Finance owner; B-26).</summary>
    public const string FinanceManualReceipt = "finance.manual-receipt";

    /// <summary>A Finance manual bank payment readable in the context company (Finance owner; B-26).</summary>
    public const string FinanceManualBankPayment = "finance.manual-bank-payment";

    /// <summary>A Finance customer instrument (çek/senet) readable in the context company (Finance owner; B-26).</summary>
    public const string FinanceCustomerInstrument = "finance.customer-instrument";
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

    /// <summary>
    /// B-26 / DEC-DOC-002: whether an EXISTING document may be linked to a record of this kind (the same physical file
    /// shown on a second record without a second upload). Fail-closed by default: an owner opts in explicitly. The
    /// Finance official sales invoice stays out (FNS-D36/FNS-D38: its PDF is uploaded to the reserved id and one PDF
    /// belongs to one invoice record).
    /// </summary>
    bool AcceptsExistingDocumentLinks => false;

    /// <summary>
    /// F-026 (PLT-S9): whether a PDF the owner's module GENERATED may be stored into the vault on a record of this kind
    /// through <see cref="IGeneratedDocumentStorePortV1"/>. Fail-closed by default: an owner opts in explicitly. The
    /// Finance official sales invoice stays out (its PDF is the official one uploaded to the reserved id).
    /// </summary>
    bool AcceptsGeneratedDocuments => false;

    /// <summary>
    /// KLN-D59: the permissions of the owner module's producing commands that may store a generated PDF on a record of
    /// this kind. The generated-document port checks the producing command's own permission (fresh, for the context
    /// actor) instead of <c>documents.files.upload</c>, and only a permission declared here: a caller that names any
    /// other permission is refused before anything is read. Empty by default (fail-closed); the owner module declares
    /// its own commands' permissions, the vault never chooses them and no new permission is opened.
    /// </summary>
    IReadOnlyCollection<string> GeneratedDocumentProducerPermissions => [];

    /// <summary>
    /// B-26 / FNS-D52: the content kinds a record of this kind may hold. Every existing owner keeps
    /// <see cref="DocumentContentPolicy.StandardKinds"/>; the owner of a bank-statement import (FIN-S21) opts in to
    /// <see cref="DocumentContentPolicy.BankStatementImportKinds"/> by overriding this. An upload, a new version and a
    /// link of an existing document are each refused when a record they touch does not accept the document's kind, so a
    /// statement file never appears on a record whose type list did not grow (DEC-DOC-009's open list stays as it is).
    /// </summary>
    IReadOnlyCollection<DocumentFileKind> AcceptedFileKinds => DocumentContentPolicy.StandardKinds;

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

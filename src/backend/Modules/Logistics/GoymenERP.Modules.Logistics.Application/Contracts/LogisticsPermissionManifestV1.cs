namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// The Logistics permission names of LOG-4 (D6). Company-wide, not critical; the central catalogue
/// (<c>ErpPermissions</c>) registers them with the central writer's step, and any grant is a human
/// gate. The service measures them afresh at every fence; an unregistered name is a 503 setup problem.
/// </summary>
public static class LogisticsPermissionManifestV1
{
    /// <summary>Open a loading instruction (Draft with lines) and issue it.</summary>
    public const string InstructionsCreate = "logistics.loading-instructions.create";

    /// <summary>List and read loading instructions of the context company.</summary>
    public const string InstructionsRead = "logistics.loading-instructions.read";

    /// <summary>Record a loaded share against an issued instruction's line.</summary>
    public const string LoadsRecord = "logistics.loads.record";

    // Sinan #66/5828506709: two independent actions, neither aliases loads.record; no grant or maker-checker rule.
    public const string ShipmentsRecord = "logistics.shipments.record";
    public const string DeliveriesRecord = "logistics.deliveries.record";

    /// <summary>
    /// AT-053: record the vehicle's manual weighbridge record on an issued instruction. Its own name, aliasing no
    /// other (master v3 §12.1 lists the Kantar kullanıcısı as its own role; the #66/5828506709 precedent). Registered
    /// centrally; no grant, role or maker-checker rule is chosen here.
    /// </summary>
    public const string WeighingsRecord = "logistics.weighings.record";

    /// <summary>
    /// AT-025: read the SALES section of the operations work list (L-01 Sevk Edilebilir Siparişler) — the approved sales order
    /// lines with Logistics's own instructed / loaded / shipped / delivered totals, price-free (Sinan §4, #66 5738113623). Its
    /// own name (each read is its own name, #66 5738113623 §4): holding it reveals nothing of purchasing, and the purchase
    /// section's name reveals nothing of sales. Registered centrally; who holds it is a human grant, not chosen here.
    /// </summary>
    public const string OperationsSalesWorkRead = "logistics.operations-work.sales.read";

    /// <summary>
    /// AT-025: read the PURCHASE section of the operations work list (L-09 Beklenen Alış / Kapı Kabulü) — the approved purchase
    /// order lines still waiting for goods receipt, price-free. Its own name, apart from the sales section's (see above).
    /// </summary>
    public const string OperationsPurchaseWorkRead = "logistics.operations-work.purchase.read";

    /// <summary>
    /// AT-055: prepare a dispatch-note (irsaliye) DRAFT from an issued instruction's definite shipments (L-08 Satış İrsaliyesi
    /// Hazırlığı; master v3 §12: the Sevkiyat yetkilisi prepares and checks the draft). Its own name, aliasing no other: holding
    /// shipment or load names does not prepare documents. Reading a draft is the instruction read. Registered centrally; no
    /// grant, role or maker-checker rule is chosen here.
    /// </summary>
    public const string DispatchNotesCreate = "logistics.dispatch-notes.create";

    /// <summary>
    /// AT-055 (user decision 2026-09-30): approve (finalise) a dispatch-note draft — the system series number is assigned on
    /// approval — and record the official e-İrsaliye number on an approved note. Its own name, apart from preparing the draft.
    /// No maker-checker rule is documented for the irsaliye (no DEC-* names one), so the preparer is not barred from approving
    /// (ASSUMPTION-AT055-APPROVER); segregating the two is a grant decision.
    /// </summary>
    public const string DispatchNotesApprove = "logistics.dispatch-notes.approve";

    /// <summary>
    /// AT-055 (user decision 2026-09-30): cancel a draft or an approved dispatch note with a reason (never a delete); the
    /// cancellation releases its shipments for a new note. Its own name.
    /// </summary>
    public const string DispatchNotesCancel = "logistics.dispatch-notes.cancel";

    /// <summary>Read customer-return requests of the context company. Registered centrally; no grant is implied.</summary>
    public const string CustomerReturnsRead = "logistics.customer-returns.read";

    /// <summary>Request a physical customer return against an exact delivered share. Registered centrally; no grant is implied.</summary>
    public const string CustomerReturnsRequest = "logistics.customer-returns.request";

    /// <summary>Approve or reject a pending customer return. The domain still requires a different actor.</summary>
    public const string CustomerReturnsDecision = "logistics.customer-returns.decision";

    public static readonly IReadOnlyList<string> Names =
        [InstructionsCreate, InstructionsRead, LoadsRecord, ShipmentsRecord, DeliveriesRecord, WeighingsRecord, OperationsSalesWorkRead, OperationsPurchaseWorkRead,
            DispatchNotesCreate, DispatchNotesApprove, DispatchNotesCancel, CustomerReturnsRead, CustomerReturnsRequest, CustomerReturnsDecision];
}

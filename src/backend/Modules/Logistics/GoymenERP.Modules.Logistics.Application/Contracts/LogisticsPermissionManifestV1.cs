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

    public static readonly IReadOnlyList<string> Names =
        [InstructionsCreate, InstructionsRead, LoadsRecord, ShipmentsRecord, DeliveriesRecord, WeighingsRecord, OperationsSalesWorkRead, OperationsPurchaseWorkRead,
            DispatchNotesCreate];
}

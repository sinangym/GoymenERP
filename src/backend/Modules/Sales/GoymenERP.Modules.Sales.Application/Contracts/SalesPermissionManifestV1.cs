using System.Collections.Frozen;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// The Sales owner's technical permission names for the order draft (Business #254 5654539215:
/// "permission technical mapping is the backend owner's"). Names only: nothing here registers,
/// classifies or grants a permission. Registration in the central catalogue (company-wide only,
/// not critical) is the central writer's step; who holds which is a human gate. A draft grants no
/// Product authority: selecting a product still needs the narrow
/// <c>product.operational-selection.read</c> of the Product owner (#276), measured by that owner.
/// </summary>
public static class SalesPermissionManifestV1
{
    // v17 (#865, API-41/42): sales.reports.read; v18 (API-04, merged after it): sales.orders.header.write; v19 (SAT-D32 /
    // SAT-D168): sales.orders.close; v20 (SAT-D30 / SAT-D168): sales.orders.over-delivery.approve; v21 (S5 API-32, Fatura çekirdeği 1): sales.invoices.write; v22 (S5 API-33, Fatura çekirdeği 2): sales.invoices.approve; v23 (S5 API-35, Fatura çekirdeği 3): sales.invoices.match; v24 (SAT-D17 / SAT-D168): sales.orders.lines.close; v25 (S5 API-36, Fatura çekirdeği 4): sales.invoices.finalize; v26 (SAT-D250 / SAT-D266 / SAT-D267, S-05 marj): sales.orders.margin.read; v27 (S-2 API-11, SAT-D179 / SAT-D251–D255, merged after v26): sales.orders.source-plan.write; v28 (S5 API-34, SAT-D236/D244/D248, EK47/EK54; merged after v27): sales.invoices.export.
    public const int Version = 28;
    public const string Subject = "Sales";

    /// <summary>POST /api/v1/sales/drafts, the reference reads of the draft form, and POST …/{id}/submission (SALES-A1: whoever may create a draft may send it for approval).</summary>
    public const string DraftsCreate = "sales.drafts.create";

    /// <summary>GET /api/v1/sales/drafts and GET /api/v1/sales/drafts/{id} (list and detail share one read).</summary>
    public const string DraftsRead = "sales.drafts.read";

    /// <summary>
    /// SALES-A1 (Business 5737989917): POST …/{id}/approval and …/{id}/rejection — the internal
    /// approval decision. Company-wide. Its criticality class is the central writer's registration
    /// decision; the grant is a human gate. No maker≠checker, threshold, role or second person is
    /// implied by the name.
    /// </summary>
    public const string OrdersApprove = "sales.orders.approve";

    /// <summary>
    /// Records customer acceptance of an internally approved order revision. Separate from internal approval,
    /// create and read; registration belongs to Central and the real grant remains a human boundary.
    /// </summary>
    public const string OrdersCustomerAcceptanceRecord = "sales.orders.customer-acceptance.record";

    /// <summary>
    /// K3 (Sinan 5748258299, closes 5745151414): writing a unit price or a discount on an order line —
    /// POST /api/v1/sales/drafts with a line carrying a commercial field. A SEPARATE name: the draft's
    /// create permission does NOT confer it, and this name confers no approval. Company-wide; the grant
    /// (to which persona, in which company) is a human gate, never implied here.
    /// </summary>
    public const string OrdersPriceWrite = "sales.orders.price.write";

    /// <summary>
    /// K3 P2 (Sinan 5748258299): READING a line's unit price, price unit and discount on the draft detail, on every
    /// command response and on the archived revisions — "Sipariş onayının fiyat/tutar bağlamı ilgili dar fiyat
    /// okumasıyla sağlanır; bu maliyet/marj/banka sınırsız okuması değildir." A SEPARATE, NARROW name: the draft
    /// read does NOT confer it (a holder of the draft read alone gets the WITHHELD marker, never the texts), and it
    /// confers nothing adjacent — no cost, margin, bank or Finance read. Company-wide; the grant is a human gate.
    /// </summary>
    public const string OrdersPriceRead = "sales.orders.price.read";

    /// <summary>
    /// SAT-D250 (EK49) / SAT-D266 / SAT-D267 (EK52): disclosing the S-05 sales margin — the margin block of GET
    /// /api/v1/sales/drafts/{id}/line-pricing (SAT-D247/SAT-D259 amount and percent; on a draft the non-persisted preview). A
    /// SEPARATE, NARROW name: the draft read and <see cref="OrdersPriceRead"/> do NOT confer it (their holders get the WITHHELD
    /// block, never a margin and never a zero), and it confers nothing adjacent — no price, cost or Finance read. Company-wide;
    /// not Critical; registered, granted to no role: SAT-D250 names Genel Müdür and Admin, the grant itself is a human gate.
    /// </summary>
    public const string OrdersMarginRead = "sales.orders.margin.read";

    /// <summary>Approves a reasoned below-floor exception; never conferred by price write or order approval.</summary>
    public const string OrdersPriceFloorExceptionApprove = "sales.orders.price-floor-exception.approve";

    /// <summary>
    /// SAT-D2 / API-12 ("limiti aşan iskonto Müdür onayına gider"): approves, with a reason, every current line below its list
    /// price beyond the limit of the actor who set it (or with no limit defined), and every priced line without a list price
    /// (SAT-D43) — POST …/{id}/discount-exception-approval. A SEPARATE name: price write, order approval and the price-floor
    /// exception approval do NOT confer it, and it confers none of them. On a listed line the approver is never the actor who
    /// set the discount (SAT-D37); a line without a list price may be approved by its writer (SAT-D45). Company-wide;
    /// registered, not granted: SAT-D37 names the Müdür persona; the grant is a human gate.
    /// </summary>
    public const string OrdersDiscountExceptionApprove = "sales.orders.discount-exception.approve";

    /// <summary>
    /// DEC-SAL-003 (KILITLI): "termin güncellemesi planlama/sevkiyat rolündedir ve yeniden onay gerektirmez, ancak
    /// auditlidir" — POST …/{id}/delivery-schedule, moving the requested delivery dates of the header, the lines and
    /// their existing slices. A SEPARATE name because the decision gives the update to the planning/shipping role, not
    /// to the draft's preparer or approver: the create, approve and price names do NOT confer it, and it confers none of
    /// them (no approval, no price, no line or quantity change). The technical literal is the Sales owner's mapping
    /// (Business #66 5808295853 / 5811361336: the owner names the literal); company-wide; registration and class are the
    /// central writer's step; which persona holds it is a human gate.
    /// </summary>
    public const string OrdersDeliveryScheduleWrite = "sales.orders.delivery-schedule.write";

    /// <summary>
    /// DEC-SAL-005 (KILITLI): cancelling an APPROVED order — GET …/{id}/cancellation-impact (the impact preview) and POST
    /// …/{id}/cancellation. A SEPARATE name because no source says who may cancel: the create, approve, price and schedule
    /// names do NOT confer it and it confers none of them. Holding it grants no second-person approval, release or reversal
    /// of any other module's record. Company-wide; registration and its class (Critical = grant-time dual control, if Sinan
    /// wants a cancellation to need two people) are the central writer's step; which persona holds it is a human gate.
    /// </summary>
    public const string OrdersCancel = "sales.orders.cancel";

    /// <summary>
    /// DEC-SAL-002 (KILITLI): "Cari, ürün, miktar, fiyat, iskonto, para, vade ve risk istisnası değişirse yeni revizyon ve
    /// yeniden onay gerekir." — POST …/{id}/quantity-revision, replacing the ordered quantity of an order's lines (an approved
    /// order is archived and re-enters approval). A SEPARATE name because no source says who may change an approved order's
    /// quantity: the create, approve, price, schedule and cancel names do NOT confer it and it confers none of them (no
    /// approval — the new revision is approved through <see cref="OrdersApprove"/> as any other). Company-wide; registration
    /// and class are the central writer's step; which persona holds it is a human gate (NEEDS-SINAN).
    /// </summary>
    public const string OrdersQuantityWrite = "sales.orders.quantity.write";

    /// <summary>
    /// API-04 (DEC-SAL-002, DEC-SAL-008; plan §3.1): POST …/{id}/header-revision — revising an order's header (customer account,
    /// currency, due terms, sales owner, delivery address and contact; an approved order is archived and re-enters approval).
    /// Its own name (the plan's literal): the create, approve, price, quantity, schedule and cancel names do NOT confer it and it
    /// confers none of them (the new revision is approved through <see cref="OrdersApprove"/>). Company-wide; not Critical;
    /// registered, granted to no role — which persona holds it is a human gate (NEEDS-SINAN).
    /// </summary>
    public const string OrdersHeaderWrite = "sales.orders.header.write";

    /// <summary>
    /// SINAN_KARARLARI_2026-10-21 No 30: force-closing an APPROVED order — GET …/{id}/force-close-impact (the impact preview) and
    /// POST …/{id}/force-close. The decision's own literal, "<c>sales.orders.cancel</c>'dan ayrı ad": the create, approve, price,
    /// schedule, quantity and cancel names do NOT confer it and it confers none of them. Holding it releases or reverses no other
    /// module's record. Company-wide; registration is the central writer's step (not Critical: no approved source adds grant-time
    /// dual control); which persona holds it is a human gate.
    /// </summary>
    public const string OrdersForceClose = "sales.orders.force-close";

    /// <summary>
    /// SAT-D32 / SAT-D168 (EK24) / SAT-D215 (EK37): closing a "kapanmaya hazır" order on the user's action — POST …/{id}/close
    /// (no reason). Its own name: the create, approve, cancel and force-close names do NOT confer it and it confers none of them
    /// (a force-close is a different act on an order that is NOT ready). Company-wide; not Critical (no approved source adds
    /// grant-time dual control); registered, granted to no role — SAT-D168/D188/D190 name the personas (Satış Uzmanı, Satış
    /// Müdürü, Genel Müdür, Admin), the grant itself is a human gate.
    /// </summary>
    public const string OrdersClose = "sales.orders.close";

    /// <summary>
    /// SAT-D30 / SAT-D168 (EK24), SAT-D187–D190 (EK28): approving an over-delivery in the tolerance APPROVAL band — POST
    /// …/{id}/over-delivery-approval. The decision names Satış Uzmanı, Satış Müdürü, Genel Müdür and Admin; which persona holds
    /// it is a human gate (registered, granted to no role). Its own name: the create, approve, price, schedule, quantity, cancel
    /// and force-close names do NOT confer it and it confers none of them. SAT-D216 (the shipper does not approve their own
    /// over-delivery) is a rule of the command, not of the catalogue. Company-wide; not Critical (no approved source adds
    /// grant-time dual control).
    /// </summary>
    public const string OrdersOverDeliveryApprove = "sales.orders.over-delivery.approve";

    /// <summary>
    /// SAT-D17 / SAT-D168 (EK24), SAT-D214–D216 (EK37): closing one order line with a shortfall — POST …/{id}/line-closure. The
    /// decision names Satış Uzmanı, Satış Müdürü, Genel Müdür and Admin; which persona holds it is a human gate (registered,
    /// granted to no role). Its own name: the order close, force-close, cancel and over-delivery names do NOT confer it and it
    /// confers none of them. SAT-D216 (the shipper does not close their own line) is a rule of the command. Company-wide; not
    /// Critical.
    /// </summary>
    public const string OrdersLineClose = "sales.orders.lines.close";

    /// <summary>
    /// SAT-D179 (EK25) / SAT-D251–SAT-D255 (EK50): saving the source plan of an order's lines — POST …/{id}/source-plan. The
    /// decision names Satış Destek, Satış Uzmanı, Müdür and Admin; which persona holds it is a human gate (registered, granted to
    /// no role). Its own name: no other Sales name confers it and it confers none. The plan read uses the draft read
    /// (<see cref="DraftsRead"/>); no new read name. Company-wide; not Critical.
    /// </summary>
    public const string OrdersSourcePlanWrite = "sales.orders.source-plan.write";

    /// <summary>
    /// API-20 (plan §3.2, S-01): recording a sales request — POST /api/v1/sales/requests. Its own name: the draft names do NOT
    /// confer it and it confers none of them (no order, no price, no approval). Company-wide; registration is the central
    /// writer's step (not Critical); which persona holds it is a human gate — no role receives it here.
    /// </summary>
    public const string RequestsWrite = "sales.requests.write";

    /// <summary>API-20: GET /api/v1/sales/requests and GET /api/v1/sales/requests/{id} (list and detail share one read).</summary>
    public const string RequestsRead = "sales.requests.read";

    /// <summary>
    /// API-21 (delegation No 160, 161): POST …/requests/{id}/approval and …/rejection — the decision on a sales request. Its own
    /// name, separate from the order approval (<see cref="OrdersApprove"/>): neither confers the other. No maker≠checker,
    /// threshold or second approver is implied by the name. Company-wide; not Critical; no role receives it here.
    /// </summary>
    public const string RequestsApprove = "sales.requests.approve";

    /// <summary>
    /// API-22 / API-23 / API-24 (plan §3.2, S-01): recording, revising and losing a sales quote, and — together with
    /// <see cref="DraftsCreate"/> — converting it into its order (No 162). Its own name: the draft and request names do NOT
    /// confer it, and it confers none of them (a conversion still needs the draft create name, and a priced line the order's
    /// own price write). Company-wide; not Critical; no role receives it here.
    /// </summary>
    public const string QuotesWrite = "sales.quotes.write";

    /// <summary>API-22: GET /api/v1/sales/quotes and GET …/{id}. The quoted prices additionally need <see cref="OrdersPriceRead"/>.</summary>
    public const string QuotesRead = "sales.quotes.read";

    /// <summary>
    /// SAT-D24 / SAT-D44: POST …/quotes/{id}/expiry-approval — the manager approval that lets the current revision of an
    /// EXPIRED quote be converted. Its own name: the quote write, draft create and the order approval names do NOT confer it,
    /// and it confers none of them (the conversion itself still needs <see cref="QuotesWrite"/> + <see cref="DraftsCreate"/>).
    /// SAT-D44 (2): its holder may approve a quote they created or revised — an explicit P6 exception for this approval only.
    /// Company-wide; not Critical; registered, granted to no role (who receives it is a grant decision and a human gate).
    /// </summary>
    public const string QuotesExpiredConversionApprove = "sales.quotes.expired-conversion.approve";

    /// <summary>
    /// API-31 (plan §3.3, S-08 "Faturaya Hazır"; DEC-SAL-006, AT-055): GET /api/v1/sales/invoice-candidates — the posted sales
    /// shipment shares an invoice may later take, grouped by the decided key. Its own name: the order read and every other Sales
    /// name do NOT confer it, and it confers none of them (no order detail, no price, no invoice write). Company-wide; not
    /// Critical; registered, granted to no role (who receives it is a grant decision and a human gate).
    /// </summary>
    public const string InvoicesRead = "sales.invoices.read";

    /// <summary>
    /// S5 API-32 (Fatura çekirdeği 1; SAT-D7, SAT-D202): POST /api/v1/sales/invoices — prepare an invoice draft from approved
    /// shipment shares. Its own name: the invoice read, the order and price names do NOT confer it, and it confers none of them
    /// (the draft's amounts additionally need <see cref="OrdersPriceRead"/>). SAT-D202 names Muhasebe, Muhasebe Müdürü, Genel
    /// Müdür and Admin; company-wide; not Critical; registered, granted to no role (the grant is a human gate).
    /// </summary>
    public const string InvoicesWrite = "sales.invoices.write";

    /// <summary>
    /// S5 API-33 (Fatura çekirdeği 2; SAT-D88, SAT-D204): POST /api/v1/sales/invoices/{id}/approval and …/rejection. Its own name:
    /// the invoice write and read names and the order names do NOT confer it, and it confers none of them. Company-wide; not
    /// Critical; registered, granted to no role (the grant is a human gate). The preparer and the submitter never decide the same
    /// invoice — a domain rule, not a property of the name. Submitting and cancelling a draft need <see cref="InvoicesWrite"/>.
    /// </summary>
    public const string InvoicesApprove = "sales.invoices.approve";

    /// <summary>
    /// S5 API-35 (Fatura çekirdeği 3; SAT-D89, SAT-D106, SAT-D229): POST /api/v1/sales/invoices/{id}/external-match. Its own name:
    /// the invoice write, read and approve names and the order names do NOT confer it, and it confers none of them. Company-wide;
    /// not Critical; registered, granted to no role (the grant is a human gate). Approving a TL difference above the tolerance
    /// additionally needs <see cref="InvoicesApprove"/> and is never the preparer's — domain rules, not properties of the name.
    /// </summary>
    public const string InvoicesMatch = "sales.invoices.match";

    /// <summary>
    /// S5 API-36 (Fatura çekirdeği 4; SAT-D208, Sinan S6): POST /api/v1/sales/invoices/{id}/finalization and …/finance-admission (the
    /// re-drive of the Finance hand-off). Its own SEPARATE name: the invoice write, read, approve and match names and the order names do
    /// NOT confer it, and it confers none of them. Company-wide; registered, granted to no role (the grant is a human gate). It is also
    /// the authority the host measures when Finance admits the open items on Sales' behalf. Not Critical: SAT-D249 (2026-11-09,
    /// SINAN_KARARLARI_2026-11-09_SATIS_EK49.md) keeps it a normal permission and replaces SAT-D235 (1). It adds no segregation of
    /// duties (SAT-D235 (2)): whoever prepared, approved or matched the invoice may finalise it when granted (KLN-D17 on the 120
    /// voucher stays as it is).
    /// </summary>
    public const string InvoicesFinalize = "sales.invoices.finalize";

    /// <summary>
    /// S5 API-34 (SAT-D236, SAT-D244, SAT-D248): GET /api/v1/sales/invoices/export — the finalised invoices of a date range as one file
    /// for Muhasebe. Only a file: no official outbound connection. Its own SEPARATE name: the invoice read, write, approve, match and
    /// finalise names do NOT confer it, and it confers none of them; the amounts in the file additionally need
    /// <see cref="OrdersPriceRead"/> (403 without it, SAT-D238 (3)). Company-wide; not Critical; registered, granted to no role (the
    /// grant is a human gate).
    /// </summary>
    public const string InvoicesExport = "sales.invoices.export";

    /// <summary>
    /// API-41 / API-42 (plan §3.4, UI-80): GET /api/v1/sales/reports/open-orders (KPI No 124, the open order quantity), …/lost-
    /// opportunities (SAT-D25 lost quotes) and …/price-variance (No 25). Its own name: the order, quote, invoice and price names do
    /// NOT confer it, and it confers none of them — a report's amount additionally needs <see cref="OrdersPriceRead"/> (withheld
    /// otherwise). Read-only. Company-wide; not Critical; registered, granted to no role (who receives it is a grant decision and a
    /// human gate).
    /// </summary>
    public const string ReportsRead = "sales.reports.read";

    public static IReadOnlySet<string> Names { get; } =
        new[] { DraftsCreate, DraftsRead, OrdersApprove, OrdersCustomerAcceptanceRecord, OrdersPriceWrite, OrdersPriceRead,
            OrdersMarginRead, OrdersPriceFloorExceptionApprove, OrdersDiscountExceptionApprove, OrdersDeliveryScheduleWrite, OrdersCancel,
            OrdersQuantityWrite, OrdersHeaderWrite, OrdersForceClose, OrdersClose, OrdersOverDeliveryApprove, OrdersLineClose, OrdersSourcePlanWrite, RequestsWrite, RequestsRead, RequestsApprove, QuotesWrite, QuotesRead,
            QuotesExpiredConversionApprove, InvoicesRead, InvoicesWrite, InvoicesApprove, InvoicesMatch, InvoicesFinalize, InvoicesExport, ReportsRead }
            .ToFrozenSet(StringComparer.Ordinal);
}

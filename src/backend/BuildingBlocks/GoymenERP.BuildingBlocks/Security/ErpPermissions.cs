namespace GoymenERP.BuildingBlocks.Security;

public static class ErpPermissions
{
    public const string OrganizationContextRead = "organization.context.read";
    public const string OrganizationSitesRead = "organization.sites.read";
    public const string OrganizationLocationsRead = "organization.locations.read";
    public const string OrganizationWorkCentersRead = "organization.work-centers.read";
    public const string OrganizationLocationsStatusUpdate =
        "organization.locations.status.update";

    /// <summary>
    /// Organization master edit (create and edit sites, locations and work centres), the owner's
    /// OrganizationPermissionManifestV1 names. Sites write is company-wide only (a site has no parent inside the
    /// company); locations and work-centres write are scoped like <see cref="OrganizationLocationsStatusUpdate"/>.
    /// None is Critical: no source makes a master-data edit a grant-time dual-control duty. The user's decision of
    /// 2026-09-27 grants them only to a disposable org03 browser actor; the real-user mapping stays a human gate.
    /// </summary>
    public const string OrganizationSitesWrite = "organization.sites.write";
    public const string OrganizationLocationsWrite = "organization.locations.write";
    public const string OrganizationWorkCentersWrite = "organization.work-centers.write";

    public const string CounterpartyPartiesRead = "counterparty.parties.read";
    public const string CounterpartyPartiesCreate = "counterparty.parties.create";
    public const string CounterpartyPartiesUpdateBasic = "counterparty.parties.update.basic";
    public const string CounterpartyAccountsCreate = "counterparty.accounts.create";
    public const string CounterpartyAccountsStatusUpdate =
        "counterparty.accounts.status.update";

    /// <summary>
    /// The narrow operational product-selection read (K-1(a), Sinan's decision relayed in Business
    /// #66 5719251344; registration asked for in 5720016574 §3). Registering the name makes it
    /// KNOWN, not granted: no role and no user receives it here, and an actor without an explicit
    /// assignment still gets <see cref="PermissionScope.Denied"/>. It is not the Product master
    /// read and carries no master management, cost/price history, audit or unbounded listing.
    /// Company-wide scope is required: the read answers for the server-resolved company and has no
    /// site, location or work-center dimension, so a narrower assignment would have nothing to mean
    /// (same reason the Counterparty reads are company-wide only).
    /// </summary>
    public const string ProductOperationalSelectionRead = "product.operational-selection.read";

    /// <summary>
    /// T07(a) Inventory reads (#310): a label's view and its stock position by position. Registering
    /// the names makes them KNOWN, not granted: no role and no user receives them here; the grant is
    /// a human gate. Company-wide scope is required because the reads are not filtered by site or
    /// location — a site-scoped assignment would otherwise see every location's stock of the company.
    /// Not critical: read-only, no stock, money or identity effect.
    /// </summary>
    public const string InventoryLabelsRead = "inventory.labels.read";

    /// <inheritdoc cref="InventoryLabelsRead"/>
    public const string InventoryBalancesRead = "inventory.balances.read";

    /// <summary>
    /// INV-OPEN-1 part 2: record (and read back) an opening count, the Inventory owner's name
    /// (InventoryPermissionManifestV1.OpeningStockRecord). Sinan's T07 = A (#66 5738113623 §2) decides that
    /// one narrowly authorized user records the opening; the name is the backend owners' technical mapping
    /// (Business #254 5654539215), confirmed by the coordinator. Registered, not granted: no role, seed or
    /// user receives it here. Company-wide only: the count names any location of the company. Not
    /// Critical, as manufacturing.facts.record: a record-type name, and no source makes it a segregated
    /// duty (T07 = A has no maker≠checker). The opening user also needs product.operational-selection.read
    /// for the per-line eligibility (a human grant pairing).
    /// </summary>
    public const string InventoryOpeningStockRecord = "inventory.opening-stock.record";

    /// <summary>
    /// STK/T11: create and explicitly release a physical allocation. This Inventory-owned command
    /// permission is deliberately separate from Sales order entry/approval, Logistics writes and the
    /// Inventory read/opening permissions. Registration grants nothing; no seeded role or actor receives
    /// it here. Company-wide only because the allocation command has no narrower scope dimension. It is
    /// not Critical: no approved source adds grant-time dual control for this name.
    /// </summary>
    public const string InventoryPhysicalAllocationWrite = "inventory.physical-allocation.write";

    /// <summary>
    /// R19 / AT-038: record one same-company label split or merge (DEC-LBL-002/003), the Inventory owner's
    /// name (<c>InventoryPermissionManifestV1.LabelTransformationRecord</c>, same string). The literal is the
    /// owner's technical mapping, confirmed by the coordinator (session decision 2026-09-27; technical names are
    /// not Sinan's, #66/5808295853 and #66/5811361336); a separate Inventory-owned command name follows the
    /// STK/T11 separation of #66/5806047097 by analogy only. Registration grants nothing: no seeded role and no
    /// real user receives it; its only grant is a disposable R19 browser test actor in the per-run database, and
    /// the real-user mapping stays with the persona card #66/5802952566. Company-wide only: the command names
    /// labels of the server-resolved company and has no narrower scope dimension. Not Critical: no approved
    /// source adds grant-time dual control for it.
    /// </summary>
    public const string InventoryLabelTransformationRecord = "inventory.label-transformation.record";

    /// <summary>
    /// AT-037 / DEC-STK-007 stock count (user decision 2026-09-27): <c>record</c> enters a counted quantity with a
    /// mandatory reason on a label's single position; <c>approve</c> decides a pending difference. Until the
    /// critical-difference threshold is decided (INV-R20-R21-01 Q2, #66/5840984992) every non-zero difference needs
    /// an actor other than its counter (fail-closed maker≠checker). No seeded role or real user receives either name;
    /// the only grants are two different disposable r14 browser test actors, and the real-user mapping stays with the
    /// persona card. Company-wide only, as the label commands. Not Critical: no approved source adds grant-time dual
    /// control for them.
    /// </summary>
    public const string InventoryStockCountRecord = "inventory.stock-count.record";

    /// <inheritdoc cref="InventoryStockCountRecord"/>
    public const string InventoryStockCountApprove = "inventory.stock-count.approve";

    /// <summary>
    /// ST-02 scrap write-off (hurda/fire çıkışı), the stock-count pattern: <c>record</c> enters the scrapped kg of a
    /// label's single position with a mandatory reason; <c>approve</c> decides it and only then posts one Scrap
    /// movement. Until the scrap approval authority is decided every scrap needs an actor other than its recorder
    /// (fail-closed maker≠checker). No seeded role or real user receives either name; the only grants are two
    /// different disposable r14 browser test actors. Company-wide only. Not Critical.
    /// </summary>
    public const string InventoryStockScrapRecord = "inventory.stock-scrap.record";

    /// <inheritdoc cref="InventoryStockScrapRecord"/>
    public const string InventoryStockScrapApprove = "inventory.stock-scrap.approve";

    /// <summary>
    /// Finance bank-account master (FS0, #312): names are the Finance owner's technical mapping
    /// (Business #254 5654539215). Registering them makes them KNOWN, not granted: no role and no
    /// user receives them here, and who holds which is a human gate. All three are company-wide
    /// only: a bank account belongs to the server-resolved company and has no site, location or
    /// work-center dimension. Propose and approve are Critical: the security contract lists the
    /// bank account among critical permissions (docs/security/AI_YETKI_SOZLESMESI.md) and Sinan's
    /// Ö4 (#66 5722637822) makes the approval maker-checker, so their GRANT follows ADR-004 §6
    /// (the requester or target cannot approve their own request). Read is not Critical: it shows
    /// only the label and currency of approved and proposed accounts (a proposal is listed, not
    /// usable, so its approver can see it).
    /// </summary>
    public const string FinanceBankAccountRead = "finance.bank-account.read";
    public const string FinanceBankAccountPropose = "finance.bank-account.propose";
    public const string FinanceBankAccountApprove = "finance.bank-account.approve";

    /// <summary>
    /// Manufacturing API (MFG-API, #313): the owner's technical mapping of its routes (Business
    /// #254 5654539215), accepted by the coordinator (K-1). Registering the names makes them KNOWN,
    /// not granted: no role and no user receives them here; the grant is a human gate (K-3).
    /// Company-wide scope is required (K-1): the routes do not filter by work center, so a
    /// work-center-scoped assignment would otherwise reach every work center of the company. Not
    /// Critical: no source makes plan creation and approval a segregated duty (K-2).
    /// </summary>
    public const string ManufacturingPlansWrite = "manufacturing.plans.write";

    /// <inheritdoc cref="ManufacturingPlansWrite"/>
    public const string ManufacturingPlansApprove = "manufacturing.plans.approve";

    /// <inheritdoc cref="ManufacturingPlansWrite"/>
    public const string ManufacturingOrdersCreate = "manufacturing.orders.create";

    /// <inheritdoc cref="ManufacturingPlansWrite"/>
    public const string ManufacturingFactsRecord = "manufacturing.facts.record";

    /// <inheritdoc cref="ManufacturingPlansWrite"/>
    public const string ManufacturingOrdersRead = "manufacturing.orders.read";

    /// <summary>
    /// The order's close (MFG-ORD, #66 5743488059): its own name, because recording a fact is not
    /// closing an order — a holder of <see cref="ManufacturingFactsRecord"/> does not thereby end the
    /// order's WIP. Registered, not granted, and company-wide only for the reason above (K-1). Not
    /// Critical: no source names the close a segregated duty (K-2); if one does, the flag is a human
    /// decision, not this file's.
    /// </summary>
    public const string ManufacturingOrdersClose = "manufacturing.orders.close";

    /// <summary>
    /// K4.4 (ORG): reading one production plan back. Its own name because approving a plan is not
    /// reading one, and because until now a plan could be created and approved but **never read
    /// again** — there was no plan read at any layer above the store. Registered, not granted.
    /// <para>
    /// COMPANY-WIDE ONLY, and the reason is the OPPOSITE of the Sales price-read's — written out
    /// because the reason is what the next module inherits, not the membership. Manufacturing
    /// narrows no read by scope: <c>ManufacturingEndpoints</c> contains ZERO occurrences of
    /// <c>IsCompanyWide</c> (measured), and the module's only two scope checks are command fences.
    /// So a site-scoped grant of this name would pass policy and then return EVERY plan of the
    /// company — it would fail OPEN. Membership is what stops that:
    /// <c>ErpAuthorizationService</c> denies a non-company-wide grant of any name in
    /// <see cref="CompanyWideOnly"/>. Contrast <see cref="SalesOrdersPriceRead"/>, which is in the
    /// same set for the reverse hazard: there the module DOES require company-wide, so a narrow
    /// grant would be accepted and then disclose nothing — a dead grant, failing closed. Same
    /// membership, opposite failure; "consistent with the others" would have been the wrong reason.
    /// </para>
    /// <para>
    /// Not Critical: Critical is the grant-time dual-control set, no source makes SEEING a plan a
    /// segregated duty, and <see cref="ManufacturingPlansApprove"/> — the heavier act — is not
    /// Critical either.
    /// </para>
    /// </summary>
    public const string ManufacturingPlansRead = "manufacturing.plans.read";

    /// <summary>
    /// Finance manual-receipt draft (FIN-T04b, #320): the Finance owner's technical names
    /// (FinancePermissionNames, Business #254 5654539215), same strings. Registered, not granted:
    /// no role and no user receives them here; who holds which is a human gate. All three are
    /// company-wide only: drafts, sources and amounts belong to the server-resolved company and the
    /// routes filter by nothing narrower. None is Critical: a draft moves no money and grants no
    /// authority. Record, which does move money, now has its own name below (FIN-T05); Reverse still
    /// has none (FIN-T06, its authority still OPEN-009 — a human decision).
    /// </summary>
    public const string FinanceManualReceiptDraftSave = "finance.manual-receipt.draft.save";

    /// <inheritdoc cref="FinanceManualReceiptDraftSave"/>
    public const string FinanceManualReceiptDraftRead = "finance.manual-receipt.draft.read";

    /// <inheritdoc cref="FinanceManualReceiptDraftSave"/>
    public const string FinanceManualReceiptAmountRead = "finance.manual-receipt.amount.read";

    /// <summary>
    /// FIN-T05: making a manual bank receipt definitive — the Finance owner's name
    /// (<c>FinancePermissionNames.Record</c>, same string), registered here so the port can measure it.
    /// <para>
    /// FIRST, BECAUSE IT IS READ WRONG OTHERWISE: <see cref="Critical"/> governs GRANT-TIME dual control
    /// (ADR-004 §6: the requester or the target may not approve their own grant), not command-time
    /// behaviour. It does not add an approval step to the command, and it is not a "this is dangerous"
    /// marker. Record creates a money event, which makes the classification worth arguing — but the
    /// question it answers is "must two people agree to GRANT this?", and no source decides that: no
    /// maker≠checker, no threshold, no second person is named anywhere for recording a receipt. So it is
    /// NOT Critical, for the same reason as the rest, and the lever is written down rather than pulled:
    /// if Sinan wants a record grant to need two people, Critical is where that decision lands.
    /// </para>
    /// <para>
    /// Company-wide only, argued from what the permission MEANS: a record writes a money event against
    /// the company's own treasury account and its own books; the command carries a company and a source
    /// and has no site, location or work-center dimension at all, so a narrower assignment would have
    /// nothing to be narrower ABOUT. (The port also refuses a non-company-wide scope — corroboration,
    /// not the reason: an implementation detail must not decide an authority classification, or the day
    /// the port changes the classification has nothing left holding it up.)
    /// </para>
    /// Registered, not granted: no seeded role and no user receives it, and who may record is a human
    /// gate. Registration is NOT behaviour-neutral here, unlike a name an endpoint already required: it
    /// moves Record from the port's "name unknown → Unavailable" branch, which asks the authority
    /// nothing, to "name known → measured", which asks and today answers Denied. Both refuse; the honest
    /// one is the second, because the capability now exists and the actor simply does not hold it.
    /// </summary>
    public const string FinanceManualReceiptRecord = "finance.manual-receipt.record";
    public const string FinanceManualReceiptReversePropose = "finance.manual-receipt.reverse.propose";
    public const string FinanceManualReceiptReverseApprove = "finance.manual-receipt.reverse.approve";

    /// <summary>
    /// R47 manual outgoing bank payments. Registration makes these names measurable; it grants
    /// nothing. The records are company-wide and the service revalidates each action afresh.
    /// </summary>
    public const string FinanceManualBankPaymentDraftSave = "finance.manual-bank-payment.draft.save";
    public const string FinanceManualBankPaymentDraftRead = "finance.manual-bank-payment.draft.read";
    public const string FinanceManualBankPaymentApprove = "finance.manual-bank-payment.approve";
    public const string FinanceManualBankPaymentRecord = "finance.manual-bank-payment.record";

    /// <summary>
    /// DEC-FIN-016 customer cheque / promissory-note portfolio: the Finance owner's names
    /// (FinancePermissionNames, same strings). Registration makes them measurable; it grants nothing — the
    /// user's decision of 2026-09-27 gives them only to the disposable fin01 browser actor, and the real-user
    /// mapping stays a human gate. Company-wide only: the portfolio belongs to the server-resolved company and
    /// has no narrower scope dimension. Not Critical: no source makes receiving, a status change or the
    /// collection a grant-time dual-control duty. The customer is credited at collection (user decision
    /// 2026-09-27); the risk view reads under the read name and discloses amounts only with the amount name.
    /// </summary>
    public const string FinanceCustomerInstrumentRead = "finance.customer-instrument.read";
    public const string FinanceCustomerInstrumentAmountRead = "finance.customer-instrument.amount.read";
    public const string FinanceCustomerInstrumentReceive = "finance.customer-instrument.receive";
    public const string FinanceCustomerInstrumentStatusChange = "finance.customer-instrument.status.change";
    public const string FinanceCustomerInstrumentCollect = "finance.customer-instrument.collect";

    /// <summary>
    /// Finance party ledger (cari hesap ekstresi): the Finance owner's names (FinancePermissionNames, same strings).
    /// Registration makes them measurable; it grants nothing — the user's decision of 2026-09-28 gives them only to
    /// the disposable fin01 browser actor, and the real-user mapping stays a human gate. Company-wide only: the
    /// ledger reads the server-resolved company's recorded facts. Not Critical: reads only; amounts are disclosed
    /// only with the amount name.
    /// </summary>
    public const string FinancePartyLedgerRead = "finance.party-ledger.read";
    public const string FinancePartyLedgerAmountRead = "finance.party-ledger.amount.read";

    /// <summary>
    /// F-03 allocation commands. Registration makes the Finance owner's permission name available
    /// to endpoint authorization; it grants no role or user access.
    /// </summary>
    public const string FinanceAllocationWrite = "finance.allocation.write";

    /// <summary>
    /// Sales order drafts (SALES-API, #321): the Sales owner's names (SalesPermissionManifestV1, same
    /// strings). Registered, not granted: no seeded role and no user receives them; who holds them is a
    /// human gate. Company-wide only: a draft belongs to the server-resolved company and the routes filter
    /// by nothing narrower (as the Counterparty reads). Not Critical: a draft commits no stock, money or
    /// price (T28/T33 are out of scope) and grants no authority.
    /// </summary>
    public const string SalesDraftsCreate = "sales.drafts.create";

    /// <inheritdoc cref="SalesDraftsCreate"/>
    public const string SalesDraftsRead = "sales.drafts.read";

    /// <summary>
    /// SALES-A1 (Business #66 5737989917): the internal approval decision on a sales order draft
    /// (POST …/approval and …/rejection), the Sales owner's name (SalesPermissionManifestV1.OrdersApprove).
    /// Registered, not granted: no seeded role and no user receives it; who approves is a human gate
    /// (Sinan's persona decision 2026-09-19 names the Müdür, applied only by a later grant card).
    /// Company-wide only, like the draft names. Not Critical, as manufacturing.plans.approve: no source
    /// makes the approval a segregated duty (no maker≠checker, threshold or second person is decided),
    /// and an approved draft commits no stock, money or price.
    /// </summary>
    public const string SalesOrdersApprove = "sales.orders.approve";

    /// <summary>
    /// SALES-P (K3, Sinan #66 5748258299, closing 5745151414): writing a manual unit price or a discount
    /// on a sales order line, the Sales owner's name (SalesPermissionManifestV1.OrdersPriceWrite, same
    /// string). A SEPARATE name on purpose: <see cref="SalesDraftsCreate"/> does not confer it, so whoever
    /// may raise a draft does not thereby set its commercial terms. Registered, not granted: no seeded role
    /// and no user receives it; who prices is a human gate. Company-wide only, like the other Sales names.
    /// Not Critical — Critical is the grant-time dual-control set (identity governance and the bank account
    /// propose/approve pair), and no source makes pricing a segregated duty: no maker≠checker, no approval
    /// threshold and no second person is decided, and the module validates arithmetic only (scale, unit,
    /// 0..100) with no minimum price, margin floor or discount ceiling. If Sinan later wants a price grant
    /// to need two people, Critical is the lever and that is his decision, not one taken here.
    /// </summary>
    public const string SalesOrdersPriceWrite = "sales.orders.price.write";

    /// <summary>
    /// SALES-P2 (K3, Sinan #66 5748258299), the Sales owner's name (SalesPermissionManifestV1.OrdersPriceRead,
    /// same string). It governs DISCLOSURE, not reachability, and that is why it needed a name of its own:
    /// <see cref="SalesDraftsRead"/> already shows the row, so without a second name there would be no way to
    /// show a Sevkiyatçı the line while withholding its price — which Sinan's acceptance requires in those
    /// words ("Sevkiyatçıya fiyat/iskonto/tutar sızmaz"), and whose other half he states as "Sipariş onayının
    /// fiyat/tutar bağlamı ilgili dar fiyat okumasıyla sağlanır; bu maliyet/marj/banka sınırsız okuması
    /// değildir". A narrow read, then: the line's unit price, price unit and discount, and nothing adjacent —
    /// no cost, margin, bank or Finance read.
    /// <para>
    /// What a holder without it sees is not an omission but an ANSWER: the three commercial fields come back
    /// null beside a never-null <c>PriceDisclosure: Withheld</c>. The owner's contract carries the reason —
    /// an absent price cannot otherwise be told apart from a price of nothing, the same argument that made
    /// <c>canOpenLocationDetail</c> required (#377).
    /// </para>
    /// <para>
    /// Company-wide only, matching what the module actually asks: its disclosure check requires a grant that
    /// <c>IsCompanyWide</c>, so a site-scoped grant would be accepted at grant time and then silently never
    /// disclose. The set makes the catalogue refuse what the code would not honour.
    /// </para>
    /// <para>
    /// Not Critical, for the reason spelled out on <see cref="SalesOrdersPriceWrite"/>: Critical is the
    /// GRANT-TIME dual-control set, and no source makes seeing a price a segregated duty.
    /// </para>
    /// <para>
    /// REGISTERING IS NOT IMPLYING, and this row was checked against the opposite reading: holding
    /// <see cref="SalesOrdersPriceWrite"/> does not confer this name. Nothing here builds an implication —
    /// membership of <see cref="All"/> only makes the name KNOWN (the module throws 503 on an unregistered
    /// one rather than disclosing), and membership of <see cref="CompanyWideOnly"/> only narrows how it may
    /// be granted. The module asks for this name specifically. Whether a pricer should also see prices is a
    /// grant, and Sinan is silent on it, so it stays a human gate rather than a side effect.
    /// </para>
    /// </summary>
    public const string SalesOrdersPriceRead = "sales.orders.price.read";

    /// <summary>
    /// DEC-SAL-003 (KILITLI): moving the requested delivery dates of an order (POST …/{id}/delivery-schedule) without
    /// re-approval, audited — the Sales owner's name (<c>SalesPermissionManifestV1.OrdersDeliveryScheduleWrite</c>, same
    /// string). The literal is the owner's technical mapping, confirmed by the coordinator (technical names are not
    /// Sinan's, #66/5808295853 and #66/5811361336). A SEPARATE name because the decision gives the update to the
    /// planning/shipping role: <see cref="SalesDraftsCreate"/>, <see cref="SalesOrdersApprove"/> and the price names do
    /// not confer it, and it confers none of them. Registration grants nothing: no seeded role and no real user receives
    /// it; its only grant (user decision 2026-09-27) is the disposable s04 planner in the per-run browser database, and
    /// the real-user mapping stays a human gate. Company-wide only, like the other Sales names. Not Critical: no approved
    /// source adds grant-time dual control for it.
    /// </summary>
    public const string SalesOrdersDeliveryScheduleWrite = "sales.orders.delivery-schedule.write";

    /// <summary>
    /// DEC-SAL-005 / AT-007: cancel an approved order after its impact preview (Sales owner name
    /// SalesPermissionManifestV1.OrdersCancel). Its own name: create, approve, price and delivery-schedule names do not
    /// confer it, and it confers none of them. Registration grants nothing: its only grant (user decision 2026-09-27) is
    /// the disposable s04 canceller in the per-run browser database; the real-user mapping stays a human gate.
    /// Company-wide only, like the other Sales names. Not Critical: the user decided a single authorised person cancels
    /// (2026-09-27); no approved source adds grant-time dual control.
    /// </summary>
    public const string SalesOrdersCancel = "sales.orders.cancel";

    /// <summary>
    /// DEC-SAL-002 / AT-023: revise an approved order's line quantities into a new revision that needs re-approval (Sales
    /// owner name SalesPermissionManifestV1.OrdersQuantityWrite). Its own name: create, approve, price, delivery-schedule
    /// and cancel names do not confer it. Registration grants nothing: its only grant (user decision 2026-09-28) is the
    /// disposable s04 preparer in the per-run browser database; the real-user mapping stays a human gate. Company-wide
    /// only, like the other Sales names. Not Critical: the revision itself returns to the approval flow.
    /// </summary>
    public const string SalesOrdersQuantityWrite = "sales.orders.quantity.write";

    /// <summary>
    /// LOG-4a (Business #66 5737790175: the loading instruction on approved order lines, one stock effect): the
    /// Logistics owner's names (LogisticsPermissionManifestV1), the backend owners' technical mapping (Business
    /// #254 5654539215) recorded and confirmed by the coordinator (#66 5743488059). Registered, not granted: no
    /// role, seed or user receives them here (Sinan #66 5738113623 §4: personas deny-first, no real
    /// assignment). Company-wide only: an instruction names the company's customer and order lines. Not
    /// Critical: no source makes any of them a segregated duty (no maker≠checker, threshold or second person
    /// is decided); a loaded share is a record-type name, as manufacturing.facts.record.
    /// </summary>
    public const string LogisticsLoadingInstructionsCreate = "logistics.loading-instructions.create";

    /// <inheritdoc cref="LogisticsLoadingInstructionsCreate"/>
    public const string LogisticsLoadingInstructionsRead = "logistics.loading-instructions.read";

    /// <inheritdoc cref="LogisticsLoadingInstructionsCreate"/>
    public const string LogisticsLoadsRecord = "logistics.loads.record";

    /// <inheritdoc cref="LogisticsLoadingInstructionsCreate"/>
    public const string LogisticsShipmentsRecord = "logistics.shipments.record";

    /// <inheritdoc cref="LogisticsLoadingInstructionsCreate"/>
    public const string LogisticsDeliveriesRecord = "logistics.deliveries.record";

    /// <inheritdoc cref="LogisticsLoadingInstructionsCreate"/>
    public const string LogisticsWeighingsRecord = "logistics.weighings.record";

    /// <summary>
    /// AT-025: the operations work list's two SEPARATE section reads (LogisticsPermissionManifestV1) — the sales section (L-01) and
    /// the purchase section (L-09), each its own name so that neither implies the other (Sinan #66 5738113623 §4: each read its own
    /// name, deny-first). Registered, not granted: no role, seed or user receives them here. Company-wide only. Not Critical.
    /// </summary>
    public const string LogisticsOperationsSalesWorkRead = "logistics.operations-work.sales.read";

    /// <inheritdoc cref="LogisticsOperationsSalesWorkRead"/>
    public const string LogisticsOperationsPurchaseWorkRead = "logistics.operations-work.purchase.read";

    /// <summary>
    /// PUR-4a (Business #66 5737790175: the purchase supply link of the core chain): the Purchasing owner's names
    /// (PurchasingPermissionManifestV1), the backend owners' technical mapping (Business #254 5654539215) recorded
    /// and confirmed by the coordinator (#66 5743488059). Read, create and approve are separate names so that
    /// entering and approving can be different personas (Sinan #66 5738113623 §4: deny-first, read and write
    /// apart); purchase requests use the create/read names (the owner's ASSUMPTION-PUR4-1). Registered, not
    /// granted: no role, seed or user receives them here. Company-wide only: an order names the company's
    /// supplier account. Not Critical (#66 5743488059): no source makes the approval a segregated duty (no
    /// maker≠checker, threshold or second person is decided).
    /// </summary>
    public const string PurchasingOrdersCreate = "purchasing.orders.create";

    /// <inheritdoc cref="PurchasingOrdersCreate"/>
    public const string PurchasingOrdersRead = "purchasing.orders.read";

    /// <inheritdoc cref="PurchasingOrdersCreate"/>
    public const string PurchasingOrdersApprove = "purchasing.orders.approve";

    /// <summary>Records a physical receipt; registered separately from order create/read/approve and grants nothing.</summary>
    public const string PurchasingGoodsReceiptsRecord = "purchasing.goods-receipts.record";

    /// <summary>
    /// ACC-V0a: the Accounting owner's names (AccountingPermissionManifestV1), the backend owners' technical mapping
    /// (Business #254 5654539215) recorded and confirmed by the coordinator (#66 5743488059). Each read is its own
    /// narrow name and writing is apart from reading (Sinan #66 5738113623 §4); approving a period opening is for a
    /// person other than its preparer (Ö4, #66 5722637822), which the Accounting service enforces on the command, not
    /// through this catalogue. Registered, not granted: no role, seed or user receives them here. Company-wide only:
    /// a period belongs to its company. Not Critical (#66 5743488059).
    /// </summary>
    public const string AccountingPeriodsRead = "accounting.periods.read";

    /// <inheritdoc cref="AccountingPeriodsRead"/>
    public const string AccountingPeriodsPropose = "accounting.periods.propose";

    /// <inheritdoc cref="AccountingPeriodsRead"/>
    public const string AccountingPeriodsApprove = "accounting.periods.approve";

    /// <inheritdoc cref="AccountingPeriodsRead"/>
    public const string AccountingPeriodsClose = "accounting.periods.close";

    /// <inheritdoc cref="AccountingPeriodsRead"/>
    public const string AccountingJournalsRead = "accounting.journals.read";

    /// <inheritdoc cref="AccountingPeriodsRead"/>
    public const string AccountingPostingQueuesRead = "accounting.posting-queues.read";

    /// <summary>
    /// ACC-V1 normal journal draft actions. Registration makes the Accounting owner's exact names
    /// measurable and grants nothing. Both are company-wide because a draft belongs to the
    /// server-resolved company and has no narrower scope dimension. They are not Critical: the
    /// approved normal-journal policy permits a narrowly authorized preparer to approve their own
    /// normal draft; reversal, period and money maker-checker boundaries are unchanged.
    /// </summary>
    public const string AccountingNormalJournalDraftCreate = "accounting.normal-journal.draft.create";

    /// <inheritdoc cref="AccountingNormalJournalDraftCreate"/>
    public const string AccountingNormalJournalDraftApprove = "accounting.normal-journal.draft.approve";

    /// <summary>
    /// ACC reversal proposal and approval actions. Registration makes the Accounting owner's exact
    /// names measurable and grants nothing. Both are company-wide because a journal and its reversal
    /// proposal belong to the server-resolved company and have no narrower scope dimension. They are
    /// not Critical: command-time maker-checker is enforced by the Accounting lifecycle, while this
    /// catalogue does not introduce a grant-time dual-control policy.
    /// </summary>
    public const string AccountingJournalReversePropose = "accounting.journal.reverse.propose";

    /// <inheritdoc cref="AccountingJournalReversePropose"/>
    public const string AccountingJournalReverseApprove = "accounting.journal.reverse.approve";

    public const string IdentityProfileRead = "identity.profile.read";
    public const string IdentityAccessRequest = "identity.access.request";
    public const string IdentityAccessApprove = "identity.access.approve";
    public const string IdentityRolesManage = "identity.roles.manage";
    public const string IdentityAuditRead = "identity.audit.read";
    /// <summary>
    /// SY-01/SY-02 read-only access view: who holds which role, permission and scope in the company. Critical and
    /// company-wide only (user decision 2026-09-28); no seeded role holds it.
    /// </summary>
    public const string IdentityAssignmentsRead = "identity.assignments.read";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [
            OrganizationContextRead,
            OrganizationSitesRead,
            OrganizationLocationsRead,
            OrganizationWorkCentersRead,
            OrganizationLocationsStatusUpdate,
            OrganizationSitesWrite,
            OrganizationLocationsWrite,
            OrganizationWorkCentersWrite,
            CounterpartyPartiesRead,
            CounterpartyPartiesCreate,
            CounterpartyPartiesUpdateBasic,
            CounterpartyAccountsCreate,
            CounterpartyAccountsStatusUpdate,
            ProductOperationalSelectionRead,
            SalesDraftsCreate,
            SalesDraftsRead,
            SalesOrdersApprove,
            SalesOrdersPriceWrite,
            SalesOrdersPriceRead,
            SalesOrdersDeliveryScheduleWrite,
            SalesOrdersCancel,
            SalesOrdersQuantityWrite,
            ManufacturingPlansWrite,
            ManufacturingPlansApprove,
            ManufacturingOrdersCreate,
            ManufacturingFactsRecord,
            ManufacturingOrdersRead,
            ManufacturingOrdersClose,
            ManufacturingPlansRead,
            InventoryLabelsRead,
            InventoryBalancesRead,
            InventoryOpeningStockRecord,
            InventoryPhysicalAllocationWrite,
            InventoryLabelTransformationRecord,
            InventoryStockCountRecord,
            InventoryStockCountApprove,
            InventoryStockScrapRecord,
            InventoryStockScrapApprove,
            LogisticsLoadingInstructionsCreate,
            LogisticsLoadingInstructionsRead,
            LogisticsLoadsRecord,
            LogisticsShipmentsRecord,
            LogisticsDeliveriesRecord,
            LogisticsWeighingsRecord,
            LogisticsOperationsSalesWorkRead,
            LogisticsOperationsPurchaseWorkRead,
            PurchasingOrdersCreate,
            PurchasingOrdersRead,
            PurchasingOrdersApprove,
            PurchasingGoodsReceiptsRecord,
            AccountingPeriodsRead,
            AccountingPeriodsPropose,
            AccountingPeriodsApprove,
            AccountingPeriodsClose,
            AccountingJournalsRead,
            AccountingPostingQueuesRead,
            AccountingNormalJournalDraftCreate,
            AccountingNormalJournalDraftApprove,
            AccountingJournalReversePropose,
            AccountingJournalReverseApprove,
            FinanceBankAccountRead,
            FinanceBankAccountPropose,
            FinanceBankAccountApprove,
            FinanceManualReceiptDraftSave,
            FinanceManualReceiptDraftRead,
            FinanceManualReceiptAmountRead,
        FinanceManualReceiptRecord,
        FinanceManualReceiptReversePropose,
        FinanceManualReceiptReverseApprove,
        FinanceManualBankPaymentDraftSave,
        FinanceManualBankPaymentDraftRead,
        FinanceManualBankPaymentApprove,
        FinanceManualBankPaymentRecord,
        FinanceCustomerInstrumentRead,
        FinanceCustomerInstrumentAmountRead,
        FinanceCustomerInstrumentReceive,
        FinanceCustomerInstrumentStatusChange,
        FinanceCustomerInstrumentCollect,
        FinancePartyLedgerRead,
        FinancePartyLedgerAmountRead,
        FinanceAllocationWrite,
            IdentityProfileRead,
            IdentityAccessRequest,
            IdentityAccessApprove,
            IdentityRolesManage,
            IdentityAuditRead,
            IdentityAssignmentsRead,
        ],
        StringComparer.Ordinal);

    public static IReadOnlySet<string> Critical { get; } = new HashSet<string>(
        [
            IdentityAccessApprove,
            IdentityRolesManage,
            IdentityAuditRead,
            IdentityAssignmentsRead,
            FinanceBankAccountPropose,
            FinanceBankAccountApprove,
        ],
        StringComparer.Ordinal);

    public static IReadOnlySet<string> CompanyWideOnly { get; } = new HashSet<string>(
        [
            OrganizationSitesWrite,
            CounterpartyPartiesRead,
            CounterpartyPartiesCreate,
            CounterpartyPartiesUpdateBasic,
            CounterpartyAccountsCreate,
            CounterpartyAccountsStatusUpdate,
            ProductOperationalSelectionRead,
            SalesDraftsCreate,
            SalesDraftsRead,
            SalesOrdersApprove,
            SalesOrdersPriceWrite,
            SalesOrdersPriceRead,
            SalesOrdersDeliveryScheduleWrite,
            SalesOrdersCancel,
            SalesOrdersQuantityWrite,
            ManufacturingPlansWrite,
            ManufacturingPlansApprove,
            ManufacturingOrdersCreate,
            ManufacturingFactsRecord,
            ManufacturingOrdersRead,
            ManufacturingOrdersClose,
            ManufacturingPlansRead,
            InventoryLabelsRead,
            InventoryBalancesRead,
            InventoryOpeningStockRecord,
            InventoryPhysicalAllocationWrite,
            InventoryLabelTransformationRecord,
            InventoryStockCountRecord,
            InventoryStockCountApprove,
            InventoryStockScrapRecord,
            InventoryStockScrapApprove,
            LogisticsLoadingInstructionsCreate,
            LogisticsLoadingInstructionsRead,
            LogisticsLoadsRecord,
            LogisticsShipmentsRecord,
            LogisticsDeliveriesRecord,
            LogisticsWeighingsRecord,
            LogisticsOperationsSalesWorkRead,
            LogisticsOperationsPurchaseWorkRead,
            PurchasingOrdersCreate,
            PurchasingOrdersRead,
            PurchasingOrdersApprove,
            PurchasingGoodsReceiptsRecord,
            AccountingPeriodsRead,
            AccountingPeriodsPropose,
            AccountingPeriodsApprove,
            AccountingPeriodsClose,
            AccountingJournalsRead,
            AccountingPostingQueuesRead,
            AccountingNormalJournalDraftCreate,
            AccountingNormalJournalDraftApprove,
            AccountingJournalReversePropose,
            AccountingJournalReverseApprove,
            FinanceBankAccountRead,
            FinanceBankAccountPropose,
            FinanceBankAccountApprove,
            FinanceManualReceiptDraftSave,
            FinanceManualReceiptDraftRead,
            FinanceManualReceiptAmountRead,
        FinanceManualReceiptRecord,
        FinanceManualReceiptReversePropose,
        FinanceManualReceiptReverseApprove,
        FinanceManualBankPaymentDraftSave,
        FinanceManualBankPaymentDraftRead,
        FinanceManualBankPaymentApprove,
        FinanceManualBankPaymentRecord,
        FinanceCustomerInstrumentRead,
        FinanceCustomerInstrumentAmountRead,
        FinanceCustomerInstrumentReceive,
        FinanceCustomerInstrumentStatusChange,
        FinanceCustomerInstrumentCollect,
        FinancePartyLedgerRead,
        FinancePartyLedgerAmountRead,
        FinanceAllocationWrite,
            IdentityAuditRead,
            IdentityAssignmentsRead,
        ],
        StringComparer.Ordinal);

    public static bool IsKnown(string permission) => All.Contains(permission);

    public static bool IsCritical(string permission) => Critical.Contains(permission);

    public static bool RequiresCompanyWideScope(string permission) =>
        CompanyWideOnly.Contains(permission);
}

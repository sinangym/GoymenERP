namespace GoymenERP.BuildingBlocks.Security;

public static class ErpPermissions
{
    public const string OrganizationContextRead = "organization.context.read";
    public const string OrganizationSitesRead = "organization.sites.read";
    public const string OrganizationLocationsRead = "organization.locations.read";
    public const string OrganizationWorkCentersRead = "organization.work-centers.read";
    public const string OrganizationLocationsStatusUpdate =
        "organization.locations.status.update";

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
    /// Finance manual-receipt draft (FIN-T04b, #320): the Finance owner's technical names
    /// (FinancePermissionNames, Business #254 5654539215), same strings. Registered, not granted:
    /// no role and no user receives them here; who holds which is a human gate. All three are
    /// company-wide only: drafts, sources and amounts belong to the server-resolved company and the
    /// routes filter by nothing narrower. None is Critical: a draft moves no money and grants no
    /// authority. Record and Reverse, which would, have no name yet (OPEN-009, a human decision).
    /// </summary>
    public const string FinanceManualReceiptDraftSave = "finance.manual-receipt.draft.save";

    /// <inheritdoc cref="FinanceManualReceiptDraftSave"/>
    public const string FinanceManualReceiptDraftRead = "finance.manual-receipt.draft.read";

    /// <inheritdoc cref="FinanceManualReceiptDraftSave"/>
    public const string FinanceManualReceiptAmountRead = "finance.manual-receipt.amount.read";

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

    public const string IdentityProfileRead = "identity.profile.read";
    public const string IdentityAccessRequest = "identity.access.request";
    public const string IdentityAccessApprove = "identity.access.approve";
    public const string IdentityRolesManage = "identity.roles.manage";
    public const string IdentityAuditRead = "identity.audit.read";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [
            OrganizationContextRead,
            OrganizationSitesRead,
            OrganizationLocationsRead,
            OrganizationWorkCentersRead,
            OrganizationLocationsStatusUpdate,
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
            ManufacturingPlansWrite,
            ManufacturingPlansApprove,
            ManufacturingOrdersCreate,
            ManufacturingFactsRecord,
            ManufacturingOrdersRead,
            ManufacturingOrdersClose,
            InventoryLabelsRead,
            InventoryBalancesRead,
            InventoryOpeningStockRecord,
            LogisticsLoadingInstructionsCreate,
            LogisticsLoadingInstructionsRead,
            LogisticsLoadsRecord,
            PurchasingOrdersCreate,
            PurchasingOrdersRead,
            PurchasingOrdersApprove,
            AccountingPeriodsRead,
            AccountingPeriodsPropose,
            AccountingPeriodsApprove,
            AccountingPeriodsClose,
            AccountingJournalsRead,
            AccountingPostingQueuesRead,
            FinanceBankAccountRead,
            FinanceBankAccountPropose,
            FinanceBankAccountApprove,
            FinanceManualReceiptDraftSave,
            FinanceManualReceiptDraftRead,
            FinanceManualReceiptAmountRead,
            IdentityProfileRead,
            IdentityAccessRequest,
            IdentityAccessApprove,
            IdentityRolesManage,
            IdentityAuditRead,
        ],
        StringComparer.Ordinal);

    public static IReadOnlySet<string> Critical { get; } = new HashSet<string>(
        [
            IdentityAccessApprove,
            IdentityRolesManage,
            IdentityAuditRead,
            FinanceBankAccountPropose,
            FinanceBankAccountApprove,
        ],
        StringComparer.Ordinal);

    public static IReadOnlySet<string> CompanyWideOnly { get; } = new HashSet<string>(
        [
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
            ManufacturingPlansWrite,
            ManufacturingPlansApprove,
            ManufacturingOrdersCreate,
            ManufacturingFactsRecord,
            ManufacturingOrdersRead,
            ManufacturingOrdersClose,
            InventoryLabelsRead,
            InventoryBalancesRead,
            InventoryOpeningStockRecord,
            LogisticsLoadingInstructionsCreate,
            LogisticsLoadingInstructionsRead,
            LogisticsLoadsRecord,
            PurchasingOrdersCreate,
            PurchasingOrdersRead,
            PurchasingOrdersApprove,
            AccountingPeriodsRead,
            AccountingPeriodsPropose,
            AccountingPeriodsApprove,
            AccountingPeriodsClose,
            AccountingJournalsRead,
            AccountingPostingQueuesRead,
            FinanceBankAccountRead,
            FinanceBankAccountPropose,
            FinanceBankAccountApprove,
            FinanceManualReceiptDraftSave,
            FinanceManualReceiptDraftRead,
            FinanceManualReceiptAmountRead,
            IdentityAuditRead,
        ],
        StringComparer.Ordinal);

    public static bool IsKnown(string permission) => All.Contains(permission);

    public static bool IsCritical(string permission) => Critical.Contains(permission);

    public static bool RequiresCompanyWideScope(string permission) =>
        CompanyWideOnly.Contains(permission);
}

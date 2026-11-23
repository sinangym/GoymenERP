/**
 * Server-published capability ids the shell knows how to mount.
 *
 * These MUST match the accepted backend registry exactly (Codex PR #149,
 * `CounterpartyShellCapabilityProvider`, sourced from `CARI_UI_VERTICAL_SLICE_v1.json`).
 * A drift here means the shell silently stops mounting a granted feature, so the ids
 * are asserted against the backend provider constants in a test.
 *
 * Listing an id here grants nothing. The server decides availability; this is only the
 * set the frontend has an implementation for.
 */

export const CAPABILITY = {
  /** C-01 Cari Kartlar list. Route `/cari`, owner CHATGPT-BUSINESS/CARI. */
  cariPartiesList: 'cari.parties.list',
  /**
   * C-02 Yeni Cari — the create subset (`scopeVariant=create-subset` is route
   * metadata, not part of the screen id). Route `/cari/yeni`, permission
   * `counterparty.parties.create`, single-company context only.
   */
  cariPartiesCreate: 'cari.parties.create',
  /**
   * CARI-LINK-WORKSPACE-001 — link an existing Party to the TARGET company.
   * Route `/cari/sirkete-bagla`, permissions `counterparty.accounts.create` +
   * `counterparty.parties.read` company-wide in the target, single-company only.
   */
  cariAccountsLink: 'cari.accounts.link',
  /**
   * C-03 Cari 360 detail. Route `/cari/:partyId`, same owner.
   *
   * Declared by the server as a SEPARATE grant (#182), so holding the list does
   * not imply holding the detail. An earlier version of the C-03 screen mounted
   * under the list capability — which meant a list-only snapshot could open the
   * detail and call its API. Codex caught that on #172.
   */
  cariPartiesDetail: 'cari.parties.detail',
  /**
   * C-06 — the selected company's operational contact and address editor. Route
   * `/cari/:partyId/bilgiler`, route id `cari.contact-edit`.
   *
   * A SEPARATE grant from the detail, for the reason #182 made the detail separate
   * from the list: holding the read must not imply holding the write. Gating the
   * editor's route on it is also the only way the user is spared an editor whose Save
   * they are not permitted to use — a capability is UI visibility, and the API
   * re-authorizes `counterparty.parties.read` + `counterparty.parties.update.basic`
   * on every request regardless.
   *
   * Listing it here grants nothing; until the server publishes it the route mounts the
   * same neutral not-found block any unknown path gets, and calls nothing.
   */
  cariAccountsContactUpdate: 'cari.accounts.contact.update',
  /**
   * U-01 Ürünler listesi. Route `/urun`, owner CLAUDE-FRONTEND-URUN.
   *
   * Declared here by the shared-integration owner from that lane's handover
   * (`src/product/SHARED_MOUNT_HANDOFF.md`); the values, routes and labels are the ones
   * `src/product/capabilities.ts` declares at pin `8558ce5`, read from that source rather
   * than assumed, and a drift test pins the two together once the Product source is in
   * the same tree.
   *
   * WHAT LISTING IT DOES NOT DO, and it matters more here than for the Cari ids: listing an
   * id grants nothing. Since #316 the server's `ProductShellCapabilityProvider` publishes these
   * four, and the shell grants each one only from the actor's global Product authority (explicit
   * Product master assignments, never a company role). An actor without that authority gets
   * none of them: no mount and no nav entry, and `CapabilityRoute` closes an ungranted route onto
   * the same neutral not-found block any unknown path gets.
   */
  productItemsList: 'product.items.list',
  /**
   * U-01 Ürün 360. Route `/urun/:productId`. A SEPARATE grant from the list, for the
   * reason #182/#172 made the Cari detail separate: holding the list must not imply
   * holding the card.
   */
  productItemsDetail: 'product.items.detail',
  /** U-02 Yeni Ürün — Draft creation only. Route `/urun/yeni`. */
  productItemsCreate: 'product.items.create',
  /**
   * U-02 basic edit of a created Draft. Route `/urun/:productId/duzenle`.
   *
   * A SEPARATE grant from the detail for the reason the Product lane gives and the Cari
   * ids already follow: reading a card and rewriting it are different authorities.
   * `PRD-PERM-001=A` happens to give both to `SYSTEM_ADMINISTRATOR` alone today, but that
   * is a fact about who holds them, not an argument for one id — the day read widens past
   * admin, a fused id would hand out the write with it.
   *
   * Its counterpart now EXISTS and was read from source: `PRODUCT_CAPABILITY.productsEdit`
   * at #238 pin `8558ce5`, with the same value, the same route and an empty nav label.
   */
  productItemsEdit: 'product.items.edit',
  /**
   * ORG-01 Ayarlar > Organizasyon — the selected company's facilities and their paged
   * locations. Route `/ayarlar/organizasyon`, owner CLAUDE-FRONTEND-ORGANIZASYON.
   *
   * Canonical AV-01/list; ORG-02 below is canonical AV-01/detail. No new catalog row
   * and no new screen count comes with them (CHATGPT-BUSINESS, same task).
   *
   * READ THE NAME CAREFULLY: the value is `organization.sites.list`, but the grant rule
   * behind it is NOT "holds sites.read". Backend #246 publishes this route capability on
   * `organization.locations.read` ALONE. That is the shape Business required in
   * https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5646417811 — when the
   * sites read is missing, the facility name is not fabricated AND the separately
   * authorized locations list is not blocked ("alan bazlı yetki yokluğu boş liste
   * değildir"). So the lane's own source comment, which proposed granting this on
   * sites.read + locations.read together, is SUPERSEDED by #246; the id value it
   * declares is unchanged and is what this file mirrors.
   *
   * The optional facility label is a separate CONTROL capability the server also
   * publishes (`organization.sites.read`, granted on locations.read + sites.read), and
   * the reasoned activate/deactivate inside ORG-02 is another
   * (`organization.locations.status`, on locations.read + status.update). Neither is a
   * route, so neither belongs in this registry — they are `hasCapability` checks inside
   * the lane's screens. Without the label capability that lane starts NO `/sites` GET at
   * all: there is no real 403 to suppress, and the global fail-closed boundary is
   * untouched.
   */
  organizationSitesList: 'organization.sites.list',
  /**
   * ORG-02 one location's card. Route `/ayarlar/organizasyon/lokasyon/:locationId`.
   *
   * A SEPARATE grant from the list for the reason #182 made the Cari detail separate,
   * even though #246 happens to publish both on `organization.locations.read` today:
   * that is a fact about who holds them, not an argument for one id.
   */
  organizationLocationsDetail: 'organization.locations.detail',
  /**
   * ORG-03 the authorized work-centre list. Route `/ayarlar/organizasyon/is-merkezleri`,
   * owner the Organization lane.
   *
   * ITS OWN PERMISSION, and that is the whole point of the slice rather than a detail:
   * the route stands on `organization.work-centers.read` ALONE. ORG-01 stands on
   * `organization.locations.read`, so a user holding work centres and not locations has
   * no link into ORG-01 at all — there is nowhere to put a work-centre entry except the
   * sidebar. The reverse holds too: holding the location screens does not reach here.
   *
   * It renders only where the server's snapshot carries the id, and the backend provider
   * does not declare it yet (four declarations on main today). So this entry is the
   * shell saying it HAS an implementation, not that anyone may see it — the same staged
   * shape `organizationSitesList` had before #246 published it.
   */
  organizationWorkCentersList: 'organization.work-centers.list',
  /**
   * ORG-04 one work centre's read-only card. Route `/ayarlar/organizasyon/is-merkezleri/:workCenterId`,
   * owner the Organization lane. The same grant as ORG-03 (`organization.work-centers.read`); reached from a
   * list row, never from the sidebar.
   */
  organizationWorkCentersDetail: 'organization.work-centers.detail',
  /**
   * ORG-05 (KIM-S23, KIM-D28) the active company's VKN and its maker-checker change flow. Route
   * `/ayarlar/organizasyon/sirket-vergi-no`, owner the Organization lane. The route stands on
   * `organization.context.read`; the VKN itself is returned only to a holder of one of the two tax-id names, and
   * the request/decide buttons follow the server's own viewer flags.
   */
  organizationCompanyTaxId: 'organization.company.tax-id',
  /** S-02 sales order draft list. Route `/satis/siparisler`, owner the Sales lane (SİPARİŞ). */
  salesDraftsList: 'sales.drafts.list',
  /** S-04 Sales order 360, one draft. Route `/satis/siparisler/:draftId`. A SEPARATE grant from the list. */
  salesDraftsDetail: 'sales.drafts.detail',
  /** S-03 new sales order draft. Route `/satis/siparisler/yeni`. A SEPARATE grant from reading. */
  salesDraftsCreate: 'sales.drafts.create',
  /** UI-60 S-07 Sevkiyat / İrsaliyeler (API-30). Route `/satis/sevkiyatlar`. Granted on `sales.drafts.read`, its own capability. */
  salesDispatchesList: 'sales.dispatches.list',
  /** UI-61 S-08 Faturaya Hazır (API-31). Route `/satis/faturaya-hazir`. Granted on its own `sales.invoices.read`. */
  salesInvoiceCandidatesList: 'sales.invoice-candidates.list',
  /** UI-63 S-09 Satış faturaları (API-32 list). Route `/satis/faturalar`. Granted on `sales.invoices.read` (SAT-D238: amounts withheld without `sales.orders.price.read`; Sales shell 1.20). */
  salesInvoicesList: 'sales.invoices.list',
  salesOfficialInvoiceCancellation: 'sales.invoices.official-cancellation',
  /** UI-80 Satış raporları (API-41 / API-42). Route `/satis/raporlar`. Granted on its own `sales.reports.read`. */
  salesReportsView: 'sales.reports.view',
  /** UI-70b S-10 Müşteri iade faturaları (API-40 list). Route `/satis/iadeler`. Granted on `sales.returns.read` + `sales.orders.price.read` (Sales shell 1.25). */
  salesCustomerReturnsList: 'sales.customer-returns.list',
  /** UI-70b S-10 new return record. Route `/satis/iadeler/yeni`. Granted on `sales.returns.write` + `sales.orders.price.read`. */
  salesCustomerReturnsCreate: 'sales.customer-returns.create',
  /** UI-70b S-10 one return. Route `/satis/iadeler/:returnId`. Granted on `sales.returns.read` + `sales.orders.price.read`. */
  salesCustomerReturnsDetail: 'sales.customer-returns.detail',
  /**
   * S-01 Talep havuzu (UI-20). Route `/satis/talepler`. Granted on its own `sales.requests.read` (Sales shell 1.8); the
   * route itself is mounted by the Sales lane's pool gate (`SalesPoolRoute`), this entry only names it in the sidebar.
   */
  salesRequestsList: 'sales.requests.list',
  /** S-01 Teklif havuzu (UI-20). Route `/satis/teklifler`. Granted on its own `sales.quotes.read` (Sales shell 1.8). */
  salesQuotesList: 'sales.quotes.list',
  /** MFG-02 the company's production orders (MFG-ORD). Route `/uretim/emirler`. */
  manufacturingOrdersList: 'manufacturing.orders.list',
  /**
   * MFG-01 production order (technical sub-view, MFG-UI D1). Route `/uretim/emir/:orderId`.
   *
   * `manufacturing.orders.facts` and `manufacturing.orders.close` are CONTROLS inside that screen
   * (read with `hasCapability`, the `organization.locations.status` precedent), not routes, so
   * neither is in this table.
   */
  manufacturingOrdersDetail: 'manufacturing.orders.detail',
  /** Create a Draft plan. Route `/uretim/plan/yeni`; reached from the order list under K5. */
  manufacturingPlansCreate: 'manufacturing.plans.create',
  /** Reopen one stored plan. Route `/uretim/plan/:planId`; reached after create or by record link. */
  manufacturingPlansDetail: 'manufacturing.plans.detail',
  /** MFG-PLAN-LIST the company's saved plans. Route `/uretim/planlar`; reached from the order list and plan detail. */
  manufacturingPlansList: 'manufacturing.plans.list',
  /**
   * URT-S13 the company's monthly scrap prices (URT-D47, URT-D65). Route `/uretim/hurda-fiyatlari`; reached from the
   * order list. Entering a month's price is the `manufacturing.scrap-prices.record` CONTROL inside it, not a route.
   */
  manufacturingScrapPricesList: 'manufacturing.scrap-prices.list',
  /** F-02 manual-receipt draft list. Route `/finans/tahsilatlar`, owner the Finance lane (FİNANS). */
  financeReceiptsList: 'finance.receipts.list',
  /** F-02 new draft, entered from a Cari account (`?partyId&companyAccountId`). Route `/finans/tahsilatlar/yeni`. */
  financeReceiptsDraft: 'finance.receipts.draft',
  /** F-02 one draft. Route `/finans/tahsilatlar/:sourceId`. A SEPARATE grant from the list. */
  financeReceiptsDetail: 'finance.receipts.detail',
  financeCustomerInstrumentsList: 'finance.customer-instruments.list',
  financeOfficialInvoiceImportUpload: 'finance.official-invoice-import.upload',
  financeOfficialInvoiceImportApprove: 'finance.official-invoice-import.approve',
  financeAllocationsList: 'finance.allocations.list',
  /** FI04 open items, read-only. Route `/finans/acik-kalemler`. */
  financeOpenItemsList: 'finance.open-items.list',
  /** FI04 one open item. Route `/finans/acik-kalemler/:openItemId`; reached from a row. */
  financeOpenItemsDetail: 'finance.open-items.detail',
  /** B-05 (KLN-D3, M-03) party ledger from Muhasebe. Route `/muhasebe/cari-hesaplar`, owner the Finance lane. */
  financePartyLedgerAccounting: 'finance.party-ledger.accounting',
  /** B-05 (KLN-D3, C-04) party ledger from Cari. Route `/cari/hesap-takibi`, owner the Finance lane. */
  financePartyLedgerCari: 'finance.party-ledger.cari',
  /** FIN-T06 pending reversal checker queue. Route `/finans/ters-kayit-onaylari`. */
  financeReceiptsReverseApprove: 'finance.receipts.reverse.approve',
  /** F-04 bank-account master. Route `/finans/banka-hesaplari`. */
  financeBankAccountsList: 'finance.bank-accounts.list',
  /**
   * F-04 propose a bank account. Route `/finans/banka-hesaplari/yeni`.
   *
   * `finance.bank-accounts.approve` is deliberately NOT here: it is a control of the F-04 list (the
   * provider's `ParentCapabilityId`), not a route, so it belongs in no route registry. The list checks
   * it itself, and the server refuses a self-approval anyway.
   */
  financeBankAccountsPropose: 'finance.bank-accounts.propose',
  /** R47 manual outgoing bank-payment list. */
  financeManualBankPaymentsList: 'finance.manual-bank-payments.list',
  /** R47 create route, entered with an explicit owner-reference context. */
  financeManualBankPaymentsCreate: 'finance.manual-bank-payments.create',
  /** R47 one manual outgoing bank payment. */
  financeManualBankPaymentsDetail: 'finance.manual-bank-payments.detail',
  /**
   * FNS-D25 (API-25) the payment not tied to a party (Masraf Ödemeleri). Its draft, approve and record are
   * CONTROLS of this workspace, never routes.
   */
  financeExpensePaymentsWorkspace: 'finance.expense-payments.workspace',
  /** FNS-D24 (API-32/34) Virman ve Döviz Bozdurma. Its commit is a CONTROL of this workspace, never a route. */
  financeCashTransfersWorkspace: 'finance.cash-transfers.workspace',
  financeExchangeBookValuesPrepare: 'finance.cash-exchange-book-values.prepare',
  financeExchangeBookValuesApprove: 'finance.cash-exchange-book-values.approve',
  /**
   * F-018 L1 (FNS-D27 / FNS-D46; one step, FNS-D57(3)) Kredi Limitleri. Its propose and approve are CONTROLS of this list, never routes.
   */
  financeCreditLimitsList: 'finance.credit-limits.list',
  /**
   * F-018 L2 (FNS-D57 / FNS-D31 / FNS-D58) Müşteri Teminatları. Its record (record, change, release in one step) is a
   * CONTROL of this list, never a route.
   */
  financeCollateralsList: 'finance.collaterals.list',
  /**
   * FIN-S9 (FNS-D23) Açılış Bakiyeleri. Its propose and approve are CONTROLS of this list, never routes.
   */
  financeOpeningBalancesList: 'finance.opening-balances.list',
  financeCreditsWorkspace: 'finance.credits.workspace',
  financePartyBalanceTransfers: 'finance.party-balance-transfers.record',
  financeBankStatements: 'finance.bank-statements.read',
  financeOfficialInvoicesRecord: 'finance.official-sales-invoices.record',
  financeOfficialInvoicesApprove: 'finance.official-sales-invoices.approve',
  accountingCostDifferencePrepare: 'accounting.manufacturing-cost-differences.prepare',
  accountingCostDifferenceApprove: 'accounting.manufacturing-cost-differences.approve',
  accountingInvoiceLineCogsPrepare: 'accounting.invoice-line-cogs.prepare',
  accountingInvoiceLineCogsApprove: 'accounting.invoice-line-cogs.approve',
  accountingSourceVouchers: 'accounting.source-vouchers.list',
  accountingPostingMappingsRead: 'accounting.posting-mappings.read',
  accountingInvoicedShipmentPrepare: 'accounting.invoiced-shipment-costs.prepare',
  accountingInvoicedShipmentApprove: 'accounting.invoiced-shipment-costs.approve',
  accountingInvoicedShipmentReverse: 'accounting.invoiced-shipment-costs.reverse',
  accountingInvoicedShipmentReverseApprove: 'accounting.invoiced-shipment-costs.reverse-approve',
  accountingPurchaseCostPrepare: 'accounting.purchase-cost-differences.prepare',
  accountingPurchaseCostApprove: 'accounting.purchase-cost-differences.approve',
  identityUsersManage: 'identity.users.manage',
  productPaintMemberships: 'paint-memberships.manage',
  /** M-07 Muhasebe Dönemleri. Route `/muhasebe/donemler`, owner the Accounting lane (FİNANS). */
  accountingPeriodsList: 'accounting.periods.list',
  /** M-07 propose a period opening. Route `/muhasebe/donemler/yeni`. A SEPARATE grant from reading. */
  accountingPeriodsPropose: 'accounting.periods.propose',
  /**
   * M-04 Muhasebe Fişleri, read-only. Route `/muhasebe/fisler`.
   *
   * `accounting.periods.approve` (Ö4), `accounting.periods.close` (Ö5) and `accounting.periods.reopen` (ROL-D12) are CONTROLS of the M-07
   * list (the provider's `ParentCapabilityId`), not routes, so neither is in these tables; the list
   * reads them with `hasCapability` and the server refuses a self-approval regardless.
   */
  accountingJournalsList: 'accounting.journals.list',
  /** M-04-DETAIL one journal, read-only. Route `/muhasebe/fisler/:journalId`. A SEPARATE grant from the list. */
  accountingJournalsDetail: 'accounting.journals.detail',
  /** ACC-V1 normal journal create, own-draft reopen and narrow approval routes. */
  accountingNormalDraftCreate: 'accounting.normal-journal-drafts.create',
  accountingNormalDraftDetail: 'accounting.normal-journal-drafts.detail',
  accountingNormalDraftApprove: 'accounting.normal-journal-drafts.approve',
  /** M-01 Muhasebe Kontrol Merkezi — the posting queues, read-only. Route `/muhasebe/kontrol`. */
  accountingPostingQueues: 'accounting.posting-queues',
  /** M-06 Mizan — posted-journal aggregates, read-only. Route `/muhasebe/mizan`. */
  accountingTrialBalance: 'accounting.trial-balance',
  /** INV-OPEN-2 opening-stock workspace. Home, create and detail share this existing permission. */
  inventoryOpeningStockRecord: 'inventory.opening-stock.record',
  /** ST-01 bounded, company-scoped physical stock-position read. */
  inventoryStockPositionsList: 'inventory.stock-positions.list',
  inventorySubcontractRelease: 'inventory.subcontract-allocations.release-unused',
  inventoryTransferPlanning: 'inventory.stock-transfer.write',
  inventoryTransferOverageApproval: 'inventory.stock-transfer.overage-approve',
  inventoryTransferLossRequest: 'inventory.stock-transfer.loss-record',
  inventoryTransferLossApproval: 'inventory.stock-transfer.loss-approve',
  /** SY-01 read subset "Kullanıcılar ve yetkiler". Route `/ayarlar/kullanicilar-ve-yetkiler`, owner Identity. Read-only. */
  identityAssignmentsList: 'identity.assignments.list',
  /** SY-02 read subset: one role's permissions and holders. Reached from a row, never the sidebar. */
  identityRolesDetail: 'identity.roles.detail',
  /**
   * UI-K02 (KLN-D2) "Denetim kaydı": the company's audit events, read-only. Route `/ayarlar/denetim-kaydi`, owner
   * Identity 1.1, on the existing `identity.audit.read` (company-wide, Critical); no grant is added here.
   */
  identityAuditList: 'identity.audit.list',
  /**
   * SAT-D61…D70 Ayarlar > İskonto limitleri: the Sales discount limit groups, their limits and the person → group
   * bindings. Route `/ayarlar/iskonto-limitleri`, owner Settings; one non-Critical name, granted to no role here.
   */
  settingsSalesDiscountLimits: 'settings.sales-discount-limits.manage',
  /** MOD-03 Ayarlar > Vergi: the tax master. Route `/ayarlar/vergi`, owner Settings; one non-Critical name, granted to no role. */
  settingsTaxMaster: 'settings.tax-master.manage',
  /**
   * F-025 / B-11 Ayarlar > Ana Veriler > Kurlar: the exchange rate days, correction, half-day mark, reverification and
   * import. Route `/ayarlar/kurlar`, owner Settings 1.2, on the read name; the three command controls are separate names
   * (`settings/capabilities.ts` `SETTINGS_EXCHANGE_RATE_CONTROL`). Non-Critical, granted to no role here.
   */
  settingsExchangeRates: 'settings.exchange-rates.read',
  /**
   * F-029 S4b, SAT-D5 Ayarlar > Teslim toleransı: the delivery tolerance timeline (warn / approval / block percents),
   * baseline transfer and new versions. Route `/ayarlar/teslim-toleransi`, owner Settings 1.3; one non-Critical name,
   * granted to no role here.
   */
  settingsSalesDeliveryTolerance: 'settings.sales-delivery-tolerances.manage',
  /**
   * F-029 S4b, SAT-D6 Ayarlar > Onay tutar eşikleri: the approval amount thresholds per currency, baseline transfer and
   * new versions. Route `/ayarlar/onay-esikleri`, owner Settings 1.3; one non-Critical name, granted to no role here.
   */
  settingsSalesApprovalThresholds: 'settings.sales-approval-thresholds.manage',
  /**
   * KIM-D34 Ayarlar > Giriş deneme sınırı: the group-wide login attempt limit and its versions. Route
   * `/ayarlar/giris-deneme-siniri`, owner Settings 1.4; one non-Critical name, granted to no role here.
   */
  settingsLoginRateLimit: 'settings.login-rate-limit.manage',
  /**
   * F-004, KIM-D37 Ayarlar > Birim çevrim politikası: the company's density, rounding and weighbridge tolerance versions
   * (no seed). Route `/ayarlar/birim-cevrim-politikasi`, owner Settings 1.4; one non-Critical name, granted to no role here.
   */
  settingsUnitConversionPolicy: 'settings.unit-conversion-policies.manage',
  /**
   * F-029 S1/S2, URN-D2…D5 Ayarlar > Birim profilleri: the company's unit profile versions (unit, scale, use) with the
   * one-time baseline transfer. Route `/ayarlar/birim-profilleri`, owner Settings 1.5; one non-Critical name, granted to no role here.
   */
  settingsUnitProfiles: 'settings.unit-profiles.manage',
  settingsMoneyProfiles: 'settings.money-profiles.manage',
  settingsSalesContractDiscountLimit: 'settings.sales-contract-discount-limit.manage',
  settingsCollateralTypes: 'settings.collateral-types.manage',
  settingsFinanceOpeningPeriod: 'settings.finance-opening-period.manage',
  settingsManufacturingScrapCost: 'settings.manufacturing-scrap-cost.manage',
  settingsNotificationRetention: 'settings.notification-retention.manage',
  settingsWorkApprovalDuration: 'settings.work-approval-duration.manage',
  /** WP-A5 (AI-20): the AI management screen, owner Settings 1.21; one non-Critical name, granted to no role here. */
  settingsAiGateway: 'settings.ai-gateway.manage',
  /** WP15 (1.16) AY-YST-01 / AY-YS-01: the legal deadline calendar and the upcoming legal deadlines. */
  settingsLegalDeadlineCalendar: 'settings.legal-deadline-calendar.manage',
  settingsLegalDeadlines: 'settings.legal-deadlines.read',
  settingsDocumentNumberSeries: 'settings.document-number-series.manage',
  settingsFinancePolicies: 'settings.finance-policies.prepare',
  settingsCashDifferencePolicies: 'settings.cash-count-difference-policies.prepare',
  settingsBankInstitutions: 'settings.bank-institutions.prepare',
  settingsCollateralRatePolicies: 'settings.collateral-rate-policies.prepare',
  settingsManufacturingParameters: 'settings.manufacturing-parameter-profiles.read',
  settingsManufacturingTolerances: 'settings.manufacturing-tolerance-profiles.read',
  /**
   * URT-D89 Ayarlar > İş merkezi kapasitesi: each work center's input capacity (kg/hour) and optional safety buffer
   * versions (no seed). Route `/ayarlar/is-merkezi-kapasitesi`, owner Settings 1.5; one non-Critical name, granted to no
   * role here.
   */
  settingsWorkCenterCapacity: 'settings.work-center-capacities.manage',
  /**
   * API-98, KIM-D49 Ayarlar > Masraf ve hizmet tipleri: the company's expense type and service type lists (no seed). Route
   * `/ayarlar/referans-listeleri`, owner Settings 1.6; one non-Critical name, granted to no role here.
   */
  settingsPurchasingReferenceLists: 'settings.reference-lists.manage',
  settingsPurchaseInvoiceVariance: 'settings.purchase-invoice-variance-policies.manage',
  settingsWarehouseTransferScale: 'settings.warehouse-transfer-scale.manage',
  settingsSalesSubcontractTax: 'settings.sales-subcontract-tax-default.manage',
  settingsPurchasingSubcontractTax: 'settings.purchasing-subcontract-tax-default.manage',
  documentsBrandingManage: 'documents.company-branding.manage',
  documentsSalesTemplatePrepare: 'documents.templates.sales.prepare',
  documentsSalesTemplatePublish: 'documents.templates.sales.publish',
  documentsInventoryTemplatePrepare: 'documents.templates.inventory.prepare',
  documentsInventoryTemplatePublish: 'documents.templates.inventory.publish',
  documentsLogisticsTemplatePrepare: 'documents.templates.logistics.prepare',
  documentsLogisticsTemplatePublish: 'documents.templates.logistics.publish',
  /**
   * B-14, KIM-D51 Ayarlar > Boya rengi ve hurda türü: the company's paint color and scrap kind lists (no seed). Route
   * `/ayarlar/uretim-referans-listeleri`, owner Settings 1.6; one non-Critical name, granted to no role here.
   */
  settingsProductionReferenceLists: 'settings.production-reference-lists.manage',
  /** L-02 loading-instruction list. Route `/lojistik/yukleme-talimatlari`, owner the Sales/Logistics lane (SİPARİŞ). */
  logisticsInstructionsList: 'logistics.instructions.list',
  /** L-03 new loading instruction. Route `/lojistik/yukleme-talimatlari/yeni`. A SEPARATE grant from reading. */
  logisticsInstructionsCreate: 'logistics.instructions.create',
  /** L-02-DETAIL one instruction. Route `/lojistik/yukleme-talimatlari/:instructionId`. A SEPARATE grant from the list. */
  logisticsInstructionsDetail: 'logistics.instructions.detail',
  /** AT-025 L-01 operations work — the SALES section. Route `/lojistik/operasyon-isleri/satis`. Its own grant. */
  logisticsOperationsSales: 'logistics.operations-work.sales',
  /** AT-025 L-09 operations work — the PURCHASE section. Route `/lojistik/operasyon-isleri/satinalma`. Its own grant. */
  logisticsOperationsPurchase: 'logistics.operations-work.purchase',
  logisticsPlanExcessApprove: 'logistics.shipments.plan-excess.approve',
  logisticsTransferDepartures: 'logistics.transfer-departures.record',
  logisticsSubcontractList: 'logistics.subcontract-deliveries.list',
  logisticsSubcontractCreate: 'logistics.subcontract-deliveries.create',
  logisticsSubcontractDetail: 'logistics.subcontract-deliveries.detail',
  logisticsSubcontractIssue: 'logistics.subcontract-deliveries.issue',
  logisticsSubcontractLoad: 'logistics.subcontract-deliveries.load',
  logisticsSubcontractWeigh: 'logistics.subcontract-deliveries.weigh',
  logisticsSubcontractShip: 'logistics.subcontract-deliveries.ship',
  logisticsSubcontractCorrect: 'logistics.subcontract-deliveries.weighing-correct',
  /** P-01 purchase-request list. Route `/satinalma/talepler`, owner the Purchasing lane. */
  purchasingRequestsList: 'purchasing.requests.list',
  /** P-01 new request. Route `/satinalma/talepler/yeni`; reached from the list under K5. */
  purchasingRequestsCreate: 'purchasing.requests.create',
  /** P-01-DETAIL one purchase request. Route `/satinalma/talepler/:requestId`. */
  purchasingRequestsDetail: 'purchasing.requests.detail',
  /** P-03 purchase-order list. Route `/satinalma/siparisler`, owner the Purchasing lane. */
  purchasingOrdersList: 'purchasing.orders.list',
  purchasingInvoicesList: 'purchasing.invoices.list',
  purchasingAdditionalCostsRead: 'purchasing.additional-costs.read',
  purchasingInvoicesDetail: 'purchasing.invoices.detail',
  purchasingServiceOrdersList: 'purchasing.service-orders.list',
  purchasingServiceOrdersCreate: 'purchasing.service-orders.create',
  purchasingServiceOrdersDetail: 'purchasing.service-orders.detail',
  /** P-04 new purchase order. Route `/satinalma/siparisler/yeni`; staged until a real Product candidate source exists. */
  purchasingOrdersCreate: 'purchasing.orders.create',
  /** P-03-DETAIL one purchase order. Route `/satinalma/siparisler/:orderId`. */
  purchasingOrdersDetail: 'purchasing.orders.detail',
} as const

/** U-01-360 action-only control. It is deliberately absent from route and nav catalogs. */
export const PRODUCT_ACTIVATE_CAPABILITY = 'product.items.activate'

export type CapabilityId = (typeof CAPABILITY)[keyof typeof CAPABILITY]

/** Route path per capability, mirroring the backend route declaration. */
export const CAPABILITY_ROUTE: Record<CapabilityId, string> = {
  [CAPABILITY.cariPartiesList]: '/cari',
  [CAPABILITY.cariPartiesCreate]: '/cari/yeni',
  [CAPABILITY.cariAccountsLink]: '/cari/sirkete-bagla',
  [CAPABILITY.cariPartiesDetail]: '/cari/:partyId',
  [CAPABILITY.cariAccountsContactUpdate]: '/cari/:partyId/bilgiler',
  [CAPABILITY.productItemsList]: '/urun',
  // Static before the parameter route, the same rule /cari/yeni follows: a deeper
  // static segment must never be readable as a record id.
  [CAPABILITY.productItemsCreate]: '/urun/yeni',
  // Longer and static-tailed first: 'duzenle' must never be read as a product id, the
  // same ordering the Cari routes follow. The Product lane declares them in this order
  // too (list, create, edit, detail).
  [CAPABILITY.productItemsEdit]: '/urun/:productId/duzenle',
  [CAPABILITY.productItemsDetail]: '/urun/:productId',
  [CAPABILITY.organizationSitesList]: '/ayarlar/organizasyon',
  // The two cannot collide whatever the order: the card carries a further STATIC
  // segment (`lokasyon`) ahead of its parameter, so `/ayarlar/organizasyon` can never
  // match the card and no static segment is readable as a location id — the #172 P1-2
  // rule the Cari and Ürün routes already follow. The Organization lane fixes that
  // shape in `organizationRoutes.tsx` rather than leaving it to this consumer.
  [CAPABILITY.organizationLocationsDetail]: '/ayarlar/organizasyon/lokasyon/:locationId',
  // A STATIC sibling of the list, not a child of the card: `/ayarlar/organizasyon` and
  // `/ayarlar/organizasyon/is-merkezleri` differ by a static segment, and neither can match the
  // card's `lokasyon/:locationId` — no url matches two of the three (#172 P1-2).
  [CAPABILITY.organizationWorkCentersList]: '/ayarlar/organizasyon/is-merkezleri',
  // ORG-04: a parameter BELOW the static list. The list never matches the card and the card never matches
  // `lokasyon/:locationId`, whose static segment differs (#172 P1-2).
  [CAPABILITY.organizationWorkCentersDetail]: '/ayarlar/organizasyon/is-merkezleri/:workCenterId',
  // ORG-05: a static sibling of the work-centre list; no url matches it and any other Organization route.
  [CAPABILITY.organizationCompanyTaxId]: '/ayarlar/organizasyon/sirket-vergi-no',
  [CAPABILITY.salesDraftsList]: '/satis/siparisler',
  // Static before the parameter route, the /cari/yeni rule: 'yeni' must never be read as a draft id.
  [CAPABILITY.salesDraftsCreate]: '/satis/siparisler/yeni',
  [CAPABILITY.salesDraftsDetail]: '/satis/siparisler/:draftId',
  // Two static Sales siblings; neither is a draft id under `/satis/siparisler/:draftId`.
  [CAPABILITY.salesDispatchesList]: '/satis/sevkiyatlar',
  [CAPABILITY.salesInvoiceCandidatesList]: '/satis/faturaya-hazir',
  // UI-63: another static Sales sibling, never a draft id.
  [CAPABILITY.salesInvoicesList]: '/satis/faturalar',
  [CAPABILITY.salesOfficialInvoiceCancellation]: '/satis/fatura-resmi-iptal',
  // UI-80: another static Sales sibling, never a draft id.
  [CAPABILITY.salesReportsView]: '/satis/raporlar',
  // UI-70b: static before the parameter route, the /cari/yeni rule: 'yeni' must never be read as a return id.
  [CAPABILITY.salesCustomerReturnsList]: '/satis/iadeler',
  [CAPABILITY.salesCustomerReturnsCreate]: '/satis/iadeler/yeni',
  [CAPABILITY.salesCustomerReturnsDetail]: '/satis/iadeler/:returnId',
  [CAPABILITY.salesRequestsList]: '/satis/talepler',
  [CAPABILITY.salesQuotesList]: '/satis/teklifler',
  // Static before the parameter route, the /cari/yeni rule: 'emirler' must never be read as an
  // order id. The lane orders its own route table the same way.
  [CAPABILITY.manufacturingOrdersList]: '/uretim/emirler',
  [CAPABILITY.manufacturingOrdersDetail]: '/uretim/emir/:orderId',
  // Static before the parameter route: `yeni` must never be read as a plan id.
  [CAPABILITY.manufacturingPlansCreate]: '/uretim/plan/yeni',
  [CAPABILITY.manufacturingPlansDetail]: '/uretim/plan/:planId',
  // Its own static segment: `planlar` is never read as `/uretim/plan/:planId`.
  [CAPABILITY.manufacturingPlansList]: '/uretim/planlar',
  [CAPABILITY.manufacturingScrapPricesList]: '/uretim/hurda-fiyatlari',
  [CAPABILITY.financeReceiptsList]: '/finans/tahsilatlar',
  // Static before the parameter route, the /cari/yeni rule: 'yeni' must never be read as a source id.
  [CAPABILITY.financeReceiptsDraft]: '/finans/tahsilatlar/yeni',
  [CAPABILITY.financeReceiptsDetail]: '/finans/tahsilatlar/:sourceId',
  [CAPABILITY.financeCustomerInstrumentsList]: '/finans/musteri-cek-senetleri',
  [CAPABILITY.financeOfficialInvoiceImportUpload]: '/finans/onayli-fatura-taksit-kaynagi-yukle',
  [CAPABILITY.financeOfficialInvoiceImportApprove]: '/finans/resmi-fatura-aktarim-onaylari',
  [CAPABILITY.financeAllocationsList]: '/finans/dagitimlar',
  [CAPABILITY.financeOpenItemsList]: '/finans/acik-kalemler',
  [CAPABILITY.financeOpenItemsDetail]: '/finans/acik-kalemler/:openItemId',
  [CAPABILITY.financePartyLedgerAccounting]: '/muhasebe/cari-hesaplar',
  [CAPABILITY.financePartyLedgerCari]: '/cari/hesap-takibi',
  [CAPABILITY.financeReceiptsReverseApprove]: '/finans/ters-kayit-onaylari',
  [CAPABILITY.financeBankAccountsList]: '/finans/banka-hesaplari',
  [CAPABILITY.financeBankAccountsPropose]: '/finans/banka-hesaplari/yeni',
  [CAPABILITY.financeManualBankPaymentsList]: '/finans/odemeler',
  [CAPABILITY.financeManualBankPaymentsCreate]: '/finans/odemeler/yeni',
  [CAPABILITY.financeManualBankPaymentsDetail]: '/finans/odemeler/:paymentId',
  [CAPABILITY.financeExpensePaymentsWorkspace]: '/finans/masraf-odemeleri',
  [CAPABILITY.financeCashTransfersWorkspace]: '/finans/virman',
  [CAPABILITY.financeExchangeBookValuesPrepare]: '/finans/doviz-defter-degeri-hazirla',
  [CAPABILITY.financeExchangeBookValuesApprove]: '/finans/doviz-defter-degeri-onaylari',
  [CAPABILITY.financeCreditLimitsList]: '/finans/kredi-limitleri',
  [CAPABILITY.financeCollateralsList]: '/finans/teminatlar',
  [CAPABILITY.financeOpeningBalancesList]: '/finans/acilis-bakiyeleri',
  [CAPABILITY.financeCreditsWorkspace]: '/finans/krediler',
  [CAPABILITY.financePartyBalanceTransfers]: '/finans/cari-bakiye-devri',
  [CAPABILITY.financeBankStatements]: '/finans/banka-ekstresi',
  [CAPABILITY.financeOfficialInvoicesRecord]: '/finans/resmi-satis-faturalari',
  [CAPABILITY.financeOfficialInvoicesApprove]: '/finans/resmi-satis-faturasi-onaylari',
  [CAPABILITY.accountingCostDifferencePrepare]: '/muhasebe/uretim-maliyet-farklari',
  [CAPABILITY.accountingCostDifferenceApprove]: '/muhasebe/uretim-maliyet-farki-onay',
  [CAPABILITY.accountingInvoiceLineCogsPrepare]: '/muhasebe/fatura-satiri-smm',
  [CAPABILITY.accountingInvoiceLineCogsApprove]: '/muhasebe/fatura-satiri-smm-onay',
  [CAPABILITY.accountingSourceVouchers]: '/muhasebe/kaynak-fisler',
  [CAPABILITY.accountingPostingMappingsRead]: '/muhasebe/hesap-eslemeleri',
  [CAPABILITY.accountingInvoicedShipmentPrepare]: '/muhasebe/sevk-smm-hazirla',
  [CAPABILITY.accountingInvoicedShipmentApprove]: '/muhasebe/sevk-smm-onay',
  [CAPABILITY.accountingInvoicedShipmentReverse]: '/muhasebe/sevk-smm-ters-kayit',
  [CAPABILITY.accountingInvoicedShipmentReverseApprove]: '/muhasebe/sevk-smm-ters-kayit-onay',
  [CAPABILITY.accountingPurchaseCostPrepare]: '/muhasebe/alis-maliyet-farki-hazirla',
  [CAPABILITY.accountingPurchaseCostApprove]: '/muhasebe/alis-maliyet-farki-onay',
  [CAPABILITY.identityUsersManage]: '/ayarlar/kullanicilar',
  [CAPABILITY.productPaintMemberships]: '/boya-urunleri',
  [CAPABILITY.accountingPeriodsList]: '/muhasebe/donemler',
  // Static before the parameter route, the /cari/yeni rule: 'yeni' must never be read as a period id.
  [CAPABILITY.accountingPeriodsPropose]: '/muhasebe/donemler/yeni',
  [CAPABILITY.accountingJournalsList]: '/muhasebe/fisler',
  [CAPABILITY.accountingJournalsDetail]: '/muhasebe/fisler/:journalId',
  [CAPABILITY.accountingNormalDraftCreate]: '/muhasebe/normal-fisler/yeni',
  [CAPABILITY.accountingNormalDraftDetail]: '/muhasebe/normal-fisler/:draftId',
  [CAPABILITY.accountingNormalDraftApprove]: '/muhasebe/normal-fis-onay/:draftId',
  [CAPABILITY.accountingPostingQueues]: '/muhasebe/kontrol',
  [CAPABILITY.accountingTrialBalance]: '/muhasebe/mizan',
  [CAPABILITY.inventoryOpeningStockRecord]: '/stok/acilis',
  [CAPABILITY.inventoryStockPositionsList]: '/stok/pozisyonlar',
  [CAPABILITY.inventorySubcontractRelease]: '/stok/fason-ayirma-birak',
  [CAPABILITY.inventoryTransferPlanning]: '/stok/transferler/planla',
  [CAPABILITY.inventoryTransferOverageApproval]: '/stok/transferler/fazla-kabul-onaylari',
  [CAPABILITY.inventoryTransferLossRequest]: '/stok/transferler/kayip-talepleri',
  [CAPABILITY.inventoryTransferLossApproval]: '/stok/transferler/kayip-onaylari',
  [CAPABILITY.identityAssignmentsList]: '/ayarlar/kullanicilar-ve-yetkiler',
  [CAPABILITY.identityRolesDetail]: '/ayarlar/kullanicilar-ve-yetkiler/roller/:roleId',
  [CAPABILITY.identityAuditList]: '/ayarlar/denetim-kaydi',
  [CAPABILITY.settingsSalesDiscountLimits]: '/ayarlar/iskonto-limitleri',
  [CAPABILITY.settingsTaxMaster]: '/ayarlar/vergi',
  [CAPABILITY.settingsExchangeRates]: '/ayarlar/kurlar',
  [CAPABILITY.settingsSalesDeliveryTolerance]: '/ayarlar/teslim-toleransi',
  [CAPABILITY.settingsSalesApprovalThresholds]: '/ayarlar/onay-esikleri',
  [CAPABILITY.settingsLoginRateLimit]: '/ayarlar/giris-deneme-siniri',
  [CAPABILITY.settingsUnitConversionPolicy]: '/ayarlar/birim-cevrim-politikasi',
  [CAPABILITY.settingsUnitProfiles]: '/ayarlar/birim-profilleri',
  [CAPABILITY.settingsMoneyProfiles]: '/ayarlar/para-profilleri',
  [CAPABILITY.settingsSalesContractDiscountLimit]: '/ayarlar/sozlesme-iskonto-siniri',
  [CAPABILITY.settingsCollateralTypes]: '/ayarlar/teminat-turleri',
  [CAPABILITY.settingsFinanceOpeningPeriod]: '/ayarlar/finans-acilis-donemi',
  [CAPABILITY.settingsManufacturingScrapCost]: '/ayarlar/fire-maliyet-katilimi',
  [CAPABILITY.settingsNotificationRetention]: '/ayarlar/bildirim-etkinlik-suresi',
  [CAPABILITY.settingsWorkApprovalDuration]: '/ayarlar/onay-sureleri',
  [CAPABILITY.settingsAiGateway]: '/ayarlar/yapay-zeka-ag-gecidi',
  [CAPABILITY.settingsLegalDeadlineCalendar]: '/ayarlar/yasal-sure-takvimi',
  [CAPABILITY.settingsLegalDeadlines]: '/ayarlar/yasal-sureler',
  [CAPABILITY.settingsDocumentNumberSeries]: '/ayarlar/belge-numara-serileri',
  [CAPABILITY.settingsFinancePolicies]: '/ayarlar/finans-onay-politikalari',
  [CAPABILITY.settingsCashDifferencePolicies]: '/ayarlar/kasa-sayim-fark-politikalari',
  [CAPABILITY.settingsBankInstitutions]: '/ayarlar/banka-kurumlari',
  [CAPABILITY.settingsCollateralRatePolicies]: '/ayarlar/teminat-oran-politikalari',
  [CAPABILITY.settingsManufacturingParameters]: '/ayarlar/uretim-parametre-profilleri',
  [CAPABILITY.settingsManufacturingTolerances]: '/ayarlar/uretim-tolerans-profilleri',
  [CAPABILITY.settingsWorkCenterCapacity]: '/ayarlar/is-merkezi-kapasitesi',
  [CAPABILITY.settingsPurchasingReferenceLists]: '/ayarlar/referans-listeleri',
  [CAPABILITY.settingsPurchaseInvoiceVariance]: '/ayarlar/alis-fatura-fark-toleransi',
  [CAPABILITY.settingsWarehouseTransferScale]: '/ayarlar/depo-transfer-tartisi',
  [CAPABILITY.settingsSalesSubcontractTax]: '/ayarlar/satis-fason-vergi-varsayilani',
  [CAPABILITY.settingsPurchasingSubcontractTax]: '/ayarlar/alis-fason-vergi-varsayilani',
  [CAPABILITY.documentsBrandingManage]: '/ayarlar/belge-kimligi',
  [CAPABILITY.documentsSalesTemplatePrepare]: '/ayarlar/belge-sablonlari/satis',
  [CAPABILITY.documentsSalesTemplatePublish]: '/ayarlar/belge-sablonlari/satis',
  [CAPABILITY.documentsInventoryTemplatePrepare]: '/ayarlar/belge-sablonlari/stok',
  [CAPABILITY.documentsInventoryTemplatePublish]: '/ayarlar/belge-sablonlari/stok',
  [CAPABILITY.documentsLogisticsTemplatePrepare]: '/ayarlar/belge-sablonlari/lojistik',
  [CAPABILITY.documentsLogisticsTemplatePublish]: '/ayarlar/belge-sablonlari/lojistik',
  [CAPABILITY.settingsProductionReferenceLists]: '/ayarlar/uretim-referans-listeleri',
  [CAPABILITY.logisticsInstructionsList]: '/lojistik/yukleme-talimatlari',
  // Static before the parameter route, the /cari/yeni rule: 'yeni' must never be read as an instruction id.
  [CAPABILITY.logisticsInstructionsCreate]: '/lojistik/yukleme-talimatlari/yeni',
  [CAPABILITY.logisticsInstructionsDetail]: '/lojistik/yukleme-talimatlari/:instructionId',
  [CAPABILITY.logisticsOperationsSales]: '/lojistik/operasyon-isleri/satis',
  [CAPABILITY.logisticsOperationsPurchase]: '/lojistik/operasyon-isleri/satinalma',
  [CAPABILITY.logisticsPlanExcessApprove]: '/lojistik/plan-ustu-sevk-onay',
  [CAPABILITY.logisticsTransferDepartures]: '/lojistik/transfer-cikislari',
  [CAPABILITY.logisticsSubcontractList]: '/lojistik/fason-teslimler',
  [CAPABILITY.logisticsSubcontractCreate]: '/lojistik/fason-teslimler/yeni',
  [CAPABILITY.logisticsSubcontractDetail]: '/lojistik/fason-teslimler/:instructionId',
  [CAPABILITY.logisticsSubcontractIssue]: '/lojistik/fason-teslimler/:instructionId',
  [CAPABILITY.logisticsSubcontractLoad]: '/lojistik/fason-teslimler/:instructionId',
  [CAPABILITY.logisticsSubcontractWeigh]: '/lojistik/fason-teslimler/:instructionId',
  [CAPABILITY.logisticsSubcontractShip]: '/lojistik/fason-teslimler/:instructionId',
  [CAPABILITY.logisticsSubcontractCorrect]: '/lojistik/fason-teslimler/:instructionId',
  [CAPABILITY.purchasingRequestsList]: '/satinalma/talepler',
  // K5 keeps creation inside the P-01 list. Static `yeni` must never be read as a request id.
  [CAPABILITY.purchasingRequestsCreate]: '/satinalma/talepler/yeni',
  [CAPABILITY.purchasingRequestsDetail]: '/satinalma/talepler/:requestId',
  [CAPABILITY.purchasingOrdersList]: '/satinalma/siparisler',
  [CAPABILITY.purchasingInvoicesList]: '/satinalma/faturalar',
  [CAPABILITY.purchasingAdditionalCostsRead]: '/satinalma/ek-masraflar/:sourceKind/:orderId',
  [CAPABILITY.purchasingInvoicesDetail]: '/satinalma/faturalar/:invoiceId',
  [CAPABILITY.purchasingServiceOrdersList]: '/satinalma/hizmet-siparisleri',
  [CAPABILITY.purchasingServiceOrdersCreate]: '/satinalma/hizmet-siparisleri/yeni',
  [CAPABILITY.purchasingServiceOrdersDetail]: '/satinalma/hizmet-siparisleri/:serviceOrderId',
  // Static before the parameter route: `yeni` must never be read as an order id. The route remains
  // mounted but unavailable while the owner declaration is staged.
  [CAPABILITY.purchasingOrdersCreate]: '/satinalma/siparisler/yeni',
  [CAPABILITY.purchasingOrdersDetail]: '/satinalma/siparisler/:orderId',
}

/**
 * Nav label per capability. Only rendered when the server granted the capability. The table lives in
 * `./navigation` with the nav order; it is re-exported here so its existing importers keep one source.
 * `navigation.ts` imports only TYPES from this file, which is what keeps this from being a cycle.
 */
export { CAPABILITY_NAV_LABEL } from './navigation'

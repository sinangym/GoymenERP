/**
 * Accounting screen mount ids and routes — exactly what the backend's
 * `AccountingShellCapabilityProvider` declares (contract 1.4), pinned by a C# test to this file.
 *
 * STATUS: the provider decides, per declaration. Published: the periods, the journal list AND detail (ACC-V1 phase 1),
 * M-06 Mizan, M-01 and the normal-draft routes. Staged: the reversal propose control and the reversal approval
 * route, and the M-05 Hesap Planı route (FIN-S16); the shell never publishes a staged declaration, so `CapabilityRoute` closes such a route onto the neutral
 * not-found block and calls nothing, and a staged control is simply not rendered.
 * The ids are mount keys, not permissions: each route's service measures its own narrow permission.
 *
 * The approval (Ö4) and the close (Ö5) are CONTROLS of the M-07 period list (the provider's
 * `ParentCapabilityId`), never routes: `hasCapability` checks inside the list. The server refuses a
 * self-approval anyway.
 */

export const ACCOUNTING_CAPABILITY = {
  invoicedShipmentPrepare: 'accounting.invoiced-shipment-costs.prepare',
  invoicedShipmentApprove: 'accounting.invoiced-shipment-costs.approve',
  invoicedShipmentReverse: 'accounting.invoiced-shipment-costs.reverse',
  invoicedShipmentReverseApprove: 'accounting.invoiced-shipment-costs.reverse-approve',
  purchaseCostPrepare: 'accounting.purchase-cost-differences.prepare',
  purchaseCostApprove: 'accounting.purchase-cost-differences.approve',
  postingMappingsRead: 'accounting.posting-mappings.read',
  postingMappingsPrepare: 'accounting.posting-mappings.prepare',
  postingMappingsActivate: 'accounting.posting-mappings.activate',
  sourceVouchers: 'accounting.source-vouchers.list',
  sourceVouchersApprove: 'accounting.source-vouchers.approve',
  invoiceLineCogsPrepare: 'accounting.invoice-line-cogs.prepare',
  invoiceLineCogsApprove: 'accounting.invoice-line-cogs.approve',
  costDifferencePrepare: 'accounting.manufacturing-cost-differences.prepare',
  costDifferenceApprove: 'accounting.manufacturing-cost-differences.approve',
  /** M-07 Dönem — the company's accounting periods. */
  periodsList: 'accounting.periods.list',
  /** M-07 — propose a period opening. */
  periodsPropose: 'accounting.periods.propose',
  /** M-07 — the approval control of the list (Ö4: someone other than the preparer; not a route). */
  periodsApprove: 'accounting.periods.approve',
  /** M-07 — the close control of the list (Ö5; not a route). */
  periodsClose: 'accounting.periods.close',
  /** M-07 — the reopening approval control of the list (ROL-D12: its own permission; not a route). */
  periodsReopen: 'accounting.periods.reopen',
  /** M-04 Muhasebe Fişleri — the journals, read-only. */
  journalsList: 'accounting.journals.list',
  /** M-04 — one journal, read-only. */
  journalsDetail: 'accounting.journals.detail',
  /** ACC-V1 — create a normal journal draft from server-approved accounting sources. */
  normalDraftCreate: 'accounting.normal-journal-drafts.create',
  /** ACC-V1 — reopen a draft prepared by the current user. */
  normalDraftDetail: 'accounting.normal-journal-drafts.detail',
  /** ACC-V1 — read and approve a company draft with the narrow approval permission. */
  normalDraftApprove: 'accounting.normal-journal-drafts.approve',
  /** M-01 Muhasebe Kontrol Merkezi — the posting queues, read-only. */
  postingQueues: 'accounting.posting-queues',
  /** M-06 Mizan — posted journal aggregates, read-only. */
  trialBalance: 'accounting.trial-balance',
  /** M-05 Hesap Planı — the active approved account plan, read-only (FIN-S16; staged). */
  accountPlan: 'accounting.account-plan',
  /** ACC-V1 — the reversal proposal control of the journal detail (not a route; propose only, never approve). */
  journalReversalPropose: 'accounting.journal-reversals.propose',
  /** ACC-V1 — approve ANOTHER person's pending reversal proposal (maker ≠ checker). */
  journalReversalApprove: 'accounting.journal-reversals.approve',
} as const

export type AccountingCapabilityId = (typeof ACCOUNTING_CAPABILITY)[keyof typeof ACCOUNTING_CAPABILITY]

export const ACCOUNTING_ROUTE = {
  invoicedShipmentPrepare: '/muhasebe/sevk-smm-hazirla',
  invoicedShipmentApprove: '/muhasebe/sevk-smm-onay',
  invoicedShipmentReverse: '/muhasebe/sevk-smm-ters-kayit',
  invoicedShipmentReverseApprove: '/muhasebe/sevk-smm-ters-kayit-onay',
  purchaseCostPrepare: '/muhasebe/alis-maliyet-farki-hazirla',
  purchaseCostApprove: '/muhasebe/alis-maliyet-farki-onay',
  postingMappings: '/muhasebe/hesap-eslemeleri',
  sourceVouchers: '/muhasebe/kaynak-fisler',
  invoiceLineCogsPrepare: '/muhasebe/fatura-satiri-smm',
  invoiceLineCogsApprove: '/muhasebe/fatura-satiri-smm-onay',
  costDifferencePrepare: '/muhasebe/uretim-maliyet-farklari',
  costDifferenceApprove: '/muhasebe/uretim-maliyet-farki-onay',
  periodsList: '/muhasebe/donemler',
  // Static before the parameter-free list's siblings; `yeni` is its own route.
  periodsPropose: '/muhasebe/donemler/yeni',
  journalsList: '/muhasebe/fisler',
  journalsDetail: '/muhasebe/fisler/:journalId',
  normalDraftCreate: '/muhasebe/normal-fisler/yeni',
  normalDraftDetail: '/muhasebe/normal-fisler/:draftId',
  normalDraftApprove: '/muhasebe/normal-fis-onay/:draftId',
  postingQueues: '/muhasebe/kontrol',
  trialBalance: '/muhasebe/mizan',
  accountPlan: '/muhasebe/hesap-plani',
  journalReversalApprove: '/muhasebe/ters-kayit-onay/:proposalId',
} as const

export const invoiceLineCogsPath = (invoiceId: string, invoiceLineId: string, mode: 'prepare' | 'approve' = 'prepare') =>
  `${mode === 'prepare' ? ACCOUNTING_ROUTE.invoiceLineCogsPrepare : ACCOUNTING_ROUTE.invoiceLineCogsApprove}?${new URLSearchParams({ invoiceId, invoiceLineId }).toString()}`

export const journalDetailPath = (journalId: string) => `/muhasebe/fisler/${encodeURIComponent(journalId)}`

export const normalDraftDetailPath = (draftId: string) =>
  `/muhasebe/normal-fisler/${encodeURIComponent(draftId)}`

export const normalDraftApprovalPath = (draftId: string) =>
  `/muhasebe/normal-fis-onay/${encodeURIComponent(draftId)}`

export const periodJournalsPath = (periodId: string) =>
  `/muhasebe/fisler?${new URLSearchParams({ periodId }).toString()}`

export const journalReversalApprovalPath = (proposalId: string) =>
  `/muhasebe/ters-kayit-onay/${encodeURIComponent(proposalId)}`

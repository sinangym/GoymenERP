/**
 * Finance screen mount ids and routes — exactly what the backend's
 * `FinanceShellCapabilityProvider` declares (FIN-UI-1a, #325), pinned by a test to that file.
 *
 * STATUS: PUBLISHED (FIN-UI-1c). Every declaration is `ImplementationAvailable: true` since the flip
 * that followed the FIN-01 browser acceptance (#332, run 35444049175). A screen is still published to
 * an actor only with its own narrow Finance permission; without it `CapabilityRoute` closes the route
 * onto the neutral not-found block and calls nothing. The ids are mount keys, not permissions: each
 * route's service measures its own permission afresh.
 *
 * The approval is a CONTROL of the F-04 list (the provider's `ParentCapabilityId`), never a route:
 * it is a `hasCapability` check inside the list, and the server refuses a self-approval (Ö4) anyway.
 */

export const FINANCE_CAPABILITY = {
  /** F-02 Tahsilat / Ödeme — the company's manual-receipt drafts. */
  receiptsList: 'finance.receipts.list',
  /** F-02 — a new manual-receipt draft (entered from a Cari account). */
  receiptsDraft: 'finance.receipts.draft',
  /** F-02 — one draft. */
  receiptsDetail: 'finance.receipts.detail',
  /** F-04 Banka Hesapları — the bank-account master. */
  bankAccountsList: 'finance.bank-accounts.list',
  /** F-04 — propose a bank account. */
  bankAccountsPropose: 'finance.bank-accounts.propose',
  /** F-04 — the approval control of the list (not a route). */
  bankAccountsApprove: 'finance.bank-accounts.approve',
  /**
   * F-02 — the Record control of the detail (not a route; FIN-T05a). Provider contract 1.1 declares it; the shared
   * shell only serves it after the central owner binds the reviewed 1.1 authority revision. Until then startup stays
   * fail-closed rather than offering an unreviewed control.
   */
  receiptsRecord: 'finance.receipts.record',
  /** FIN-T06/B1: proposal control on a recorded receipt detail. */
  receiptsReversePropose: 'finance.receipts.reverse.propose',
  /** FIN-T06/B1: pending reversal checker route. */
  receiptsReverseApprove: 'finance.receipts.reverse.approve',
} as const

export type FinanceCapabilityId = (typeof FINANCE_CAPABILITY)[keyof typeof FINANCE_CAPABILITY]

export const FINANCE_ROUTE = {
  receiptsList: '/finans/tahsilatlar',
  // Static before the parameter route: `yeni` must never be read as a source id.
  receiptsDraft: '/finans/tahsilatlar/yeni',
  receiptsDetail: '/finans/tahsilatlar/:sourceId',
  reversalApprovals: '/finans/ters-kayit-onaylari',
  bankAccountsList: '/finans/banka-hesaplari',
  bankAccountsPropose: '/finans/banka-hesaplari/yeni',
} as const

export const receiptDetailPath = (sourceId: string) => `/finans/tahsilatlar/${encodeURIComponent(sourceId)}`

/**
 * The entry into a new draft: the Cari account screen links here with the party and its company
 * account (Finance has no party picker; the draft-context read validates both server-side).
 */
export const receiptDraftPath = (partyId: string, companyAccountId: string) =>
  `/finans/tahsilatlar/yeni?${new URLSearchParams({ partyId, companyAccountId }).toString()}`

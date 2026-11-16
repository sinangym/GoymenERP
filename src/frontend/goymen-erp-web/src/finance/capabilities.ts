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
  /** Customer cheque / note portfolio — read-only route, independent from receipt-list access. */
  customerInstrumentsList: 'finance.customer-instruments.list',
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
  manualBankPaymentsList: 'finance.manual-bank-payments.list',
  manualBankPaymentsCreate: 'finance.manual-bank-payments.create',
  manualBankPaymentsDetail: 'finance.manual-bank-payments.detail',
  manualBankPaymentsApprove: 'finance.manual-bank-payments.approve',
  manualBankPaymentsRecord: 'finance.manual-bank-payments.record',
  /** API-23: propose the reversal of a recorded payment (control of the payment detail). */
  manualBankPaymentsReversePropose: 'finance.manual-bank-payments.reverse.propose',
  /** API-23: approve or reject a pending payment reversal (another user than the proposer). */
  manualBankPaymentsReverseApprove: 'finance.manual-bank-payments.reverse.approve',
  /** FNS-D25 (API-25): the payment not tied to a party — list, preparation, approval and record on one workspace. */
  expensePaymentsWorkspace: 'finance.expense-payments.workspace',
  /** FNS-D25: prepare or replace a draft (control of the workspace). */
  expensePaymentsDraft: 'finance.expense-payments.draft',
  /** FNS-D25: approve another user's draft (control of the workspace; the server refuses a self-approval). */
  expensePaymentsApprove: 'finance.expense-payments.approve',
  /** FNS-D25: the final, explicitly confirmed record — the money fact (control of the workspace). */
  expensePaymentsRecord: 'finance.expense-payments.record',
  /** FNS-D24 (API-32/34): the transfer between two treasury accounts and the currency exchange — one workspace. */
  cashTransfersWorkspace: 'finance.cash-transfers.workspace',
  /** FNS-D24: the explicitly confirmed transfer record — the money fact (control of the workspace). */
  cashTransfersCommit: 'finance.cash-transfers.commit',
  officialInvoiceImportUpload: 'finance.official-invoice-import.upload',
  officialInvoiceImportApprove: 'finance.official-invoice-import.approve',
  allocationsList: 'finance.allocations.list',
  allocationsWrite: 'finance.allocations.write',
  /** FI04 Açık kalemler — the company's admitted open items, read-only. */
  openItemsList: 'finance.open-items.list',
  /** FI04 — one open item, reached from a row. Money and status stay the amount authority's (not a capability). */
  openItemsDetail: 'finance.open-items.detail',
  /** B-05 (KLN-D3, M-03) Muhasebe > Cari Hesaplar — the party ledger and balance, under `finance.party-ledger.read`. */
  partyLedgerAccounting: 'finance.party-ledger.accounting',
  /** B-05 (KLN-D3, C-04) Cari > Cari Hesap Takibi — the same party-ledger read, entered from Cari. */
  partyLedgerCari: 'finance.party-ledger.cari',
  /** F-018 L1 Kredi Limitleri — the company accounts' credit limits, under `finance.credit-limits.read`. */
  creditLimitsList: 'finance.credit-limits.list',
  /** FNS-D27a: propose a set, change or removal (control of the list; nothing changes until an approver decides it). */
  creditLimitsPropose: 'finance.credit-limits.propose',
  /** FNS-D27 / FNS-D46: set or remove a limit in one step, or decide a proposal (control of the list; FNS-D56 / FNS-D57(3)). */
  creditLimitsApprove: 'finance.credit-limits.approve',
  /** F-018 L2 Müşteri Teminatları — an account's received collaterals, under `finance.collaterals.read`. */
  collateralsList: 'finance.collaterals.list',
  /** FNS-D57: record, change or release a collateral in one step (control of the list; no approval). */
  collateralsRecord: 'finance.collaterals.record',
  /** FIN-S9 (FNS-D23) Açılış Bakiyeleri — under `finance.opening-balances.read`; shown only while the period is open. */
  openingBalancesList: 'finance.opening-balances.list',
  /** FNS-D23: a specialist proposes an opening balance (control of the list). */
  openingBalancesPropose: 'finance.opening-balances.propose',
  /** FNS-D23: a manager approves or rejects a proposal, never their own (control of the list). */
  openingBalancesApprove: 'finance.opening-balances.approve',
} as const

export type FinanceCapabilityId = (typeof FINANCE_CAPABILITY)[keyof typeof FINANCE_CAPABILITY]

export const FINANCE_ROUTE = {
  receiptsList: '/finans/tahsilatlar',
  // Static before the parameter route: `yeni` must never be read as a source id.
  receiptsDraft: '/finans/tahsilatlar/yeni',
  receiptsDetail: '/finans/tahsilatlar/:sourceId',
  customerInstrumentsList: '/finans/musteri-cek-senetleri',
  reversalApprovals: '/finans/ters-kayit-onaylari',
  bankAccountsList: '/finans/banka-hesaplari',
  bankAccountsPropose: '/finans/banka-hesaplari/yeni',
  manualBankPaymentsList: '/finans/odemeler',
  manualBankPaymentsCreate: '/finans/odemeler/yeni',
  manualBankPaymentsDetail: '/finans/odemeler/:paymentId',
  expensePayments: '/finans/masraf-odemeleri',
  cashTransfers: '/finans/virman',
  officialInvoiceImportUpload: '/finans/onayli-fatura-taksit-kaynagi-yukle',
  officialInvoiceImportApprove: '/finans/resmi-fatura-aktarim-onaylari',
  allocationsList: '/finans/dagitimlar',
  openItemsList: '/finans/acik-kalemler',
  openItemsDetail: '/finans/acik-kalemler/:openItemId',
  partyLedgerAccounting: '/muhasebe/cari-hesaplar',
  partyLedgerCari: '/cari/hesap-takibi',
  creditLimitsList: '/finans/kredi-limitleri',
  collateralsList: '/finans/teminatlar',
  openingBalancesList: '/finans/acilis-bakiyeleri',
} as const

export const receiptDetailPath = (sourceId: string) => `/finans/tahsilatlar/${encodeURIComponent(sourceId)}`

export const openItemDetailPath = (openItemId: string) => `/finans/acik-kalemler/${encodeURIComponent(openItemId)}`

export const manualBankPaymentDetailPath = (paymentId: string) =>
  `/finans/odemeler/${encodeURIComponent(paymentId)}`

/**
 * The entry into a new draft: the Cari account screen links here with the party and its company
 * account (Finance has no party picker; the draft-context read validates both server-side).
 */
export const receiptDraftPath = (partyId: string, companyAccountId: string) =>
  `/finans/tahsilatlar/yeni?${new URLSearchParams({ partyId, companyAccountId }).toString()}`

/**
 * DEC-FIN-016: this query parameter names the instrument whose detail is open on the dedicated portfolio route.
 * Keeping list and detail on one canonical address lets a user close and reopen the same instrument without requiring
 * receipt-list access.
 */
export const INSTRUMENT_QUERY = 'cekSenet'

export const portfolioInstrumentPath = (instrumentId: string) =>
  `${FINANCE_ROUTE.customerInstrumentsList}?${new URLSearchParams({ [INSTRUMENT_QUERY]: instrumentId }).toString()}`

/**
 * The party ledger (cari hesap ekstresi) is addressed by these query parameters on its routes — Muhasebe > Cari
 * Hesaplar and Cari > Cari Hesap Takibi (B-05) — and, as before, on the F-02 list: they name the party account and the
 * date range, so the range survives opening a row's source and coming back, and the same address reopens the same
 * ledger.
 */
export const LEDGER_QUERY = {
  party: 'ekstreCari',
  account: 'ekstreHesap',
  from: 'ekstreBaslangic',
  to: 'ekstreBitis',
} as const

export const partyLedgerPath = (
  partyId: string,
  companyAccountId: string,
  range?: { readonly from: string; readonly to: string },
  base: string = FINANCE_ROUTE.receiptsList,
) => {
  const params = new URLSearchParams({ [LEDGER_QUERY.party]: partyId, [LEDGER_QUERY.account]: companyAccountId })
  if (range !== undefined) {
    params.set(LEDGER_QUERY.from, range.from)
    params.set(LEDGER_QUERY.to, range.to)
  }
  return `${base}?${params.toString()}`
}

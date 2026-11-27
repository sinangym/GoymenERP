/**
 * The shell mounts exactly the Finance capability ids and routes that lane declares.
 *
 * Two files, two single writers: `src/finance/capabilities.ts` belongs to the Finance lane (FİNANS),
 * `src/shell/capabilities.ts` to the shared-integration owner. A rename on one side alone would make
 * the shell quietly stop mounting a screen the server has granted.
 *
 * WHAT THIS IS NOT: backend parity or availability. The lane pins its ids to the C# provider
 * (`FinanceShellCapabilityProviderTests`); whether a Finance screen opens is the server's published
 * snapshot, and a staged declaration keeps `CapabilityRoute` on the neutral not-found block.
 */

import { describe, expect, it } from 'vitest'
import { matchPath, matchRoutes } from 'react-router'
import { CAPABILITY, CAPABILITY_ROUTE, CAPABILITY_NAV_LABEL, type CapabilityId } from './capabilities'
import {
  FINANCE_WORK_DECISION_CAPABILITIES,
  FINANCE_CAPABILITY,
  FINANCE_ROUTE,
  manualBankPaymentDetailPath,
  openItemDetailPath,
  receiptDetailPath,
  receiptDraftPath,
} from '../finance/capabilities'
import { financeRoutes } from '../finance/financeRoutes'
import { workRoutes } from '../work/workRoutes'
import { shellRoutes } from './router'
import {
  OFFICIAL_INVOICE_IMPORT_APPROVALS_ROUTE,
  OFFICIAL_INVOICE_IMPORT_UPLOAD_ROUTE,
} from '../finance/officialInvoiceImportRoutes'

const ROUTED = [
  [CAPABILITY.financeExchangeBookValuesPrepare, FINANCE_CAPABILITY.exchangeBookValuesPrepare, FINANCE_ROUTE.exchangeBookValuesPrepare],
  [CAPABILITY.financeExchangeBookValuesApprove, FINANCE_CAPABILITY.exchangeBookValuesApprove, FINANCE_ROUTE.exchangeBookValuesApprove],
  [CAPABILITY.financePartyBalanceTransfers, FINANCE_CAPABILITY.partyBalanceTransfers, FINANCE_ROUTE.partyBalanceTransfers],
  [CAPABILITY.financeBankStatements, FINANCE_CAPABILITY.bankStatements, FINANCE_ROUTE.bankStatements],
  [CAPABILITY.financeCreditsWorkspace, FINANCE_CAPABILITY.creditsWorkspace, FINANCE_ROUTE.creditsWorkspace],
  [CAPABILITY.financeOfficialInvoicesRecord, FINANCE_CAPABILITY.officialInvoicesRecord, FINANCE_ROUTE.officialInvoicesRecord],
  [CAPABILITY.financeOfficialInvoicesApprove, FINANCE_CAPABILITY.officialInvoicesApprove, FINANCE_ROUTE.officialInvoicesApprove],
  [CAPABILITY.financeReceiptsList, FINANCE_CAPABILITY.receiptsList, FINANCE_ROUTE.receiptsList],
  [CAPABILITY.financeReceiptsDraft, FINANCE_CAPABILITY.receiptsDraft, FINANCE_ROUTE.receiptsDraft],
  [CAPABILITY.financeReceiptsDetail, FINANCE_CAPABILITY.receiptsDetail, FINANCE_ROUTE.receiptsDetail],
  [CAPABILITY.financeCustomerInstrumentsList, FINANCE_CAPABILITY.customerInstrumentsList,
    FINANCE_ROUTE.customerInstrumentsList],
  [CAPABILITY.financeOfficialInvoiceImportUpload, FINANCE_CAPABILITY.officialInvoiceImportUpload,
    FINANCE_ROUTE.officialInvoiceImportUpload],
  [CAPABILITY.financeOfficialInvoiceImportApprove, FINANCE_CAPABILITY.officialInvoiceImportApprove,
    FINANCE_ROUTE.officialInvoiceImportApprove],
  [CAPABILITY.financeAllocationsList, FINANCE_CAPABILITY.allocationsList, FINANCE_ROUTE.allocationsList],
  [CAPABILITY.financeOpenItemsList, FINANCE_CAPABILITY.openItemsList, FINANCE_ROUTE.openItemsList],
  [CAPABILITY.financeOpenItemsDetail, FINANCE_CAPABILITY.openItemsDetail, FINANCE_ROUTE.openItemsDetail],
  [CAPABILITY.financePartyLedgerAccounting, FINANCE_CAPABILITY.partyLedgerAccounting, FINANCE_ROUTE.partyLedgerAccounting],
  [CAPABILITY.financePartyLedgerCari, FINANCE_CAPABILITY.partyLedgerCari, FINANCE_ROUTE.partyLedgerCari],
  [
    CAPABILITY.financeReceiptsReverseApprove,
    FINANCE_CAPABILITY.receiptsReverseApprove,
    FINANCE_ROUTE.reversalApprovals,
  ],
  [CAPABILITY.financeBankAccountsList, FINANCE_CAPABILITY.bankAccountsList, FINANCE_ROUTE.bankAccountsList],
  [CAPABILITY.financeBankAccountsPropose, FINANCE_CAPABILITY.bankAccountsPropose, FINANCE_ROUTE.bankAccountsPropose],
  [CAPABILITY.financeManualBankPaymentsList, FINANCE_CAPABILITY.manualBankPaymentsList,
    FINANCE_ROUTE.manualBankPaymentsList],
  [CAPABILITY.financeManualBankPaymentsCreate, FINANCE_CAPABILITY.manualBankPaymentsCreate,
    FINANCE_ROUTE.manualBankPaymentsCreate],
  [CAPABILITY.financeManualBankPaymentsDetail, FINANCE_CAPABILITY.manualBankPaymentsDetail,
    FINANCE_ROUTE.manualBankPaymentsDetail],
  [CAPABILITY.financeExpensePaymentsWorkspace, FINANCE_CAPABILITY.expensePaymentsWorkspace,
    FINANCE_ROUTE.expensePayments],
  [CAPABILITY.financeCashTransfersWorkspace, FINANCE_CAPABILITY.cashTransfersWorkspace, FINANCE_ROUTE.cashTransfers],
  [CAPABILITY.financeCreditLimitsList, FINANCE_CAPABILITY.creditLimitsList, FINANCE_ROUTE.creditLimitsList],
  [CAPABILITY.financeCollateralsList, FINANCE_CAPABILITY.collateralsList, FINANCE_ROUTE.collateralsList],
  [CAPABILITY.financeOpeningBalancesList, FINANCE_CAPABILITY.openingBalancesList, FINANCE_ROUTE.openingBalancesList],
] as const

/**
 * CONTROLS are capabilities the lane offers INSIDE a screen, never as a screen of their own: the
 * bank-account approval on F-04's list, and the Record of a definitive receipt on F-02's detail
 * (FIN-T05a), and reversal proposal on that recorded receipt (FIN-T06), and the API-23 payment reversal
 * propose/approve controls on the recorded payment's detail. Each is deliberately absent
 * from the shell's table, from `CAPABILITY_ROUTE` and from
 * `CAPABILITY_NAV_LABEL` — a control with a route would be a second entry point to an action that
 * belongs inside its list, and a control with a nav label would be a sidebar link to something that
 * is not a screen. They are listed here rather than excused one by one, so the next control fails
 * this test loudly instead of being quietly appended to a filter.
 */
const CONTROLS: readonly string[] = [
  FINANCE_CAPABILITY.bankStatementsImport,
  FINANCE_CAPABILITY.bankStatementsMatch,
  FINANCE_CAPABILITY.creditsPropose,
  FINANCE_CAPABILITY.creditsApprove,
  FINANCE_CAPABILITY.officialInvoicesWithdraw,
  FINANCE_CAPABILITY.bankAccountsApprove,
  FINANCE_CAPABILITY.receiptsRecord,
  FINANCE_CAPABILITY.receiptsReversePropose,
  FINANCE_CAPABILITY.manualBankPaymentsApprove,
  FINANCE_CAPABILITY.manualBankPaymentsRecord,
  FINANCE_CAPABILITY.manualBankPaymentsReversePropose,
  FINANCE_CAPABILITY.manualBankPaymentsReverseApprove,
  FINANCE_CAPABILITY.expensePaymentsDraft,
  FINANCE_CAPABILITY.expensePaymentsApprove,
  FINANCE_CAPABILITY.expensePaymentsRecord,
  FINANCE_CAPABILITY.cashTransfersCommit,
  FINANCE_CAPABILITY.creditLimitsPropose,
  FINANCE_CAPABILITY.creditLimitsApprove,
  FINANCE_CAPABILITY.collateralsRecord,
  FINANCE_CAPABILITY.openingBalancesPropose,
  FINANCE_CAPABILITY.openingBalancesApprove,
  FINANCE_CAPABILITY.allocationsWrite,
]

describe('shell ↔ Finance lane capability ids', () => {
  it('mounts the official-invoice uploader and checker once under their own authority boundaries', () => {
    expect(financeRoutes.filter((route) => route.path === OFFICIAL_INVOICE_IMPORT_UPLOAD_ROUTE.slice(1)))
      .toHaveLength(1)
    expect(financeRoutes.filter((route) => route.path === OFFICIAL_INVOICE_IMPORT_APPROVALS_ROUTE.slice(1)))
      .toHaveLength(1)
  })

  it.each(FINANCE_WORK_DECISION_CAPABILITIES)('mounts the Finance purpose %s once under its own capability with static kind precedence', (id, path) => {
    expect(Object.values(CAPABILITY)).toContain(id)
    expect(CAPABILITY_ROUTE[id]).toBe(path)
    expect(CAPABILITY_NAV_LABEL[id]).toBe('')
    const mounted=workRoutes.filter(route=>route.path===path.slice(1))
    expect(mounted).toHaveLength(1)
    expect(matchRoutes(shellRoutes,path.replace(':approvalId','cccccccc-0000-4000-8000-0000000000f1'))?.at(-1)?.route).toBe(mounted[0])
  })

  it('classifies every lane id as either routed or a control', () => {
    // Without this, an unclassified id still fails — but as a phantom MISSING MOUNT in the next
    // case, which is how finance.receipts.record read at f3800f3c: "expected 5 to equal 6", sending
    // the reader to the shell's table to add something that must never be there. Here the id is
    // printed as unclassified, with the two places it can go.
    const classified = [...ROUTED.map(([, laneId]) => laneId as string), ...CONTROLS]
    expect(
      (Object.values(FINANCE_CAPABILITY) as string[]).sort(),
      'a lane id is either a screen (add it to ROUTED with its shell id and route) or a control (add it to CONTROLS)',
    ).toEqual([...classified].sort())
  })

  it('declares the lane’s twelve route ids with the lane’s values and routes', () => {
    for (const [shellId, laneId, route] of ROUTED) {
      expect(shellId).toBe(laneId)
      expect(CAPABILITY_ROUTE[shellId]).toBe(route)
    }
  })

  it('mounts every routed Finance id and nothing else under finance.; a control is no route', () => {
    const shellFinanceIds = (Object.values(CAPABILITY) as string[]).filter((id) => id.startsWith('finance.'))
    const laneRoutedIds = (Object.values(FINANCE_CAPABILITY) as string[]).filter(
      (id) => !CONTROLS.includes(id),
    )
    expect([...shellFinanceIds].sort()).toEqual([...laneRoutedIds, ...FINANCE_WORK_DECISION_CAPABILITIES.map(([id]) => id)].sort())
    for (const control of CONTROLS) {
      expect(Object.keys(CAPABILITY_ROUTE), `${control} is a control, so it must have no route`).not.toContain(control)
      expect(
        Object.keys(CAPABILITY_NAV_LABEL),
        `${control} is a control, so it must have no nav label`,
      ).not.toContain(control)
    }
    // A filter that excused everything would make the comparison above meaningless, so pin that the
    // lane still declares screens as well as controls.
    expect(laneRoutedIds.length).toBeGreaterThan(0)
  })

  it('matches the lane’s own link helpers, and the static routes are never a source id', () => {
    const sourceId = '7b1c0f2e-0000-4a00-8000-000000000004'
    const detail = matchPath(CAPABILITY_ROUTE[CAPABILITY.financeReceiptsDetail], receiptDetailPath(sourceId))
    expect(detail?.params.sourceId).toBe(sourceId)
    const entry = new URL(receiptDraftPath('p', 'a'), 'http://shell.invalid')
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.financeReceiptsDraft], entry.pathname)).not.toBeNull()
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.financeReceiptsList], FINANCE_ROUTE.receiptsDraft)).toBeNull()
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.financeBankAccountsList], FINANCE_ROUTE.bankAccountsPropose)).toBeNull()
    const paymentId = '7b1c0f2e-0000-4a00-8000-000000000005'
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.financeManualBankPaymentsDetail],
      manualBankPaymentDetailPath(paymentId))?.params.paymentId).toBe(paymentId)
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.financeManualBankPaymentsList],
      FINANCE_ROUTE.manualBankPaymentsCreate)).toBeNull()
    const openItemId = '7b1c0f2e-0000-4a00-8000-000000000006'
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.financeOpenItemsDetail],
      openItemDetailPath(openItemId))?.params.openItemId).toBe(openItemId)
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.financeOpenItemsList], openItemDetailPath(openItemId))).toBeNull()
  })

  it('mounts the FI04 open-item list and detail once each, and the amount authority is no capability', () => {
    expect(financeRoutes.filter((route) => route.path === FINANCE_ROUTE.openItemsList.slice(1))).toHaveLength(1)
    expect(financeRoutes.filter((route) => route.path === FINANCE_ROUTE.openItemsDetail.slice(1))).toHaveLength(1)
    const ids = [...(Object.values(CAPABILITY) as string[]), ...(Object.values(FINANCE_CAPABILITY) as string[])]
    expect(ids.filter((id) => id.startsWith('finance.open-items.')).sort()).toEqual([
      'finance.open-items.detail', 'finance.open-items.detail', 'finance.open-items.list', 'finance.open-items.list',
    ])
    expect(ids).not.toContain('finance.open-items.amount.read')
  })

  it('gives the Finance work lists and import actions nav entries and the other finance routes none', () => {
    const labels: Record<string, string> = {}
    for (const [shellId] of ROUTED) labels[shellId] = CAPABILITY_NAV_LABEL[shellId as CapabilityId]
    expect(labels).toEqual({
      'finance.cash-exchange-book-values.prepare': 'Döviz defter değeri hazırla',
      'finance.cash-exchange-book-values.approve': 'Döviz defter değeri onayları',
      'finance.party-balance-transfers.record': 'Cari Bakiye Devri',
      'finance.bank-statements.read': 'Banka Ekstresi',
      'finance.credits.workspace': 'Krediler',
      'finance.official-sales-invoices.record': 'Resmî satış faturaları',
      'finance.official-sales-invoices.approve': 'Resmî satış faturası onayları',
      'finance.receipts.list': 'Tahsilat Taslakları',
      'finance.receipts.draft': '',
      'finance.receipts.detail': '',
      'finance.customer-instruments.list': 'Müşteri Çekleri ve Senetleri',
      'finance.official-invoice-import.upload': 'Onaylı fatura taksit kaynağı yükle',
      'finance.official-invoice-import.approve': 'Resmî fatura aktarım onayları',
      'finance.allocations.list': 'Finansal Dağıtımlar',
      'finance.open-items.list': 'Açık kalemler',
      'finance.open-items.detail': '',
      'finance.party-ledger.accounting': 'Cari Hesaplar',
      'finance.party-ledger.cari': 'Cari Hesap Takibi',
      'finance.receipts.reverse.approve': 'Ters Kayıt Onayları',
      'finance.bank-accounts.list': 'Banka Hesapları',
      'finance.bank-accounts.propose': '',
      'finance.manual-bank-payments.list': 'Giden Ödemeler',
      'finance.manual-bank-payments.create': '',
      'finance.manual-bank-payments.detail': '',
      'finance.expense-payments.workspace': 'Masraf Ödemeleri',
      'finance.cash-transfers.workspace': 'Virman ve Döviz Bozdurma',
      'finance.credit-limits.list': 'Kredi Limitleri',
      'finance.collaterals.list': 'Müşteri Teminatları',
      'finance.opening-balances.list': 'Açılış Bakiyeleri',
    })
  })
})

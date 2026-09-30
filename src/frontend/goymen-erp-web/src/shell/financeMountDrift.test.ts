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
import { matchPath } from 'react-router'
import { CAPABILITY, CAPABILITY_ROUTE, CAPABILITY_NAV_LABEL, type CapabilityId } from './capabilities'
import {
  FINANCE_CAPABILITY,
  FINANCE_ROUTE,
  manualBankPaymentDetailPath,
  receiptDetailPath,
  receiptDraftPath,
} from '../finance/capabilities'
import { financeRoutes } from '../finance/financeRoutes'
import { OFFICIAL_INVOICE_IMPORT_APPROVALS_ROUTE } from '../finance/officialInvoiceImportRoutes'

const ROUTED = [
  [CAPABILITY.financeReceiptsList, FINANCE_CAPABILITY.receiptsList, FINANCE_ROUTE.receiptsList],
  [CAPABILITY.financeReceiptsDraft, FINANCE_CAPABILITY.receiptsDraft, FINANCE_ROUTE.receiptsDraft],
  [CAPABILITY.financeReceiptsDetail, FINANCE_CAPABILITY.receiptsDetail, FINANCE_ROUTE.receiptsDetail],
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
] as const

/**
 * CONTROLS are capabilities the lane offers INSIDE a screen, never as a screen of their own: the
 * bank-account approval on F-04's list, and the Record of a definitive receipt on F-02's detail
 * (FIN-T05a), and reversal proposal on that recorded receipt (FIN-T06). Each is deliberately absent
 * from the shell's table, from `CAPABILITY_ROUTE` and from
 * `CAPABILITY_NAV_LABEL` — a control with a route would be a second entry point to an action that
 * belongs inside its list, and a control with a nav label would be a sidebar link to something that
 * is not a screen. They are listed here rather than excused one by one, so the next control fails
 * this test loudly instead of being quietly appended to a filter.
 */
const CONTROLS: readonly string[] = [
  FINANCE_CAPABILITY.bankAccountsApprove,
  FINANCE_CAPABILITY.receiptsRecord,
  FINANCE_CAPABILITY.receiptsReversePropose,
  FINANCE_CAPABILITY.manualBankPaymentsApprove,
  FINANCE_CAPABILITY.manualBankPaymentsRecord,
]

describe('shell ↔ Finance lane capability ids', () => {
  it('mounts the official-invoice checker once under the existing Finance list screen boundary', () => {
    expect(financeRoutes.filter((route) => route.path === OFFICIAL_INVOICE_IMPORT_APPROVALS_ROUTE.slice(1)))
      .toHaveLength(1)
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

  it('declares the lane’s nine route ids with the lane’s values and routes', () => {
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
    expect([...shellFinanceIds].sort()).toEqual([...laneRoutedIds].sort())
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
  })

  it('gives the two lists and reversal queue nav entries and the other three none', () => {
    const labels: Record<string, string> = {}
    for (const [shellId] of ROUTED) labels[shellId] = CAPABILITY_NAV_LABEL[shellId as CapabilityId]
    expect(labels).toEqual({
      'finance.receipts.list': 'Tahsilat Taslakları',
      'finance.receipts.draft': '',
      'finance.receipts.detail': '',
      'finance.receipts.reverse.approve': 'Ters Kayıt Onayları',
      'finance.bank-accounts.list': 'Banka Hesapları',
      'finance.bank-accounts.propose': '',
      'finance.manual-bank-payments.list': 'Giden Ödemeler',
      'finance.manual-bank-payments.create': '',
      'finance.manual-bank-payments.detail': '',
    })
  })
})

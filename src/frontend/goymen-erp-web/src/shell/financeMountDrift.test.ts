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
import { FINANCE_CAPABILITY, FINANCE_ROUTE, receiptDetailPath, receiptDraftPath } from '../finance/capabilities'

const ROUTED = [
  [CAPABILITY.financeReceiptsList, FINANCE_CAPABILITY.receiptsList, FINANCE_ROUTE.receiptsList],
  [CAPABILITY.financeReceiptsDraft, FINANCE_CAPABILITY.receiptsDraft, FINANCE_ROUTE.receiptsDraft],
  [CAPABILITY.financeReceiptsDetail, FINANCE_CAPABILITY.receiptsDetail, FINANCE_ROUTE.receiptsDetail],
  [CAPABILITY.financeBankAccountsList, FINANCE_CAPABILITY.bankAccountsList, FINANCE_ROUTE.bankAccountsList],
  [CAPABILITY.financeBankAccountsPropose, FINANCE_CAPABILITY.bankAccountsPropose, FINANCE_ROUTE.bankAccountsPropose],
] as const

/**
 * CONTROLS are capabilities the lane offers INSIDE a screen, never as a screen of their own: the
 * bank-account approval on F-04's list, and the Record of a definitive receipt on F-02's detail
 * (FIN-T05a). Each is deliberately absent from the shell's table, from `CAPABILITY_ROUTE` and from
 * `CAPABILITY_NAV_LABEL` — a control with a route would be a second entry point to an action that
 * belongs inside its list, and a control with a nav label would be a sidebar link to something that
 * is not a screen. They are listed here rather than excused one by one, so the next control fails
 * this test loudly instead of being quietly appended to a filter.
 */
const CONTROLS: readonly string[] = [
  FINANCE_CAPABILITY.bankAccountsApprove,
  FINANCE_CAPABILITY.receiptsRecord,
]

describe('shell ↔ Finance lane capability ids', () => {
  it('declares the lane’s five route ids with the lane’s values and routes', () => {
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
  })

  it('gives the two lists nav entries and the other three none', () => {
    const labels: Record<string, string> = {}
    for (const [shellId] of ROUTED) labels[shellId] = CAPABILITY_NAV_LABEL[shellId as CapabilityId]
    expect(labels).toEqual({
      'finance.receipts.list': 'Tahsilat Taslakları',
      'finance.receipts.draft': '',
      'finance.receipts.detail': '',
      'finance.bank-accounts.list': 'Banka Hesapları',
      'finance.bank-accounts.propose': '',
    })
  })
})

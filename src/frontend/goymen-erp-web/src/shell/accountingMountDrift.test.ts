/**
 * The shell mounts exactly the Accounting capability ids and routes that lane declares as ROUTES.
 *
 * Two files, two single writers: `src/accounting/capabilities.ts` belongs to the Accounting lane
 * (FİNANS), `src/shell/capabilities.ts` to the shared-integration owner. A rename on one side alone
 * would make the shell quietly stop mounting a screen the server has granted.
 *
 * The lane declares SEVEN ids but only FIVE routes: the approval (Ö4) and the close (Ö5) are controls
 * of the M-07 period list, read with `hasCapability` inside that screen. A control in a route table
 * would mount a screen that does not exist, so this test pins their absence as hard as it pins the
 * routes' presence.
 *
 * WHAT THIS IS NOT: backend parity or availability. The lane's own tests pin its ids to the backend
 * provider; whether an Accounting screen opens is the server's snapshot, and every declaration is
 * staged until the acc01 acceptance.
 */

import { describe, expect, it } from 'vitest'
import { matchPath } from 'react-router'
import { CAPABILITY, CAPABILITY_ROUTE, CAPABILITY_NAV_LABEL } from './capabilities'
import { ACCOUNTING_CAPABILITY, ACCOUNTING_ROUTE, journalDetailPath } from '../accounting/capabilities'

/** The lane's routed ids, paired with the route key that serves each. */
const ROUTED = [
  [CAPABILITY.accountingPeriodsList, ACCOUNTING_CAPABILITY.periodsList, ACCOUNTING_ROUTE.periodsList],
  [CAPABILITY.accountingPeriodsPropose, ACCOUNTING_CAPABILITY.periodsPropose, ACCOUNTING_ROUTE.periodsPropose],
  [CAPABILITY.accountingJournalsList, ACCOUNTING_CAPABILITY.journalsList, ACCOUNTING_ROUTE.journalsList],
  [CAPABILITY.accountingJournalsDetail, ACCOUNTING_CAPABILITY.journalsDetail, ACCOUNTING_ROUTE.journalsDetail],
  [CAPABILITY.accountingPostingQueues, ACCOUNTING_CAPABILITY.postingQueues, ACCOUNTING_ROUTE.postingQueues],
] as const

describe('shell ↔ Accounting lane capability ids', () => {
  it('declares the same id the lane does for every route', () => {
    for (const [shellId, laneId] of ROUTED) {
      expect(shellId).toBe(laneId)
    }
  })

  it('mounts the five routed ids and NEITHER control', () => {
    const shellIds = (Object.values(CAPABILITY) as string[]).filter((id) => id.startsWith('accounting.'))
    expect([...shellIds].sort()).toEqual([...ROUTED.map(([shellId]) => shellId as string)].sort())
    for (const control of [ACCOUNTING_CAPABILITY.periodsApprove, ACCOUNTING_CAPABILITY.periodsClose]) {
      expect(shellIds).not.toContain(control)
      expect(Object.values(ACCOUNTING_ROUTE) as string[]).not.toContain(control)
    }
  })

  it('agrees with the lane on every route path, value by value', () => {
    for (const [shellId, , path] of ROUTED) {
      expect(CAPABILITY_ROUTE[shellId]).toBe(path)
    }
  })

  it('matches the lane’s own link helper, and no static path is read as a journal or period id', () => {
    const journalId = '7b1c0f2e-0000-4a00-8000-000000000002'
    const match = matchPath(CAPABILITY_ROUTE[CAPABILITY.accountingJournalsDetail], journalDetailPath(journalId))
    expect(match?.params.journalId).toBe(journalId)
    // `donemler/yeni` is its own route, and the journal list must not swallow a detail path.
    expect(
      matchPath(CAPABILITY_ROUTE[CAPABILITY.accountingPeriodsList], ACCOUNTING_ROUTE.periodsPropose),
    ).toBeNull()
    expect(
      matchPath(CAPABILITY_ROUTE[CAPABILITY.accountingJournalsList], journalDetailPath(journalId)),
    ).toBeNull()
  })

  it('gives the three lists a nav label and the propose screen and detail none', () => {
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.accountingPeriodsList]).toBe('Muhasebe Dönemleri')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.accountingJournalsList]).toBe('Muhasebe Fişleri')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.accountingPostingQueues]).toBe('Muhasebe Kontrol')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.accountingPeriodsPropose]).toBe('')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.accountingJournalsDetail]).toBe('')
  })
})

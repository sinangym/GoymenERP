/**
 * The location card's back target.
 *
 * The cases that matter are the hostile ones: `location.state` is whatever the previous
 * history entry left behind, so this module must never turn it into a path.
 */

import { describe, expect, it } from 'vitest'
import {
  DEFAULT_BACK_TARGET,
  DETAIL_ORIGIN,
  backTarget,
  workCenterReturnState,
} from './detailReturn'
import {
  ORGANIZATION_CAPABILITY,
  ORGANIZATION_ROUTE,
  workCenterListPath,
} from './capabilities'

const NS = 'company-a::gen-1'

describe('resolving where the card goes back to', () => {
  it('returns to the work-center list when that is where the row was', () => {
    const target = backTarget({ ns: NS, from: DETAIL_ORIGIN.workCenters }, NS)
    expect(target.path).toBe(workCenterListPath())
    expect(target.label).toBe('İş Merkezleri')
  })

  it('returns to the location list for the ORG-01 origin', () => {
    const target = backTarget({ ns: NS, from: DETAIL_ORIGIN.locations, page: 3 }, NS)
    expect(target.path).toBe(ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.settingsList])
    expect(target.label).toBe('Organizasyon')
  })

  it('defaults to the location list when no origin was recorded', () => {
    // ORG-01's existing rows carry `{page, ns}` and no `from`. They must keep working
    // exactly as before rather than becoming an unknown origin.
    expect(backTarget({ ns: NS, page: 2 }, NS)).toEqual(DEFAULT_BACK_TARGET)
  })

  it('ignores an origin recorded under a different company context', () => {
    // A switch keeps the history entry, so company A's origin must not steer company B.
    expect(backTarget({ ns: NS, from: DETAIL_ORIGIN.workCenters }, 'company-b::gen-1')).toEqual(
      DEFAULT_BACK_TARGET,
    )
  })

  it('ignores every origin while the context has no namespace', () => {
    expect(backTarget({ ns: NS, from: DETAIL_ORIGIN.workCenters }, null)).toEqual(
      DEFAULT_BACK_TARGET,
    )
  })

  it('never navigates to a value taken from the state itself', () => {
    /**
     * The whole reason the origin is a token. Each of these is something a crafted
     * history entry could contain; none of them may reach the router.
     */
    const hostile: unknown[] = [
      { ns: NS, from: 'https://example.invalid/steal' },
      { ns: NS, from: '/cari' },
      { ns: NS, from: '//example.invalid' },
      { ns: NS, from: 'javascript:alert(1)' },
      { ns: NS, from: { path: '/cari' } },
      { ns: NS, from: ['work-centers'] },
      { ns: NS, from: null },
      'work-centers',
      42,
      null,
      undefined,
      [],
    ]
    for (const state of hostile) {
      expect(backTarget(state, NS)).toEqual(DEFAULT_BACK_TARGET)
    }
  })

  it('only ever yields one of the two known targets', () => {
    const known = [DEFAULT_BACK_TARGET.path, workCenterListPath()]
    for (const state of [
      { ns: NS, from: DETAIL_ORIGIN.locations },
      { ns: NS, from: DETAIL_ORIGIN.workCenters },
      { ns: NS },
      {},
    ]) {
      expect(known).toContain(backTarget(state, NS).path)
    }
  })
})

describe('the state ORG-03 attaches to a row', () => {
  it('names this context and this origin', () => {
    expect(workCenterReturnState(NS)).toEqual({ ns: NS, from: DETAIL_ORIGIN.workCenters })
  })

  it('records nothing at all without a namespace', () => {
    // An unnamespaced origin would survive a company switch, which is the one thing
    // the namespace check exists to stop.
    expect(workCenterReturnState(null)).toBeUndefined()
  })
})

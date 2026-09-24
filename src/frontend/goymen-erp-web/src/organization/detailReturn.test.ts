import { describe, expect, it } from 'vitest'
import {
  DEFAULT_DETAIL_BACK_TARGET,
  DETAIL_ORIGIN,
  detailBackTarget,
  workCenterDetailReturn,
} from './detailReturn'
import { ORGANIZATION_CAPABILITY, ORGANIZATION_ROUTE } from './capabilities'

const NAMESPACE = 'company-a::capability-generation-1'

describe('location detail return target', () => {
  it('returns to the work-center list only for its known current-context token', () => {
    expect(
      detailBackTarget({ ns: NAMESPACE, from: DETAIL_ORIGIN.workCenters }, NAMESPACE),
    ).toEqual({
      path: ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.workCentersList],
      label: 'İş Merkezleri',
    })
  })

  it('keeps the location-list origin and its existing page state compatible', () => {
    expect(
      detailBackTarget({ ns: NAMESPACE, from: DETAIL_ORIGIN.locations, page: 3 }, NAMESPACE),
    ).toEqual(DEFAULT_DETAIL_BACK_TARGET)
    expect(detailBackTarget({ ns: NAMESPACE, page: 3 }, NAMESPACE)).toEqual(
      DEFAULT_DETAIL_BACK_TARGET,
    )
  })

  it('falls back when the token belongs to another context', () => {
    expect(
      detailBackTarget(
        { ns: 'company-b::capability-generation-1', from: DETAIL_ORIGIN.workCenters },
        NAMESPACE,
      ),
    ).toEqual(DEFAULT_DETAIL_BACK_TARGET)
  })

  it('never treats router state as a navigation path', () => {
    const untrustedStates: readonly unknown[] = [
      { ns: NAMESPACE, from: '/cari' },
      { ns: NAMESPACE, from: 'https://example.invalid' },
      { ns: NAMESPACE, from: 'javascript:alert(1)' },
      { ns: NAMESPACE, from: { path: '/cari' } },
      { ns: NAMESPACE, from: [DETAIL_ORIGIN.workCenters] },
      'work-centers',
      [],
      null,
      undefined,
    ]

    for (const state of untrustedStates) {
      expect(detailBackTarget(state, NAMESPACE)).toEqual(DEFAULT_DETAIL_BACK_TARGET)
    }
  })

  it('falls back when there is no resolved context namespace', () => {
    expect(
      detailBackTarget({ ns: NAMESPACE, from: DETAIL_ORIGIN.workCenters }, null),
    ).toEqual(DEFAULT_DETAIL_BACK_TARGET)
  })
})

describe('work-center detail return state', () => {
  it('records the current namespace and the fixed work-center token', () => {
    expect(workCenterDetailReturn(NAMESPACE)).toEqual({
      ns: NAMESPACE,
      from: DETAIL_ORIGIN.workCenters,
    })
  })

  it('records no origin without a resolved context', () => {
    expect(workCenterDetailReturn(null)).toBeUndefined()
  })
})

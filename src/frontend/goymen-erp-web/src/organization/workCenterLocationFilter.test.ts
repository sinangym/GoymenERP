import { describe, expect, it } from 'vitest'
import { workCenterListPath } from './capabilities'
import {
  filterWorkCenters, filteredWorkCenterBackPath, filteredWorkCenterDetailReturn,
  isCanonicalLocationId, readWorkCenterLocationFilter,
} from './workCenterLocationFilter'
import type { WorkCenterSummary } from './contract'

const A = 'aaaaaaaa-1111-2222-3333-444444444444'
const B = 'bbbbbbbb-1111-2222-3333-444444444444'
const PATH = '/ayarlar/organizasyon/is-merkezleri'
const NS = 'SENTETIK-current-company-context'
const rows: readonly WorkCenterSummary[] = Object.freeze([
  Object.freeze({ id: A, locationId: A, code: 'SENTETIK-A', name: 'A', kind: 'Paint', isActive: false, canOpenLocationDetail: false }),
  Object.freeze({ id: B, locationId: B, code: 'SENTETIK-B', name: 'B', kind: 'Slitting', isActive: true, canOpenLocationDetail: true }),
])

describe('exact location filter over authorized Organization rows', () => {
  it('accepts no filter and a single canonical nonempty location', () => {
    expect(readWorkCenterLocationFilter('')).toEqual({ kind: 'all' })
    expect(readWorkCenterLocationFilter(`?locationId=${A}`)).toEqual({ kind: 'location', locationId: A })
    expect(workCenterListPath()).toBe(PATH)
    expect(workCenterListPath(A)).toBe(`${PATH}?locationId=${A}`)
  })

  it.each([
    '?locationId=', '?locationId=null', '?locationId=00000000-0000-0000-0000-000000000000',
    `?locationId=${A.toUpperCase()}`, `?locationId=%20${A}`, `?locationId=${A}%20`,
    `?locationId=${A}&locationId=${A}`, `?locationId=${A}&locationId=${B}`,
    `?locationId=${A}&returnTo=https://example.invalid`, `?LocationId=${A}`,
    '?locationId=../../cari', '?locationId=%ZZ', '?returnTo=/cari',
  ])('fails closed instead of broadening malformed query %s', (search) => {
    const filter = readWorkCenterLocationFilter(search)
    expect(filter).toEqual({ kind: 'invalid' })
    expect(filterWorkCenters(rows, filter)).toEqual([])
  })

  it('does not silently rewrite id spelling', () => {
    expect(isCanonicalLocationId(A)).toBe(true)
    expect(isCanonicalLocationId(A.toUpperCase())).toBe(false)
    expect(isCanonicalLocationId(null)).toBe(false)
  })

  it('keeps server row order, passive state and per-row location entitlement', () => {
    const selected = filterWorkCenters(rows, { kind: 'location', locationId: A })
    expect(selected).toEqual([rows[0]])
    expect(selected[0]).toBe(rows[0])
    expect(selected[0].isActive).toBe(false)
    expect(selected[0].canOpenLocationDetail).toBe(false)
    expect(filterWorkCenters(rows, { kind: 'all' })).toBe(rows)
    expect(rows).toHaveLength(2)
  })

  it('returns no rows when this authorized result has no exact match', () => {
    expect(filterWorkCenters([], { kind: 'location', locationId: A })).toEqual([])
    expect(filterWorkCenters([rows[1]], { kind: 'location', locationId: A })).toEqual([])
  })

  it('preserves the existing full-list origin, and binds a filtered origin without a path', () => {
    expect(filteredWorkCenterDetailReturn(null, A)).toBeUndefined()
    expect(filteredWorkCenterDetailReturn(NS, null)).toEqual({ ns: NS, from: 'work-centers' })
    const state = filteredWorkCenterDetailReturn(NS, A)
    expect(state).toEqual({ ns: NS, from: 'work-centers', locationId: A })
    expect(filteredWorkCenterBackPath(state, NS, A)).toBe(workCenterListPath(A))
  })

  it.each([
    null, [], 'url', { ns: NS, from: 'locations', locationId: A },
    { ns: 'old-context', from: 'work-centers', locationId: A },
    { ns: NS, from: 'work-centers', locationId: B },
    { ns: NS, from: 'work-centers', locationId: A, returnTo: '/cari' },
    { ns: NS, from: 'work-centers', locationId: '../../cari' },
  ])('rejects malformed, foreign or arbitrary return state %j', (state) => {
    expect(filteredWorkCenterBackPath(state, NS, A)).toBeNull()
  })

  it('cannot restore a return filter outside the current context and route', () => {
    const state = filteredWorkCenterDetailReturn(NS, A)
    expect(filteredWorkCenterBackPath(state, null, A)).toBeNull()
    expect(filteredWorkCenterBackPath(state, NS, undefined)).toBeNull()
  })
})

import { describe, expect, it } from 'vitest'
import {
  decodeLocation,
  decodeLocationPage,
  decodeSites,
  decodeVersionConflict,
  pageCount,
  versionToken,
} from './contract'

const SITE = '11111111-1111-1111-1111-111111111111'
const LOC = '33333333-3333-3333-3333-333333333333'

function location(overrides: Record<string, unknown> = {}) {
  return {
    id: LOC,
    siteId: SITE,
    code: 'OSM-H0',
    name: 'Göymen Osmaniye Depo Hol-0',
    isExternal: false,
    isActive: true,
    version: 1,
    ...overrides,
  }
}

describe('decodeSites', () => {
  it('accepts the merged SiteSummary shape', () => {
    const sites = decodeSites([
      { id: SITE, code: 'SITE-OSM', name: 'Osmaniye', type: 'Factory', isActive: true },
    ])
    expect(sites).toEqual([
      { id: SITE, code: 'SITE-OSM', name: 'Osmaniye', type: 'Factory', isActive: true },
    ])
  })

  it('passes through a site type it does not recognise', () => {
    // `type` is an open string in the schema. A new server-side SiteType is data,
    // not a malformed response — failing here would blank the whole screen.
    const sites = decodeSites([
      { id: SITE, code: 'S', name: 'N', type: 'SomethingNew', isActive: true },
    ])
    expect(sites?.[0].type).toBe('SomethingNew')
  })

  it('rejects the WHOLE list when one row is bad, rather than dropping it', () => {
    // A silently shortened facility list is indistinguishable from a correct one.
    expect(
      decodeSites([
        { id: SITE, code: 'S', name: 'N', type: 'Factory', isActive: true },
        { id: 'not-a-uuid', code: 'S2', name: 'N2', type: 'Factory', isActive: true },
      ]),
    ).toBeNull()
  })

  it('rejects a non-array', () => {
    expect(decodeSites({ items: [] })).toBeNull()
  })
})

describe('decodeLocation', () => {
  it('accepts the merged LocationSummary shape', () => {
    expect(decodeLocation(location())).toEqual(location())
  })

  it.each([
    ['missing version', { version: undefined }],
    ['version 0 — below the contract minimum of 1', { version: 0 }],
    ['negative version', { version: -3 }],
    ['fractional version', { version: 1.5 }],
    ['version as a string — this surface sends a number', { version: '2' }],
    ['bad id', { id: 'nope' }],
    ['bad siteId', { siteId: 'nope' }],
    ['empty code', { code: '   ' }],
    ['non-boolean isActive', { isActive: 'true' }],
  ])('fails closed on %s', (_label, overrides) => {
    expect(decodeLocation(location(overrides))).toBeNull()
  })

  it('fails closed on a version past the exactly-representable integer range', () => {
    /**
     * The wire type is int64 but JSON numbers are doubles, so a version past 2^53
     * has ALREADY lost precision by the time it reaches us. We cannot recover it,
     * and sending a rounded value back as `expectedVersion` would let a stale write
     * be accepted as a current one. Refusing is the only honest outcome.
     */
    expect(decodeLocation(location({ version: Number.MAX_SAFE_INTEGER + 2 }))).toBeNull()
  })

  it('accepts the largest version it can vouch for', () => {
    expect(decodeLocation(location({ version: Number.MAX_SAFE_INTEGER }))?.version).toBe(
      Number.MAX_SAFE_INTEGER,
    )
  })
})

describe('decodeLocationPage', () => {
  it('reads the SERVER’s echoed paging values', () => {
    // The server clamps pageSize into [1,100] and page to >= 1, so what it served
    // can differ from what we asked for. The pager must follow the server.
    const decoded = decodeLocationPage({
      items: [location()],
      page: 1,
      pageSize: 100,
      totalCount: 8,
    })
    expect(decoded).toMatchObject({ page: 1, pageSize: 100, totalCount: 8 })
    expect(decoded?.items).toHaveLength(1)
  })

  it('accepts an empty page past the end', () => {
    const decoded = decodeLocationPage({ items: [], page: 4, pageSize: 3, totalCount: 8 })
    expect(decoded).toMatchObject({ items: [], page: 4, totalCount: 8 })
  })

  it('rejects a page holding more rows than its own pageSize', () => {
    expect(
      decodeLocationPage({
        items: [location(), location({ id: '44444444-4444-4444-4444-444444444444' })],
        page: 1,
        pageSize: 1,
        totalCount: 2,
      }),
    ).toBeNull()
  })

  it.each([
    ['page 0', { page: 0 }],
    ['pageSize 0', { pageSize: 0 }],
    ['negative totalCount', { totalCount: -1 }],
    ['items not an array', { items: {} }],
  ])('fails closed on %s', (_label, overrides) => {
    expect(
      decodeLocationPage({ items: [], page: 1, pageSize: 25, totalCount: 0, ...overrides }),
    ).toBeNull()
  })

  it('fails the page when a single row is bad', () => {
    expect(
      decodeLocationPage({
        items: [location(), location({ version: 0 })],
        page: 1,
        pageSize: 25,
        totalCount: 2,
      }),
    ).toBeNull()
  })
})

describe('decodeVersionConflict', () => {
  it('reads the conflict extension off a 409 body', () => {
    expect(
      decodeVersionConflict({ title: 'Kayıt güncellendi.', conflict: { expectedVersion: 1, currentVersion: 2 } }),
    ).toEqual({ expectedVersion: 1, currentVersion: 2 })
  })

  it('returns null when the body carries no conflict — the idempotency 409', () => {
    // This is the discriminator between the two 409s. No conflict object means the
    // key was reused with a different payload, NOT that the row moved.
    expect(decodeVersionConflict({ title: 'Idempotency-Key çakışması.', status: 409 })).toBeNull()
  })

  it('returns null for an entirely absent body', () => {
    expect(decodeVersionConflict(null)).toBeNull()
  })

  it('returns null for a malformed conflict rather than half-reading it', () => {
    expect(decodeVersionConflict({ conflict: { expectedVersion: 1 } })).toBeNull()
    expect(decodeVersionConflict({ conflict: { expectedVersion: 0, currentVersion: 2 } })).toBeNull()
  })
})

describe('versionToken', () => {
  it('serializes to the decimal string the request schema allows', () => {
    // `expectedVersion` accepts integer OR a ^[1-9][0-9]*$ string.
    expect(versionToken(1)).toBe('1')
    expect(versionToken(42)).toBe('42')
    expect(versionToken(1)).toMatch(/^[1-9][0-9]*$/)
  })
})

describe('pageCount', () => {
  it('counts an empty result as one page, not zero', () => {
    expect(pageCount({ items: [], page: 1, pageSize: 25, totalCount: 0 })).toBe(1)
  })

  it('rounds a partial last page up', () => {
    expect(pageCount({ items: [], page: 1, pageSize: 3, totalCount: 8 })).toBe(3)
  })

  it('is exact on a full boundary', () => {
    expect(pageCount({ items: [], page: 1, pageSize: 4, totalCount: 8 })).toBe(2)
  })
})

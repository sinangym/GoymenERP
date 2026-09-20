import { describe, expect, it } from 'vitest'
import {
  decodeLocation,
  decodeLocationPage,
  decodeSites,
  decodeVersionConflict,
  decodeWorkCenter,
  decodeWorkCenters,
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

describe('decodeWorkCenters', () => {
  const ROW = {
    id: '88888888-8888-8888-8888-888888888881',
    locationId: '33333333-3333-3333-3333-333333333333',
    code: 'WC-PAINT',
    name: 'Boyama Hattı',
    kind: 'Paint',
    isActive: true,
    canOpenLocationDetail: true,
  }

  it('reads the seven declared fields and nothing else', () => {
    expect(decodeWorkCenter({ ...ROW, capacity: 120 })).toEqual(ROW)
  })

  it('accepts a kind token this build does not know', () => {
    // `kind` is an open string in the OpenAPI schema. A kind added server-side is data,
    // not corruption — the label layer shows the token verbatim.
    const decoded = decodeWorkCenter({ ...ROW, kind: 'Galvanizing' })
    expect(decoded?.kind).toBe('Galvanizing')
  })

  it('keeps a passive work center — isActive is a state, not a filter', () => {
    expect(decodeWorkCenter({ ...ROW, isActive: false })?.isActive).toBe(false)
  })

  it('rejects a row whose ids are not the UUIDs we would send back', () => {
    expect(decodeWorkCenter({ ...ROW, id: 'WC-PAINT' })).toBeNull()
    expect(decodeWorkCenter({ ...ROW, locationId: '' })).toBeNull()
    expect(decodeWorkCenter({ ...ROW, locationId: null })).toBeNull()
  })

  it('rejects a row missing a label the screen would otherwise render blank', () => {
    expect(decodeWorkCenter({ ...ROW, code: '   ' })).toBeNull()
    expect(decodeWorkCenter({ ...ROW, name: '' })).toBeNull()
    expect(decodeWorkCenter({ ...ROW, kind: '' })).toBeNull()
  })

  it('keeps a row the caller may not open, and never defaults the eligibility', () => {
    // A false flag is a fact about this caller, not corruption: the row stays listed.
    expect(decodeWorkCenter({ ...ROW, canOpenLocationDetail: false })?.canOpenLocationDetail).toBe(
      false,
    )
    // Absent or coerced values would have to be guessed one way or the other; neither
    // guess is the screen's to make, so the row is unreadable instead.
    const { canOpenLocationDetail: _omitted, ...withoutFlag } = ROW
    expect(decodeWorkCenter(withoutFlag)).toBeNull()
    expect(decodeWorkCenter({ ...ROW, canOpenLocationDetail: 'true' })).toBeNull()
    expect(decodeWorkCenter({ ...ROW, canOpenLocationDetail: 1 })).toBeNull()
    expect(decodeWorkCenter({ ...ROW, canOpenLocationDetail: null })).toBeNull()
  })

  it('rejects a non-boolean status rather than coercing it', () => {
    expect(decodeWorkCenter({ ...ROW, isActive: 'true' })).toBeNull()
    expect(decodeWorkCenter({ ...ROW, isActive: 1 })).toBeNull()
  })

  it('fails the WHOLE list on one bad row', () => {
    /**
     * The endpoint is UNPAGED, so the array is the caller's entire authorized set.
     * Dropping a row would produce a list that is silently short and indistinguishable
     * from a correct one — the same rule `decodeSites` follows.
     */
    expect(decodeWorkCenters([ROW, { ...ROW, kind: 42 }])).toBeNull()
    expect(decodeWorkCenters([ROW])).toEqual([ROW])
  })

  it('reads an empty list as a real empty list, not as a failure', () => {
    expect(decodeWorkCenters([])).toEqual([])
  })

  it('rejects a body that is not an array at all', () => {
    expect(decodeWorkCenters({ items: [ROW] })).toBeNull()
    expect(decodeWorkCenters(null)).toBeNull()
  })
})

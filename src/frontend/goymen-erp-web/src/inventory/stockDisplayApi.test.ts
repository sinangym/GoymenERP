import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  compareStockMeasures,
  decodeStockProductFamilyOptions,
  fetchStockDisplayPage,
  fetchStockProductFamilyOptions,
  isStockMeasure,
  NO_STOCK_FILTERS,
  STOCK_DISPLAY_HEADER,
  STOCK_PRODUCT_FAMILIES_PATH,
  stockMeasureFromInput,
  stockMeasureInputText,
  stockPositionQuery,
  type StockPositionFilters,
} from './stockDisplayApi'

const COMPANY = '10000000-0000-4000-8000-0000000000a1'
const OTHER_COMPANY = '10000000-0000-4000-8000-0000000000b2'
const LABEL = '22222222-2222-4222-8222-222222222222'
const LOT = '33333333-3333-4333-8333-333333333333'
const LOCATION = '44444444-4444-4444-8444-444444444444'
const PRODUCT = '55555555-5555-4555-8555-555555555555'
const COUNTERPARTY = '88888888-8888-4888-8888-888888888888'
const FAMILY = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const FAMILY_2 = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaab'

/** The #597 producer fixture row (`InventoryStockPositionHttpAcceptanceTests`), opt-in shape. */
function row(companyId = COMPANY) {
  return {
    companyId, labelId: LABEL, labelVersion: '3', labelCode: 'ROLL-1', productId: PRODUCT, productVersion: '7',
    classification: 'Standard', lot: { id: LOT, heatNumber: 'HEAT-1', coilNumber: 'COIL-1' }, heatNumber: 'HEAT-1',
    coilNumber: 'COIL-1', documentStatus: 'Complete', ownerKind: 'Company', ownerId: companyId,
    custodianKind: 'Company', custodianId: companyId, locationId: LOCATION, physicalKg: '10', freePhysicalKg: '8',
    blockedPhysicalKg: '2', activeAllocationKg: '1', availableKg: '7',
    productDisplay: {
      productId: PRODUCT, expectedProductVersion: '7',
      identity: { productId: PRODUCT, productVersion: '7', code: 'P-1', name: 'Galvaniz bant' },
      technical: null, errors: [],
    },
    // STOCK-LOCATION-2: the 2.0 row's Organization display of its own location (#607).
    locationDisplay: { locationId: LOCATION, locationCode: 'HOL-1', locationName: 'Hol 1 · Boy kesme' },
    // STOCK-OWNER-DISPLAY-3: the 3.0 row's owner display (#664) — null for a Company owner.
    ownerDisplay: null as unknown,
  }
}

/** The same row owned by a Cari, with the identity the 3.0 read binds to that owner. */
function counterpartyRow() {
  return {
    ...row(), ownerKind: 'Counterparty', ownerId: COUNTERPARTY,
    ownerDisplay: { partyId: COUNTERPARTY, code: 'CAR-000042', legalName: 'Çağlayan Öğütücü Sanayi A.Ş.', tradeName: 'Çağlayan' },
  }
}

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

const fetchMock = vi.fn()

beforeEach(() => {
  fetchMock.mockReset()
  vi.stubGlobal('fetch', fetchMock)
  vi.stubGlobal('navigator', { onLine: true })
})
afterEach(() => vi.unstubAllGlobals())

function requested(): { readonly url: URL; readonly headers: Headers; readonly init: RequestInit } {
  const [input, init] = fetchMock.mock.calls.at(-1) as [string, RequestInit]
  return { url: new URL(input, 'http://erp.test'), headers: new Headers(init.headers), init }
}

describe('stockPositionQuery — only the published keys, each once', () => {
  it('sends just the page size when nothing is filtered', () => {
    expect(stockPositionQuery(NO_STOCK_FILTERS, null)?.toString()).toBe('limit=25')
  })

  it('serializes every supported filter and the cursor with their exact names and values', () => {
    const filters: StockPositionFilters = {
      ...NO_STOCK_FILTERS,
      q: '  ROLL-1 ', productId: PRODUCT, locationId: LOCATION, classification: 'SecondQuality',
      owner: { kind: 'Counterparty', id: COUNTERPARTY },
    }
    const query = stockPositionQuery(filters, 'opaque-1')!
    expect([...query.keys()]).toEqual(['q', 'limit', 'cursor', 'productId', 'locationId', 'classification', 'ownerKind', 'ownerId'])
    expect(Object.fromEntries(query)).toEqual({
      q: 'ROLL-1', limit: '25', cursor: 'opaque-1', productId: PRODUCT, locationId: LOCATION,
      classification: 'SecondQuality', ownerKind: 'Counterparty', ownerId: COUNTERPARTY,
    })
  })

  it('never sends an owner kind without its id, nor a value the server would refuse', () => {
    expect(stockPositionQuery({ ...NO_STOCK_FILTERS, q: 'x'.repeat(65) }, null)).toBeNull()
    expect(stockPositionQuery({ ...NO_STOCK_FILTERS, q: ` ${'x'.repeat(64)} ` }, null)?.get('q')).toHaveLength(64)
    expect(stockPositionQuery({ ...NO_STOCK_FILTERS, productId: 'not-a-guid' }, null)).toBeNull()
    expect(stockPositionQuery({ ...NO_STOCK_FILTERS, locationId: '00000000-0000-0000-0000-000000000000' }, null)).toBeNull()
    expect(stockPositionQuery({ ...NO_STOCK_FILTERS, classification: 'standard' as never }, null)).toBeNull()
    expect(stockPositionQuery({ ...NO_STOCK_FILTERS, owner: { kind: 'Company', id: 'x' } }, null)).toBeNull()
    expect(stockPositionQuery({ ...NO_STOCK_FILTERS, owner: { kind: 'company' as never, id: COMPANY } }, null)).toBeNull()
  })
})

describe('fetchStockDisplayPage — the #597 opt-in read', () => {
  it('asks for the opt-in shape exactly once, with the company, no-store and the exact query', async () => {
    fetchMock.mockResolvedValue(json(200, { items: [row()], searchWindowFull: false, nextCursor: null }))
    const outcome = await fetchStockDisplayPage(COMPANY, { ...NO_STOCK_FILTERS, q: 'ROLL' }, null)

    expect(outcome.type).toBe('ok-single')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const { url, headers, init } = requested()
    expect(url.pathname).toBe('/api/v1/inventory/positions')
    expect(url.search).toBe('?q=ROLL&limit=25')
    expect(headers.get(STOCK_DISPLAY_HEADER)).toBe('3.0')
    expect(headers.get('X-Company-Id')).toBe(COMPANY)
    expect(init.cache).toBe('no-store')
  })

  it('passes the server’s cursor back untouched for the next page', async () => {
    fetchMock.mockResolvedValue(json(200, { items: [], searchWindowFull: false, nextCursor: null }))
    await fetchStockDisplayPage(COMPANY, NO_STOCK_FILTERS, 'eyJrIjoiUk9MTC0xIn0.c2ln')
    expect(requested().url.searchParams.get('cursor')).toBe('eyJrIjoiUk9MTC0xIn0.c2ln')
  })

  it('refuses a page carrying a row of another company', async () => {
    fetchMock.mockResolvedValue(json(200, { items: [row(OTHER_COMPANY)], searchWindowFull: false, nextCursor: null }))
    expect(await fetchStockDisplayPage(COMPANY, NO_STOCK_FILTERS, null)).toEqual({ type: 'malformed' })
  })

  it('refuses the previously published shape when the opt-in shape was asked for', async () => {
    const { productVersion: _v, classification: _c, productDisplay: _d, locationDisplay: _l, ownerDisplay: _o, ...legacy } = row()
    fetchMock.mockResolvedValue(json(200, { items: [legacy], searchWindowFull: false }))
    expect(await fetchStockDisplayPage(COMPANY, NO_STOCK_FILTERS, null)).toEqual({ type: 'malformed' })
  })

  it.each([400, 401, 403, 409, 503])('answers HTTP %i as itself, with one request and no legacy retry', async (status) => {
    const problem = { code: 'inventory_stock_position_forbidden' }
    fetchMock.mockResolvedValue(json(status, problem))
    expect(await fetchStockDisplayPage(COMPANY, NO_STOCK_FILTERS, null)).toEqual({ type: 'http', status, body: problem })
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(requested().headers.get(STOCK_DISPLAY_HEADER)).toBe('3.0')
  })

  it('does not send a request for a filter the server would refuse', async () => {
    expect(await fetchStockDisplayPage(COMPANY, { ...NO_STOCK_FILTERS, q: 'x'.repeat(65) }, null)).toEqual({ type: 'malformed' })
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('tells a fenced (aborted), offline and failed request apart', async () => {
    const aborted = new AbortController()
    aborted.abort()
    fetchMock.mockRejectedValue(new DOMException('aborted', 'AbortError'))
    expect(await fetchStockDisplayPage(COMPANY, NO_STOCK_FILTERS, null, aborted.signal)).toEqual({ type: 'fence-stale' })
    expect(await fetchStockDisplayPage(COMPANY, NO_STOCK_FILTERS, null)).toEqual({ type: 'network-error' })
    vi.stubGlobal('navigator', { onLine: false })
    expect(await fetchStockDisplayPage(COMPANY, NO_STOCK_FILTERS, null)).toEqual({ type: 'offline' })
  })
})

/** STOCK-LOCATION-2 members stay part of the read: the `3.0` row is the `2.0` row plus the owner display. */
describe('fetchStockDisplayPage — the location display inside the 3.0 selection', () => {
  it('returns the row with its location display, from one request carrying exactly one 3.0 selector', async () => {
    fetchMock.mockResolvedValue(json(200, { items: [row()], searchWindowFull: false, nextCursor: 'next-1' }))
    const outcome = await fetchStockDisplayPage(COMPANY, { ...NO_STOCK_FILTERS, locationId: LOCATION }, 'opaque-1')
    expect(outcome.type === 'ok-single' && outcome.data.items[0].locationDisplay)
      .toEqual({ locationId: LOCATION, locationCode: 'HOL-1', locationName: 'Hol 1 · Boy kesme' })
    expect(outcome.type === 'ok-single' && outcome.data.nextCursor).toBe('next-1')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const { url, headers } = requested()
    expect(headers.get(STOCK_DISPLAY_HEADER)).toBe('3.0')
    expect(url.searchParams.get('cursor')).toBe('opaque-1')
    expect(url.searchParams.get('locationId')).toBe(LOCATION)
  })

  it.each([
    ['the 1.0 (Product-only) shape', () => { const { locationDisplay: _d, ownerDisplay: _o, ...productOnly } = row(); return productOnly }],
    ['a null location display', () => ({ ...row(), locationDisplay: null })],
    ['a display of another location', () => ({ ...row(), locationDisplay: { locationId: PRODUCT, locationCode: 'X', locationName: 'Y' } })],
  ])('answers %s as malformed — no silent 1.0 or legacy fallback, no second request', async (_, body) => {
    fetchMock.mockResolvedValue(json(200, { items: [body()], searchWindowFull: false, nextCursor: null }))
    expect(await fetchStockDisplayPage(COMPANY, NO_STOCK_FILTERS, null)).toEqual({ type: 'malformed' })
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(requested().headers.get(STOCK_DISPLAY_HEADER)).toBe('3.0')
  })
})

/** STOCK-OWNER-DISPLAY-3: the screen selects the #664 `3.0` shape, strictly, and accepts nothing else for it. */
describe('fetchStockDisplayPage — the 3.0 (Product + Location + Owner) selection', () => {
  it('sends the selector exactly once, as 3.0, and never a second or fallback selector', async () => {
    fetchMock.mockResolvedValue(json(200, { items: [row()], searchWindowFull: false, nextCursor: null }))
    await fetchStockDisplayPage(COMPANY, NO_STOCK_FILTERS, null)
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    const sent = Object.entries(init.headers as Record<string, string>).filter(([name]) => name.toLowerCase() === STOCK_DISPLAY_HEADER.toLowerCase())
    expect(sent).toEqual([[STOCK_DISPLAY_HEADER, '3.0']])
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('reads a Company owner with a null display and a Cari owner with its exact identity, in one request', async () => {
    const second = { ...counterpartyRow(), labelId: '22222222-2222-4222-8222-222222222223', labelCode: 'ROLL-2' }
    fetchMock.mockResolvedValue(json(200, { items: [row(), second], searchWindowFull: false, nextCursor: null }))
    const outcome = await fetchStockDisplayPage(COMPANY, { ...NO_STOCK_FILTERS, owner: { kind: 'Counterparty', id: COUNTERPARTY } }, null)
    expect(outcome.type === 'ok-single' && outcome.data.items.map((item) => item.ownerDisplay)).toEqual([
      null,
      { partyId: COUNTERPARTY, code: 'CAR-000042', legalName: 'Çağlayan Öğütücü Sanayi A.Ş.', tradeName: 'Çağlayan' },
    ])
    // The identity arrives with the page: one read, and the owner filter is still the row's exact id.
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(requested().url.searchParams.get('ownerId')).toBe(COUNTERPARTY)
    expect(requested().url.pathname).toBe('/api/v1/inventory/positions')
  })

  it.each([
    ['the 2.0 shape (no ownerDisplay key)', () => { const { ownerDisplay: _o, ...location } = row(); return location }],
    ['a Company owner carrying a display', () => ({ ...row(), ownerDisplay: counterpartyRow().ownerDisplay })],
    ['a Cari owner with a null display', () => ({ ...counterpartyRow(), ownerDisplay: null })],
    ['a Cari display bound to another party', () => ({ ...counterpartyRow(), ownerDisplay: { ...counterpartyRow().ownerDisplay, partyId: PRODUCT } })],
    ['a Cari display with a blank code', () => ({ ...counterpartyRow(), ownerDisplay: { ...counterpartyRow().ownerDisplay, code: '  ' } })],
    ['a Cari display with an extra member', () => ({ ...counterpartyRow(), ownerDisplay: { ...counterpartyRow().ownerDisplay, taxNumber: '1' } })],
  ])('answers %s as malformed — no 2.0, 1.0 or legacy fallback, no second request', async (_, body) => {
    fetchMock.mockResolvedValue(json(200, { items: [body()], searchWindowFull: false, nextCursor: null }))
    expect(await fetchStockDisplayPage(COMPANY, NO_STOCK_FILTERS, null)).toEqual({ type: 'malformed' })
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(requested().headers.get(STOCK_DISPLAY_HEADER)).toBe('3.0')
  })
})

/** STOCK-TYPED-FILTER-UI-01: the typed filters of #690, by the server's own names and grammar. */
describe('stockPositionQuery — family, thickness and width (#690)', () => {
  it('sends the family and an exact and a range measure under the published names, after the earlier keys', () => {
    const query = stockPositionQuery({
      ...NO_STOCK_FILTERS, q: 'ROLL', productFamilyId: FAMILY, thicknessMm: '1.5', widthMinMm: '1000', widthMaxMm: '1250.5',
    }, 'opaque-1')!
    expect([...query.keys()]).toEqual(['q', 'limit', 'cursor', 'productFamilyId', 'thicknessMm', 'widthMinMm', 'widthMaxMm'])
    expect(Object.fromEntries(query)).toEqual({
      q: 'ROLL', limit: '25', cursor: 'opaque-1', productFamilyId: FAMILY, thicknessMm: '1.5', widthMinMm: '1000', widthMaxMm: '1250.5',
    })
  })

  it('sends a range with one open end, and min equal to max (both ends belong to the range)', () => {
    expect(stockPositionQuery({ ...NO_STOCK_FILTERS, thicknessMinMm: '2' }, null)?.toString()).toBe('limit=25&thicknessMinMm=2')
    expect(stockPositionQuery({ ...NO_STOCK_FILTERS, widthMaxMm: '1250' }, null)?.toString()).toBe('limit=25&widthMaxMm=1250')
    expect(stockPositionQuery({ ...NO_STOCK_FILTERS, thicknessMinMm: '3', thicknessMaxMm: '3.000' }, null)?.toString())
      .toBe('limit=25&thicknessMinMm=3&thicknessMaxMm=3.000')
  })

  it('keeps the digits exactly as given: nothing is rounded, padded or trimmed', () => {
    const query = stockPositionQuery({ ...NO_STOCK_FILTERS, thicknessMm: '1.250', widthMm: '0.125' }, null)!
    expect(query.get('thicknessMm')).toBe('1.250')
    expect(query.get('widthMm')).toBe('0.125')
  })

  it.each([
    ['an exact value together with a range', { thicknessMm: '1.5', thicknessMinMm: '1' }],
    ['an exact width together with a maximum', { widthMm: '1250', widthMaxMm: '1300' }],
    ['a minimum above the maximum', { widthMinMm: '1250.001', widthMaxMm: '1250' }],
    ['a decimal comma', { thicknessMm: '1,5' }],
    ['a fourth fraction digit', { thicknessMm: '1.2345' }],
    ['a leading zero', { widthMm: '01250' }],
    ['zero', { widthMm: '0' }],
    ['zero written with a fraction', { thicknessMm: '0.000' }],
    ['a sign', { thicknessMm: '-1' }],
    ['an exponent', { widthMm: '1e3' }],
    ['more than 32 characters', { widthMm: '1'.repeat(33) }],
    ['an empty value', { thicknessMaxMm: '' }],
    ['a family that is not an id', { productFamilyId: 'coil' }],
    ['the nil family id', { productFamilyId: '00000000-0000-0000-0000-000000000000' }],
  ])('refuses %s without a request, as the server would answer 400', async (_, patch) => {
    const filters = { ...NO_STOCK_FILTERS, ...patch } as StockPositionFilters
    expect(stockPositionQuery(filters, null)).toBeNull()
    expect(await fetchStockDisplayPage(COMPANY, filters, null)).toEqual({ type: 'malformed' })
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('reads the filtered page once, with the 3.0 selector and every typed condition on the wire', async () => {
    fetchMock.mockResolvedValue(json(200, { items: [row()], searchWindowFull: false, nextCursor: null }))
    await fetchStockDisplayPage(COMPANY, { ...NO_STOCK_FILTERS, productFamilyId: FAMILY, thicknessMinMm: '1', thicknessMaxMm: '3', widthMm: '1250' }, 'c-1')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const { url, headers } = requested()
    expect(url.pathname).toBe('/api/v1/inventory/positions')
    expect(url.search).toBe(`?limit=25&cursor=c-1&productFamilyId=${FAMILY}&thicknessMinMm=1&thicknessMaxMm=3&widthMm=1250`)
    expect(headers.get(STOCK_DISPLAY_HEADER)).toBe('3.0')
  })
})

describe('stock measures — decimal text, never floating point', () => {
  it.each([
    ['9', '10', -1], ['10', '9', 1], ['1.5', '1.50', 0], ['1.25', '1.3', -1], ['1250', '1250.001', -1],
    ['0.125', '0.12', 1], ['3', '3.000', 0], ['12345678901234567890.001', '12345678901234567890.002', -1],
  ])('compares %s with %s', (left, right, expected) => {
    expect(compareStockMeasures(left, right)).toBe(expected)
  })

  it.each([
    ['1,5', '1.5'], [' 1250 ', '1250'], ['0,125', '0.125'], ['1,250', '1.250'], ['3,10', '3.10'],
  ])('reads "%s" as the wire value %s — the typed digits, a comma for the fraction', (text, wire) => {
    expect(stockMeasureFromInput(text)).toEqual({ wire })
  })

  it.each([
    ['1.5', 'format'], ['1.250', 'format'], ['1 250', 'format'], ['abc', 'format'], ['-1', 'format'], ['0 3', 'format'], ['0.3', 'format'],
    ['01', 'leadingZero'], ['03', 'leadingZero'], ['00', 'leadingZero'], ['03,5', 'leadingZero'], ['000,125', 'leadingZero'],
    ['03,', 'format'], ['03,1234', 'precision'], [`0${'1'.repeat(40)}`, 'format'],
    ['1,', 'format'], [',5', 'format'], ['', 'format'], ['1,2345', 'precision'], ['0', 'zero'], ['0,000', 'zero'],
  ])('refuses "%s" (%s) rather than guessing or rounding it', (text, problem) => {
    expect(stockMeasureFromInput(text)).toEqual({ problem })
  })

  it('names the leading zero without accepting it, and keeps a fraction below one valid', () => {
    // The reason is new; the refusal is not: "03" is never read as 3.
    expect(stockMeasureFromInput('03')).not.toHaveProperty('wire')
    expect(stockMeasureFromInput('0,5')).toEqual({ wire: '0.5' })
    expect(stockMeasureFromInput('3')).toEqual({ wire: '3' })
  })

  it('shows a wire value in the field again with the same digits', () => {
    expect(stockMeasureInputText('1.250')).toBe('1,250')
    expect(stockMeasureInputText('1250')).toBe('1250')
    expect(stockMeasureInputText(null)).toBe('')
    expect(isStockMeasure('1.5')).toBe(true)
    expect(isStockMeasure(1.5)).toBe(false)
  })
})

describe('authorized product family options (#695)', () => {
  const options = [
    { familyId: FAMILY, code: 'gdc-flat-coil', displayName: 'Yassı rulo' },
    { familyId: FAMILY_2, code: 'gdc-sheet', displayName: 'Sac levha' },
  ]

  it('reads the list once from the published path, with no query and no stock-display selector', async () => {
    fetchMock.mockResolvedValue(json(200, { items: options }))
    expect(await fetchStockProductFamilyOptions(COMPANY)).toEqual({ type: 'ok-single', data: options })
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const { url, headers, init } = requested()
    expect(url.pathname).toBe('/api/v1/inventory/references/product-families')
    expect(STOCK_PRODUCT_FAMILIES_PATH).toBe('/api/v1/inventory/references/product-families')
    expect(url.search).toBe('')
    expect(headers.get('X-Company-Id')).toBe(COMPANY)
    expect(headers.get(STOCK_DISPLAY_HEADER)).toBeNull()
    expect(init.cache).toBe('no-store')
  })

  it('keeps the server order and the names as published', () => {
    const published = [options[1], { familyId: FAMILY.toUpperCase(), code: 'GDC İnce', displayName: 'ince SAC ürünleri' }]
    expect(decodeStockProductFamilyOptions({ items: published })).toEqual([
      options[1], { familyId: FAMILY, code: 'GDC İnce', displayName: 'ince SAC ürünleri' },
    ])
  })

  it.each([
    ['no body', null],
    ['an array instead of the envelope', options],
    ['no items member', { families: options }],
    ['an empty list (the endpoint answers 409 for it)', { items: [] }],
    ['a family without an id', { items: [{ code: 'x', displayName: 'X' }] }],
    ['the nil id', { items: [{ familyId: '00000000-0000-0000-0000-000000000000', code: 'x', displayName: 'X' }] }],
    ['a blank code', { items: [{ ...options[0], code: ' ' }] }],
    ['a blank name', { items: [{ ...options[0], displayName: '' }] }],
    ['a name that is not text', { items: [{ ...options[0], displayName: 7 }] }],
    ['the same id twice', { items: [options[0], { ...options[1], familyId: FAMILY }] }],
    ['the same code twice', { items: [options[0], { ...options[1], code: 'gdc-flat-coil' }] }],
    ['one unreadable family among readable ones', { items: [options[0], null] }],
  ])('refuses %s whole — a partial list is never offered as all families', async (_, body) => {
    expect(decodeStockProductFamilyOptions(body)).toBeNull()
    fetchMock.mockResolvedValue(json(200, body))
    expect(await fetchStockProductFamilyOptions(COMPANY)).toEqual({ type: 'malformed' })
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it.each([400, 401, 403, 409, 503])('answers a %i as itself, with its body, and does not retry', async (status) => {
    const problem = { status, code: 'inventory_product_family_option_read_conflict' }
    fetchMock.mockResolvedValue(json(status, problem))
    expect(await fetchStockProductFamilyOptions(COMPANY)).toEqual({ type: 'http', status, body: problem })
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('tells an abandoned read from a network failure and from being offline', async () => {
    const aborted = new AbortController()
    aborted.abort()
    fetchMock.mockRejectedValue(new Error('down'))
    expect(await fetchStockProductFamilyOptions(COMPANY, aborted.signal)).toEqual({ type: 'fence-stale' })
    expect(await fetchStockProductFamilyOptions(COMPANY)).toEqual({ type: 'network-error' })
    vi.stubGlobal('navigator', { onLine: false })
    expect(await fetchStockProductFamilyOptions(COMPANY)).toEqual({ type: 'offline' })
  })
})

import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchManufacturingOutputLocations, fetchManufacturingOutputProducts } from './api'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'

function answer(body: unknown, status = 200) {
  const fetchMock = vi.fn(async () => new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  }))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

afterEach(() => vi.unstubAllGlobals())

describe('Manufacturing output selectors', () => {
  it('uses the Manufacturing-scoped product route with the exact local business date and company', async () => {
    const fetchMock = answer({
      items: [{
        productId: 'a0000000-0000-4000-8000-000000000010',
        productVersion: '12',
        code: 'PRD-12',
        name: 'Galvaniz bant',
        stockUnitCode: 'kg',
      }],
      searchWindowFull: false,
    })

    const outcome = await fetchManufacturingOutputProducts(COMPANY, '2026-09-25', ' bant ')

    expect(outcome.type).toBe('ok-single')
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('/api/v1/manufacturing/references/output-products?effectiveOn=2026-09-25&limit=25&q=bant')
    expect(init.headers).toMatchObject({ 'X-Company-Id': COMPANY })
    expect(init.cache).toBe('no-store')
  })

  it('uses the Manufacturing-scoped location route and fails closed on an owner-shape leak', async () => {
    const fetchMock = answer({
      items: [{
        locationId: 'a0000000-0000-4000-8000-000000000020',
        locationCode: 'HOL-1',
        locationName: 'Üretim holü',
        siteId: 'a0000000-0000-4000-8000-000000000021',
        siteCode: 'OSM',
        siteName: 'Osmangazi',
        companyId: COMPANY,
      }],
      searchWindowFull: false,
    })

    expect(await fetchManufacturingOutputLocations(COMPANY)).toEqual({ type: 'malformed' })
    const [url] = fetchMock.mock.calls[0] as unknown as [string]
    expect(url).toBe('/api/v1/manufacturing/references/output-locations?limit=25')
  })
})

import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  fetchOpeningCount,
  fetchOpeningLocations,
  fetchOpeningProducts,
  recordOpeningCount,
} from './api'

const COUNT = '11111111-1111-4111-8111-111111111111'
const COMPANY = '22222222-2222-4222-8222-222222222222'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const LINE = '55555555-5555-4555-8555-555555555555'
const LABEL = '66666666-6666-4666-8666-666666666666'
const LOT = '77777777-7777-4777-8777-777777777777'
const PRODUCT = '88888888-8888-4888-8888-888888888888'
const KEY = '99999999-9999-4999-8999-999999999999'

const request = {
  countId: COUNT, locationId: LOCATION, countedOn: '2026-09-23', sheetReference: 'SAYIM-1',
  lines: [{
    lineId: LINE, labelId: LABEL, labelCode: 'LBL-1', lotId: LOT, heatNumber: null, coilNumber: 'COIL-1',
    productId: PRODUCT, productVersion: '7', quantityKg: '12.5',
  }],
}
const stored = {
  countId: COUNT, companyId: COMPANY, locationId: LOCATION, countedOn: '2026-09-23', sheetReference: 'SAYIM-1',
  lines: request.lines.map(({ productVersion: _productVersion, ...line }) => line),
}

afterEach(() => vi.unstubAllGlobals())

describe('Inventory opening API', () => {
  it('binds a first record response to the exact immutable request', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(stored), {
      status: 201, headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetch)
    const outcome = await recordOpeningCount(request, { companyId: COMPANY, idempotencyKey: KEY })
    expect(outcome).toMatchObject({ type: 'applied', replayed: false })
    expect(JSON.parse(fetch.mock.calls[0][1].body as string)).toEqual(request)
    expect(fetch.mock.calls[0][1].headers['Idempotency-Key']).toBe(KEY)
  })

  it('accepts a replay only with the canonical replay headers', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(stored), {
      status: 200,
      headers: {
        'Content-Type': 'application/json', 'Idempotency-Replayed': 'true',
        'Idempotency-Original-Correlation-Id': 'corr-1',
      },
    })))
    expect(await recordOpeningCount(request, { companyId: COMPANY, idempotencyKey: KEY }))
      .toMatchObject({ type: 'applied', replayed: true })
  })

  it('fails closed on a cross-company detail and malformed candidate bodies', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...stored, companyId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ items: [], searchWindowFull: 'false' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ items: [], searchWindowFull: 'false' }), { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    expect((await fetchOpeningCount(COMPANY, COUNT)).type).toBe('malformed')
    expect((await fetchOpeningProducts(COMPANY, '2026-09-23')).type).toBe('malformed')
    expect((await fetchOpeningLocations(COMPANY)).type).toBe('malformed')
  })
})

import { afterEach, describe, expect, it, vi } from 'vitest'
import { inventoryAllocationReleasePath, releaseAllocation } from './allocationChangeApi'
import { allocationReleaseRefusalText, decodeLabelAllocation, describesRelease } from './allocationChangeContract'

/**
 * AT-063 release on the EXISTING Inventory command (`POST /inventory/physical-allocations/{id}/release`): the client
 * sends the version the user saw and the reason under an idempotency key, and shows only an answer that is this
 * allocation, in this company, Released and holding no kg; anything else is UNKNOWN (resend with the same key).
 */
const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const ALLOCATION = '44444444-4444-4444-4444-444444444444'
const OTHER = '55555555-5555-5555-5555-555555555555'
const ORDER = '66666666-6666-6666-6666-666666666666'
const LINE = '77777777-7777-7777-7777-777777777777'
const PRODUCT = '88888888-8888-8888-8888-888888888888'
const KEY = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'

function released(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    allocationId: ALLOCATION, companyId: COMPANY, version: '3', status: 'Released', labelId: LABEL, productId: PRODUCT,
    demandKind: 'sales.order-line', demandId: ORDER, demandLineId: LINE, demandProductVersion: '7',
    allocatedKg: '25', remainingKg: '0', reason: 'SENTETIK', reservedUntilUtc: null,
    createdAtUtc: '2026-09-28T08:00:00+00:00', updatedAtUtc: '2026-09-29T09:00:00+00:00', ...overrides,
  }
}

function answer(status: number, body: unknown, headers: Record<string, string> = {}) {
  const fetchMock = vi.fn(async () => new Response(JSON.stringify(body), {
    status, headers: { 'Content-Type': 'application/json', ...headers },
  }))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const send = () => releaseAllocation(ALLOCATION, { expectedAllocationVersion: '2', reason: 'Sipariş iptal' },
  { companyId: COMPANY, idempotencyKey: KEY })

afterEach(() => vi.unstubAllGlobals())

describe('allocation release client (AT-063)', () => {
  it('posts the version and reason to the existing release route with the key and company', async () => {
    const fetchMock = answer(200, released())
    expect(await send()).toMatchObject({ type: 'applied', replayed: false, response: { allocationId: ALLOCATION, status: 'Released' } })
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe(inventoryAllocationReleasePath(ALLOCATION))
    expect(url).toMatch(new RegExp(`/inventory/physical-allocations/${ALLOCATION}/release$`))
    expect(init.method).toBe('POST')
    expect(init.headers).toMatchObject({ 'X-Company-Id': COMPANY, 'Idempotency-Key': KEY, 'Content-Type': 'application/json' })
    expect(JSON.parse(init.body as string)).toEqual({ expectedAllocationVersion: '2', reason: 'Sipariş iptal' })
  })

  it('shows a replay only with both replay headers', async () => {
    answer(200, released(), { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' })
    expect(await send()).toMatchObject({ type: 'applied', replayed: true })
    answer(200, released(), { 'Idempotency-Replayed': 'true' })
    expect(await send()).toEqual({ type: 'unknown', reason: 'undecodable' })
  })

  it('treats an answer that is not this allocation released in this company as unknown', async () => {
    for (const body of [released({ status: 'Active', remainingKg: '25' }), released({ allocationId: OTHER }),
      released({ companyId: OTHER }), { ...released(), extra: 1 }, null]) {
      answer(200, body)
      expect(await send()).toEqual({ type: 'unknown', reason: 'undecodable' })
    }
  })

  it('maps refusals by status and code, and 5xx or transport failure to unknown', async () => {
    answer(409, { code: 'inventory_allocation_version_conflict' })
    expect(await send()).toEqual({ type: 'failed', status: 409, code: 'inventory_allocation_version_conflict' })
    answer(422, { code: 'inventory_allocation_invalid_reference' })
    expect(await send()).toEqual({ type: 'failed', status: 422, code: 'inventory_allocation_invalid_reference' })
    answer(503, {})
    expect(await send()).toEqual({ type: 'unknown', reason: 'service-unavailable' })
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('offline') }))
    expect((await send()).type).toBe('unknown')
  })
})

describe('allocation release contract', () => {
  it('describes a release only for this allocation, company, Released and zero kg', () => {
    const view = decodeLabelAllocation(released())!
    expect(describesRelease(view, ALLOCATION.toUpperCase(), COMPANY)).toBe(true)
    expect(describesRelease(view, OTHER, COMPANY)).toBe(false)
    expect(describesRelease(view, ALLOCATION, OTHER)).toBe(false)
    expect(describesRelease(decodeLabelAllocation(released({ status: 'Consumed' }))!, ALLOCATION, COMPANY)).toBe(false)
  })

  it('names release refusals in Turkish by code and never shows server text', () => {
    expect(allocationReleaseRefusalText(409, 'inventory_allocation_state_conflict')).toMatch(/serbest bırakılamaz/)
    expect(allocationReleaseRefusalText(409, 'inventory_allocation_version_conflict')).toMatch(/siz bakarken değişti/)
    expect(allocationReleaseRefusalText(422, 'inventory_allocation_invalid_reference')).toMatch(/satış siparişi satırına ait değil/)
    expect(allocationReleaseRefusalText(403, null)).toBe('Bu şirkette tahsis serbest bırakma yetkiniz yok.')
    expect(allocationReleaseRefusalText(404, null)).toBe('Tahsis bu şirkette bulunamadı.')
    expect(allocationReleaseRefusalText(400, 'unheard_of')).toBe('Tahsis serbest bırakılmadı (unheard_of).')
  })
})

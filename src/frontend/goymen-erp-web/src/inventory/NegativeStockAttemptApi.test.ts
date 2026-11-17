/** Synthetic fetch sources only; no live HTTP or SQL acceptance is asserted. */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchNegativeStockAttempts, NEGATIVE_STOCK_ATTEMPT_INVALID_QUERY } from './NegativeStockAttemptApi'
import type { NegativeStockAttemptQuery } from './NegativeStockAttemptContract'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const PRODUCT = '22222222-2222-4222-8222-222222222222'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const query: NegativeStockAttemptQuery = { productId: null, locationId: null, openOnly: true }
const page = () => ({ companyId: COMPANY, items: [], windowFull: false })
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('negative stock attempts owner read transport', () => {
  it('sends an explicit company, bounded limit and all requested filters without a write key', async () => {
    const fetch = vi.fn(async () => json(page())); vi.stubGlobal('fetch', fetch)
    const signal = new AbortController().signal
    const result = await fetchNegativeStockAttempts(COMPANY, { productId: PRODUCT, locationId: LOCATION, openOnly: false }, signal)
    expect(result.type).toBe('ok-single')
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(url.split('?')[0]).toBe('/api/v1/inventory/negative-stock-attempts')
    expect(Object.fromEntries(new URL(url, 'https://synthetic.invalid').searchParams)).toEqual({ openOnly: 'false', limit: '50', productId: PRODUCT, locationId: LOCATION })
    expect(new Headers(init.headers).get('X-Company-Id')).toBe(COMPANY)
    expect(new Headers(init.headers).has('Idempotency-Key')).toBe(false)
    expect(init.method ?? 'GET').toBe('GET'); expect(init.cache).toBe('no-store'); expect(init.signal).toBe(signal)
  })
  it('omits optional filters rather than sending empty or invented ids', async () => {
    const fetch = vi.fn(async () => json(page())); vi.stubGlobal('fetch', fetch)
    await fetchNegativeStockAttempts(COMPANY, query)
    const [url] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(Object.fromEntries(new URL(url, 'https://synthetic.invalid').searchParams)).toEqual({ openOnly: 'true', limit: '50' })
  })
  it.each(['productId', 'locationId'])('refuses invalid local query %s before any request', async (field) => {
    const fetch = vi.fn(); vi.stubGlobal('fetch', fetch)
    expect(await fetchNegativeStockAttempts(COMPANY, { ...query, [field]: 'invalid' })).toMatchObject({ type: 'http', status: 400, body: { code: NEGATIVE_STOCK_ATTEMPT_INVALID_QUERY } })
    expect(fetch).not.toHaveBeenCalled()
  })
  it('never releases a foreign-company page or a malformed success as empty', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ ...page(), companyId: PRODUCT })))
    expect(await fetchNegativeStockAttempts(COMPANY, query)).toEqual({ type: 'malformed' })
    vi.stubGlobal('fetch', vi.fn(async () => new Response('SENTETIK invalid JSON', { status: 200 })))
    expect(await fetchNegativeStockAttempts(COMPANY, query)).toEqual({ type: 'malformed' })
  })
  it.each([400, 401, 403, 404, 409, 503])('keeps the owner HTTP %i result distinct', async (status) => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ code: 'inventory_negative_stock_attempts_unavailable' }, status)))
    expect(await fetchNegativeStockAttempts(COMPANY, query)).toMatchObject({ type: 'http', status })
  })
  it.each([201, 204])('does not accept undocumented success status %i', async (status) => {
    vi.stubGlobal('fetch', vi.fn(async () => status === 204 ? new Response(null, { status }) : json(page(), status)))
    expect(await fetchNegativeStockAttempts(COMPANY, query)).toMatchObject({ type: 'http', status })
  })
  it('distinguishes offline, online failure and an aborted request', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('SENTETIK network') }))
    vi.stubGlobal('navigator', { onLine: false })
    expect(await fetchNegativeStockAttempts(COMPANY, query)).toEqual({ type: 'offline' })
    vi.stubGlobal('navigator', { onLine: true })
    expect(await fetchNegativeStockAttempts(COMPANY, query)).toEqual({ type: 'network-error' })
    const controller = new AbortController(); controller.abort()
    expect(await fetchNegativeStockAttempts(COMPANY, query, controller.signal)).toEqual({ type: 'fence-stale' })
  })
  it('drops a response whose body finishes only after its context was aborted', async () => {
    const controller = new AbortController()
    vi.stubGlobal('fetch', vi.fn(async () => ({ status: 200, json: async () => { controller.abort(); return page() } } as unknown as Response)))
    expect(await fetchNegativeStockAttempts(COMPANY, query, controller.signal)).toEqual({ type: 'fence-stale' })
  })
})

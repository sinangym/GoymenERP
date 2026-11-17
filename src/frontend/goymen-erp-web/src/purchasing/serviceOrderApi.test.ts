import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchServiceOrder, fetchServiceOrders, submitServiceOrder } from './serviceOrderApi'
import { serviceFixture, serviceId as id } from './serviceOrderFixtures.testSupport'
const reply = (body: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(body),
  { status, headers: { 'Content-Type': 'application/json', ...headers } })
afterEach(() => vi.unstubAllGlobals())
describe('ALC-S7 transport source (synthetic, NOT_ENV)', () => {
  it('sends the version and supplied idempotency key without client company or stock assertions', async () => {
    const order = serviceFixture(), request = { expectedVersion: '1', reason: 'SENTETIK onay' }
    const fetch = vi.fn().mockResolvedValue(reply({ order, isReplay: false, originalCorrelationId: 'SENTETIK' }))
    vi.stubGlobal('fetch', fetch)
    expect((await submitServiceOrder(id(1), id(2), 'approve', request, 'SENTETIK-key')).type).toBe('applied')
    expect(fetch.mock.calls[0][0]).toContain('/purchasing/service-orders/' + id(2) + '/approval')
    expect(fetch.mock.calls[0][1].headers).toMatchObject({ 'X-Company-Id': id(1), 'Idempotency-Key': 'SENTETIK-key' })
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual(request)
  })
  it('refuses a replay body whose original-correlation header is missing or different', async () => {
    const body = { order: serviceFixture(), isReplay: true, originalCorrelationId: 'SENTETIK-original' }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply(body, 200, { 'Idempotency-Replayed': 'true' })))
    expect(await submitServiceOrder(id(1), id(2), 'approve', {}, 'same-key')).toEqual({ type: 'unknown', reason: 'malformed' })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply(body, 200,
      { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original' })))
    expect((await submitServiceOrder(id(1), id(2), 'approve', {}, 'same-key')).type).toBe('applied')
  })
  it.each([401, 403, 404, 409, 503])('preserves read HTTP %s and never uses a prior successful body', async status => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({}, status)))
    expect(await fetchServiceOrder(id(1), id(2), new AbortController().signal)).toEqual({ type: 'http', status })
  })
  it('rejects foreign-company list data and reads without caching', async () => {
    const fetch = vi.fn().mockResolvedValue(reply([serviceFixture()]))
    vi.stubGlobal('fetch', fetch)
    expect(await fetchServiceOrders(id(99), new AbortController().signal)).toEqual({ type: 'malformed' })
    expect(fetch.mock.calls[0][1].cache).toBe('no-store')
  })
  it('keeps unavailable and malformed writes unresolved without automatic retry', async () => {
    const fetch = vi.fn().mockResolvedValue(reply({ private: 'SENTETIK-owner' }, 503))
    vi.stubGlobal('fetch', fetch)
    expect((await submitServiceOrder(id(1), id(2), 'accept', {}, 'same-key')).type).toBe('unknown')
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})

import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchTransfers } from './transferApi'

const companyId = '11111111-1111-4111-8111-111111111111'
describe('transfer reads', () => {
  afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })

  it('uses company header and no-store, rejects another company response', async () => {
    const mock = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response(JSON.stringify({ companyId, items: [], windowFull: false })))
      .mockResolvedValueOnce(new Response(JSON.stringify({ companyId: '22222222-2222-4222-8222-222222222222', items: [], windowFull: false })))
    expect((await fetchTransfers(companyId)).type).toBe('ok-single')
    expect(mock.mock.calls[0]).toEqual(['/api/v1/inventory/transfers', expect.objectContaining({ cache: 'no-store', headers: expect.objectContaining({ 'X-Company-Id': companyId }) })])
    expect((await fetchTransfers(companyId)).type).toBe('malformed')
  })

  it.each([401, 403, 404, 409, 503])('preserves HTTP %s without success interpretation', async status => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('{}', { status }))
    expect(await fetchTransfers(companyId)).toEqual({ type: 'http', status, body: {} })
  })

  it('keeps malformed, offline and cancelled results distinct', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response('broken')).mockRejectedValue(new TypeError('Synthetic network failure'))
    expect(await fetchTransfers(companyId)).toEqual({ type: 'malformed' })
    vi.stubGlobal('navigator', { onLine: false })
    expect(await fetchTransfers(companyId)).toEqual({ type: 'offline' })
    const controller = new AbortController(); controller.abort()
    expect(await fetchTransfers(companyId, controller.signal)).toEqual({ type: 'fence-stale' })
  })
})

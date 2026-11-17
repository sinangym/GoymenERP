import { afterEach, describe, expect, it, vi } from 'vitest'
import { decideStockScrap } from './stockScrapApi'

const companyId = '82400000-0000-4000-8000-000000000001'
const scrapId = '82400000-0000-4000-8000-000000000002'
const send = () => decideStockScrap(scrapId, { decision: 'Approve', reason: 'SENTETIK approval' },
  { companyId, idempotencyKey: 'synthetic-frozen-key' })
const answer = (body: unknown, status: number) => new Response(JSON.stringify(body), { status,
  headers: { 'Content-Type': 'application/problem+json' } })

afterEach(() => vi.unstubAllGlobals())
describe('Stock scrap physical refusal HTTP contract, synthetic NOT_ENV', () => {
  it.each(['Sistemde 0 kg var, 2.5 kg istendi.', 'Sistemde -0.125 kg var, 2.5 kg istendi.'])
  ('preserves the authoritative canonical kg sentence: %s', async (detail) => {
    const fetcher = vi.fn().mockResolvedValue(answer({ status: 409, code: 'inventory_negative_physical_stock', detail }, 409))
    vi.stubGlobal('fetch', fetcher)
    expect(await send()).toEqual({ type: 'failed', status: 409, code: 'inventory_negative_physical_stock', detail })
    const [, init] = fetcher.mock.calls[0] as [string, RequestInit]
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe('synthetic-frozen-key')
    expect(JSON.parse(init.body as string)).toEqual({ decision: 'Approve', reason: 'SENTETIK approval' })
  })
  it.each(['Sistemde -0 kg var, 2 kg istendi.', 'Sistemde 1 kg var, 0 kg istendi.',
    'Sistemde 1.0001 kg var, 2 kg istendi.', 'Sistemde 01 kg var, 2 kg istendi.',
    'Sistemde 1.00 kg var, 2 kg istendi.', 'Sistemde 1 kg var, 2 kg istendi.\n',
    'Sistemde 1\u0000 kg var, 2 kg istendi.', 'Sistemde 1000000000000000 kg var, 2 kg istendi.',
    '<script>unsafe</script>', null])
  ('drops unsafe or noncanonical detail without changing the definite refusal: %s', async (detail) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(answer({ status: 409, code: 'inventory_negative_physical_stock', detail }, 409)))
    expect(await send()).toEqual({ type: 'failed', status: 409, code: 'inventory_negative_physical_stock' })
  })
  it.each([400, 401, 403, 404, 422])('does not show physical stock detail for HTTP %s', async (status) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(answer({ status, code: 'synthetic', detail: 'Sistemde 1 kg var, 2 kg istendi.' }, status)))
    expect(await send()).not.toHaveProperty('detail')
  })
  it('keeps a 503 unknown even when its body contains a plausible stock detail', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(answer({ status: 503, detail: 'Sistemde 1 kg var, 2 kg istendi.' }, 503)))
    expect(await send()).toEqual({ type: 'unknown', reason: 'service-unavailable' })
  })
  it('ignores detail when the problem status disagrees with HTTP', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(answer({ status: 503, detail: 'Sistemde 1 kg var, 2 kg istendi.' }, 409)))
    expect(await send()).not.toHaveProperty('detail')
  })
})

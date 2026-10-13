import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchOpenItem, fetchOpenItems } from './openItemReadHttp'

// Synthetic transport tests, NOT_ENV: no real API, grant or browser.
const COMPANY = '11111111-1111-4111-8111-111111111111'
const CURRENCY = '22222222-2222-4222-8222-222222222222'
const ITEM = 'aaaaaaaa-0000-4000-8000-000000000001'
const OTHER = 'aaaaaaaa-0000-4000-8000-000000000002'

const item = (patch: Record<string, unknown> = {}) => ({
  id: ITEM, companyId: COMPANY, sourceKind: 'official.invoice-installment.v1', sourceId: OTHER, sourceLineId: OTHER,
  sourceVersion: '4', partyId: OTHER, companyAccountId: OTHER, currencyId: CURRENCY, version: '1', dueOn: '2026-10-31',
  status: null, createdAtUtc: '2026-10-13T08:00:00+00:00', initialAmount: { disclosure: 'Redacted', money: null },
  outstandingAmount: { disclosure: 'Redacted', money: null }, ...patch,
})
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

interface Call { readonly url: string; readonly init: RequestInit }
function serve(answer: (call: Call) => Response | Promise<Response>) {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init: RequestInit = {}) => {
    const call = { url, init }
    calls.push(call)
    return answer(call)
  }))
  return calls
}

afterEach(() => vi.unstubAllGlobals())

describe('FI04 open item transport', () => {
  it('reads the first page with a GET, the company header, no query and no browser cache', async () => {
    const calls = serve(() => json({ companyId: COMPANY, items: [item()], nextCursor: null }))
    const outcome = await fetchOpenItems(COMPANY, null)
    expect(outcome.type).toBe('ok-single')
    expect(calls).toHaveLength(1)
    expect(calls[0].url).toBe('/api/v1/finance/open-items')
    expect(calls[0].init.method).toBe('GET')
    expect(calls[0].init.cache).toBe('no-store')
    expect(calls[0].init.body).toBeUndefined()
    expect((calls[0].init.headers as Record<string, string>)['X-Company-Id']).toBe(COMPANY)
  })

  it('sends the opaque cursor back unchanged as the only query parameter', async () => {
    const cursor = 'a+b/c=d&e f'
    const calls = serve(() => json({ companyId: COMPANY, items: [], nextCursor: null }))
    await fetchOpenItems(COMPANY, cursor)
    const url = new URL(calls[0].url, 'http://x')
    expect([...url.searchParams.keys()]).toEqual(['cursor'])
    expect(url.searchParams.get('cursor')).toBe(cursor)
  })

  it('reads one item by its own address with no query', async () => {
    const calls = serve(() => json(item()))
    const outcome = await fetchOpenItem(COMPANY, ITEM)
    expect(outcome).toMatchObject({ type: 'ok-single', data: { id: ITEM, status: null } })
    expect(calls[0].url).toBe(`/api/v1/finance/open-items/${ITEM}`)
    expect(calls[0].init.method).toBe('GET')
  })

  it.each([400, 401, 403, 404, 503])('reports HTTP %i as itself and reads no body for it', async (status) => {
    serve(() => json({ type: 'about:blank', status, items: [item()] }, status))
    expect(await fetchOpenItems(COMPANY, null)).toEqual({ type: 'http', status })
    expect(await fetchOpenItem(COMPANY, ITEM)).toEqual({ type: 'http', status })
  })

  it('refuses a 200 that is not the contract: broken JSON, another item, another company', async () => {
    serve(() => new Response('{"items":[', { status: 200 }))
    expect(await fetchOpenItems(COMPANY, null)).toEqual({ type: 'malformed' })
    serve(() => json(item({ id: OTHER })))
    expect(await fetchOpenItem(COMPANY, ITEM)).toEqual({ type: 'malformed' })
    serve(() => json({ companyId: OTHER, items: [], nextCursor: null }))
    expect(await fetchOpenItems(COMPANY, null)).toEqual({ type: 'malformed' })
  })

  it('tells a request that never answered from a bad answer, and its own abort from both', async () => {
    serve(() => { throw new TypeError('failed') })
    expect(await fetchOpenItems(COMPANY, null)).toEqual({ type: 'network-error' })
    const controller = new AbortController()
    serve(() => { controller.abort(); throw new DOMException('aborted', 'AbortError') })
    expect(await fetchOpenItems(COMPANY, null, controller.signal)).toEqual({ type: 'fence-stale' })
  })

  it('sends nothing for an identity or cursor the server would refuse', async () => {
    const calls = serve(() => json({}))
    expect(await fetchOpenItem(COMPANY, 'not-an-id')).toEqual({ type: 'malformed' })
    expect(await fetchOpenItems('not-a-company', null)).toEqual({ type: 'malformed' })
    expect(await fetchOpenItems(COMPANY, ' ')).toEqual({ type: 'malformed' })
    expect(await fetchOpenItems(COMPANY, 'c'.repeat(8193))).toEqual({ type: 'malformed' })
    expect(calls).toHaveLength(0)
  })
})

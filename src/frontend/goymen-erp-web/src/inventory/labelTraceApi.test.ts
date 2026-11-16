import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  fetchInventoryLabelTrace,
  LABEL_TRACE_INVALID_QUERY,
  labelTraceProblemCode,
  searchInventoryLabelTrace,
} from './labelTraceApi'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const LABEL = '22222222-2222-4222-8222-222222222222'

const trace = {
  labelId: LABEL, labelCode: 'SYN-LBL-1', direction: 'backward',
  labels: [{ labelId: LABEL, code: 'SYN-LBL-1', depth: 0 }], links: [], events: [], parties: [],
  productionStatus: 'Answered', partyStatus: 'Answered', partyNameStatus: 'Answered', windowFull: false,
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

describe('fetchInventoryLabelTrace', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('reads only the owner trace endpoint with the company header, no-store and the one direction key', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(json(trace))

    const result = await fetchInventoryLabelTrace(COMPANY, LABEL, 'backward')

    expect(result.type).toBe('ok-single')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const url = new URL(String(fetchMock.mock.calls[0][0]), 'http://localhost')
    expect(url.pathname).toBe(`/api/v1/inventory/labels/${LABEL}/trace`)
    expect([...url.searchParams.entries()]).toEqual([['direction', 'backward']])
    expect(fetchMock.mock.calls[0][1]).toMatchObject({ cache: 'no-store', headers: { 'X-Company-Id': COMPANY } })
  })

  it('treats a 200 for another direction or label as malformed', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(json({ ...trace, direction: 'forward' }))
    expect(await fetchInventoryLabelTrace(COMPANY, LABEL, 'backward')).toEqual({ type: 'malformed' })
  })

  it('passes a problem through with its body and reads the closed code', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(json({ code: LABEL_TRACE_INVALID_QUERY }, 400))
    const result = await fetchInventoryLabelTrace(COMPANY, LABEL, 'forward')
    expect(result).toEqual({ type: 'http', status: 400, body: { code: LABEL_TRACE_INVALID_QUERY } })
    expect(labelTraceProblemCode(result.type === 'http' ? result.body : null)).toBe(LABEL_TRACE_INVALID_QUERY)
    expect(labelTraceProblemCode([])).toBeNull()
    expect(labelTraceProblemCode({ code: '' })).toBeNull()
  })

  it('reports an aborted read as stale and a dropped one as a network error', async () => {
    const controller = new AbortController()
    controller.abort()
    vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new DOMException('aborted', 'AbortError'))
    expect(await fetchInventoryLabelTrace(COMPANY, LABEL, 'forward', controller.signal)).toEqual({ type: 'fence-stale' })

    vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new TypeError('failed'))
    expect((await fetchInventoryLabelTrace(COMPANY, LABEL, 'forward')).type).toMatch(/^(network-error|offline)$/)
  })
})

describe('searchInventoryLabelTrace', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('reads only the owner search endpoint with the one text key, encoded', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(json({
      items: [{ labelId: LABEL, code: 'SYN-LBL-1', supplierLabelNumber: 'SUP 1/2', matchedBy: 'SupplierLabelNumber' }],
      windowFull: false, supplierSearchStatus: 'Answered',
    }))

    const result = await searchInventoryLabelTrace(COMPANY, 'SUP 1/2')

    expect(result.type).toBe('ok-single')
    const url = new URL(String(fetchMock.mock.calls[0][0]), 'http://localhost')
    expect(url.pathname).toBe('/api/v1/inventory/label-trace/search')
    expect([...url.searchParams.entries()]).toEqual([['text', 'SUP 1/2']])
    expect(fetchMock.mock.calls[0][1]).toMatchObject({ cache: 'no-store', headers: { 'X-Company-Id': COMPANY } })
  })

  it('treats an unexpected 200 body as malformed', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(json({ items: [] }))
    expect(await searchInventoryLabelTrace(COMPANY, 'SY')).toEqual({ type: 'malformed' })
  })
})

import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  fetchInventoryMovementTimeline,
  MOVEMENT_TIMELINE_INVALID_QUERY,
  MOVEMENT_TIMELINE_PAGE_SIZE,
  movementTimelineProblemCode,
} from './movementTimelineApi'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const LABEL = '22222222-2222-4222-8222-222222222222'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const PRODUCT = '44444444-4444-4444-8444-444444444444'
const SOURCE = '55555555-5555-4555-8555-555555555555'
const MOVEMENT = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'

const movement = {
  movementId: MOVEMENT, kind: 'Receipt', occurredAtUtc: '2026-10-08T09:00:00+00:00',
  sourceKind: 'inventory.opening-count', sourceId: SOURCE, sourceLineId: null, sourceGrain: 'Whole',
  lines: [{
    sequence: 1, direction: 'In', labelId: LABEL, labelCode: 'SYN-LBL-1', ownerKind: 'Company', ownerId: COMPANY,
    custodianKind: 'Company', custodianId: COMPANY, locationId: LOCATION, productId: PRODUCT, block: 'Free',
    quantityKg: '12.5',
  }],
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

describe('fetchInventoryMovementTimeline', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('reads only the owner endpoint with the company header, no-store and the fixed page limit', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(json({ items: [movement], nextCursor: 'c.1' }))

    const result = await fetchInventoryMovementTimeline(COMPANY, null)

    expect(result).toEqual({ type: 'ok-single', data: { items: [movement], nextCursor: 'c.1' } })
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(String(fetchMock.mock.calls[0][0])).toBe(`/api/v1/inventory/movements?limit=${MOVEMENT_TIMELINE_PAGE_SIZE}`)
    expect(fetchMock.mock.calls[0][1]).toMatchObject({ cache: 'no-store', headers: { 'X-Company-Id': COMPANY } })
  })

  it('sends a continuation with the same limit and the opaque cursor, encoded', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(json({ items: [], nextCursor: null }))

    await fetchInventoryMovementTimeline(COMPANY, 'a+b/c=.sig')

    const url = new URL(String(fetchMock.mock.calls[0][0]), 'http://localhost')
    expect(url.pathname).toBe('/api/v1/inventory/movements')
    expect([...url.searchParams.keys()]).toEqual(['limit', 'cursor'])
    expect(url.searchParams.get('limit')).toBe(String(MOVEMENT_TIMELINE_PAGE_SIZE))
    expect(url.searchParams.get('cursor')).toBe('a+b/c=.sig')
  })

  it('keeps the page size inside the owner bound', () => {
    expect(MOVEMENT_TIMELINE_PAGE_SIZE).toBeGreaterThanOrEqual(1)
    expect(MOVEMENT_TIMELINE_PAGE_SIZE).toBeLessThanOrEqual(100)
  })

  it.each([400, 401, 403, 500, 503])('returns HTTP %s with its problem body and never falls back', async (status) => {
    const body = { status, code: status === 400 ? MOVEMENT_TIMELINE_INVALID_QUERY : undefined }
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(json(body, status))

    const result = await fetchInventoryMovementTimeline(COMPANY, null)

    expect(result).toEqual({ type: 'http', status, body: JSON.parse(JSON.stringify(body)) })
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('fails closed on a malformed 200 body', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(json({ items: [{ ...movement, kind: 'Reversal' }], nextCursor: null }))
    await expect(fetchInventoryMovementTimeline(COMPANY, null)).resolves.toEqual({ type: 'malformed' })
  })

  it('fails closed on a page longer than the limit it asked for', async () => {
    const items = Array.from({ length: MOVEMENT_TIMELINE_PAGE_SIZE + 1 }, (_, index) => ({
      ...movement,
      movementId: `aaaaaaaa-aaaa-4aaa-8aaa-${index.toString(16).padStart(12, '0')}`,
    }))
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(json({ items, nextCursor: null }))
    await expect(fetchInventoryMovementTimeline(COMPANY, null)).resolves.toEqual({ type: 'malformed' })
  })

  it('reports an aborted read as fence-stale', async () => {
    const controller = new AbortController()
    vi.spyOn(globalThis, 'fetch').mockImplementationOnce(async () => {
      controller.abort()
      throw new DOMException('aborted', 'AbortError')
    })
    await expect(fetchInventoryMovementTimeline(COMPANY, null, controller.signal)).resolves.toEqual({ type: 'fence-stale' })
  })

  it('reads the owner problem code only from an object body', () => {
    expect(movementTimelineProblemCode({ code: MOVEMENT_TIMELINE_INVALID_QUERY })).toBe(MOVEMENT_TIMELINE_INVALID_QUERY)
    expect(movementTimelineProblemCode({ code: '' })).toBeNull()
    expect(movementTimelineProblemCode(['x'])).toBeNull()
    expect(movementTimelineProblemCode(null)).toBeNull()
  })
})

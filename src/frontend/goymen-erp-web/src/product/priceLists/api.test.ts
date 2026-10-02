import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchPriceList, sendPriceListCommand } from './api'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const LIST = '22222222-2222-4222-8222-222222222222'
const OTHER = '33333333-3333-4333-8333-333333333333'

function view(id = LIST) {
  return {
    id, name: 'Liste', currencyCode: 'TRY', channel: 'Sales', vatBasis: 'ExcludingVat', version: '1',
    createdAtUtc: '2026-10-02T00:00:00Z', updatedAtUtc: '2026-10-02T00:00:00Z', activeVersionNo: null,
    draftVersionNo: 1, versions: [],
  }
}

function json(body: unknown, status: number, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

afterEach(() => vi.unstubAllGlobals())

describe('price-list response identity', () => {
  it('treats a detail response for another list as malformed', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(view(OTHER), 200)))
    expect(await fetchPriceList(LIST, COMPANY)).toEqual({ type: 'malformed' })
  })

  it('keeps a wrong-id command success unknown under the same idempotency key', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ priceList: view(OTHER), isReplay: false, originalCorrelationId: 'c1' }, 200)))
    const outcome = await sendPriceListCommand({
      kind: 'publish', priceListId: LIST, payload: { expectedVersion: '1', versionNo: 1 },
    }, COMPANY, 'same-key')
    expect(outcome).toEqual({ type: 'unknown', reason: 'malformed-response' })
  })

  it('requires replay body and both replay headers to agree', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ priceList: view(), isReplay: true, originalCorrelationId: 'original' }, 200,
      { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'different' })))
    const outcome = await sendPriceListCommand({
      kind: 'create', payload: { priceListId: LIST, versionId: OTHER, name: 'Liste', currencyCode: 'TRY', channel: 'Sales',
        vatBasis: 'ExcludingVat', validFrom: '2026-10-02', validThrough: null, lines: [] },
    }, COMPANY, 'same-key')
    expect(outcome).toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
})

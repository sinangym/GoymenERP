import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchPriceList, fetchPriceLists, sendPriceListCommand } from './api'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const LIST = '22222222-2222-4222-8222-222222222222'
const OTHER = '33333333-3333-4333-8333-333333333333'

function version(overrides: Record<string, unknown> = {}) {
  return {
    versionId: '44444444-4444-4444-8444-444444444444', versionNo: 1, state: 'Draft', status: 'Draft',
    validFrom: '2026-10-02', validThrough: null, createdAtUtc: '2026-10-02T00:00:00Z', createdBy: 'owner',
    publishedAtUtc: null, publishedBy: null, lines: [], ...overrides,
  }
}

function view(id = LIST) {
  return {
    id, name: 'Liste', currencyCode: 'TRY', channel: 'Sales', vatBasis: 'ExcludingVat', version: '1',
    createdAtUtc: '2026-10-02T00:00:00Z', updatedAtUtc: '2026-10-02T00:00:00Z', activeVersionNo: null,
    draftVersionNo: 1, versions: [version()],
  }
}

function summary(overrides: Record<string, unknown> = {}) {
  return {
    id: LIST, name: 'Liste', currencyCode: 'TRY', channel: 'Sales', vatBasis: 'ExcludingVat', version: '1',
    activeVersionNo: 1, activeValidFrom: '2026-10-02', draftVersionNo: null, versionCount: 1,
    updatedAtUtc: '2026-10-02T00:00:00Z', ...overrides,
  }
}

function json(body: unknown, status: number, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

afterEach(() => vi.unstubAllGlobals())

describe('price-list response identity', () => {
  it('accepts the canonical create success before each negative mutates one target field', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({
      priceList: { ...view(), versions: [version({ versionId: OTHER })] },
      isReplay: false,
      originalCorrelationId: 'c1',
    }, 201)))
    const outcome = await sendPriceListCommand({
      kind: 'create', payload: { priceListId: LIST, versionId: OTHER, name: 'Liste', currencyCode: 'TRY', channel: 'Sales',
        vatBasis: 'ExcludingVat', validFrom: '2026-10-02', validThrough: null, lines: [] },
    }, COMPANY, 'same-key')
    expect(outcome.type).toBe('succeeded')
  })

  it('accepts one canonical list page with its opaque continuation', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ items: [summary()], nextCursor: 'opaque/next==' }, 200)))
    const outcome = await fetchPriceLists(COMPANY, null)
    expect(outcome).toMatchObject({ type: 'ok-single', data: { nextCursor: 'opaque/next==' } })
  })

  it.each([
    ['an empty continuation', { items: [summary()], nextCursor: '' }],
    ['active facts without their date', { items: [summary({ activeValidFrom: null })], nextCursor: null }],
    ['an active date without its version', { items: [summary({ activeVersionNo: null })], nextCursor: null }],
    ['a duplicate page identity', { items: [summary(), summary({ name: 'Duplicate identity' })], nextCursor: null }],
  ])('refuses an incoherent list page: %s', async (_label, body) => {
    vi.stubGlobal('fetch', vi.fn(async () => json(body, 200)))
    expect(await fetchPriceLists(COMPANY, null)).toEqual({ type: 'malformed' })
  })

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

  it.each([
    ['a draft with publication facts', { versions: [version({ publishedAtUtc: '2026-10-02T01:00:00Z', publishedBy: 'publisher' })] }],
    ['a published version with Draft status', {
      activeVersionNo: 1, draftVersionNo: null,
      versions: [version({ state: 'Published', status: 'Draft', publishedAtUtc: '2026-10-02T01:00:00Z', publishedBy: 'publisher' })],
    }],
    ['a published version without publication facts', {
      activeVersionNo: 1, draftVersionNo: null, versions: [version({ state: 'Published', status: 'Active' })],
    }],
    ['an active pointer to a planned version', {
      activeVersionNo: 1, draftVersionNo: null,
      versions: [version({ state: 'Published', status: 'Planned', publishedAtUtc: '2026-10-02T01:00:00Z', publishedBy: 'publisher' })],
    }],
    ['a draft pointer to a published version', {
      activeVersionNo: 1, draftVersionNo: 1,
      versions: [version({ state: 'Published', status: 'Active', publishedAtUtc: '2026-10-02T01:00:00Z', publishedBy: 'publisher' })],
    }],
  ])('keeps an impossible successful contract unknown: %s', async (_label, overrides) => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ priceList: { ...view(), ...overrides }, isReplay: false, originalCorrelationId: 'c1' }, 200)))
    const outcome = await sendPriceListCommand({
      kind: 'publish', priceListId: LIST, payload: { expectedVersion: '1', versionNo: 1 },
    }, COMPANY, 'same-key')
    expect(outcome).toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
})

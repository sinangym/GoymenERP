import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchPaintCandidates, fetchPaintCatalog, sendPaintCommand, validatePaintFile } from './api'
import { PAINT_SOURCE, type PaintCommand } from './contract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const PRODUCT = '22222222-2222-2222-2222-222222222222'
const OTHER = '33333333-3333-3333-3333-333333333333'
const SHA = 'a'.repeat(64)
const command: PaintCommand = { kind: 'set', productId: PRODUCT, payload: { expectedVersion: '1', expectedProductVersion: '7', expectedMembershipVersion: '1', isMember: false, reason: 'SENTETIK' } }
const mutation = (isReplay = false) => ({ companyId: COMPANY, version: '2', changedEntries: [], removedProductIds: [PRODUCT], isReplay, originalCorrelationId: 'synthetic-original' })
function json(body: unknown, status = 200, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })
describe('paint membership company-bound transport', () => {
  it('reads bounded candidate identity under the committed company without a global Product request or mutation key', async () => {
    const fetch = vi.fn(async () => json({ companyId: COMPANY, outcome: 1, version: '2', q: '00012 & boya', limit: 25,
      items: [], searchWindowFull: false, sourceVersion: PAINT_SOURCE }))
    vi.stubGlobal('fetch', fetch)
    const controller = new AbortController()
    expect((await fetchPaintCandidates(COMPANY, '00012 & boya', controller.signal)).type).toBe('ok-single')
    expect(fetch.mock.calls[0]).toMatchObject(['/api/v1/product-paint-memberships/candidates?limit=25&q=00012+%26+boya',
      { method: 'GET', signal: controller.signal, headers: { 'X-Company-Id': COMPANY } }])
    expect(new Headers((fetch.mock.calls[0] as unknown as [string, RequestInit])[1].headers).has('Idempotency-Key')).toBe(false)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
  it.each([{ companyId: OTHER }, { q: 'other' }, { limit: 50 }, { sourceVersion: 'other-source' }])('refuses a mismatched candidate response %j', async (change) => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: COMPANY, outcome: 1, version: '2', q: null, limit: 25,
      items: [], searchWindowFull: false, sourceVersion: PAINT_SOURCE, ...change })))
    expect(await fetchPaintCandidates(COMPANY, null)).toEqual({ type: 'malformed' })
  })
  it('propagates aborted candidate reads as stale rather than a network failure', async () => {
    const controller = new AbortController()
    vi.stubGlobal('fetch', vi.fn(async () => { controller.abort(); throw new DOMException('Synthetic abort', 'AbortError') }))
    expect(await fetchPaintCandidates(COMPANY, null, controller.signal)).toEqual({ type: 'fence-stale' })
  })
  it('reads only the current endpoint with the committed company header', async () => {
    const fetch = vi.fn(async () => json({ companyId: COMPANY, outcome: 2, version: '0', entries: [], sourceVersion: PAINT_SOURCE }))
    vi.stubGlobal('fetch', fetch)
    expect((await fetchPaintCatalog(COMPANY)).type).toBe('ok-single')
    expect(fetch.mock.calls[0]).toMatchObject(['/api/v1/product-paint-memberships/current', { method: 'GET', headers: { 'X-Company-Id': COMPANY } }])
  })
  it('refuses a foreign company catalog', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ companyId: OTHER, outcome: 2, version: '0', entries: [], sourceVersion: PAINT_SOURCE })))
    expect(await fetchPaintCatalog(COMPANY)).toEqual({ type: 'malformed' })
  })
  it('sends exact validation bytes and mapped column without a mutation key', async () => {
    const request = { fileName: 'SENTETIK.xlsx', contentBase64: 'AAE=', productCodeColumn: 'Kod' }
    const fetch = vi.fn(async () => json({ companyId: COMPANY, version: '0', fileSha256: SHA, productCodeColumn: 'Kod', previewToken: 't', sourceRowCount: 0, rows: [], errors: [], ready: false }))
    vi.stubGlobal('fetch', fetch)
    expect((await validatePaintFile(COMPANY, request, SHA)).type).toBe('ok-single')
    expect(fetch.mock.calls[0]).toMatchObject(['/api/v1/product-paint-memberships/imports/validate', { method: 'POST', body: JSON.stringify(request) }])
    expect(new Headers((fetch.mock.calls[0] as unknown as [string, RequestInit])[1].headers).has('Idempotency-Key')).toBe(false)
  })
  it('sends a PUT with only the caller key and frozen request payload', async () => {
    const fetch = vi.fn(async () => json(mutation()))
    vi.stubGlobal('fetch', fetch)
    expect((await sendPaintCommand(COMPANY, command, 'one-key')).type).toBe('applied')
    expect(fetch.mock.calls[0]).toMatchObject([`/api/v1/product-paint-memberships/products/${PRODUCT}`, { method: 'PUT', headers: { 'Idempotency-Key': 'one-key', 'X-Company-Id': COMPANY }, body: JSON.stringify(command.payload) }])
  })
  it('requires matching replay headers for a replay success', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(mutation(true), 200, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original' })))
    expect((await sendPaintCommand(COMPANY, command, 'one-key')).type).toBe('applied')
  })
  it('posts only the explicit import payload and never sends local response-proof metadata or absent products', async () => {
    const imported: PaintCommand = { kind: 'import', expectedProductIds: [PRODUCT], payload: { expectedVersion: '1', fileName: 'SENTETIK.xlsx', contentBase64: 'AAH=',
      productCodeColumn: 'Ürün kodu', fileSha256: SHA, previewToken: 'source-bound-token', sourceLabel: 'SENTETIK', reason: 'SENTETIK' } }
    const fetch = vi.fn(async () => json({ ...mutation(), removedProductIds: [], changedEntries: [{ productId: PRODUCT, productVersion: '7',
      membershipVersion: '2', productCode: '00012', productName: 'SENTETIK boya', sourceVersion: PAINT_SOURCE }] }))
    vi.stubGlobal('fetch', fetch)
    expect((await sendPaintCommand(COMPANY, imported, 'import-key')).type).toBe('applied')
    expect(fetch.mock.calls[0]).toMatchObject(['/api/v1/product-paint-memberships/imports', { method: 'POST',
      headers: { 'X-Company-Id': COMPANY, 'Idempotency-Key': 'import-key' }, body: JSON.stringify(imported.payload) }])
    expect(JSON.stringify(imported.payload)).not.toContain('expectedProductIds')
    expect(JSON.stringify(imported.payload)).not.toContain(OTHER)
  })
  it.each<readonly [unknown, Record<string, string>]>([
    [mutation(true), {}],
    [mutation(true), { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'different' }],
    [mutation(false), { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original' }],
    [{ ...mutation(), companyId: OTHER }, {}],
    [{ ...mutation(), removedProductIds: [OTHER] }, {}],
    [{ ...mutation(), extra: 1 }, {}],
  ])('keeps an incoherent successful response unknown %j', async (body, headers) => {
    vi.stubGlobal('fetch', vi.fn(async () => json(body, 200, headers)))
    expect(await sendPaintCommand(COMPANY, command, 'one-key')).toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
  it.each([201, 204, 302, 500, 503])('keeps unexpected command status %i unknown', async (status) => {
    vi.stubGlobal('fetch', vi.fn(async () => status === 204 ? new Response(null, { status }) : json({}, status)))
    expect((await sendPaintCommand(COMPANY, command, 'one-key')).type).toBe('unknown')
  })
  it.each([401, 403, 404, 409, 422])('keeps definite HTTP %i separate', async (status) => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ code: 'product_paint_membership_version_conflict' }, status)))
    expect(await sendPaintCommand(COMPANY, command, 'one-key')).toMatchObject({ type: 'refused', status, terminalReplay: false })
  })
  it('recognizes a stored refusal replay while a permission failure cannot settle an earlier unknown', async () => {
    const headers = { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original' }
    vi.stubGlobal('fetch', vi.fn(async () => json({ code: 'product_paint_membership_version_conflict' }, 409, headers)))
    expect(await sendPaintCommand(COMPANY, command, 'one-key')).toMatchObject({ type: 'refused', terminalReplay: true })
    vi.stubGlobal('fetch', vi.fn(async () => json({}, 403, headers)))
    expect(await sendPaintCommand(COMPANY, command, 'one-key')).toMatchObject({ type: 'refused', terminalReplay: false })
  })
  it('distinguishes offline from a failed online connection', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('SENTETIK network') }))
    vi.stubGlobal('navigator', { onLine: false })
    expect(await fetchPaintCatalog(COMPANY)).toEqual({ type: 'offline' })
    expect(await sendPaintCommand(COMPANY, command, 'one-key')).toEqual({ type: 'unknown', reason: 'offline' })
    vi.stubGlobal('navigator', { onLine: true })
    expect(await fetchPaintCatalog(COMPANY)).toEqual({ type: 'network-error' })
  })
})

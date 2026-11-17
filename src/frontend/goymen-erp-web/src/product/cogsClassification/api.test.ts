import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchCogs, setCogs } from './api'
import { COGS_SOURCE, type SetCogsRequest } from './contract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const PRODUCT = '22222222-2222-2222-2222-222222222222'
const OTHER = '33333333-3333-3333-3333-333333333333'
const request: SetCogsRequest = { expectedProductVersion: '7', expectedClassificationVersion: '0', classification: 2, reason: 'SENTETIK' }
const view = () => ({ companyId: COMPANY, productId: PRODUCT, productVersion: '7', classificationVersion: '1', classification: 2,
  changedInCompanyId: COMPANY, updatedAtUtc: '2026-11-17T10:00:00Z', sourceVersion: COGS_SOURCE })
const mutation = (isReplay = false) => ({ classification: view(), isReplay, originalCorrelationId: 'synthetic-original' })
const json = (body: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(body), {
  status, headers: { 'Content-Type': 'application/json', ...headers },
})
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('COGS company and product bound transport', () => {
  it('gets only the real owner endpoint under the explicit company', async () => {
    const fetch = vi.fn(async () => json(view()))
    vi.stubGlobal('fetch', fetch)
    const signal = new AbortController().signal
    expect(await fetchCogs(COMPANY, PRODUCT, signal)).toEqual({ type: 'ok-single', data: view() })
    expect(fetch.mock.calls[0]).toMatchObject([`/api/v1/product-cogs-classifications/${PRODUCT}`, { headers: { 'X-Company-Id': COMPANY }, signal }])
  })
  it.each(['companyId', 'productId'])('rejects a foreign read %s', async (field) => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ ...view(), [field]: OTHER })))
    expect(await fetchCogs(COMPANY, PRODUCT)).toEqual({ type: 'malformed' })
  })
  it('uses exactly the frozen caller key, numeric kind and two lossless revision fences', async () => {
    const fetch = vi.fn(async () => json(mutation()))
    vi.stubGlobal('fetch', fetch)
    expect(await setCogs(COMPANY, PRODUCT, request, 'synthetic-key')).toEqual({ type: 'applied', data: mutation() })
    expect(fetch.mock.calls[0]).toMatchObject([`/api/v1/product-cogs-classifications/${PRODUCT}`, { method: 'PUT',
      headers: { 'X-Company-Id': COMPANY, 'Idempotency-Key': 'synthetic-key', 'Content-Type': 'application/json' }, body: JSON.stringify(request) }])
  })
  it('accepts a replay only with matching recorded correlation headers', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(mutation(true), 200, {
      'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original',
    })))
    expect((await setCogs(COMPANY, PRODUCT, request, 'synthetic-key')).type).toBe('applied')
  })
  it.each<readonly [unknown, Record<string, string>]>([
    [mutation(true), {}], [mutation(true), { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'different' }],
    [mutation(), { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original' }],
    [{ ...mutation(), classification: { ...view(), companyId: OTHER } }, {}],
    [{ ...mutation(), classification: { ...view(), productId: OTHER } }, {}],
    [{ ...mutation(), classification: { ...view(), classification: 1 } }, {}],
    [{ ...mutation(), extra: 1 }, {}],
  ])('keeps an incoherent 200 unknown %j', async (body, headers) => {
    vi.stubGlobal('fetch', vi.fn(async () => json(body, 200, headers)))
    expect(await setCogs(COMPANY, PRODUCT, request, 'synthetic-key')).toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
  it.each([201, 204, 302, 500, 503])('retains an unexpected write status %i as unknown', async (status) => {
    vi.stubGlobal('fetch', vi.fn(async () => status === 204 ? new Response(null, { status }) : json({}, status)))
    expect((await setCogs(COMPANY, PRODUCT, request, 'synthetic-key')).type).toBe('unknown')
  })
  it.each([401, 403, 404, 409, 503])('keeps read HTTP %i distinct', async (status) => {
    vi.stubGlobal('fetch', vi.fn(async () => json({}, status)))
    expect(await fetchCogs(COMPANY, PRODUCT)).toMatchObject({ type: 'http', status })
  })
  it.each([400, 401, 403, 404, 409, 422])('keeps a plain write refusal %i distinct without claiming replay', async (status) => {
    vi.stubGlobal('fetch', vi.fn(async () => json({}, status)))
    expect(await setCogs(COMPANY, PRODUCT, request, 'synthetic-key')).toEqual({ type: 'refused', status, terminalReplay: false })
  })
  it.each<readonly [number, string]>([
    [404, 'product_cogs_classification_product_not_found'], [409, 'product_cogs_classification_version_conflict'],
  ])('settles only the documented stored refusal %i/%s', async (status, code) => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ code }, status, {
      'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original',
    })))
    expect(await setCogs(COMPANY, PRODUCT, request, 'synthetic-key')).toEqual({ type: 'refused', status, terminalReplay: true })
  })
  it('never lets a replay-looking permission refusal settle an earlier unknown', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ code: 'product_cogs_classification_version_conflict' }, 403, {
      'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original',
    })))
    expect(await setCogs(COMPANY, PRODUCT, request, 'synthetic-key')).toEqual({ type: 'refused', status: 403, terminalReplay: false })
  })
  it('keeps a stored-refusal claim with malformed correlation evidence unresolved', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ code: 'product_cogs_classification_version_conflict' }, 409, {
      'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'a'.repeat(129),
    })))
    expect(await setCogs(COMPANY, PRODUCT, request, 'synthetic-key')).toEqual({ type: 'refused', status: 409, terminalReplay: false })
  })
  it('distinguishes a cancelled read, offline and online network failure without rotating write identity', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('SENTETIK network') }))
    const controller = new AbortController(); controller.abort()
    expect(await fetchCogs(COMPANY, PRODUCT, controller.signal)).toEqual({ type: 'fence-stale' })
    vi.stubGlobal('navigator', { onLine: false })
    expect(await fetchCogs(COMPANY, PRODUCT)).toEqual({ type: 'offline' })
    expect(await setCogs(COMPANY, PRODUCT, request, 'synthetic-key')).toEqual({ type: 'unknown', reason: 'offline' })
    vi.stubGlobal('navigator', { onLine: true })
    expect(await fetchCogs(COMPANY, PRODUCT)).toEqual({ type: 'network-error' })
    expect(await setCogs(COMPANY, PRODUCT, request, 'synthetic-key')).toEqual({ type: 'unknown', reason: 'network-error' })
  })
})

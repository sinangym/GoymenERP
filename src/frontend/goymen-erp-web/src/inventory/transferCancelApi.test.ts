import { afterEach, describe, expect, it, vi } from 'vitest'
import { cancelTransferDispatch } from './transferCancelApi'
import type { TransferCancelAttempt } from './transferCancelAttempt'

const company = '11111111-1111-4111-8111-111111111111'
const transfer = '22222222-2222-4222-8222-222222222222'
const attempt: TransferCancelAttempt = { binding: 'SENTETIK-session', companyId: company, transferId: transfer,
  key: '33333333-3333-4333-8333-333333333333', payload: { expectedTransferVersion: '2', cancelledAtUtc: '2026-11-16T12:00:00.000Z', reason: 'SENTETIK gerekçe' } }
const view = { transferId: transfer, companyId: company, version: '3', status: 'DispatchCancelled',
  sourceLocationId: '44444444-4444-4444-8444-444444444444', destinationLocationId: '55555555-5555-4555-8555-555555555555',
  sourceCustodianKind: 'Company', sourceCustodianId: company, destinationCustodianKind: 'Company', destinationCustodianId: company,
  plannedAtUtc: '2026-11-16T09:00:00Z', dispatchedAtUtc: '2026-11-16T10:00:00Z', lines: [] }
function response(body: unknown, status = 201, headers?: HeadersInit) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })

describe('transfer dispatch cancel transport, SENTETIK NOT_ENV', () => {
  it('uses the owner route, company, immutable version, reason and the same idempotency key', async () => {
    const fetcher = vi.spyOn(globalThis, 'fetch').mockResolvedValue(response(view))
    expect(await cancelTransferDispatch(attempt)).toEqual({ type: 'applied', transfer: view, replayed: false })
    expect(fetcher.mock.calls[0]).toEqual([`/api/v1/inventory/transfers/${transfer}/cancel-dispatch`, expect.objectContaining({
      method: 'POST', cache: 'no-store', headers: expect.objectContaining({ 'X-Company-Id': company, 'Idempotency-Key': attempt.key }),
      body: JSON.stringify(attempt.payload),
    })])
    expect(JSON.parse(fetcher.mock.calls[0][1]!.body as string)).not.toHaveProperty('companyId')
  })
  it('accepts a replay only with both owner replay headers', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(response(view, 200))
      .mockResolvedValueOnce(response(view, 200, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original' }))
    expect((await cancelTransferDispatch(attempt)).type).toBe('unknown')
    expect(await cancelTransferDispatch(attempt)).toEqual({ type: 'applied', transfer: view, replayed: true })
  })
  it.each(['foreign-company', 'foreign-target', 'not-cancelled', 'malformed', 'headers'])('preserves unknown on a %s success answer', async fault => {
    const body = fault === 'foreign-company' ? { ...view, companyId: transfer } : fault === 'foreign-target' ? { ...view, transferId: company }
      : fault === 'not-cancelled' ? { ...view, status: 'InTransit' } : fault === 'malformed' ? {} : view
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(response(body, 201, fault === 'headers' ? { 'Idempotency-Replayed': 'true' } : {}))
    expect(await cancelTransferDispatch(attempt)).toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
  it.each([400, 401, 403, 404, 409, 415])('preserves a published HTTP %s refusal with its closed code set', async status => {
    const code = status === 400 ? 'validation_failed' : status === 409 ? 'inventory_transfer_cancel_window_closed' : null
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(response(code === null ? {} : { code }, status))
    expect(await cancelTransferDispatch(attempt)).toEqual({ type: 'failed', status, code })
  })
  it.each(['code', 'missing', 'unpublished-status', 'broken-replay', 'foreign-replay'])('retains unknown for a %s refusal', async fault => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(response(fault === 'missing' ? {} : { code: fault === 'code'
      ? 'SENTETIK-unknown' : fault === 'foreign-replay' ? 'inventory_transfer_version_conflict' : 'inventory_transfer_cancel_window_closed' },
    fault === 'unpublished-status' ? 422 : 409, fault === 'broken-replay' || fault === 'foreign-replay' ? { 'Idempotency-Replayed': 'true' } : {}))
    expect(await cancelTransferDispatch(attempt)).toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
  it('recognises the recorded closed-window replay without retrying it as a new key', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(response({ code: 'inventory_transfer_cancel_window_closed' }, 409,
      { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original' }))
    expect(await cancelTransferDispatch(attempt)).toEqual({ type: 'failed', status: 409, code: 'inventory_transfer_cancel_window_closed' })
  })
  it.each([401, 403])('preserves the HTTP %s authority boundary even with a malformed problem body', async status => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('{SENTETIK-broken', { status }))
    expect(await cancelTransferDispatch(attempt)).toEqual({ type: 'failed', status, code: null })
  })
  it('keeps service, offline and network unknowns distinct without minting or mutating an attempt', async () => {
    const original = JSON.stringify(attempt)
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(response({}, 503)).mockRejectedValue(new TypeError('SENTETIK network'))
    expect(await cancelTransferDispatch(attempt)).toEqual({ type: 'unknown', reason: 'service-unavailable' })
    vi.stubGlobal('navigator', { onLine: false })
    expect(await cancelTransferDispatch(attempt)).toEqual({ type: 'unknown', reason: 'offline' })
    vi.stubGlobal('navigator', { onLine: true })
    expect(await cancelTransferDispatch(attempt)).toEqual({ type: 'unknown', reason: 'network-error' })
    expect(JSON.stringify(attempt)).toBe(original)
  })
})

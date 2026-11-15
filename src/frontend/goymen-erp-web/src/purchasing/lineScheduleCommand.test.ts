import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setPurchaseOrderLineSchedule, type LineScheduleSliceInput } from './lineScheduleApi'
import {
  pendingLineScheduleKey,
  recallPendingLineSchedule,
  releasePendingLineSchedule,
  reservePendingLineSchedule,
  type PendingLineSchedule,
} from './pendingLineScheduleStore'

const companyId = '11111111-1111-4111-8111-111111111111'
const orderId = '22222222-2222-4222-8222-222222222222'
const lineId = '33333333-3333-4333-8333-333333333333'
const productId = '44444444-4444-4444-8444-444444444444'
const unitId = '55555555-5555-4555-8555-555555555555'
const keyId = '66666666-6666-4666-8666-666666666666'
const actorId = 'SENTETIK-actor'
const partyId = '88888888-8888-4888-8888-888888888888'
const sliceA = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const sliceB = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
const at = '2026-11-03T09:00:00Z'

const sent: readonly LineScheduleSliceInput[] = [
  { sliceId: sliceA, quantity: '4', deliveryDate: '2026-11-20' },
  { sliceId: sliceB, quantity: '6', deliveryDate: '2026-12-04' },
]
const answeredSchedule = sent.map((slice, index) => ({ ...slice, sliceNo: index + 1 }))
const line = (deliverySchedule: unknown = answeredSchedule) => ({
  lineId, lineNo: 1, kind: 'General', parentLineId: null, sourceRequestLineId: null, productId, productVersion: '7', unitId,
  ordered: '10', technical: '0', committed: '10', accepted: '0', remaining: '10', cancelled: '0', deliverySchedule,
})
const detail = (over: { company?: string; order?: string; lineId?: string; deliverySchedule?: unknown } = {}) => ({
  order: {
    id: over.order ?? orderId, companyId: over.company ?? companyId, status: 'Approved', version: '5', supplierPartyId: partyId,
    companyAccountId: '99999999-9999-4999-8999-999999999999', sourceRequestId: null, effectiveOn: '2026-11-03',
    createdAtUtc: at, updatedAtUtc: at, lineCount: 1,
  },
  lines: [{ ...line(over.deliverySchedule ?? answeredSchedule), lineId: over.lineId ?? lineId }],
  acceptances: [],
  cancellation: null,
})

function response(payload: unknown, status = 200, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(payload), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

beforeEach(() => sessionStorage.clear())
afterEach(() => vi.unstubAllGlobals())

describe('F-027 line termin change wire contract (UI-07, EK16 §2a; ALS-D94, ALS-D81)', () => {
  it('posts the expected version and the slices as entered under the Idempotency-Key and accepts this order\'s line', async () => {
    const fetchMock = vi.fn(async () => response({ detail: detail(), isReplay: false, originalCorrelationId: 'corr-1' }))
    vi.stubGlobal('fetch', fetchMock)
    const outcome = await setPurchaseOrderLineSchedule(companyId, orderId, lineId, '4', keyId, sent)
    expect(outcome).toMatchObject({ type: 'applied', replayed: false })
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toMatch(new RegExp(`/purchasing/orders/${orderId}/lines/${lineId}/schedule$`))
    expect(init.method).toBe('POST')
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe(keyId)
    expect((init.headers as Record<string, string>)['X-Company-Id']).toBe(companyId)
    expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json')
    expect(JSON.parse(init.body as string)).toEqual({ expectedVersion: '4', slices: sent })
  })

  it.each([
    ['another company', detail({ company: '12121212-1212-4212-8212-121212121212' })],
    ['another order', detail({ order: '13131313-1313-4313-8313-131313131313' })],
    ['a body without the line', detail({ lineId: '14141414-1414-4414-8414-141414141414' })],
    ['a line whose slices are not the ones sent', detail({ deliverySchedule: [{ ...answeredSchedule[0], quantity: '10' }] })],
  ])('treats a 200 that answers %s as unknown', async (_name, body) => {
    vi.stubGlobal('fetch', vi.fn(async () => response({ detail: body, isReplay: false, originalCorrelationId: 'corr-1' })))
    expect(await setPurchaseOrderLineSchedule(companyId, orderId, lineId, '4', keyId, sent)).toEqual({ type: 'unknown', reason: 'undecodable' })
  })

  it('accepts a replay only when its headers agree with the body', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => response({ detail: detail(), isReplay: true, originalCorrelationId: 'corr-1' })))
    expect(await setPurchaseOrderLineSchedule(companyId, orderId, lineId, '4', keyId, sent)).toEqual({ type: 'unknown', reason: 'undecodable' })
    vi.stubGlobal('fetch', vi.fn(async () => response({ detail: detail(), isReplay: false, originalCorrelationId: 'corr-1' }, 200,
      { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' })))
    expect(await setPurchaseOrderLineSchedule(companyId, orderId, lineId, '4', keyId, sent)).toEqual({ type: 'unknown', reason: 'undecodable' })
    vi.stubGlobal('fetch', vi.fn(async () => response({ detail: detail(), isReplay: true, originalCorrelationId: 'corr-1' }, 200,
      { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' })))
    expect(await setPurchaseOrderLineSchedule(companyId, orderId, lineId, '4', keyId, sent)).toMatchObject({ type: 'applied', replayed: true })
  })

  it('classifies refusals as failed with the code and the field problems, and a lost answer, a 5xx or another status as unknown', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => response({
      code: 'validation_failed', errors: [{ field: 'slices', code: 'purchasing_schedule_received_portion_locked' }, { field: 1, code: 'x' }],
    }, 422)))
    expect(await setPurchaseOrderLineSchedule(companyId, orderId, lineId, '4', keyId, sent)).toEqual({
      type: 'failed', status: 422, code: 'validation_failed', replayed: false,
      problems: [{ field: 'slices', code: 'purchasing_schedule_received_portion_locked' }],
    })
    vi.stubGlobal('fetch', vi.fn(async () => response({
      code: 'version_conflict', errors: [{ field: 'expectedVersion', code: 'version_conflict' }],
    }, 409, { 'Idempotency-Replayed': 'true' })))
    expect(await setPurchaseOrderLineSchedule(companyId, orderId, lineId, '4', keyId, sent)).toEqual({
      type: 'failed', status: 409, code: 'version_conflict', replayed: true, problems: [{ field: 'expectedVersion', code: 'version_conflict' }],
    })
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 404 })))
    expect(await setPurchaseOrderLineSchedule(companyId, orderId, lineId, '4', keyId, sent))
      .toEqual({ type: 'failed', status: 404, code: null, replayed: false, problems: [] })
    vi.stubGlobal('fetch', vi.fn(async () => response({}, 503)))
    expect(await setPurchaseOrderLineSchedule(companyId, orderId, lineId, '4', keyId, sent)).toEqual({ type: 'unknown', reason: 'service-unavailable' })
    vi.stubGlobal('fetch', vi.fn(async () => response({}, 302)))
    expect(await setPurchaseOrderLineSchedule(companyId, orderId, lineId, '4', keyId, sent)).toEqual({ type: 'unknown', reason: 'unexpected-status' })
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('network') }))
    expect((await setPurchaseOrderLineSchedule(companyId, orderId, lineId, '4', keyId, sent)).type).toBe('unknown')
  })
})

describe('F-027 pending termin change store', () => {
  const snapshot: PendingLineSchedule = { actorId, companyId, orderId, lineId, expectedVersion: '4', idempotencyKey: keyId, slices: sent }

  it('binds the attempt to the session, actor, company, order and line, and keeps it until its own key releases it', () => {
    const key = pendingLineScheduleKey(1, actorId, companyId, orderId, lineId)
    expect(key).not.toBeNull()
    expect(pendingLineScheduleKey(1, null, companyId, orderId, lineId)).toBeNull()
    expect(pendingLineScheduleKey(1, actorId, null, orderId, lineId)).toBeNull()
    expect(pendingLineScheduleKey(1, actorId, companyId, orderId, 'satir-1')).toBeNull()
    expect(pendingLineScheduleKey(2, actorId, companyId, orderId, lineId)).not.toBe(key)
    reservePendingLineSchedule(key, snapshot)
    expect(recallPendingLineSchedule(key)).toEqual(snapshot)
    expect(recallPendingLineSchedule(pendingLineScheduleKey(1, actorId, companyId, orderId, sliceA))).toBeNull()
    releasePendingLineSchedule(key, '67676767-6767-4767-8767-676767676767')
    expect(recallPendingLineSchedule(key)).toEqual(snapshot)
    releasePendingLineSchedule(key, keyId)
    expect(recallPendingLineSchedule(key)).toBeNull()
  })

  it.each([
    ['an extra field', { ...snapshot, reason: 'x' }],
    ['no slices', { ...snapshot, slices: [] }],
    ['a slice with a localized quantity', { ...snapshot, slices: [{ ...sent[0], quantity: '4,5' }] }],
    ['a slice without a date', { ...snapshot, slices: [{ ...sent[0], deliveryDate: '' }] }],
    ['a slice with an extra field', { ...snapshot, slices: [{ ...sent[0], sliceNo: 1 }] }],
    ['a zero version', { ...snapshot, expectedVersion: '0' }],
  ])('ignores a stored snapshot with %s', (_name, stored) => {
    const key = pendingLineScheduleKey(1, actorId, companyId, orderId, lineId)
    sessionStorage.setItem(key as string, JSON.stringify(stored))
    expect(recallPendingLineSchedule(key)).toBeNull()
  })
})

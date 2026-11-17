import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodePlanQueue } from './planQueueApi'
import { clearQueueMove, decodeQueueMoveAttempt, moveQueuePlan, persistQueueMove, readQueueMove, type QueueMoveAttempt } from './manualQueueApi'

const company = '10000000-0000-0000-0000-000000000001'
const plan = '20000000-0000-0000-0000-000000000002'
const other = '20000000-0000-0000-0000-000000000003'
const attempt: QueueMoveAttempt = { idempotencyKey: '40000000-0000-0000-0000-000000000004',
  workCenterId: '30000000-0000-0000-0000-000000000003', expectedVersion: 3, expectedQueueRevision: 0,
  movingPlanId: other, beforePlanId: plan, reason: 'Synthetic manual order' }
const line = { dueDate: null, priorityScore: 0, priorityRevision: 0, plannedKg: '10', processedKg: '0', remainingKg: '10', isOverPlanned: false, isQueued: true }
const reply = { contractVersion: 1, companyId: company, workCenterId: attempt.workCenterId, targetPlanId: plan, targetPlanVersion: 3,
  queueRevision: 1, hasManualOrder: true, items: [{ ...line, planId: other }, { ...line, planId: plan }] }
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear() })

describe('manual queue command contract', () => {
  it('rejects unsafe versions, self anchors, extra fields and incompatible queue metadata', () => {
    expect(decodeQueueMoveAttempt(attempt)).toEqual(attempt)
    expect(decodeQueueMoveAttempt({ ...attempt, beforePlanId: other })).toBeNull()
    expect(decodeQueueMoveAttempt({ ...attempt, expectedQueueRevision: Number.MAX_SAFE_INTEGER + 1 })).toBeNull()
    expect(decodeQueueMoveAttempt({ ...attempt, reason: ' trailing ' })).toBeNull()
    expect(decodeQueueMoveAttempt({ ...attempt, extra: true })).toBeNull()
    expect(decodePlanQueue({ ...reply, hasManualOrder: false })).toBeNull()
    expect(decodePlanQueue({ ...reply, queueRevision: -1 })).toBeNull()
  })
  it('stores an exact attempt and refuses malformed or unavailable storage', () => {
    expect(persistQueueMove('synthetic-queue', attempt)).toBe(true)
    expect(readQueueMove('synthetic-queue')).toEqual({ kind: 'pending', attempt })
    expect(clearQueueMove('synthetic-queue')).toBe(true)
    sessionStorage.setItem('synthetic-queue', '{')
    expect(readQueueMove('synthetic-queue')).toEqual({ kind: 'blocked' })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('synthetic unavailable') })
    expect(persistQueueMove('synthetic-queue', attempt)).toBe(false)
  })
  it('keeps ambiguous responses unknown and accepts only the matching order and replay provenance', async () => {
    const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock)
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(reply), { status: 503 }))
    expect(await moveQueuePlan(company, plan, attempt)).toEqual({ type: 'unknown' })
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ ...reply, items: [...reply.items].reverse() }), { status: 200 }))
    expect(await moveQueuePlan(company, plan, attempt)).toEqual({ type: 'unknown' })
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(reply), { status: 200, headers: { 'Idempotency-Replayed': 'true' } }))
    expect(await moveQueuePlan(company, plan, attempt)).toEqual({ type: 'unknown' })
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(reply), { status: 200,
      headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-correlation' } }))
    expect(await moveQueuePlan(company, plan, attempt)).toEqual({ type: 'applied', resource: reply })
    const init = fetchMock.mock.calls[3][1] as RequestInit
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe(attempt.idempotencyKey)
    expect(JSON.parse(init.body as string)).toEqual({ expectedVersion: 3, expectedQueueRevision: 0, movingPlanId: other, beforePlanId: plan, reason: attempt.reason })
  })
})

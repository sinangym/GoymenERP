import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ManualQueueControls } from './ManualQueueControls'
import { moveQueuePlan } from './manualQueueApi'
import { MANUAL_QUEUE_TEXT as T } from './manualQueueText'
import type { PlanQueue } from './planQueueApi'
vi.mock('./manualQueueApi', async original => ({ ...await original<typeof import('./manualQueueApi')>(), moveQueuePlan: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({}), report: vi.fn() }) }))
const companyId = '10000000-0000-0000-0000-000000000001'; const planId = '20000000-0000-0000-0000-000000000002'
const secondId = '20000000-0000-0000-0000-000000000003'
const line = { dueDate: null, priorityScore: 0, priorityRevision: 0, plannedKg: '10', processedKg: '0', remainingKg: '10', isOverPlanned: false, isQueued: true }
const queue: PlanQueue = { contractVersion: 1, companyId, workCenterId: '30000000-0000-0000-0000-000000000003', targetPlanId: planId,
  targetPlanVersion: 3, queueRevision: 0, hasManualOrder: false, items: [{ ...line, planId }, { ...line, planId: secondId }] }
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.clearAllMocks(); sessionStorage.clear() })
function controls(allowed = true, onRefresh = vi.fn()) {
  return <ManualQueueControls companyId={companyId} planId={planId} namespace="synthetic-queue" queue={queue}
    allowed={allowed} blocked={false} onRefresh={onRefresh} onLocked={vi.fn()} />
}
function fill() {
  fireEvent.change(screen.getByLabelText(T.moving), { target: { value: secondId } })
  fireEvent.change(screen.getByLabelText(T.before), { target: { value: planId } })
  fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'Synthetic manager override' } })
  fireEvent.click(screen.getByRole('button', { name: T.save }))
}
describe('manual queue controls', () => {
  it('hides the write form without the separate capability', () => {
    render(controls(false)); expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })
  it('persists an unknown request over reload and resends the exact body and key', async () => {
    vi.mocked(moveQueuePlan).mockResolvedValue({ type: 'unknown' })
    const first = render(controls()); fill()
    expect(await screen.findByText(T.unknown)).toBeTruthy()
    expect((screen.getByLabelText(T.moving) as HTMLSelectElement).disabled).toBe(true)
    const original = vi.mocked(moveQueuePlan).mock.calls[0]
    first.unmount(); render(controls())
    fireEvent.click(screen.getByRole('button', { name: T.resend }))
    await waitFor(() => expect(moveQueuePlan).toHaveBeenCalledTimes(2))
    expect(vi.mocked(moveQueuePlan).mock.calls[1]).toEqual(original)
  })
  it('never sends when the exact attempt cannot be durably saved in session storage', async () => {
    render(controls())
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('synthetic unavailable') })
    fill(); expect(await screen.findByRole('alert')).toHaveTextContent(T.blocked)
    expect(moveQueuePlan).not.toHaveBeenCalled()
  })
  it('clears a known conflict and refreshes the current queue before another command', async () => {
    vi.mocked(moveQueuePlan).mockResolvedValue({ type: 'failed', status: 409 }); const refresh = vi.fn()
    render(controls(true, refresh)); fill()
    expect(await screen.findByText(T.conflict)).toBeTruthy(); expect(refresh).toHaveBeenCalledTimes(1)
    expect(sessionStorage.length).toBe(0)
  })
})

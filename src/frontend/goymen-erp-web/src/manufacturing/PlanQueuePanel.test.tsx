import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { PlanQueuePanel } from './PlanQueuePanel'
import { changePlanPriority, decodePlanQueue, fetchPlanPriority, fetchPlanQueue, type PlanPriority, type PlanQueue } from './planQueueApi'
import { PLAN_QUEUE_TEXT as T } from './manufacturingText'
vi.mock('./planQueueApi', async (original) => ({
  ...await original<typeof import('./planQueueApi')>(), fetchPlanPriority: vi.fn(), fetchPlanQueue: vi.fn(), changePlanPriority: vi.fn(),
}))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({}), report: vi.fn() }) }))
afterEach(() => { cleanup(); vi.clearAllMocks() })
const companyId = '10000000-0000-0000-0000-000000000001'
const planId = '20000000-0000-0000-0000-000000000002'
const priority: PlanPriority = { companyId, planId, planVersion: 3, priorityScore: 0, priorityRevision: 0, recordedBy: null, recordedAtUtc: null, reason: null }
const queue: PlanQueue = { contractVersion: 1, companyId, workCenterId: '30000000-0000-0000-0000-000000000003', targetPlanId: planId, targetPlanVersion: 3,
  items: [{ planId, dueDate: null, priorityScore: 0, priorityRevision: 0, plannedKg: '10', processedKg: '11', remainingKg: '0', isOverPlanned: true, isQueued: true }] }
function ready() {
  vi.mocked(fetchPlanPriority).mockResolvedValue({ type: 'ok-single', data: priority })
  vi.mocked(fetchPlanQueue).mockResolvedValue({ type: 'ok-single', data: queue })
}
describe('plan queue and priority', () => {
  it('shows the owner remaining kg and over-plan flag and hides the approval control from readers', async () => {
    ready()
    render(<PlanQueuePanel companyId={companyId} planId={planId} planVersion={3} namespace="reader" canApprove={false} />)
    expect(await screen.findByRole('table')).toBeTruthy()
    expect(screen.getByText(`${T.queued} — ${T.overPlanned}`)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    expect(screen.getAllByRole('cell').map((cell) => cell.textContent)).toContain('0')
  })
  it('keeps an unknown write frozen and resends exactly the same key and request', async () => {
    ready(); vi.mocked(changePlanPriority).mockResolvedValue({ type: 'unknown' })
    render(<PlanQueuePanel companyId={companyId} planId={planId} planVersion={3} namespace="unknown-write" canApprove />)
    await screen.findByRole('table')
    fireEvent.change(screen.getByLabelText(T.score), { target: { value: '7' } })
    fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'Synthetic priority reason' } })
    fireEvent.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.unknown)).toBeTruthy()
    expect((screen.getByLabelText(T.score) as HTMLInputElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: T.resend }))
    await waitFor(() => expect(changePlanPriority).toHaveBeenCalledTimes(2))
    expect(vi.mocked(changePlanPriority).mock.calls[1]).toEqual(vi.mocked(changePlanPriority).mock.calls[0])
  })
  it('rejects a queue read with a different priority generation and exposes no mixed old table', async () => {
    ready(); vi.mocked(fetchPlanQueue).mockResolvedValue({ type: 'ok-single', data: { ...queue, items: [{ ...queue.items[0], priorityRevision: 1 }] } })
    render(<PlanQueuePanel companyId={companyId} planId={planId} planVersion={3} namespace="concurrent" canApprove />)
    await waitFor(() => expect(fetchPlanQueue).toHaveBeenCalled())
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })
  it('rejects duplicate plans and malformed server kilograms without deriving a replacement value', () => {
    expect(decodePlanQueue(queue)).toEqual(queue)
    expect(decodePlanQueue({ ...queue, items: [queue.items[0], queue.items[0]] })).toBeNull()
    expect(decodePlanQueue({ ...queue, items: [{ ...queue.items[0], remainingKg: '-1' }] })).toBeNull()
  })
})

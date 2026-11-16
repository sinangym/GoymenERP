import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { PlanInputAvailabilityPanel } from './PlanInputAvailabilityPanel'
import { fetchPlanInputAvailability, decodePlanInputAvailability, type PlanInputAvailability } from './planInputAvailabilityApi'
import { PLAN_INPUT_AVAILABILITY_TEXT as T } from './manufacturingText'

vi.mock('./planInputAvailabilityApi', async (original) => ({
  ...await original<typeof import('./planInputAvailabilityApi')>(), fetchPlanInputAvailability: vi.fn(),
}))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({}), report: vi.fn() }) }))
afterEach(() => { cleanup(); vi.clearAllMocks() })
const companyId = '10000000-0000-0000-0000-000000000001'
const planId = '20000000-0000-0000-0000-000000000002'
const view: PlanInputAvailability = {
  contractVersion: 1, companyId, planId, planVersion: 3, readAtUtc: '2026-11-16T08:00:00Z',
  inputs: [{ labelId: '30000000-0000-0000-0000-000000000003', productId: '40000000-0000-0000-0000-000000000004',
    labelVersion: '8', requestedKg: '600', physicalKg: '1000', blockedKg: '100', allocatedKg: '550', freeKg: '350',
    missingKg: '250', freePositionCount: 1 }],
  cancelledOrders: [{ orderId: '50000000-0000-0000-0000-000000000005', planVersionAfterCancel: 3 }],
}
describe('plan input availability', () => {
  it('shows separate owner quantities and cancellation history without computing any kilograms', async () => {
    vi.mocked(fetchPlanInputAvailability).mockResolvedValue({ type: 'ok-single', data: view })
    render(<PlanInputAvailabilityPanel companyId={companyId} planId={planId} planVersion={3} />)
    expect(await screen.findByRole('table')).toBeTruthy()
    expect(screen.getByRole('columnheader', { name: T.physical })).toBeTruthy()
    expect(screen.getByRole('columnheader', { name: T.free })).toBeTruthy()
    expect(screen.getByRole('cell', { name: '250' })).toBeTruthy()
    expect(screen.getByText(T.cancelledLine(view.cancelledOrders[0].orderId, 3))).toBeTruthy()
  })
  it('never displays the old quantities after a failed owner read', async () => {
    vi.mocked(fetchPlanInputAvailability).mockResolvedValue({ type: 'http', status: 503 })
    render(<PlanInputAvailabilityPanel companyId={companyId} planId={planId} planVersion={3} />)
    expect(await screen.findByRole('button')).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })
  it('decodes lossless kilograms and refuses duplicates or malformed quantities', () => {
    expect(decodePlanInputAvailability(view)).toEqual(view)
    expect(decodePlanInputAvailability({ ...view, inputs: [view.inputs[0], view.inputs[0]] })).toBeNull()
    expect(decodePlanInputAvailability({ ...view, inputs: [{ ...view.inputs[0], missingKg: '-1' }] })).toBeNull()
  })
  it('hides the previous company quantities while a new scope read is pending', async () => {
    vi.mocked(fetchPlanInputAvailability).mockResolvedValueOnce({ type: 'ok-single', data: view })
      .mockImplementationOnce(() => new Promise(() => {}))
    const mounted = render(<PlanInputAvailabilityPanel companyId={companyId} planId={planId} planVersion={3} />)
    await screen.findByRole('table')
    mounted.rerender(<PlanInputAvailabilityPanel companyId="90000000-0000-0000-0000-000000000009" planId={planId} planVersion={3} />)
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.queryByText(view.inputs[0].productId)).toBeNull()
  })
})

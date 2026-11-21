import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PlanReapprovalSection } from './PlanReapprovalSection'
import { reapprovePlan } from './planCommandApi'
import { decodePlan } from './contract'
import { plan } from './testHarness'
import { PLAN_REAPPROVAL_TEXT as T } from './manufacturingText'

vi.mock('./planCommandApi', () => ({ reapprovePlan: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({}), report: vi.fn() }) }))
afterEach(() => { cleanup(); vi.clearAllMocks() })

describe('cancelled-order plan reapproval', () => {
  it('requires explicit labels and reuses the frozen selection and key after an unknown outcome', async () => {
    const user = userEvent.setup()
    const current = decodePlan(plan({ status: 'Approved', version: 3, requiresReapproval: true, orderId: null,
      approvedByUserId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', approvedAtUtc: '2026-11-16T08:00:00Z' }))!
    expect(current).not.toBeNull()
    const applied = { ...current, version: 4, requiresReapproval: false }
    vi.mocked(reapprovePlan).mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
      .mockResolvedValueOnce({ type: 'applied', resource: applied, replayed: true, originalCorrelationId: 'SENTETIK' })
    const onApplied = vi.fn(async () => {})
    render(<PlanReapprovalSection plan={current} companyId={current.companyId} namespace="SENTETIK-reapproval"
      canApprove onApplied={onApplied} />)
    await user.click(screen.getByRole('button', { name: T.submit }))
    expect(reapprovePlan).not.toHaveBeenCalled()
    expect(screen.getByText(T.chooseAll)).toBeTruthy()
    for (const checkbox of screen.getAllByRole('checkbox')) await user.click(checkbox)
    await user.click(screen.getByRole('button', { name: T.submit }))
    expect(await screen.findByText(T.pending)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.resend }))
    await waitFor(() => expect(onApplied).toHaveBeenCalledWith(applied))
    expect(vi.mocked(reapprovePlan).mock.calls[1][2]).toEqual(vi.mocked(reapprovePlan).mock.calls[0][2])
  })
})

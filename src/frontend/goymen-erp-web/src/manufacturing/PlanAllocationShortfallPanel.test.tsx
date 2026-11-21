import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { PlanAllocationShortfallPanel } from './PlanAllocationShortfallPanel'
import { decodePlanAllocationShortfall } from './planAllocationShortfall'
import { approvePlan } from './planCommandApi'
import { PLAN_ALLOCATION_SHORTFALL_TEXT as T } from './manufacturingText'
afterEach(() => { cleanup(); vi.unstubAllGlobals() })
const companyId = '10000000-0000-0000-0000-000000000001'
const planId = '20000000-0000-0000-0000-000000000002'
const snapshot = { companyId, planId, labelId: '30000000-0000-0000-0000-000000000003', productId: '40000000-0000-0000-0000-000000000004',
  labelVersion: '8', requestedKg: '600', availableKg: '350', missingKg: '250', freePositionCount: 1 as const }
describe('plan allocation refusal', () => {
  it('renders exactly the refused owner quantities and distinguishes them from a fresh advisory read', () => {
    render(<PlanAllocationShortfallPanel value={snapshot} />)
    expect(screen.getByText(T.help)).toBeTruthy(); expect(screen.getByText(snapshot.productId)).toBeTruthy()
    expect(screen.getByText('600')).toBeTruthy(); expect(screen.getByText('350')).toBeTruthy(); expect(screen.getByText('250')).toBeTruthy()
  })
  it('refuses foreign, missing and malformed detail without inventing any quantities', () => {
    expect(decodePlanAllocationShortfall(snapshot, companyId, planId)).toEqual(snapshot)
    expect(decodePlanAllocationShortfall({ ...snapshot, companyId: planId }, companyId, planId)).toBeNull()
    expect(decodePlanAllocationShortfall({ ...snapshot, availableKg: '-1' }, companyId, planId)).toBeNull()
    expect(decodePlanAllocationShortfall(null, companyId, planId)).toBeNull()
  })
  it('decodes optional quantities only on a fresh exact allocation refusal and preserves code-only compatibility', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ code: 'inventory_manufacturing_allocation_insufficient_free_stock', planAllocationShortfall: snapshot }), { status: 409 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ code: 'inventory_manufacturing_allocation_insufficient_free_stock' }), { status: 409 }))
    vi.stubGlobal('fetch', fetcher)
    const first = await approvePlan(companyId, planId, { kind: 'approve', idempotencyKey: 'synthetic', expectedVersion: 1 })
    expect(first.type === 'failed' && first.planAllocationShortfall).toEqual(snapshot)
    const second = await approvePlan(companyId, planId, { kind: 'approve', idempotencyKey: 'synthetic', expectedVersion: 1 })
    expect(second.type === 'failed' && second.planAllocationShortfall).toBeUndefined()
  })
  it('does not display optional quantity detail carried by an old replayed answer', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'inventory_manufacturing_allocation_insufficient_free_stock', planAllocationShortfall: snapshot }),
      { status: 409, headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original' } })))
    const answer = await approvePlan(companyId, planId, { kind: 'approve', idempotencyKey: 'synthetic', expectedVersion: 1 })
    expect(answer.type === 'failed' && answer.planAllocationShortfall).toBeUndefined()
  })
})

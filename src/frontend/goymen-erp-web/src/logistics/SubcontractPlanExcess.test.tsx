/** SENTETIK source-only proof. Does not assert a real Inventory reservation, SQL transaction or permission grant. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { initialShellState, type ShellState } from '../shell/shellState'
import { SubcontractDeliveryDetailPage, additionalAllocationPayload } from './SubcontractDeliveryPages'
import { SUBCONTRACT_CAPABILITY as CAP, decodeSubcontractInstruction, type SubcontractInstruction } from './subcontractDeliveryContract'
import { beginSubcontractAttempt, resetSubcontractAttemptsForTests, sendSubcontractAttempt } from './subcontractDeliveryApi'
import { PLAN_EXCESS_CAPABILITY, type PlanExcessView } from './planExcessApi'
import { PLAN_EXCESS_TEXT as T } from './planExcessText'
import { SUBCONTRACT_DELIVERY_TEXT as D } from './subcontractDeliveryMessages.tr'
import { REALIZATION_CAPABILITY } from './realizationContract'
import { resetPlanExcessAttempts } from './PlanExcessPreparationPanel'

const id = (n: number) => `12000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const company = id(1), instruction = id(2), lineId = id(13), exceptionId = id(20), allocation = id(21)
let shell: ShellState
const clients: QueryClient[] = []
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell }))
const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ synthetic: true })), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
const source = () => ({ companyId: company, customerPartyId: id(3), customerAccountId: id(4), salesOrderId: id(5), salesOrderLineId: id(6), salesOrderVersion: '4', salesLineRevision: 0,
  productId: id(7), productVersion: '2', salesServiceProductId: id(8), manufacturingOrderId: id(9), manufacturingOrderVersion: '7', manufacturingPlanId: id(10), manufacturingPlanVersion: '5',
  manufacturingSalesLinkRevision: '1', outputFactId: id(11), labelId: id(12), outputNetKg: '120', manufacturingSourceVersion: 'A'.repeat(64), labelCode: 'SYN-LABEL', salesDocumentNo: 'SYN-SALES', serviceCode: 'SYN-SERVICE', serviceName: 'SENTETIK hizmet' })
const value = (): SubcontractInstruction => ({ companyId: company, instructionId: instruction, version: '5', status: 'Issued', customerPartyId: id(3), customerAccountId: id(4),
  lines: [{ lineId, source: source(), allocationId: id(14), instructedKg: '100', loadedKg: '0', shippedKg: '0', deliveredKg: '0', allocationVersion: '1', labelVersion: '2', approvedPlanExcess: '20' }],
  weighing: null, originalWeighing: null, recordedLoadId: null, corrections: [], isReplay: false, originalCorrelationId: 'SENTETIK correlation' })
const reserved = (allocationId = allocation): SubcontractInstruction => ({ ...value(), lines: [{ ...value().lines[0], labelVersion: '3', additionalAllocations: [{ allocationId, exceptionId, version: '1', allocatedKg: '20', remainingKg: '20' }] }] })
const loaded = (): SubcontractInstruction => {
  const weighing = { weighingId: id(22), revision: 1, direction: 'TareFirst' as const, firstKg: '1000', secondKg: '1120', netKg: '120', loadedKg: '120', differenceKg: '0' }
  return { ...reserved(), lines: [{ ...reserved().lines[0], loadedKg: '120' }], weighing, originalWeighing: weighing }
}
const exception = (overrides: Partial<PlanExcessView> = {}): PlanExcessView => ({ companyId: company, exceptionId, instructionId: instruction, lineId, salesOrderId: id(5), salesOrderLineId: id(6), unitId: id(23), version: '2', instructionVersion: '5', status: 'Approved',
  instructed: '100', quantity: '20', loadedUsed: '0', shippedUsed: '0', availableToLoad: '20', availableToShip: '0', reason: 'SENTETIK extra', proposedBy: 'SENTETIK', proposedAtUtc: '2026-11-18T09:00:00Z', approvedBy: 'SENTETIK', approvedAtUtc: '2026-11-18T09:01:00Z', ...overrides })
const json = (body: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } }); clients.push(client)
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[`/lojistik/fason-teslimler/${instruction}`]}><Routes><Route path="/lojistik/fason-teslimler/:instructionId" element={<SubcontractDeliveryDetailPage />} /></Routes></MemoryRouter></QueryClientProvider>)
}
beforeEach(() => { shell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1, activeContext: {
  userId: 'SENTETIK', companyId: company, scopeMode: 'single', capabilityGeneration: 'SENTETIK', capabilities: [CAP.detail, CAP.load, CAP.ship, PLAN_EXCESS_CAPABILITY.prepare, REALIZATION_CAPABILITY.allocation],
} } })
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); resetSubcontractAttemptsForTests(); resetPlanExcessAttempts(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('SAT_D417 actual optional subcontract reservation frame', () => {
  it('retains the original instruction and original allocation while reading separate approved capacity and reservation', () => {
    expect(decodeSubcontractInstruction(reserved(), company, instruction)).toEqual(reserved())
    expect(decodeSubcontractInstruction(loaded(), company, instruction)?.lines[0]).toMatchObject({ instructedKg: '100', loadedKg: '120', approvedPlanExcess: '20', allocationId: id(14) })
    const legacy = { ...value(), lines: [{ ...value().lines[0] }] }; delete (legacy.lines[0] as Partial<typeof legacy.lines[0]>).approvedPlanExcess
    expect(decodeSubcontractInstruction(legacy, company, instruction)?.lines[0]).not.toHaveProperty('additionalAllocations')
  })
  it.each([{ additionalAllocations: null }, { additionalAllocations: 20 }, { additionalAllocations: [{ allocationId: allocation, exceptionId, version: 1, allocatedKg: '20', remainingKg: '20' }] },
    { additionalAllocations: [{ allocationId: id(14), exceptionId, version: '1', allocatedKg: '20', remainingKg: '20' }] },
    { additionalAllocations: [{ allocationId: allocation, exceptionId, version: '1', allocatedKg: '20', remainingKg: '-1' }] },
    { additionalAllocations: [{ allocationId: allocation, exceptionId, version: '1', allocatedKg: '20', remainingKg: '20', price: '1' }] },
  ])('refuses malformed missing or primary masquerading additional reservation %j', ({ additionalAllocations }) => {
    expect(decodeSubcontractInstruction({ ...reserved(), lines: [{ ...reserved().lines[0], additionalAllocations }] }, company, instruction)).toBeNull()
  })
  it('forwards only the explicitly selected actual reservation version and exact entered quantity', () => {
    const line = reserved().lines[0]
    expect(additionalAllocationPayload([], line)).toEqual({})
    expect(additionalAllocationPayload([{ allocationId: allocation, expectedVersion: '1', quantityKg: '2,5' }], line)).toEqual({ additionalAllocations: [{ allocationId: allocation, expectedVersion: '1', quantityKg: '2.5' }] })
    for (const use of [{ allocationId: id(14), expectedVersion: '1', quantityKg: '20' }, { allocationId: allocation, expectedVersion: '2', quantityKg: '20' }, { allocationId: allocation, expectedVersion: '1', quantityKg: '0' }]) {
      expect(additionalAllocationPayload([use], line)).toBeNull()
    }
  })
  it.each(['matching', 'allocation', 'exception', 'quantity', 'version', 'instructionVersion', 'missing'])('requires the original actual reservation success proof %s', async fault => {
    const sent = beginSubcontractAttempt('SENTETIK reserve proof', company, instruction, `/logistics/subcontract-deliveries/${instruction}/lines/${lineId}/plan-excess/${exceptionId}/reserve`, 'subcontract', {
      expectedInstructionVersion: '5', expectedExceptionVersion: '2', allocationId: allocation, expectedLabelVersion: '2', quantityKg: '20', reason: 'SENTETIK reserve',
    })!
    const row = reserved().lines[0].additionalAllocations![0]
    const changed = fault === 'allocation' ? { ...row, allocationId: id(99) } : fault === 'exception' ? { ...row, exceptionId: id(99) }
      : fault === 'quantity' ? { ...row, allocatedKg: '21' } : fault === 'version' ? { ...row, version: '2' } : row
    const body = { ...reserved(), originalCorrelationId: sent.correlation, version: fault === 'instructionVersion' ? '6' : '5',
      lines: [{ ...reserved().lines[0], additionalAllocations: fault === 'missing' ? [] : [changed] }] }
    vi.stubGlobal('fetch', vi.fn(async () => json(body)))
    expect((await sendSubcontractAttempt(sent)).type).toBe(fault === 'matching' ? 'applied' : 'unknown')
  })
  it('reserves an explicitly entered additional quantity and preserves the original id and full frozen retry after a lost answer', async () => {
    let current = value(); const posts: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (url: unknown, init?: RequestInit) => {
      if (String(url).includes('/plan-excess?')) return json({ items: [exception()], nextCursor: null })
      if (init?.method !== 'POST') return json(current)
      posts.push(init); if (posts.length === 1) throw new TypeError('SENTETIK lost reserve reply')
      const body = JSON.parse(String(init.body)); current = { ...reserved(body.allocationId), isReplay: true, originalCorrelationId: 'SENTETIK original' }
      return json(current, 200, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK original' })
    }))
    mount(); const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: `${T.reserve} · SYN-LABEL` }))
    const panel = screen.getByRole('region', { name: `${T.reserve} SYN-LABEL` })
    await user.selectOptions(await within(panel).findByLabelText(T.exception), exceptionId)
    await user.type(within(panel).getByLabelText(T.reservationQuantity), '20'); await user.type(within(panel).getByLabelText(T.reason), 'SENTETIK ayrı ayırma')
    await user.click(within(panel).getByRole('button', { name: T.reserve }))
    expect(await screen.findByText(D.unknown)).toBeTruthy(); await user.click(screen.getByRole('button', { name: D.retry }))
    expect(await screen.findByText(D.replayed)).toBeTruthy(); expect(posts).toHaveLength(2)
    expect(posts[1].body).toBe(posts[0].body); expect(new Headers(posts[1].headers).get('Idempotency-Key')).toBe(new Headers(posts[0].headers).get('Idempotency-Key'))
    expect(JSON.parse(String(posts[0].body))).toMatchObject({ expectedInstructionVersion: '5', expectedExceptionVersion: '2', expectedLabelVersion: '2', quantityKg: '20' })
    expect(JSON.parse(String(posts[0].body)).allocationId).not.toBe(id(14))
    expect(String(posts[0].body)).not.toMatch(/companyId|instructedKg|grant/)
    expect(current.lines[0].instructedKg).toBe('100'); expect(current.lines[0].allocationId).toBe(id(14))
  })
  it('ships the actual primary and explicitly selected additional reservation and plan exception without computing a split', async () => {
    let current = loaded(); const posts: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (url: unknown, init?: RequestInit) => {
      if (String(url).includes('/plan-excess?')) return json({ items: [exception({ loadedUsed: '20', availableToLoad: '0', availableToShip: '20' })], nextCursor: null })
      if (init?.method !== 'POST') return json(current)
      posts.push(init); current = { ...current, originalCorrelationId: new Headers(init.headers).get('X-Correlation-Id')!, lines: [{ ...current.lines[0], shippedKg: '120' }] }
      return json(current)
    }))
    mount(); const user = userEvent.setup(); await user.click(await screen.findByRole('button', { name: T.use }))
    await user.click(await screen.findByRole('checkbox', { name: `${T.exception} ${exceptionId}` })); await user.type(screen.getByLabelText(T.useQuantity), '20')
    await user.click(screen.getByRole('checkbox', { name: `${T.allocation} ${allocation}` })); await user.type(screen.getByLabelText(T.reservationUseQuantity), '20')
    fireEvent.change(screen.getByLabelText(D.occurred), { target: { value: '2026-11-18T13:15:00' } })
    await user.click(screen.getByRole('button', { name: D.ship })); await waitFor(() => expect(posts).toHaveLength(1))
    const body = JSON.parse(String(posts[0].body))
    expect(body.lines[0]).toMatchObject({ lineId, expectedAllocationVersion: '1', expectedLabelVersion: '3',
      additionalAllocations: [{ allocationId: allocation, expectedVersion: '1', quantityKg: '20' }], planExcessUses: [{ exceptionId, expectedVersion: '2', quantity: '20' }] })
    expect(body.lines[0]).not.toHaveProperty('quantityKg'); expect(body.lines[0]).not.toHaveProperty('instructedKg')
  })
  it('does not offer extra stock reservation without its existing allocation capability', async () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [CAP.detail, PLAN_EXCESS_CAPABILITY.prepare] } }
    vi.stubGlobal('fetch', vi.fn(async () => json(value()))); mount()
    await screen.findByText('SYN-LABEL'); expect(screen.queryByRole('button', { name: `${T.reserve} · SYN-LABEL` })).toBeNull()
  })
})

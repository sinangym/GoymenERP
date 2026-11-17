/** Synthetic DOM sources, not real HTTP/SQL/browser acceptance. Runtime NOT_RUN. */
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { initialShellState, type ShellState } from '../shell/shellState'
import type { ViewOutcome } from '../shell/uiState'
import { AdditionalCostPage } from './AdditionalCostPage'
import { fetchAdditionalCosts, fetchAdditionalCostSource, fetchAdditionalCostReferences, recordAdditionalCost } from './additionalCostApi'
import { ADDITIONAL_COST_CAPABILITY as C, type AdditionalCost } from './additionalCostContract'
import { ADDITIONAL_COST_TEXT as T } from './additionalCostText'
import { expenseFact, expenseId as id, expenseReferences, expenseSource } from './additionalCostFixtures.testSupport'
import { fetchPurchasingSupplierOptions } from './api'
import { resetAdjustmentAttemptsForTests } from './useReceiptAdjustmentAttempt'
let current: ShellState
const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({})), report: vi.fn() }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => current }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('./additionalCostApi', () => ({ fetchAdditionalCosts: vi.fn(), fetchAdditionalCostSource: vi.fn(), fetchAdditionalCostReferences: vi.fn(), recordAdditionalCost: vi.fn() }))
vi.mock('./api', () => ({ fetchPurchasingSupplierOptions: vi.fn() }))
const clients: QueryClient[] = []
function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, refetchOnWindowFocus: false } } }); clients.push(client)
  const content = () => <QueryClientProvider client={client}><MemoryRouter initialEntries={['/satinalma/ek-masraflar/goods/' + id(2)]}>
    <Routes><Route path="/satinalma/ek-masraflar/:sourceKind/:orderId" element={<AdditionalCostPage />} /></Routes>
  </MemoryRouter></QueryClientProvider>
  const view = render(content()); return { refresh: () => view.rerender(content()) }
}
beforeEach(() => {
  vi.clearAllMocks(); resetAdjustmentAttemptsForTests()
  current = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
    activeContext: { userId: 'SENTETIK-actor', companyId: id(1), scopeMode: 'single', capabilityGeneration: 'g1', capabilities: [C.read] } }
  vi.mocked(fetchAdditionalCostSource).mockResolvedValue({ type: 'ok-single', data: expenseSource() })
  vi.mocked(fetchAdditionalCosts).mockResolvedValue({ type: 'ok-single', data: [expenseFact()] })
  vi.mocked(fetchAdditionalCostReferences).mockResolvedValue({ type: 'ok-single', data: expenseReferences() })
  vi.mocked(fetchPurchasingSupplierOptions).mockResolvedValue({ type: 'ok-single', data: { items: [
    { supplierPartyId: id(5), companyAccountId: id(6), label: 'SENTETIK taşıyıcı' }], nextCursor: null } })
  vi.mocked(recordAdditionalCost).mockResolvedValue({ type: 'unknown', reason: 'malformed' })
})
afterEach(() => { cleanup(); clients.splice(0).forEach(x => x.clear()); resetAdjustmentAttemptsForTests() })
describe('ALC-S8 user-completable read source', () => {
  it('shows frozen expense totals and keeps undecided recording closed without loading write prerequisites', async () => {
    mount(); await screen.findAllByText('SENTETIK nakliye')
    expect(screen.getByText('120.01 TRY')).toBeTruthy(); expect(screen.getByText(T.unavailableWrite)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.record })).toBeNull()
    expect(fetchAdditionalCostReferences).not.toHaveBeenCalled(); expect(recordAdditionalCost).not.toHaveBeenCalled()
  })
  it('records an explicitly described untyped expense for another real supplier and freezes unknown writes for exact retry', async () => {
    current = { ...current, activeContext: { ...current.activeContext!, capabilities: [C.read, C.record] } }
    mount(); await screen.findAllByText('SENTETIK nakliye')
    fireEvent.change(screen.getByLabelText(T.date), { target: { value: '2026-11-17' } })
    await screen.findByText(T.untyped)
    fireEvent.change(screen.getByLabelText(T.supplier), { target: { value: id(6) } })
    fireEvent.change(screen.getByLabelText(T.description), { target: { value: 'SENTETIK taşıma' } })
    fireEvent.change(screen.getByLabelText(T.net), { target: { value: '100,01' } })
    fireEvent.change(screen.getByLabelText(T.vat), { target: { value: 'SENTETIK-20' } })
    fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'SENTETIK belge' } })
    fireEvent.click(screen.getByRole('button', { name: T.record })); await screen.findByText(T.unknown)
    const first = vi.mocked(recordAdditionalCost).mock.calls[0]
    expect(first[2]).toMatchObject({ expectedOrderVersion: '3', supplierPartyId: id(5), companyAccountId: id(6),
      expenseTypeId: null, expectedExpenseTypeVersion: null, netAmount: '100.01', expectedVatListVersion: '1' })
    expect(screen.getByRole('button', { name: T.record }).matches(':disabled')).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(recordAdditionalCost).toHaveBeenCalledTimes(2))
    expect(vi.mocked(recordAdditionalCost).mock.calls[1]).toEqual(first)
  })
  it.each([403, 404, 409, 503])('shows the %s failure without former financial data', async status => {
    vi.mocked(fetchAdditionalCosts).mockResolvedValue({ type: 'http', status })
    mount(); await waitFor(() => expect(fetchAdditionalCosts).toHaveBeenCalled())
    expect(screen.queryByText('SENTETIK nakliye')).toBeNull()
    if (status === 403) await waitFor(() => expect(boundary.report).toHaveBeenCalledWith(403, expect.anything()))
  })
  it('discards an in-flight former-company response after a company change', async () => {
    let resolve!: (value: ViewOutcome<readonly AdditionalCost[]>) => void
    vi.mocked(fetchAdditionalCosts).mockImplementationOnce(() => new Promise(done => { resolve = done }))
      .mockResolvedValue({ type: 'ok-single', data: [] })
    const view = mount(); await waitFor(() => expect(fetchAdditionalCosts).toHaveBeenCalledTimes(1))
    current = { ...current, committedContextRevision: 2, activeContext: { ...current.activeContext!, companyId: id(99) } }
    view.refresh(); await waitFor(() => expect(fetchAdditionalCosts).toHaveBeenCalledTimes(2))
    await act(async () => resolve({ type: 'ok-single', data: [expenseFact()] }))
    expect(screen.queryByText('SENTETIK nakliye')).toBeNull()
  })
})

/** Synthetic DOM sources; real HTTP, SQL, browser and runtime acceptance are NOT_RUN. */
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { initialShellState, type ShellState } from '../shell/shellState'
import type { ViewOutcome } from '../shell/uiState'
import { ServiceOrderCreatePage, ServiceOrderDetailPage, ServiceOrderListPage } from './ServiceOrderPages'
import { fetchServiceOrder, fetchServiceOrders, fetchServiceOrderReferences, fetchServiceOrderTaxOptions, submitServiceOrder } from './serviceOrderApi'
import { serviceFixture, serviceId as id } from './serviceOrderFixtures.testSupport'
import { SERVICE_ORDER_CAPABILITY as C, type ServiceOrder } from './serviceOrderContract'
import { SERVICE_ORDER_TEXT as T } from './serviceOrderText'
import { SUBCONTRACT_TAX_TEXT as TAX } from '../settings/subcontractTaxText'
import { resetAdjustmentAttemptsForTests } from './useReceiptAdjustmentAttempt'
let currentShell: ShellState
const auth = vi.hoisted(() => ({ capture: vi.fn(() => ({})), report: vi.fn() }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => currentShell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => auth }))
vi.mock('./serviceOrderApi', () => ({ fetchServiceOrder: vi.fn(), fetchServiceOrders: vi.fn(), fetchServiceOrderReferences: vi.fn(), fetchServiceOrderTaxOptions: vi.fn(), submitServiceOrder: vi.fn() }))
vi.mock('./api', () => ({ fetchPurchasingSupplierOptions: vi.fn(async () => ({ type: 'ok-single', data: { items: [] } })),
  fetchPurchasingCurrencyCandidates: vi.fn(async () => ({ type: 'ok-single', data: { items: [] } })) }))
vi.mock('../manufacturing/planReferences', () => ({ fetchParentRollCandidates: vi.fn() }))
vi.mock('../inventory/api', () => ({ fetchOpeningProducts: vi.fn() }))
const clients: QueryClient[] = []
function mount(page: 'list' | 'detail' | 'create' = 'list') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, refetchOnWindowFocus: false } } }); clients.push(client)
  const content = () => <QueryClientProvider client={client}><MemoryRouter initialEntries={[
    page === 'create' ? '/satinalma/hizmet-siparisleri/yeni' : page === 'detail' ? '/satinalma/hizmet-siparisleri/' + id(2) : '/satinalma/hizmet-siparisleri']}>
    <Routes><Route path="/satinalma/hizmet-siparisleri" element={<ServiceOrderListPage />} />
      <Route path="/satinalma/hizmet-siparisleri/yeni" element={<ServiceOrderCreatePage />} />
      <Route path="/satinalma/hizmet-siparisleri/:serviceOrderId" element={<ServiceOrderDetailPage />} /></Routes>
  </MemoryRouter></QueryClientProvider>
  const rendered = render(content()); return { refresh: () => rendered.rerender(content()) }
}
beforeEach(() => {
  vi.clearAllMocks(); resetAdjustmentAttemptsForTests()
  currentShell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
    activeContext: { userId: 'SENTETIK-actor', companyId: id(1), scopeMode: 'single', capabilityGeneration: 'g1', capabilities: Object.values(C) } }
  vi.mocked(fetchServiceOrder).mockResolvedValue({ type: 'ok-single', data: serviceFixture() })
  vi.mocked(fetchServiceOrders).mockResolvedValue({ type: 'ok-single', data: [serviceFixture()] })
  vi.mocked(fetchServiceOrderReferences).mockResolvedValue({ type: 'ok-single', data: { companyId: id(1), serviceTypes: [], units: [] } })
  vi.mocked(fetchServiceOrderTaxOptions).mockResolvedValue({ type: 'http', status: 503 })
  vi.mocked(submitServiceOrder).mockResolvedValue({ type: 'unknown', reason: 'malformed' })
})
afterEach(() => { cleanup(); clients.splice(0).forEach(x => x.clear()); resetAdjustmentAttemptsForTests() })
describe('ALC-S7 user workflow source (synthetic, NOT_ENV)', () => {
  it('opens a numbered service order and never presents its technical identity as the selection input', async () => {
    mount()
    expect((await screen.findByRole('link', { name: serviceFixture().documentNumber })).getAttribute('href'))
      .toBe('/satinalma/hizmet-siparisleri/' + id(2))
    expect(screen.queryByDisplayValue(id(2))).toBeNull()
  })
  it('keeps an actual empty service catalog empty and prevents an assumed default submission', async () => {
    mount('create'); await screen.findByText(T.noCatalog)
    expect(screen.getByRole('button', { name: T.save }).matches(':disabled')).toBe(true)
    expect(screen.getByLabelText(T.serviceType).querySelectorAll('option')).toHaveLength(1)
    expect(submitServiceOrder).not.toHaveBeenCalled()
  })
  it('keeps unknown approval frozen and retries the exact body and key', async () => {
    mount('detail'); await screen.findByText(T.withheld)
    fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'SENTETIK onay gerekçesi' } })
    fireEvent.click(screen.getByRole('button', { name: T.approve }))
    await screen.findByText(T.unknown)
    const first = vi.mocked(submitServiceOrder).mock.calls[0]
    expect(first[2]).toBe('approve')
    expect(first[3]).toEqual({ expectedVersion: '1', reason: 'SENTETIK onay gerekçesi' })
    expect(screen.getByRole('button', { name: T.approve }).matches(':disabled')).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(submitServiceOrder).toHaveBeenCalledTimes(2))
    expect(vi.mocked(submitServiceOrder).mock.calls[1]).toEqual(first)
  })
  it('hides writes when the current capability set lacks approval or acceptance', async () => {
    currentShell = { ...currentShell, activeContext: { ...currentShell.activeContext!, capabilities: [C.detail] } }
    mount('detail'); await screen.findByText(T.withheld)
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
    expect(screen.queryByRole('button', { name: T.accept })).toBeNull()
  })
  it('enters a later price as a new revision without changing accepted quantities or submitting physical facts', async () => {
    vi.mocked(fetchServiceOrder).mockResolvedValue({ type: 'ok-single', data: { ...serviceFixture(), status: 'Approved', isOpen: false,
      version: '3', lines: [{ ...serviceFixture().lines[0], acceptedQuantity: '10' }] } })
    mount('detail'); await screen.findByText(T.priceRevisionHelp)
    fireEvent.change(screen.getByLabelText('SENTETIK hizmet — ' + T.revisedPrice), { target: { value: '2,0000' } })
    fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'SENTETIK gerçek fiyat' } })
    fireEvent.click(screen.getByRole('button', { name: T.revisePrices }))
    await screen.findByText(T.unknown)
    expect(vi.mocked(submitServiceOrder).mock.calls[0][2]).toBe('prices')
    expect(vi.mocked(submitServiceOrder).mock.calls[0][3]).toEqual({ expectedVersion: '3', reason: 'SENTETIK gerçek fiyat',
      lines: [{ lineId: id(6), unitPrice: '2' }] })
  })
  it('submits a tax-only approval-authorized revision from actual lists without quantity price or physical fields', async () => {
    vi.mocked(fetchServiceOrder).mockResolvedValue({ type: 'ok-single', data: { ...serviceFixture(), kind: 'OwnMaterialSubcontract' } })
    vi.mocked(fetchServiceOrderTaxOptions).mockResolvedValue({ type: 'ok-single', data: { companyId: id(1), side: 2, on: '2026-11-17', defaultVersion: '1',
      default: { vatApplies: true, vatPercent: '20', exemptionCode: null, withholdingApplies: false, withholdingPercent: null, withholdingCode: null },
      vatRates: [{ code: '10', name: 'SENTETIK KDV', percent: '10', taxCode: null }], withholdingRatios: [], exemptionCodes: [], withholdingCodes: [], sourceVersion: 'SENTETIK-lists:1' } })
    mount('detail'); const heading = await screen.findByRole('heading', { name: TAX.taxRevision })
    const form = within(heading.closest('form')!)
    await waitFor(() => expect(form.getByLabelText(TAX.vat).matches(':disabled')).toBe(false))
    fireEvent.change(form.getByLabelText(T.serviceType), { target: { value: id(6) } })
    fireEvent.change(form.getByLabelText(TAX.vat), { target: { value: '10' } })
    fireEvent.change(form.getByLabelText(TAX.reason), { target: { value: 'SENTETIK satır vergisi' } })
    fireEvent.click(form.getByRole('button', { name: TAX.save })); await screen.findByText(T.unknown)
    expect(vi.mocked(submitServiceOrder).mock.calls[0][2]).toBe('tax')
    expect(vi.mocked(submitServiceOrder).mock.calls[0][3]).toEqual({ expectedVersion: '1', lineId: id(6), useDefault: false,
      expectedDefaultVersion: '1', expectedSourceVersion: 'SENTETIK-lists:1', vatRateCode: '10', exemptionCode: null, withholdingRatioCode: null, reason: 'SENTETIK satır vergisi' })
    const first = vi.mocked(submitServiceOrder).mock.calls[0]; fireEvent.click(form.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(submitServiceOrder).toHaveBeenCalledTimes(2)); expect(vi.mocked(submitServiceOrder).mock.calls[1]).toEqual(first)
  })
  it('reports permission refusal and does not disclose a former successful order', async () => {
    vi.mocked(fetchServiceOrder).mockResolvedValue({ type: 'http', status: 403 })
    mount('detail'); await waitFor(() => expect(auth.report).toHaveBeenCalledWith(403, expect.anything()))
    expect(screen.queryByText(serviceFixture().documentNumber)).toBeNull()
  })
  it('discards a late list after changing companies', async () => {
    let finish!: (v: ViewOutcome<readonly ServiceOrder[]>) => void
    vi.mocked(fetchServiceOrders).mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
      .mockResolvedValue({ type: 'ok-single', data: [] })
    const view = mount(); await waitFor(() => expect(fetchServiceOrders).toHaveBeenCalledTimes(1))
    currentShell = { ...currentShell, committedContextRevision: 2, activeContext: { ...currentShell.activeContext!, companyId: id(99) } }
    view.refresh(); await waitFor(() => expect(fetchServiceOrders).toHaveBeenCalledTimes(2))
    await act(async () => finish({ type: 'ok-single', data: [serviceFixture()] }))
    expect(screen.queryByText(serviceFixture().documentNumber)).toBeNull()
  })
})

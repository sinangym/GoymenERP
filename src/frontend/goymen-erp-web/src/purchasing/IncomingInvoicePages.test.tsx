/** Synthetic UI sources only; runtime/real HTTP/SQL/browser acceptance remains NOT_RUN. */
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { initialShellState, type ShellState } from '../shell/shellState'
import type { ViewOutcome } from '../shell/uiState'
import { IncomingInvoiceDetailPage, IncomingInvoiceListPage } from './IncomingInvoicePages'
import { fetchIncomingInvoice, fetchIncomingInvoices, fetchInvoiceReceipts, fetchInvoiceAdditionalCosts, submitInvoiceCommand } from './incomingInvoiceApi'
import { incomingInvoiceFixture, invoiceReceiptFixture, invoiceTestId as id } from './incomingInvoiceFixtures.testSupport'
import { INCOMING_INVOICE_PROBLEM_TEXT, INCOMING_INVOICE_TEXT as T } from './incomingInvoiceText'
import { PURCHASE_KG_PRICE_UNAVAILABLE_TEXT } from './purchaseOrderDetailText'
import { currencyShown } from '../finance/currencyCodes'
import { resetAdjustmentAttemptsForTests } from './useReceiptAdjustmentAttempt'
import type { InvoicePage } from './incomingInvoiceContract'
let currentShell: ShellState
const auth = vi.hoisted(() => ({ capture: vi.fn(() => ({ synthetic: true })), report: vi.fn() }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => currentShell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => auth }))
vi.mock('./incomingInvoiceApi', () => ({ fetchIncomingInvoice: vi.fn(), fetchIncomingInvoices: vi.fn(), fetchInvoiceReceipts: vi.fn(), fetchInvoiceAdditionalCosts: vi.fn(), submitInvoiceCommand: vi.fn() }))
const clients: QueryClient[] = []
function mount(detail = false) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, refetchOnWindowFocus: false } } }); clients.push(client)
  const content = () => <QueryClientProvider client={client}><MemoryRouter initialEntries={[detail ? `/satinalma/faturalar/${id(1)}` : '/satinalma/faturalar']}>
    <Routes><Route path="/satinalma/faturalar" element={<IncomingInvoiceListPage />} />
      <Route path="/satinalma/faturalar/:invoiceId" element={<IncomingInvoiceDetailPage />} /></Routes>
  </MemoryRouter></QueryClientProvider>
  const result = render(content())
  return { ...result, refresh: () => result.rerender(content()) }
}
beforeEach(() => {
  vi.clearAllMocks(); resetAdjustmentAttemptsForTests()
  currentShell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
    activeContext: { userId: 'synthetic-actor', companyId: id(2), scopeMode: 'single', capabilityGeneration: 'g1',
      capabilities: ['purchasing.invoices.list', 'purchasing.invoices.detail', 'purchasing.invoices.match'] } }
  vi.mocked(fetchIncomingInvoices).mockResolvedValue({ type: 'ok-single', data: { pageIndex: 0, hasNext: false, items: [incomingInvoiceFixture()] } })
  vi.mocked(fetchIncomingInvoice).mockResolvedValue({ type: 'ok-single', data: incomingInvoiceFixture() })
  vi.mocked(fetchInvoiceReceipts).mockResolvedValue({ type: 'ok-single', data: invoiceReceiptFixture() })
  vi.mocked(submitInvoiceCommand).mockResolvedValue({ type: 'unknown', reason: 'malformed' })
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()); resetAdjustmentAttemptsForTests() })
describe('Incoming invoice workspace (synthetic, NOT_ENV)', () => {
  it('submits a partial expense as money, keeps official quantities intact and retries the exact expense selection and key', async () => {
    const existing = incomingInvoiceFixture(), line = { ...existing.lines[0], productId: '00000000-0000-0000-0000-000000000000', itemType: 'Expense', linePurpose: 'expense' }
    const invoice = { ...existing, lines: [line] }
    vi.mocked(fetchIncomingInvoice).mockResolvedValue({ type: 'ok-single', data: invoice })
    vi.mocked(fetchInvoiceAdditionalCosts).mockResolvedValue({ type: 'ok-single', data: { invoiceId: invoice.id, invoiceLineId: line.id,
      invoiceVersion: invoice.version, currencyId: invoice.currencyId, pageIndex: 0, hasNext: false,
      items: [{ costId: id(90), purchaseOrderId: id(91), purchaseOrderNumber: 'SENTETIK-EXP-1', description: 'SENTETIK taşıma', expenseTypeId: null,
        expenseTypeName: null, businessDate: '2026-11-17', netAmount: '10000', availableNetAmount: '4000', vatPercent: line.vatPercent }] } })
    mount(true); await screen.findByText(/SENTETIK taşıma/)
    expect(fetchInvoiceReceipts).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('checkbox', { name: T.select }))
    fireEvent.change(screen.getByLabelText(T.expenseShare), { target: { value: '4000,00' } })
    fireEvent.click(screen.getByRole('button', { name: T.match })); await screen.findByText(T.unknown)
    const first = vi.mocked(submitInvoiceCommand).mock.calls[0]
    expect(first[2]).toEqual({ action: 'matching', request: { expectedVersion: '1', receipts: [], additionalCosts: [
      { invoiceLineId: line.id, purchaseOrderId: id(91), costId: id(90), netAmount: '4000' }] } })
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(submitInvoiceCommand).toHaveBeenCalledTimes(2))
    expect(vi.mocked(submitInvoiceCommand).mock.calls[1]).toEqual(first)
  })
  it('searches by the official number and opens the actual record without a technical-id field', async () => {
    mount()
    expect((await screen.findByRole('link', { name: 'SENTETIK-2026-0001' })).getAttribute('href')).toBe(`/satinalma/faturalar/${id(1)}`)
    fireEvent.change(screen.getByLabelText(T.number), { target: { value: 'SENTETIK-2026' } })
    fireEvent.click(screen.getByRole('button', { name: T.search }))
    await waitFor(() => expect(fetchIncomingInvoices).toHaveBeenLastCalledWith(id(2), 'SENTETIK-2026', 0, expect.any(AbortSignal)))
    expect(screen.queryByDisplayValue(id(1))).toBeNull()
  })
  it('shows no invoice data and reports a permission refusal to the current auth boundary', async () => {
    vi.mocked(fetchIncomingInvoices).mockResolvedValue({ type: 'http', status: 403 })
    mount()
    await waitFor(() => expect(auth.report).toHaveBeenCalledWith(403, expect.anything()))
    expect(screen.queryByText('SENTETIK-2026-0001')).toBeNull()
  })
  it('keeps a malformed write unresolved and retries the exact same selection and key', async () => {
    mount(true)
    await screen.findByText(/SENTETIK-PO-01/)
    fireEvent.click(screen.getByRole('checkbox', { name: T.select }))
    fireEvent.change(screen.getByLabelText(T.share), { target: { value: '2,000000' } })
    fireEvent.click(screen.getByRole('button', { name: T.match }))
    await screen.findByText(T.unknown)
    const first = vi.mocked(submitInvoiceCommand).mock.calls[0]
    expect(first[2]).toEqual({ action: 'matching', request: { expectedVersion: '1', receipts: [
      { invoiceLineId: id(8), purchaseOrderId: id(11), acceptanceId: id(13), quantity: '2' }] } })
    expect(screen.getByRole('button', { name: T.match }).matches(':disabled')).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(submitInvoiceCommand).toHaveBeenCalledTimes(2))
    expect(vi.mocked(submitInvoiceCommand).mock.calls[1]).toEqual(first)
  })
  it('does not calculate or submit a match when one of the receipt quantities is missing', async () => {
    mount(true); await screen.findByText(/SENTETIK-PO-01/)
    fireEvent.click(screen.getByRole('checkbox', { name: T.select }))
    fireEvent.click(screen.getByRole('button', { name: T.match }))
    expect(await screen.findByText(T.invalidSelection)).not.toBeNull()
    expect(submitInvoiceCommand).not.toHaveBeenCalled()
  })
  it('AK-3b: shows the line and document kg price as the server sent them, in the invoice currency, with the reasons in words', async () => {
    const existing = incomingInvoiceFixture()
    const invoice = { ...existing, kgPrice: { theoreticalKgPrice: '20.25', theoreticalUnavailable: null, realKgPrice: '19.99', realUnavailable: null } as const,
      lines: [{ ...existing.lines[0], kgPrice: { theoreticalKgPrice: '20.25', theoreticalUnavailable: null, realKgPrice: null, realUnavailable: 'WeighedKgUnavailable' } as const }] }
    vi.mocked(fetchIncomingInvoice).mockResolvedValue({ type: 'ok-single', data: invoice })
    mount(true)
    const document = await screen.findByTestId('incoming-invoice-kg-price')
    const currency = currencyShown(invoice.currencyId, null)
    expect(within(document).getByRole('heading').textContent).toBe(T.kgPriceHeading)
    expect(within(document).getAllByRole('definition').map((cell) => cell.textContent))
      .toEqual([T.kgPricePerKg('20.25', currency), T.kgPricePerKg('19.99', currency)])
    expect(within(screen.getByTestId('incoming-invoice-line-1-kg-price')).getAllByRole('definition').map((cell) => cell.textContent))
      .toEqual([T.kgPricePerKg('20.25', currency), PURCHASE_KG_PRICE_UNAVAILABLE_TEXT.WeighedKgUnavailable])
  })
  it('AK-3b: a document hidden by a service or expense line shows the reason, never a figure; no kg price member draws nothing', async () => {
    const hidden = { theoreticalKgPrice: null, theoreticalUnavailable: 'NotAllLinesHaveKg', realKgPrice: null, realUnavailable: 'NotAllLinesHaveKg' } as const
    vi.mocked(fetchIncomingInvoice).mockResolvedValue({ type: 'ok-single', data: { ...incomingInvoiceFixture(), kgPrice: hidden } })
    const first = mount(true)
    expect(within(await screen.findByTestId('incoming-invoice-kg-price')).getAllByRole('definition').map((cell) => cell.textContent))
      .toEqual([PURCHASE_KG_PRICE_UNAVAILABLE_TEXT.NotAllLinesHaveKg, PURCHASE_KG_PRICE_UNAVAILABLE_TEXT.NotAllLinesHaveKg])
    expect(screen.queryByTestId('incoming-invoice-line-1-kg-price')).toBeNull()
    first.unmount()
    vi.mocked(fetchIncomingInvoice).mockResolvedValue({ type: 'ok-single', data: incomingInvoiceFixture() })
    mount(true); await screen.findByText(/SENTETIK-PO-01/)
    expect(screen.queryByTestId('incoming-invoice-kg-price')).toBeNull()
  })
  it.each([
    ['malformed (decoder fail-closed)', { type: 'malformed' }, 'malformed-response'],
    ['503', { type: 'http', status: 503 }, 'service-unavailable'],
    ['409', { type: 'http', status: 409 }, 'conflict'],
    ['404', { type: 'http', status: 404 }, 'not-found'],
  ] as const)('AK-3b: a %s invoice read draws its state and no kg price', async (_, outcome, state) => {
    vi.mocked(fetchIncomingInvoice).mockResolvedValue(outcome)
    mount(true)
    await screen.findByText((_content, element) => element?.getAttribute('data-state') === state)
    expect(screen.queryByTestId('incoming-invoice-kg-price')).toBeNull()
  })
  it('AK-3b: names the whole-piece matching refusal (409) in user words and keeps the conflict state', async () => {
    vi.mocked(submitInvoiceCommand).mockResolvedValue({ type: 'failed', status: 409, code: 'purchasing_invoice_piece_quantity_invalid' })
    mount(true); await screen.findByText(/SENTETIK-PO-01/)
    fireEvent.click(screen.getByRole('checkbox', { name: T.select }))
    fireEvent.change(screen.getByLabelText(T.share), { target: { value: '1,5' } })
    fireEvent.click(screen.getByRole('button', { name: T.match }))
    expect(await screen.findByText(INCOMING_INVOICE_PROBLEM_TEXT.purchasing_invoice_piece_quantity_invalid)).not.toBeNull()
    expect(screen.queryByText(T.conflict)).toBeNull()
    expect(vi.mocked(submitInvoiceCommand).mock.calls[0][2]).toEqual({ action: 'matching', request: { expectedVersion: '1', receipts: [
      { invoiceLineId: id(8), purchaseOrderId: id(11), acceptanceId: id(13), quantity: '1.5' }] } })
  })
  it('does not reveal the former company when its late read completes after a context switch', async () => {
    let finish!: (answer: ViewOutcome<InvoicePage>) => void
    vi.mocked(fetchIncomingInvoices).mockImplementationOnce(() => new Promise((resolve) => { finish = resolve }))
      .mockResolvedValue({ type: 'ok-single', data: { pageIndex: 0, hasNext: false, items: [] } })
    const view = mount()
    await waitFor(() => expect(fetchIncomingInvoices).toHaveBeenCalledTimes(1))
    currentShell = { ...currentShell, committedContextRevision: 2, activeContext: { ...currentShell.activeContext!, companyId: id(99) } }
    view.refresh()
    await waitFor(() => expect(fetchIncomingInvoices).toHaveBeenCalledTimes(2))
    await act(async () => finish({ type: 'ok-single', data: { pageIndex: 0, hasNext: false, items: [incomingInvoiceFixture()] } }))
    expect(screen.queryByText('SENTETIK-2026-0001')).toBeNull()
  })
})

/** Synthetic UI sources; real HTTP/SQL/browser runtime acceptance NOT_RUN. */
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { initialShellState, type ShellState } from '../shell/shellState'
import { InvoiceImportAdmissionPanel } from './InvoiceImportAdmissionPanel'
import { fetchImportMappingReferences, fetchImportProducts, resolveImportSupplier, recordInvoiceImport } from './invoiceImportAdmissionApi'
import { fetchPurchasingSupplierOptions } from './api'
import { admissionFacts, admissionReferences, admissionProducts, admissionSupplier } from './invoiceImportAdmissionFixtures.testSupport'
import { importRequestFixture, importTestId as id } from './invoiceImportFixtures.testSupport'
import { resetAdjustmentAttemptsForTests } from './useReceiptAdjustmentAttempt'
import { INVOICE_IMPORT_TEXT as T } from './invoiceImportText'

let shell: ShellState
const auth = vi.hoisted(() => ({ capture: vi.fn(() => ({ synthetic: true })), report: vi.fn() }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => auth }))
vi.mock('./invoiceImportAdmissionApi', () => ({ fetchImportMappingReferences: vi.fn(), fetchImportProducts: vi.fn(), resolveImportSupplier: vi.fn(), recordInvoiceImport: vi.fn() }))
vi.mock('./api', () => ({ fetchPurchasingSupplierOptions: vi.fn() }))
const clients: QueryClient[] = []
function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, refetchOnWindowFocus: false } } }); clients.push(client)
  return render(<MemoryRouter><QueryClientProvider client={client}><InvoiceImportAdmissionPanel binding={{ company: id(2), namespace: 'synthetic-import:actor:company', entry: 1 }}
    id={id(1)} source={importRequestFixture()} invoice={admissionFacts()} /></QueryClientProvider></MemoryRouter>)
}
async function map() {
  fireEvent.click(screen.getByRole('button', { name: T.mapAndRecord }))
  await screen.findByRole('option', { name: 'SENTETIK tedarikçi' })
  fireEvent.change(screen.getByLabelText(T.supplierSelection), { target: { value: `${id(14)}:${id(15)}` } })
  await waitFor(() => expect(resolveImportSupplier).toHaveBeenCalledTimes(1))
  fireEvent.change(screen.getByLabelText(T.currencySelection), { target: { value: id(12) } })
  fireEvent.change(screen.getByLabelText(T.lineKind), { target: { value: 'product' } })
  fireEvent.change(screen.getByLabelText(T.productSearch), { target: { value: 'SYN' } })
  fireEvent.click(screen.getAllByRole('button', { name: T.find })[1])
  await screen.findByRole('option', { name: 'SYN · SENTETIK ürün · KGM' })
  fireEvent.change(screen.getByLabelText(T.productSelection), { target: { value: id(16) } })
  fireEvent.change(screen.getByLabelText(T.purchasePurpose), { target: { value: 'resale' } })
  await waitFor(() => expect((screen.getByRole('button', { name: T.recordInvoice }) as HTMLButtonElement).disabled).toBe(false))
}
beforeEach(() => {
  vi.clearAllMocks(); resetAdjustmentAttemptsForTests()
  shell = { ...initialShellState, phase: 'idle', activeContext: { companyId: id(2), userId: 'synthetic-actor', scopeMode: 'single', capabilityGeneration: 'g1',
    capabilities: ['purchasing.invoices.import'] } }
  vi.mocked(fetchImportMappingReferences).mockResolvedValue({ type: 'ok-single', data: admissionReferences() })
  vi.mocked(fetchImportProducts).mockResolvedValue({ type: 'ok-single', data: admissionProducts() })
  vi.mocked(resolveImportSupplier).mockResolvedValue({ type: 'ok-single', data: admissionSupplier() })
  vi.mocked(fetchPurchasingSupplierOptions).mockResolvedValue({ type: 'ok-single', data: { items: [{ supplierPartyId: id(14), companyAccountId: id(15), label: 'SENTETIK tedarikçi' }], nextCursor: null } })
  vi.mocked(recordInvoiceImport).mockResolvedValue({ type: 'unknown', reason: 'network-error' })
})
afterEach(() => { cleanup(); clients.splice(0).forEach(x => x.clear()); resetAdjustmentAttemptsForTests() })
describe('actual-reference invoice admission workspace', () => {
  it('requires the import control and an explicit open before reading source references', () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [] } }; mount()
    expect(screen.queryByRole('button', { name: T.mapAndRecord })).toBeNull(); expect(fetchImportMappingReferences).not.toHaveBeenCalled()
  })
  it('selects actual supplier versions and product identities without GUID or JSON inputs or amount recomputation', async () => {
    mount(); expect(fetchImportMappingReferences).not.toHaveBeenCalled(); await map()
    expect(resolveImportSupplier).toHaveBeenCalledWith(id(2), id(1), importRequestFixture(), id(14), id(15), expect.any(AbortSignal))
    expect((screen.getByLabelText(T.amount) as HTMLInputElement).readOnly).toBe(true)
    expect(screen.queryByRole('textbox', { name: /guid|json|kimlik/i })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: T.recordInvoice })); await screen.findByText(T.unknown)
    expect(recordInvoiceImport).toHaveBeenCalledWith(id(2), id(1), expect.objectContaining({ request: expect.objectContaining({ expectedPartyVersion: '4', expectedAccountVersion: '5',
      lines: [expect.objectContaining({ productId: id(16), expectedProductVersion: '6', linePurpose: 'resale' })],
      installments: [expect.objectContaining({ amount: '12', functionalAmount: null })] }) }), expect.any(String))
  })
  it('freezes the original request and retries with the same key after an unknown response', async () => {
    const view = mount(); await map(); fireEvent.click(screen.getByRole('button', { name: T.recordInvoice })); await screen.findByText(T.unknown)
    const first = vi.mocked(recordInvoiceImport).mock.calls[0]
    expect(screen.getByLabelText(T.currencySelection).closest('fieldset')?.disabled).toBe(true)
    view.unmount(); mount(); fireEvent.click(screen.getByRole('button', { name: T.mapAndRecord }))
    fireEvent.click(await screen.findByRole('button', { name: T.retry })); await waitFor(() => expect(recordInvoiceImport).toHaveBeenCalledTimes(2))
    expect(vi.mocked(recordInvoiceImport).mock.calls[1]).toEqual(first)
  })
  it.each([403, 404, 409, 503])('renders mapping source status %s and refuses unavailable mapping', async status => {
    vi.mocked(fetchImportMappingReferences).mockResolvedValue({ type: 'http', status }); mount()
    fireEvent.click(screen.getByRole('button', { name: T.mapAndRecord }))
    const state: Record<number, string> = { 403: 'forbidden', 404: 'not-found', 409: 'conflict', 503: 'service-unavailable' }
    await waitFor(() => expect(document.querySelector(`[data-state="${state[status]}"]`)).not.toBeNull())
    expect(screen.queryByRole('button', { name: T.recordInvoice })).toBeNull(); expect(recordInvoiceImport).not.toHaveBeenCalled()
    if (status === 403) expect(auth.report).toHaveBeenCalledWith(403, expect.anything())
  })
  it.each(['offline', 'malformed'] as const)('renders %s without converting missing source to an empty successful list', async kind => {
    vi.mocked(fetchImportMappingReferences).mockResolvedValue({ type: kind }); mount(); fireEvent.click(screen.getByRole('button', { name: T.mapAndRecord }))
    await waitFor(() => expect(document.querySelector(`[data-state="${kind === 'malformed' ? 'malformed-response' : kind}"]`)).not.toBeNull()); expect(recordInvoiceImport).not.toHaveBeenCalled()
  })
})

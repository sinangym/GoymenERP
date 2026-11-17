/** Synthetic contract/UI sources; runtime/HTTP/SQL/browser acceptance NOT_RUN. */
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { initialShellState, type ShellState } from '../shell/shellState'
import { fetchDocuments } from '../documents/api'
import { InvoiceImportPanel } from './InvoiceImportPanel'
import { reserveInvoiceImport, previewInvoiceImport, fetchInvoiceImports } from './invoiceImportApi'
import { importDocumentsFixture, importPreviewFixture, importReservationFixture, importTestId as id } from './invoiceImportFixtures.testSupport'
import { INVOICE_IMPORT_TEXT as T } from './invoiceImportText'
import { resetAdjustmentAttemptsForTests } from './useReceiptAdjustmentAttempt'
import type { ViewOutcome } from '../shell/uiState'
import type { ImportPreview } from './invoiceImportContract'
let shell: ShellState
const auth = vi.hoisted(() => ({ capture: vi.fn(() => ({ synthetic: true })), report: vi.fn() }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => auth }))
vi.mock('../documents/DocumentsPanel', () => ({ DocumentsPanel: () => null }))
vi.mock('../documents/api', async importOriginal => ({ ...await importOriginal<typeof import('../documents/api')>(), fetchDocuments: vi.fn() }))
vi.mock('./invoiceImportApi', () => ({ reserveInvoiceImport: vi.fn(), previewInvoiceImport: vi.fn(), fetchInvoiceImports: vi.fn() }))
const clients: QueryClient[] = []
function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, refetchOnWindowFocus: false } } }); clients.push(client)
  const content = (company = id(2)) => <QueryClientProvider client={client}><InvoiceImportPanel binding={{ company, namespace: `test:${company}`, entry: 1 }} /></QueryClientProvider>
  const result = render(content())
  return { ...result, switchCompany: () => result.rerender(content(id(99))) }
}
async function chooseSource() {
  fireEvent.click(screen.getByRole('button', { name: T.start }))
  await screen.findByRole('option', { name: 'sentetik.xml — Sürüm 2' })
  fireEvent.change(screen.getByLabelText(T.source), { target: { value: `${id(3)}:2:${'a'.repeat(64)}` } })
}
beforeEach(() => {
  vi.clearAllMocks(); resetAdjustmentAttemptsForTests()
  shell = { ...initialShellState, phase: 'idle', activeContext: { companyId: id(2), userId: 'synthetic-actor', scopeMode: 'single',
    capabilityGeneration: 'g1', capabilities: ['purchasing.invoices.import'] } }
  vi.mocked(reserveInvoiceImport).mockResolvedValue({ type: 'applied', response: importReservationFixture() })
  vi.mocked(fetchDocuments).mockResolvedValue({ type: 'ok-single', data: importDocumentsFixture() })
  vi.mocked(previewInvoiceImport).mockResolvedValue({ type: 'ok-single', data: importPreviewFixture() })
  vi.mocked(fetchInvoiceImports).mockResolvedValue({ type: 'ok-single', data: { companyId: id(2), pageIndex: 0, hasNext: false, items: [] } })
})
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); resetAdjustmentAttemptsForTests() })
describe('Purchase invoice source workspace (synthetic, NOT_ENV)', () => {
  it('does not fetch or reserve without the import control', () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: ['purchasing.invoices.list'] } }; mount()
    expect(screen.queryByRole('button', { name: T.start })).toBeNull()
    expect(reserveInvoiceImport).not.toHaveBeenCalled(); expect(fetchDocuments).not.toHaveBeenCalled()
    expect(fetchInvoiceImports).not.toHaveBeenCalled()
  })
  it('reopens an actual company reservation without a technical-id input or another reservation write', async () => {
    vi.mocked(fetchInvoiceImports).mockResolvedValue({ type: 'ok-single', data: { companyId: id(2), pageIndex: 0, hasNext: false,
      items: [importReservationFixture().reservation] } })
    mount(); fireEvent.click(await screen.findByRole('button', { name: T.open }))
    await screen.findByRole('option', { name: 'sentetik.xml — Sürüm 2' })
    expect(reserveInvoiceImport).not.toHaveBeenCalled()
    expect(fetchDocuments).toHaveBeenCalledWith('purchasing.invoice-import', id(1), id(2), expect.any(AbortSignal))
  })
  it('uses a real listed clean document version and displays source decimals unchanged', async () => {
    mount(); await chooseSource(); fireEvent.click(screen.getByRole('button', { name: T.preview }))
    await screen.findByText('Sentetik kaynak malzeme')
    expect(screen.getByText('0.123456789123456789')).not.toBeNull()
    expect(screen.getByText('0007')).not.toBeNull()
    expect(screen.getByText('Kaynak masrafı')).not.toBeNull()
    expect(previewInvoiceImport).toHaveBeenCalledWith(id(2), id(1), expect.objectContaining({ documentId: id(3), documentVersion: 2,
      documentSha256: 'a'.repeat(64) }), expect.any(AbortSignal))
  })
  it('never offers a document whose security scan is incomplete', async () => {
    const docs = importDocumentsFixture()
    vi.mocked(fetchDocuments).mockResolvedValue({ type: 'ok-single', data: { ...docs, items: [{ ...docs.items[0], scanState: 'NotScanned', downloadable: false }] } })
    mount(); fireEvent.click(screen.getByRole('button', { name: T.start })); await screen.findByText(T.cleanRequired)
    expect(screen.queryByRole('option', { name: 'sentetik.xml — Sürüm 2' })).toBeNull()
    expect(previewInvoiceImport).not.toHaveBeenCalled()
  })
  it('retries an unknown reservation with exactly the same company and key', async () => {
    vi.mocked(reserveInvoiceImport).mockResolvedValue({ type: 'unknown', reason: 'malformed' })
    mount(); fireEvent.click(screen.getByRole('button', { name: T.start })); await screen.findByText(T.unknown)
    const first = vi.mocked(reserveInvoiceImport).mock.calls[0]
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(reserveInvoiceImport).toHaveBeenCalledTimes(2))
    expect(vi.mocked(reserveInvoiceImport).mock.calls[1]).toEqual(first)
  })
  it.each([403, 404, 409, 503])('shows source-read %s without stale invoice facts', async status => {
    vi.mocked(previewInvoiceImport).mockResolvedValue({ type: 'http', status })
    mount(); await chooseSource(); fireEvent.click(screen.getByRole('button', { name: T.preview }))
    const states: Record<number, string> = { 403: 'forbidden', 404: 'not-found', 409: 'conflict', 503: 'service-unavailable' }
    await waitFor(() => expect(document.querySelector(`[data-state="${states[status]}"]`)).not.toBeNull())
    expect(screen.queryByText('Sentetik kaynak malzeme')).toBeNull()
    if (status === 403) expect(auth.report).toHaveBeenCalledWith(403, expect.anything())
  })
  it('discards the old company source when its delayed preview completes', async () => {
    let finish!: (answer: ViewOutcome<ImportPreview>) => void
    vi.mocked(previewInvoiceImport).mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
    const view = mount(); await chooseSource(); fireEvent.click(screen.getByRole('button', { name: T.preview }))
    await waitFor(() => expect(previewInvoiceImport).toHaveBeenCalledTimes(1))
    view.switchCompany(); await act(async () => finish({ type: 'ok-single', data: importPreviewFixture() }))
    expect(screen.queryByText('Sentetik kaynak malzeme')).toBeNull()
  })
})

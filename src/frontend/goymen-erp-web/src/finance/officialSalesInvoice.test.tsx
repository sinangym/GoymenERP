import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { OfficialSalesInvoicesPage } from './OfficialSalesInvoicesPage'
import { continueOfficialInvoice, decodeOfficialInvoice, decodeOfficialInvoicePage, type OfficialInvoiceCreateAttempt } from './officialSalesInvoiceApi'

const Company = 'ef361111-1111-4111-8111-111111111111'
const Invoice = 'ef362222-2222-4222-8222-222222222222'
const Currency = 'ef363333-3333-4333-8333-333333333333'
const Party = 'ef364444-4444-4444-8444-444444444444'
const Pdf = 'ef365555-5555-4555-8555-555555555555'
const row = (patch: Record<string, unknown> = {}) => ({ invoiceId: Invoice, companyId: Company, officialNo: 'SENTETIK-1', ettn: Invoice,
  customerPartyId: Party, currencyId: Currency, invoiceDate: '2026-11-17', payableAmount: '10.0001', payableFunctionalAmount: '10.0001',
  taxBaseFunctionalAmount: '10.0001', vatFunctionalAmount: '0', withholdingFunctionalAmount: '0', pdfDocumentId: Pdf, pdfDocumentVersionNo: 1,
  pdfSha256: 'a'.repeat(64), version: '1', status: 'PendingApproval', recordedBy: 'SENTETIK-maker', recordedAtUtc: '2026-11-17T10:00:00Z',
  approvedBy: null, approvedAtUtc: null, rejectedBy: null, rejectedAtUtc: null, rejectionReason: null, withdrawnBy: null, withdrawnAtUtc: null, ...patch })
const json = (body: unknown, status = 200, extra: Record<string, string> = {}) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...extra } })
const replayHeaders = { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK' }
const result = (value: unknown) => ({ outcome: 'Succeeded', resource: value, isReplay: false, originalCorrelationId: 'SENTETIK', issues: [], existingOfficialNo: null })
const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({})), report: vi.fn() }))
const shell = vi.hoisted(() => ({ generation: 0, capabilities: ['finance.official-sales-invoices.approve'],
  state: { activeContext: { companyId: 'ef361111-1111-4111-8111-111111111111', scopeMode: 'single', userId: 'SENTETIK-checker' }, routeEntryGeneration: 0 } }))
const documents = vi.hoisted(() => ({ upload: vi.fn(), list: vi.fn() }))
const parties = vi.hoisted(() => ({ detail: vi.fn() }))
vi.mock('../cari/detailApi', () => ({ fetchCounterpartyDetail: parties.detail }))
vi.mock('../documents/api', () => ({ uploadDocument: documents.upload, fetchDocuments: documents.list }))
vi.mock('../documents/DocumentsPanel', () => ({ DocumentsPanel: () => <p>SENTETIK belge paneli</p> }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell.state }))
vi.mock('../shell/shellState', async original => ({ ...await original<typeof import('../shell/shellState')>(),
  contextKey: () => ['SENTETIK', String(shell.generation)], contextKeyString: (value: readonly string[]) => value.join(':'),
  hasCapability: (_state: unknown, capability: string) => shell.capabilities.includes(capability),
}))
const mount = () => render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><OfficialSalesInvoicesPage /></QueryClientProvider>)
beforeEach(() => { shell.generation++; shell.state.activeContext.userId = 'SENTETIK-checker'; documents.upload.mockReset(); documents.list.mockReset(); boundary.report.mockClear()
  parties.detail.mockReset(); parties.detail.mockResolvedValue({ type: 'ok-single', data: { id: Party, code: 'SENTETIK-CARI', legalName: 'SENTETIK unvan', account: { companyId: Company } } }) })
afterEach(() => { cleanup(); vi.unstubAllGlobals() })
const attempt = (): OfficialInvoiceCreateAttempt => ({ company: Company, actor: 'SENTETIK-maker', input: {
  officialNo: 'SENTETIK-1', ettn: Invoice, customerPartyId: Party, currencyId: Currency, invoiceDate: '2026-11-17',
  payableAmount: '10.0001', payableFunctionalAmount: '10.0001', taxBaseFunctionalAmount: '10.0001', vatFunctionalAmount: '0', withholdingFunctionalAmount: '0' },
  file: new File(['SENTETIK-PDF'], 'SENTETIK.pdf', { type: 'application/pdf' }), reserveKey: 'SENTETIK-reserve', uploadKey: 'SENTETIK-upload', recordKey: 'SENTETIK-record',
  invoiceId: null, documentId: null, phase: 'reserve' })

describe('official PDF first sales invoice', () => {
  it('keeps exact source amounts and legacy unknown date while rejecting numeric money foreign company or inconsistent decisions', () => {
    expect(decodeOfficialInvoice(row(), Company)?.payableAmount).toBe('10.0001')
    expect(decodeOfficialInvoice(row({ invoiceDate: null }), Company)?.invoiceDate).toBeNull()
    expect(decodeOfficialInvoice(row({ companyId: Party }), Company)).toBeNull()
    expect(decodeOfficialInvoice(row({ payableAmount: 10.0001 }), Company)).toBeNull()
    expect(decodeOfficialInvoice(row({ status: 'Approved', approvedBy: 'SENTETIK-maker', approvedAtUtc: '2026-11-17T11:00:00Z' }), Company)).toBeNull()
    expect(decodeOfficialInvoicePage({ companyId: Company, items: [row(), row()], nextCursor: null }, Company)).toBeNull()
    expect(decodeOfficialInvoice(row({ sourceDifferences: [{ importBatchId: Party, sourceDocumentId: Invoice, officialNo: 'SENTETIK-1', ettn: Invoice,
      fields: ['invoiceDate'], invoiceDate: null, payableFunctionalAmount: null, winner: 'manual' }] }), Company)?.sourceDifferences).toHaveLength(1)
  })
  it('reserves once uploads the same PDF only once waits for scan and retries lost final record with exact identity and source bytes', async () => {
    const posts: { path: string; init: RequestInit }[] = []; let records = 0
    vi.stubGlobal('fetch', vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
      posts.push({ path: String(url), init: init! })
      if (String(url).endsWith('/reservations')) {
        const { existingOfficialNo: _omitted, ...reservation } = result({ invoiceId: Invoice, companyId: Company, reservedBy: 'SENTETIK-maker', reservedAtUtc: '2026-11-17T10:00:00Z' })
        return json(reservation)
      }
      records++; return records === 1 ? json({}, 503) : json({ ...result(row()), isReplay: true }, 200, replayHeaders)
    }))
    documents.upload.mockResolvedValue({ type: 'stored', upload: { document: { id: Pdf, contentType: 'application/pdf' } }, replayed: false })
    const pending = { id: Pdf, contentType: 'application/pdf', downloadable: false, scanState: 'NotScanned' }
    documents.list.mockResolvedValueOnce({ type: 'ok-single', data: { items: [pending] } })
      .mockResolvedValueOnce({ type: 'ok-single', data: { items: [{ ...pending, downloadable: true, scanState: 'Clean' }] } })
    const source = attempt()
    expect((await continueOfficialInvoice(source)).type).toBe('waiting-scan')
    expect(source.invoiceId).toBe(Invoice); expect(source.documentId).toBe(Pdf); expect(records).toBe(0)
    expect((await continueOfficialInvoice(source)).type).toBe('unknown')
    expect((await continueOfficialInvoice(source)).type).toBe('applied')
    expect(documents.upload).toHaveBeenCalledTimes(1); expect(posts.filter(x => x.path.endsWith('/reservations'))).toHaveLength(1)
    const final = posts.filter(x => !x.path.endsWith('/reservations')); expect(final[0].init.body).toBe(final[1].init.body)
    expect(new Headers(final[1].init.headers).get('Idempotency-Key')).toBe('SENTETIK-record')
    expect(JSON.parse(String(final[1].init.body)).payableAmount).toBe('10.0001')
  })
  it('does not record after document read denial or a malformed scan answer', async () => {
    const source = attempt(); source.phase = 'scan'; source.invoiceId = Invoice; source.documentId = Pdf
    const fetch = vi.fn(); vi.stubGlobal('fetch', fetch)
    documents.list.mockResolvedValueOnce({ type: 'http', status: 403, body: {} }).mockResolvedValueOnce({ type: 'malformed' })
    expect(await continueOfficialInvoice(source)).toMatchObject({ type: 'refused', status: 403 })
    expect((await continueOfficialInvoice(source)).type).toBe('unknown'); expect(fetch).not.toHaveBeenCalled()
  })
  it('prevents self approval and preserves an unknown separate approval across remount and transport denial', async () => {
    let status = 503; const writes: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
      if (String(url).endsWith('/currency-candidates')) return json({ companyId: Company, items: [{ currencyId: Currency, code: 'TRY', displayLabel: 'SENTETIK TL' }] })
      if (init?.method !== 'POST') return json({ companyId: Company, items: [row()], nextCursor: null })
      writes.push(init)
      return status === 200 ? json(result(row({ status: 'Approved', version: '2', approvedBy: 'SENTETIK-checker', approvedAtUtc: '2026-11-17T11:00:00Z' }))) : json({}, status)
    }))
    shell.state.activeContext.userId = 'SENTETIK-maker'; const own = mount(); fireEvent.click(await screen.findByRole('button', { name: 'Seçin' }))
    await screen.findByText('Kendi kaydınızı onaylayamaz veya reddedemezsiniz.'); expect(screen.queryByRole('button', { name: 'Onayla' })).toBeNull(); own.unmount()
    shell.state.activeContext.userId = 'SENTETIK-checker'; const first = mount(); fireEvent.click(await screen.findByRole('button', { name: 'Seçin' }))
    await waitFor(() => expect((screen.getByRole('button', { name: 'Onayla' }) as HTMLButtonElement).disabled).toBe(false)); fireEvent.click(screen.getByRole('button', { name: 'Onayla' }))
    await screen.findByRole('button', { name: 'Aynı fatura işlemini sürdür' }); first.unmount(); status = 403
    const second = mount(); fireEvent.click(await screen.findByRole('button', { name: 'Aynı fatura işlemini sürdür' }))
    await waitFor(() => { expect(writes).toHaveLength(2); expect((screen.getByRole('button', { name: 'Aynı fatura işlemini sürdür' }) as HTMLButtonElement).disabled).toBe(false) })
    second.unmount(); status = 200; mount(); fireEvent.click(await screen.findByRole('button', { name: 'Aynı fatura işlemini sürdür' })); await screen.findByText('Resmî fatura işlemi kaydedildi.')
    expect(writes).toHaveLength(3); expect(writes[0].body).toBe(writes[2].body)
    expect(new Headers(writes[0].headers).get('Idempotency-Key')).toBe(new Headers(writes[2].headers).get('Idempotency-Key'))
  })
  it('shows owner denial and never enables approval when customer identity cannot be read', async () => {
    parties.detail.mockResolvedValue({ type: 'http', status: 403, body: {} })
    const fetch = vi.fn(async (url: RequestInfo | URL, _init?: RequestInit) => String(url).endsWith('/currency-candidates')
      ? json({ companyId: Company, items: [{ currencyId: Currency, code: 'TRY', displayLabel: 'SENTETIK TL' }] })
      : json({ companyId: Company, items: [row()], nextCursor: null }))
    vi.stubGlobal('fetch', fetch); mount(); fireEvent.click(await screen.findByRole('button', { name: 'Seçin' }))
    await waitFor(() => expect(boundary.report).toHaveBeenCalledWith(403, expect.anything()))
    expect((screen.getByRole('button', { name: 'Onayla' }) as HTMLButtonElement).disabled).toBe(true)
    expect(fetch.mock.calls.every(([, init]) => (init as RequestInit | undefined)?.method !== 'POST')).toBe(true)
  })
})

import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useParams } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryLabelTracePage, parseLabelTraceTarget } from './InventoryLabelTracePage'
import { INVENTORY_LABEL_TRACE_ROUTE, inventoryLabelTracePath } from './capabilities'
import { LABEL_TRACE_INVALID_QUERY } from './labelTraceApi'
import type { InventoryLabelTrace, InventoryLabelTraceSearch } from './labelTraceContract'
import type { ViewOutcome } from '../shell/uiState'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const ROOT = '22222222-2222-4222-8222-222222222222'
const OUTPUT = '23232323-2323-4323-8323-232323232323'
const TRANSFORMATION = '33333333-3333-4333-8333-333333333333'
const ORDER = '44444444-4444-4444-8444-444444444444'
const SOURCE = '55555555-5555-4555-8555-555555555555'
const SUPPLIER = '66666666-6666-4666-8666-666666666666'
const CUSTOMER = '77777777-7777-4777-8777-777777777777'

const api = vi.hoisted(() => ({ trace: vi.fn(), search: vi.fn() }))
const scenario = vi.hoisted(() => ({
  companyId: '11111111-1111-4111-8111-111111111111', namespace: 'SENTETIK-a' as string | null,
  scopeMode: 'single' as 'single' | 'all', report: vi.fn(),
}))
vi.mock('./labelTraceApi', async (importOriginal) => ({
  ...await importOriginal<typeof import('./labelTraceApi')>(),
  fetchInventoryLabelTrace: api.trace,
  searchInventoryLabelTrace: api.search,
}))
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({ sessionEpoch: 1, routeEntryGeneration: 1,
    activeContext: { userId: 'actor-a', companyId: scenario.companyId, scopeMode: scenario.scopeMode, capabilities: [] } }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => scenario.namespace === null ? null : [scenario.namespace],
  contextKeyString: (key: readonly string[]) => key[0],
  hasCapability: () => true,
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ namespace: scenario.namespace }), report: scenario.report }),
}))

const TRACE: InventoryLabelTrace = {
  labelId: ROOT, labelCode: 'SYN-ROOT', direction: 'forward',
  labels: [{ labelId: ROOT, code: 'SYN-ROOT', depth: 0 }, { labelId: OUTPUT, code: 'SYN-OUT', depth: 1 }],
  links: [{
    transformationId: TRANSFORMATION, kind: 'Manufacturing', occurredAtUtc: '2026-11-02T10:00:00Z',
    sourceLabelId: ROOT, outputLabelId: OUTPUT, sourceQuantityKg: '100', outputQuantityKg: '80',
    productionOrderId: ORDER, consumedKg: '100', consumedShare: '1', shareStatus: 'Recorded',
  }],
  events: [
    {
      movementId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', lineNumber: 1, labelId: ROOT, step: 'SupplierReceipt',
      movementKind: 'Receipt', occurredAtUtc: '2026-11-01T08:00:00Z', sourceKind: 'purchasing.goods-receipt',
      sourceId: SOURCE, sourceLineId: null, direction: 'In', quantityKg: '100', partyId: SUPPLIER,
      supplierLabelNumber: 'SUP-0001',
    },
    {
      movementId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', lineNumber: 1, labelId: OUTPUT, step: 'Shipment',
      movementKind: 'Issue', occurredAtUtc: '2026-11-03T08:00:00Z', sourceKind: 'logistics.shipment',
      sourceId: SOURCE, sourceLineId: null, direction: 'Out', quantityKg: '80', partyId: CUSTOMER,
      supplierLabelNumber: null,
    },
  ],
  parties: [
    { partyId: SUPPLIER, role: 'Supplier', code: 'TED-1', legalName: 'Sentetik Tedarik A.Ş.', tradeName: null },
    { partyId: CUSTOMER, role: 'Customer', code: 'MUS-1', legalName: 'Sentetik Müşteri Ltd.', tradeName: 'Müşteri' },
  ],
  productionStatus: 'Answered', partyStatus: 'Answered', partyNameStatus: 'Answered', windowFull: false,
}

const SEARCH: InventoryLabelTraceSearch = {
  items: [{ labelId: OUTPUT, code: 'SYN-OUT', supplierLabelNumber: 'SUP-0001', matchedBy: 'SupplierLabelNumber' }],
  windowFull: false, supplierSearchStatus: 'Answered',
}

function ok<T>(data: T): ViewOutcome<T> {
  return { type: 'ok-single', data }
}

function LabelReceipt() {
  const { labelId } = useParams()
  return <h1>Label route: {labelId}</h1>
}

function mount(path: string = INVENTORY_LABEL_TRACE_ROUTE) {
  return render(<MemoryRouter initialEntries={[path]}>
    <Routes>
      <Route path={INVENTORY_LABEL_TRACE_ROUTE} element={<InventoryLabelTracePage />} />
      <Route path="/stok/etiket/:labelId" element={<LabelReceipt />} />
    </Routes>
  </MemoryRouter>)
}

const eventRows = () => screen.getAllByRole('row').filter((row) => row.hasAttribute('data-movement-id'))

beforeEach(() => {
  scenario.companyId = COMPANY; scenario.namespace = 'SENTETIK-a'; scenario.scopeMode = 'single'
  scenario.report.mockReset()
  api.trace.mockReset().mockResolvedValue(ok(TRACE))
  api.search.mockReset().mockResolvedValue(ok(SEARCH))
})
afterEach(() => cleanup())

describe('parseLabelTraceTarget', () => {
  it('reads exactly one label and at most one direction, forward when absent', () => {
    expect(parseLabelTraceTarget(new URLSearchParams())).toEqual({ kind: 'none' })
    expect(parseLabelTraceTarget(new URLSearchParams({ etiket: ROOT }))).toEqual({ kind: 'trace', labelId: ROOT, direction: 'forward' })
    expect(parseLabelTraceTarget(new URLSearchParams({ etiket: ROOT, yon: 'geri' })))
      .toEqual({ kind: 'trace', labelId: ROOT, direction: 'backward' })
    expect(parseLabelTraceTarget(new URLSearchParams({ etiket: 'x', yon: 'ileri' }))).toEqual({ kind: 'invalid' })
    expect(parseLabelTraceTarget(new URLSearchParams({ etiket: ROOT, yon: 'yukari' }))).toEqual({ kind: 'invalid' })
    expect(parseLabelTraceTarget(new URLSearchParams(`etiket=${ROOT}&etiket=${OUTPUT}`))).toEqual({ kind: 'invalid' })
    expect(parseLabelTraceTarget(new URLSearchParams(`etiket=${ROOT}&yon=ileri&yon=geri`))).toEqual({ kind: 'invalid' })
    expect(parseLabelTraceTarget(new URLSearchParams({ yon: 'geri' }))).toEqual({ kind: 'invalid' })
  })
})

describe('InventoryLabelTracePage', () => {
  it('shows the trace from supplier receipt through production to the customer with the owner answers', async () => {
    mount(inventoryLabelTracePath(ROOT, 'forward'))

    expect(await screen.findByRole('heading', { level: 2, name: 'SYN-ROOT etiketinin izi' })).not.toBeNull()
    expect(api.trace).toHaveBeenCalledTimes(1)
    expect(api.trace).toHaveBeenCalledWith(COMPANY, ROOT, 'forward', expect.any(AbortSignal))
    expect(api.search).not.toHaveBeenCalled()
    expect(document.querySelector('main')).toBeNull()

    const [receipt, shipment] = eventRows()
    const receiptCells = within(receipt).getAllByRole('cell').map((cell) => cell.textContent)
    expect(receiptCells[1]).toBe('Tedarikçi kabulü')
    expect(receiptCells[2]).toBe('SYN-ROOT')
    expect(receiptCells[4]).toBe('100 kg')
    expect(receiptCells[6]).toBe('TED-1 · Sentetik Tedarik A.Ş.')
    expect(receiptCells[7]).toBe('SUP-0001')
    const shipmentCells = within(shipment).getAllByRole('cell').map((cell) => cell.textContent)
    expect(shipmentCells[1]).toBe('Müşteriye sevk')
    expect(shipmentCells[6]).toBe('MUS-1 · Müşteri')
    expect(shipmentCells[7]).toBe('—')

    const link = screen.getAllByRole('row').find((row) => row.getAttribute('data-transformation-id') === TRANSFORMATION)!
    const linkCells = within(link).getAllByRole('cell').map((cell) => cell.textContent)
    expect(linkCells[0]).toBe('Üretim')
    expect(linkCells[4]).toBe(ORDER)
    expect(linkCells[6]).toBe('1')
    expect(linkCells[7]).toBe('Recorded')
    expect(screen.getByRole('link', { name: 'İleri iz (tedarikçiden müşteriye)' }).getAttribute('aria-current')).toBe('page')
    expect(screen.getByRole('link', { name: 'Etiket detayını aç' }).getAttribute('href')).toBe(`/stok/etiket/${ROOT}`)
    for (const table of screen.getAllByRole('table')) expect(table.querySelector('caption')).not.toBeNull()
  })

  it('names each unanswered owner enrichment and never fills it in', async () => {
    api.trace.mockResolvedValue(ok<InventoryLabelTrace>({
      ...TRACE,
      links: [{ ...TRACE.links[0], productionOrderId: null, consumedKg: null, consumedShare: null, shareStatus: null }],
      events: TRACE.events.map((item) => ({ ...item, partyId: null })),
      parties: [], productionStatus: 'Unavailable', partyStatus: 'Denied', partyNameStatus: 'NotAnswered', windowFull: true,
    }))
    mount(inventoryLabelTracePath(ROOT, 'forward'))

    await screen.findByRole('heading', { level: 2, name: 'SYN-ROOT etiketinin izi' })
    expect(document.querySelector('[data-enrichment="production"]')?.getAttribute('data-status')).toBe('Unavailable')
    expect(document.querySelector('[data-enrichment="party"]')?.getAttribute('data-status')).toBe('Denied')
    expect(document.querySelector('[data-enrichment="party-name"]')?.getAttribute('data-status')).toBe('NotAnswered')
    expect(screen.getByText(/sınırını aştı/)).not.toBeNull()
    expect(within(eventRows()[0]).getAllByRole('cell')[6].textContent).toBe('Bilinmiyor')
    const link = screen.getAllByRole('row').find((row) => row.getAttribute('data-transformation-id') === TRANSFORMATION)!
    expect(within(link).getAllByRole('cell')[6].textContent).toBe('Cevap yok')
  })

  it('switches direction by URL and reads the owner again for the new direction only', async () => {
    const user = userEvent.setup()
    api.trace.mockImplementation((_company: string, labelId: string, direction: 'forward' | 'backward') =>
      Promise.resolve(ok<InventoryLabelTrace>({ ...TRACE, labelId, direction })))
    mount(inventoryLabelTracePath(ROOT, 'forward'))
    await screen.findByRole('heading', { level: 2, name: 'SYN-ROOT etiketinin izi' })

    await user.click(screen.getByRole('link', { name: 'Geri iz (müşteriden tedarikçiye)' }))

    await waitFor(() => expect(api.trace).toHaveBeenLastCalledWith(COMPANY, ROOT, 'backward', expect.any(AbortSignal)))
    expect(await screen.findByRole('link', { name: 'Geri iz (müşteriden tedarikçiye)', current: 'page' })).not.toBeNull()
  })

  it('searches by our code or the supplier number and opens the trace in the current direction', async () => {
    const user = userEvent.setup()
    mount()
    expect(screen.getByText(/İzini görmek istediğiniz etiketi arayın/)).not.toBeNull()
    expect(api.trace).not.toHaveBeenCalled()

    await user.type(screen.getByRole('searchbox', { name: 'Etiket kodu veya tedarikçi etiket no' }), '  SUP-0001 ')
    await user.click(screen.getByRole('button', { name: 'Ara' }))

    expect(api.search).toHaveBeenCalledWith(COMPANY, 'SUP-0001', expect.any(AbortSignal))
    const row = await screen.findByRole('row', { name: /SYN-OUT/ })
    expect(within(row).getAllByRole('cell')[2].textContent).toBe('Tedarikçi etiket no')
    await user.click(within(row).getByRole('link', { name: 'SYN-OUT izini aç' }))

    await waitFor(() => expect(api.trace).toHaveBeenCalledWith(COMPANY, OUTPUT, 'forward', expect.any(AbortSignal)))
  })

  it('refuses an invalid search text locally without a request', async () => {
    const user = userEvent.setup()
    mount()
    await user.type(screen.getByRole('searchbox'), ' a ')
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    expect(screen.getByText(/boşluksuz en az 2/)).not.toBeNull()
    expect(screen.getByRole('searchbox').getAttribute('aria-invalid')).toBe('true')
    expect(api.search).not.toHaveBeenCalled()
  })

  it('shows the empty result, the full window and the supplier-number status honestly', async () => {
    const user = userEvent.setup()
    api.search.mockResolvedValue(ok<InventoryLabelTraceSearch>({ items: [], windowFull: true, supplierSearchStatus: 'Unavailable' }))
    mount()
    await user.type(screen.getByRole('searchbox'), 'SYN')
    await user.click(screen.getByRole('button', { name: 'Ara' }))

    expect(await screen.findByText('Bu metinle başlayan etiket bulunamadı.')).not.toBeNull()
    expect(screen.getByText(/aramayı daraltın/)).not.toBeNull()
    expect(document.querySelector('[data-supplier-search-status="Unavailable"]')).not.toBeNull()
  })

  it('shows the owner closed 400 as an invalid query and other failures as the shared states with retry', async () => {
    const user = userEvent.setup()
    api.search.mockResolvedValue({ type: 'http', status: 400, body: { code: LABEL_TRACE_INVALID_QUERY } })
    api.trace.mockResolvedValueOnce({ type: 'http', status: 503, body: { code: 'inventory_label_trace_unavailable' } })
      .mockResolvedValueOnce(ok(TRACE))
    mount(inventoryLabelTracePath(ROOT, 'forward'))

    expect(await screen.findByText(/^Servis geçici olarak kullanılamıyor — etiket izi$/)).not.toBeNull()
    await user.click(screen.getByRole('button', { name: /Tekrar dene/ }))
    expect(await screen.findByRole('heading', { level: 2, name: 'SYN-ROOT etiketinin izi' })).not.toBeNull()
    expect(api.trace).toHaveBeenCalledTimes(2)

    await user.type(screen.getByRole('searchbox'), 'SYN')
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    expect(await screen.findByText(/Arama isteği geçersiz bulundu/)).not.toBeNull()
  })

  it('reports a forbidden read to the auth boundary without disclosing the label', async () => {
    api.trace.mockResolvedValue({ type: 'http', status: 403, body: null })
    mount(inventoryLabelTracePath(ROOT, 'backward'))

    expect(await screen.findByText(/^Yetkiniz yok — etiket izi$/)).not.toBeNull()
    expect(scenario.report).toHaveBeenCalledWith(403, { namespace: 'SENTETIK-a' })
    expect(screen.queryByText('SYN-ROOT')).toBeNull()
  })

  it('reads nothing for an invalid link or without a single company context', async () => {
    mount(`${INVENTORY_LABEL_TRACE_ROUTE}?etiket=not-a-guid&yon=ileri`)
    expect(screen.getByText(/İz bağlantısı geçersiz/)).not.toBeNull()
    cleanup()

    scenario.scopeMode = 'all'
    mount(inventoryLabelTracePath(ROOT, 'forward'))
    expect(screen.getByText(/tek şirket bağlamı seçilmeden/)).not.toBeNull()
    expect(api.trace).not.toHaveBeenCalled()
    expect(api.search).not.toHaveBeenCalled()
  })

  it('drops a late answer of a previous label', async () => {
    const user = userEvent.setup()
    let resolveFirst!: (value: ViewOutcome<InventoryLabelTrace>) => void
    api.trace.mockImplementationOnce(() => new Promise((resolve) => { resolveFirst = resolve }))
      .mockResolvedValueOnce(ok<InventoryLabelTrace>({ ...TRACE, labelId: OUTPUT, labelCode: 'SYN-OUT', labels: [{ labelId: OUTPUT, code: 'SYN-OUT', depth: 0 }], links: [], events: [], parties: [] }))
    mount(inventoryLabelTracePath(ROOT, 'forward'))

    await user.type(screen.getByRole('searchbox'), 'SYN')
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    await user.click(await screen.findByRole('link', { name: 'SYN-OUT izini aç' }))
    expect(await screen.findByRole('heading', { level: 2, name: 'SYN-OUT etiketinin izi' })).not.toBeNull()

    resolveFirst(ok(TRACE))
    await Promise.resolve()
    expect(screen.queryByRole('heading', { level: 2, name: 'SYN-ROOT etiketinin izi' })).toBeNull()
  })
})

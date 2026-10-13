import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PurchaseOrderDetailPage } from './PurchaseOrderDetailPage'
import type { PurchaseOrderAcceptanceHistory, PurchaseOrderLine } from './contract'
import { PURCHASE_ORDER_DETAIL_TEXT as T } from './purchaseOrderDetailText'

const scenario = vi.hoisted(() => ({
  status: 'Draft' as 'Draft' | 'Approved',
  canApprove: true,
  canReadSourceRequest: true,
  capabilities: [] as string[],
  sourceRequestId: null as string | null,
  activeCompanyId: '22222222-2222-4222-8222-222222222222',
  detailCompanyId: '22222222-2222-4222-8222-222222222222',
  detailOrderId: '11111111-1111-4111-8111-111111111111',
  routeEntryGeneration: 1,
  lines: [] as PurchaseOrderLine[],
  acceptances: [] as PurchaseOrderAcceptanceHistory[],
  documentMounts: [] as Array<Record<string, unknown>>,
  supplierDisplay: undefined as unknown,
}))
const COMPANY = '22222222-2222-4222-8222-222222222222'
const ORDER = '11111111-1111-4111-8111-111111111111'
const ACTOR = '33333333-3333-4333-8333-333333333333'
const LINE = '77777777-7777-4777-8777-777777777777'
const ACCEPTANCE = '88888888-8888-4888-8888-888888888888'
const OTHER_COMPANY = '99999999-9999-4999-8999-999999999999'
const OTHER_ORDER = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'

function serverLine(): PurchaseOrderLine {
  return {
    lineId: LINE, lineNo: 7, kind: 'General', parentLineId: null, sourceRequestLineId: null,
    productId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', productVersion: '3',
    unitId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', ordered: '10', technical: '0',
    committed: '10', accepted: '4.2500', remaining: '5.7500',
  }
}

function serverAcceptance(): PurchaseOrderAcceptanceHistory {
  return { acceptanceId: ACCEPTANCE, lineId: LINE, quantity: '4.2500', recordedAtUtc: '2026-10-05T18:30:00Z' }
}

function summary() {
  return {
    id: scenario.detailOrderId,
    companyId: scenario.detailCompanyId,
    status: scenario.status,
    version: scenario.status === 'Approved' ? '3' : '2',
    supplierPartyId: '44444444-4444-4444-8444-444444444444',
    companyAccountId: '55555555-5555-4555-8555-555555555555',
    sourceRequestId: scenario.sourceRequestId,
    effectiveOn: '2026-09-23',
    createdAtUtc: '2026-09-23T09:00:00Z',
    updatedAtUtc: '2026-09-23T09:00:00Z',
    lineCount: scenario.lines.length,
    ...(scenario.supplierDisplay === undefined ? {} : { supplierDisplay: scenario.supplierDisplay }),
  }
}

const SUPPLIER_PARTY = '44444444-4444-4444-8444-444444444444'
const LONG_LEGAL = 'KARADENİZ ÇELİK DÖKÜM MAKİNA İMALAT SANAYİ VE DIŞ TİCARET ANONİM ŞİRKETİ GEBZE ORGANİZE SANAYİ BÖLGESİ ŞUBESİ'
function supplier(over: Record<string, unknown> = {}) {
  return { partyId: SUPPLIER_PARTY, code: 'CAR-000042', legalName: LONG_LEGAL, tradeName: 'Karadeniz Döküm', ...over }
}

vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    phase: 'idle',
    sessionEpoch: 1,
    routeEntryGeneration: scenario.routeEntryGeneration,
    pendingTarget: null,
    activeContext: {
      userId: ACTOR,
      companyId: scenario.activeCompanyId,
      scopeMode: 'single',
      capabilityGeneration: 'published-v1',
      capabilities: [
        ...(scenario.canApprove ? ['purchasing.orders.approve'] : []),
        ...(scenario.canReadSourceRequest ? ['purchasing.requests.detail'] : []),
        ...scenario.capabilities,
      ],
    },
  }),
}))

vi.mock('../shell/shellState', async () => {
  const actual = await vi.importActual<typeof import('../shell/shellState')>('../shell/shellState')
  return { ...actual, contextKey: () => ['fixture'], contextKeyString: () => 'fixture' }
})

vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: vi.fn() }),
}))

vi.mock('@tanstack/react-query', () => ({
  useQuery: () => ({
    isPending: false,
    isFetching: false,
    data: { type: 'ok-single', data: { order: summary(), lines: scenario.lines, acceptances: scenario.acceptances } },
    refetch: vi.fn(async () => undefined),
  }),
}))

vi.mock('./api', () => ({ fetchPurchaseOrder: vi.fn() }))

vi.mock('./approvalApi', () => ({
  approvePurchaseOrder: vi.fn(async () => {
    scenario.status = 'Approved'
    return { type: 'applied', replayed: true, detail: { order: summary(), lines: scenario.lines, acceptances: scenario.acceptances }, originalCorrelationId: 'pur01-original' }
  }),
}))

vi.mock('./GoodsReceiptPanel', () => ({
  GoodsReceiptPanel: ({ enabled }: { readonly enabled: boolean }) => enabled
    ? <section aria-label="Mal kabul paneli">Mal kabul</section>
    : null,
}))

vi.mock('./GoodsReceiptDetail', () => ({
  GoodsReceiptDetailRow: (props: { readonly binding: string; readonly acceptanceId: string; readonly name: string;
    readonly colSpan: number; readonly detail: { readonly order: { readonly id: string } } }) =>
    <tr role="row" data-testid={`purchase-order-acceptance-detail-${props.acceptanceId}`}><td role="cell" colSpan={props.colSpan}>
      <button type="button" data-binding={props.binding} data-order={props.detail.order.id}
        data-acceptance={props.acceptanceId} aria-label={props.name}>Ayrıntı</button>
    </td></tr>,
}))

vi.mock('../documents/DocumentsPanel', () => ({
  DocumentsPanel: (props: Record<string, unknown>) => {
    scenario.documentMounts.push(props)
    return <section aria-label="Sipariş belge kasası">Belgeler</section>
  },
}))

function LocationReceipt() {
  const location = useLocation()
  return <output aria-label="Navigation state">{JSON.stringify(location.state)}</output>
}

function DetailRoutes() {
  return (
    <MemoryRouter initialEntries={[`/satinalma/siparisler/${ORDER}`]}>
      <Routes>
        <Route path="/satinalma/siparisler/:orderId" element={<PurchaseOrderDetailPage />} />
        <Route path="/satinalma/talepler/:requestId" element={<LocationReceipt />} />
      </Routes>
    </MemoryRouter>
  )
}

function renderDetail() {
  return render(<DetailRoutes />)
}

beforeEach(() => {
  scenario.status = 'Draft'
  scenario.canApprove = true
  scenario.canReadSourceRequest = true
  scenario.capabilities = []
  scenario.sourceRequestId = null
  scenario.activeCompanyId = COMPANY
  scenario.detailCompanyId = COMPANY
  scenario.detailOrderId = ORDER
  scenario.routeEntryGeneration = 1
  scenario.lines = []
  scenario.acceptances = []
  scenario.documentMounts = []
  scenario.supplierDisplay = supplier()
  sessionStorage.clear()
})

describe('PurchaseOrderDetailPage record-bound supplier identity (#661)', () => {
  it('shows the supplier code, the whole long legal name and the trade name beside the existing id facts', () => {
    renderDetail()
    const identity = screen.getByTestId('purchase-order-supplier-identity')
    expect(identity.textContent).toContain(`CAR-000042 — ${LONG_LEGAL}`)
    expect(identity.textContent).toContain(`${T.supplierTradeName}: Karadeniz Döküm`)
    expect(identity.textContent).not.toContain('…')
    // The #660 id facts stay exactly where the browser acceptance reads them.
    const facts = screen.getByTestId('purchase-order-supplier-facts')
    expect(facts.contains(identity)).toBe(true)
    expect(facts.textContent).toContain(SUPPLIER_PARTY)
    expect(facts.textContent).toContain('55555555-5555-4555-8555-555555555555')
  })

  it('links to the Cari card only with cari.parties.detail', () => {
    scenario.capabilities = ['cari.parties.detail']
    renderDetail()
    const link = screen.getByRole('link', { name: `CAR-000042 — ${LONG_LEGAL}` })
    expect(link.getAttribute('href')).toBe(`/cari/${SUPPLIER_PARTY}`)
  })

  it('is plain text without that capability, whatever else the reader holds', () => {
    scenario.capabilities = ['purchasing.orders.detail', 'cari.parties.list']
    renderDetail()
    const identity = screen.getByTestId('purchase-order-supplier-identity')
    expect(identity.textContent).toContain(`CAR-000042 — ${LONG_LEGAL}`)
    expect(identity.querySelector('a')).toBeNull()
  })

  it.each([
    ['null', null],
    ['the same as the legal name', LONG_LEGAL],
  ])('shows the code and legal name with no separate trade-name line when the trade name is %s', (_name, tradeName) => {
    scenario.capabilities = ['cari.parties.detail']
    scenario.supplierDisplay = supplier({ tradeName })
    renderDetail()
    const identity = screen.getByTestId('purchase-order-supplier-identity')
    // The identity itself is there, read from the record and linked — only the trade-name line is left out.
    expect(identity.querySelector('[data-supplier-display="record"]')?.textContent).toBe(`CAR-000042 — ${LONG_LEGAL}`)
    expect(identity.querySelector('[data-supplier-display="unavailable"]')).toBeNull()
    expect(screen.getByRole('link', { name: `CAR-000042 — ${LONG_LEGAL}` }).getAttribute('href')).toBe(`/cari/${SUPPLIER_PARTY}`)
    expect(identity.textContent).not.toContain(T.supplierTradeName)
  })

  it('treats a whitespace-only trade name as a malformed identity: unavailable, with no code, name or link', () => {
    scenario.capabilities = ['cari.parties.detail']
    scenario.supplierDisplay = supplier({ tradeName: '   ' })
    renderDetail()
    const identity = screen.getByTestId('purchase-order-supplier-identity')
    expect(identity.querySelector('[data-supplier-display="unavailable"]')?.textContent).toBe(T.supplierUnavailable)
    expect(identity.querySelector('[data-supplier-display="record"]')).toBeNull()
    expect(identity.querySelector('a')).toBeNull()
    expect(identity.textContent).not.toContain('CAR-000042')
    expect(identity.textContent).not.toContain(LONG_LEGAL)
    expect(identity.textContent).not.toContain(T.supplierTradeName)
  })

  it('never links or names a supplier from an answer for another company or order', () => {
    scenario.capabilities = ['cari.parties.detail']
    scenario.detailCompanyId = OTHER_COMPANY
    renderDetail()
    expect(screen.getByTestId('purchase-order-supplier-identity').querySelector('a')).toBeNull()
  })

  it.each([
    ['absent', undefined],
    ['bound to another party', { partyId: OTHER_ORDER, code: 'CAR-000042', legalName: 'Başka', tradeName: null }],
    ['malformed', { partyId: SUPPLIER_PARTY, code: '', legalName: 'Adı var', tradeName: null }],
  ])('says the identity could not be read when it is %s, and shows no name, link or id in its place', (_name, display) => {
    scenario.capabilities = ['cari.parties.detail']
    scenario.supplierDisplay = display
    renderDetail()
    const identity = screen.getByTestId('purchase-order-supplier-identity')
    expect(identity.querySelector('[data-supplier-display="unavailable"]')?.textContent).toBe(T.supplierUnavailable)
    expect(identity.querySelector('a')).toBeNull()
    expect(identity.textContent).not.toContain('Başka')
    expect(identity.textContent).not.toContain('Adı var')
  })

  it('keeps approval working: the command answers without a supplier identity and the order is re-read', async () => {
    renderDetail()
    await userEvent.click(screen.getByRole('button', { name: 'Onayla' }))
    expect(await screen.findByRole('status')).toBeTruthy()
    expect(screen.getByTestId('purchase-order-supplier-identity').textContent).toContain('CAR-000042')
  })
})

describe('PurchaseOrderDetailPage document-vault mount', () => {
  it('binds the exact current order, company, namespace and route generation', () => {
    scenario.routeEntryGeneration = 7
    renderDetail()

    expect(screen.getByRole('region', { name: 'Sipariş belge kasası' })).toBeTruthy()
    expect(scenario.documentMounts).toEqual([expect.objectContaining({
      ownerKind: 'purchasing.purchase-order',
      ownerId: ORDER,
      companyId: COMPANY,
      namespace: 'fixture',
      entryGeneration: 7,
    })])
  })

  it('shows the server-backed 360 summary, supplier identities, commitment and linked documents', () => {
    scenario.status = 'Approved'
    scenario.lines = [serverLine()]
    renderDetail()

    expect(screen.getByTestId('purchase-order-summary').textContent).toContain(ORDER)
    // The business date is shown as the order list shows it; the wire value stays on the element.
    expect(screen.getByTestId('purchase-order-status-facts').textContent).toContain('23.09.2026')
    expect(screen.getByTestId('purchase-order-status-facts').textContent).not.toContain('2026-09-23')
    expect(screen.getByTestId('purchase-order-supplier-facts').textContent).toContain('44444444-4444-4444-8444-444444444444')
    expect(screen.getByTestId('purchase-order-supplier-facts').textContent).toContain('55555555-5555-4555-8555-555555555555')
    expect(screen.getByTestId('purchase-order-lines').textContent).toContain('5.7500')
    expect(screen.getByRole('region', { name: 'Sipariş belge kasası' })).toBeTruthy()
  })

  it.each([
    { name: 'foreign company answer', detailCompanyId: OTHER_COMPANY, detailOrderId: ORDER },
    { name: 'another order answer', detailCompanyId: COMPANY, detailOrderId: OTHER_ORDER },
  ])('does not mount for $name', ({ detailCompanyId, detailOrderId }) => {
    scenario.detailCompanyId = detailCompanyId
    scenario.detailOrderId = detailOrderId
    renderDetail()

    expect(screen.queryByRole('region', { name: 'Sipariş belge kasası' })).toBeNull()
    expect(scenario.documentMounts).toEqual([])
  })
})

describe('PurchaseOrderDetailPage canonical acceptance history', () => {
  // PUR-RECEIPT-DETAIL-UI addendum: the detail read needs only the order read, so its control sits in the summary every
  // order reader sees — not in the record panel.
  it('offers "Ayrıntı" for each acceptance to a reader without the receipt-record capability', () => {
    scenario.status = 'Approved'
    scenario.lines = [serverLine()]
    scenario.acceptances = [serverAcceptance()]
    renderDetail()

    expect(screen.queryByRole('region', { name: 'Mal kabul paneli' })).toBeNull()
    const detailRow = screen.getByTestId(`purchase-order-acceptance-detail-${ACCEPTANCE}`)
    expect(screen.getByRole('region', { name: T.acceptanceHistoryHeading }).contains(detailRow)).toBe(true)
    // It follows its own acceptance row and leaves that row's four cells as they were.
    expect(detailRow.previousElementSibling).toBe(screen.getByTestId(`purchase-order-acceptance-${ACCEPTANCE}`))
    expect(within(detailRow).getAllByRole('cell')).toHaveLength(1)
    expect(within(detailRow).getByRole('cell').getAttribute('colspan')).toBe('4')
    const control = within(detailRow).getByRole('button', { name: 'Mal kabul ayrıntısı: satır 7, miktar 4.2500, 2026-10-05T18:30:00Z' })
    expect(control.textContent).toBe('Ayrıntı')
    // The same order and acceptance, under the page's own context key.
    expect(control.getAttribute('data-acceptance')).toBe(ACCEPTANCE)
    expect(control.getAttribute('data-order')).toBe(summary().id)
    expect(control.getAttribute('data-binding')).toBe('fixture')
  })

  it('offers no "Ayrıntı" when the order has no acceptance', () => {
    scenario.status = 'Approved'
    renderDetail()
    expect(screen.queryByRole('button', { name: /Mal kabul ayrıntısı/ })).toBeNull()
  })

  it('explains an empty normalized history without requiring the receipt-record capability', () => {
    scenario.status = 'Approved'
    renderDetail()

    const history = screen.getByRole('region', { name: T.acceptanceHistoryHeading })
    expect(history.textContent).toContain(T.acceptanceHistoryEmpty)
    expect(screen.queryByRole('region', { name: 'Mal kabul paneli' })).toBeNull()
  })

  it('shows exact server quantity and UTC text with the line number from the same detail only', () => {
    scenario.status = 'Approved'
    scenario.lines = [serverLine()]
    scenario.acceptances = [serverAcceptance()]
    renderDetail()

    const row = screen.getByTestId(`purchase-order-acceptance-${ACCEPTANCE}`)
    expect(row.textContent).toContain(ACCEPTANCE)
    expect(row.textContent).toContain('7')
    expect(row.textContent).toContain('4.2500')
    expect(row.textContent).toContain('2026-10-05T18:30:00Z')
    expect(screen.getByText(T.recordedAtUtc)).toBeTruthy()
    expect(screen.queryByRole('region', { name: 'Mal kabul paneli' })).toBeNull()
  })

  it('publishes history only after a fresh server detail replaces the empty detail', () => {
    scenario.status = 'Approved'
    const view = renderDetail()
    expect(screen.getByText(T.acceptanceHistoryEmpty)).toBeTruthy()

    scenario.lines = [serverLine()]
    scenario.acceptances = [serverAcceptance()]
    view.rerender(<DetailRoutes />)

    expect(screen.getByTestId(`purchase-order-acceptance-${ACCEPTANCE}`)).toBeTruthy()
    expect(screen.queryByText(T.acceptanceHistoryEmpty)).toBeNull()
  })

  it('discards history when a fresh shell context no longer matches the server company and record', () => {
    scenario.status = 'Approved'
    scenario.lines = [serverLine()]
    scenario.acceptances = [serverAcceptance()]
    const view = renderDetail()
    expect(screen.getByTestId(`purchase-order-acceptance-${ACCEPTANCE}`)).toBeTruthy()

    scenario.activeCompanyId = OTHER_COMPANY
    scenario.detailOrderId = OTHER_ORDER
    scenario.routeEntryGeneration += 1
    view.rerender(<DetailRoutes />)

    expect(screen.queryByTestId(`purchase-order-acceptance-${ACCEPTANCE}`)).toBeNull()
    expect(screen.queryByRole('region', { name: T.acceptanceHistoryHeading })).toBeNull()
  })
})

describe('PurchaseOrderDetailPage approval outcome', () => {
  it('keeps the replay notice after the refetched order becomes Approved and removes command controls', async () => {
    const user = userEvent.setup()
    renderDetail()

    await user.click(screen.getByRole('button', { name: 'Onayla' }))

    expect(await screen.findByText('Önceki onay yanıtı yeniden gösterildi.')).toBeTruthy()
    expect(screen.getByText('Onaylandı')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Onayla' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Aynı onayı yeniden gönder' })).toBeNull()
  })

  it('shows no approval outcome or command controls for an already Approved idle order', () => {
    scenario.status = 'Approved'
    renderDetail()

    expect(screen.queryByRole('status')).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Onayla' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Aynı onayı yeniden gönder' })).toBeNull()
  })

  it('links the source request through the canonical route when request detail is authorized', () => {
    scenario.sourceRequestId = '66666666-6666-4666-8666-666666666666'
    renderDetail()

    expect(screen.getByRole('link', { name: scenario.sourceRequestId }).getAttribute('href'))
      .toBe(`/satinalma/talepler/${scenario.sourceRequestId}`)
  })

  it('keeps the source request as non-navigable text when request detail is not authorized', () => {
    scenario.sourceRequestId = '66666666-6666-4666-8666-666666666666'
    scenario.canReadSourceRequest = false
    renderDetail()

    expect(screen.queryByRole('link', { name: scenario.sourceRequestId })).toBeNull()
    expect(screen.getByText(scenario.sourceRequestId)).toBeTruthy()
  })
})

describe('PurchaseOrderDetailPage source-return hint', () => {
  it('carries only the current namespace and exact server request/order identities', async () => {
    scenario.sourceRequestId = '66666666-6666-4666-8666-666666666666'
    const user = userEvent.setup()
    renderDetail()
    await user.click(screen.getByRole('link', { name: scenario.sourceRequestId }))
    expect(JSON.parse(screen.getByLabelText('Navigation state').textContent ?? 'null')).toEqual({
      ns: 'fixture', requestId: scenario.sourceRequestId, orderId: ORDER,
    })
  })

  it('does not manufacture a request link for an unlinked order', () => {
    renderDetail()
    expect(screen.getByText(T.sourceNotLinked)).toBeTruthy()
    expect(screen.queryByRole('link')).toBeNull()
  })
})

describe('PurchaseOrderDetailPage goods-receipt capability mount', () => {
  it('shows the panel for the exact capability published by the backend', () => {
    scenario.capabilities = ['purchasing.goods-receipts.record']
    renderDetail()

    expect(screen.getByRole('region', { name: 'Mal kabul paneli' })).toBeTruthy()
  })

  it.each([
    { name: 'capability absent', capabilities: [] },
    { name: 'unpublished legacy id', capabilities: ['purchasing.orders.goods-receipt'] },
  ])('hides the panel when $name', ({ capabilities }) => {
    scenario.capabilities = capabilities
    renderDetail()

    expect(screen.queryByRole('region', { name: 'Mal kabul paneli' })).toBeNull()
  })
})

/**
 * PURCHASING-DS-DETAIL-01: the order detail on GOY-DS-001. One real table per record set, for every width: from
 * 768 px a compact table, below it the same rows drawn as labelled cards by the page stylesheet. The cells keep
 * their roles and their order, so nothing is duplicated and nothing has to be found twice.
 */
describe('PurchaseOrderDetailPage on the design system (PURCHASING-DS-DETAIL-01)', () => {
  const nodeProcess = (globalThis as unknown as {
    readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
  }).process
  const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
  const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
  // Vitest runs from the web package (`npm test` in src/frontend/goymen-erp-web).
  const css = fs
    .readFileSync(path.resolve(nodeProcess.cwd(), 'src/purchasing/PurchaseOrderDetailPage.module.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '')
  const phone = css.slice(css.indexOf('@media (max-width: 767px)'))
  const rule = (source: string, selector: string) => {
    // The last rule for a selector: a grouped rule may name it earlier, its own rule comes after.
    const start = source.lastIndexOf(`\n${selector} {`)
    return start < 0 ? '' : source.slice(start, source.indexOf('}', start))
  }
  const LINE_LABELS = ['No', T.productId, T.unitId, T.ordered, T.technical, T.committed, T.accepted, T.remaining]
  const ACCEPTANCE_LABELS = [T.acceptanceId, T.lineNo, T.quantity, T.recordedAtUtc]

  it('is one labelled region under its own heading, not a second main landmark', () => {
    renderDetail()
    const heading = screen.getByRole('heading', { level: 1, name: T.heading })
    const region = screen.getByRole('region', { name: T.heading })
    expect(region.getAttribute('aria-labelledby')).toBe(heading.id)
    expect(region.contains(screen.getByTestId('purchase-order-summary'))).toBe(true)
    expect(document.querySelector('main')).toBeNull()
  })

  it('shows the business date in Turkish order and keeps the wire date on the time element', () => {
    renderDetail()
    const time = screen.getByTestId('purchase-order-status-facts').querySelector('time') as HTMLTimeElement
    expect(time.textContent).toBe('23.09.2026')
    expect(time.getAttribute('datetime')).toBe('2026-09-23')
    expect(time.closest('div')?.querySelector('dt')?.textContent).toBe(T.businessDate)
  })

  it('draws the lines as one named table whose every cell carries its column and its role', () => {
    scenario.lines = [serverLine()]
    renderDetail()
    const table = screen.getByRole('table', { name: T.linesCaption })
    expect(screen.getByTestId('purchase-order-lines').querySelectorAll('table')).toHaveLength(1)
    const headers = within(table).getAllByRole('columnheader')
    expect(headers.map((header) => header.textContent)).toEqual(LINE_LABELS)
    expect(headers.every((header) => header.getAttribute('scope') === 'col')).toBe(true)
    const row = screen.getByTestId('purchase-order-line-7')
    expect(row.getAttribute('role')).toBe('row')
    const cells = within(row).getAllByRole('cell')
    // The label a card shows is the column's own name, in the column's own position.
    expect(cells.map((cell) => cell.getAttribute('data-label'))).toEqual(LINE_LABELS)
    expect(cells.map((cell) => cell.textContent)).toEqual([
      '7', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', '10', '0', '10', '4.2500', '5.7500',
    ])
    expect(cells.every((cell) => cell.tagName === 'TD' && cell.getAttribute('role') === 'cell')).toBe(true)
  })

  it('draws the acceptance history the same way, with the recorded instant exactly as the server sent it', () => {
    scenario.lines = [serverLine()]
    scenario.acceptances = [serverAcceptance()]
    renderDetail()
    const table = screen.getByRole('table', { name: T.acceptanceHistoryHeading })
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent)).toEqual(ACCEPTANCE_LABELS)
    const cells = within(screen.getByTestId(`purchase-order-acceptance-${ACCEPTANCE}`)).getAllByRole('cell')
    expect(cells.map((cell) => cell.getAttribute('data-label'))).toEqual(ACCEPTANCE_LABELS)
    expect(cells.map((cell) => cell.textContent)).toEqual([ACCEPTANCE, '7', '4.2500', '2026-10-05T18:30:00Z'])
    expect(cells[3].querySelector('time')?.getAttribute('datetime')).toBe('2026-10-05T18:30:00Z')
  })

  it('shows the status as text beside the heading, with a tone that is never the only signal', () => {
    scenario.status = 'Approved'
    const approved = renderDetail()
    const tone = document.querySelector('header [data-tone]') as HTMLElement
    expect([tone.textContent, tone.getAttribute('data-tone')]).toEqual([T.approved, 'success'])
    approved.unmount()
    scenario.status = 'Draft'
    renderDetail()
    const draft = document.querySelector('header [data-tone]') as HTMLElement
    expect([draft.textContent, draft.getAttribute('data-tone')]).toEqual([T.draft, 'warning'])
  })

  it('keeps the related operations in reading order: summary, lines, history, documents, approval, goods receipt', () => {
    scenario.lines = [serverLine()]
    scenario.capabilities = ['purchasing.goods-receipts.record']
    renderDetail()
    const order = [
      screen.getByTestId('purchase-order-summary'),
      screen.getByTestId('purchase-order-lines'),
      screen.getByTestId('purchase-order-acceptance-history'),
      screen.getByRole('region', { name: 'Sipariş belge kasası' }),
      screen.getByRole('button', { name: 'Onayla' }),
      screen.getByTestId('purchase-order-receipt-action'),
    ]
    for (let index = 1; index < order.length; index += 1) {
      expect(order[index - 1].compareDocumentPosition(order[index]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    }
    expect(within(screen.getByTestId('purchase-order-receipt-action')).getByRole('region', { name: 'Mal kabul paneli' })).toBeTruthy()
  })

  it('below 768 px draws the same rows as labelled cards without a second copy (page stylesheet)', () => {
    expect(css).toContain('@media (max-width: 767px)')
    // Each cell shows its column's name before its value.
    expect(rule(phone, '  .table td::before')).toMatch(/content:\s*attr\(data-label\)/)
    expect(rule(phone, '  .table tr')).toMatch(/border:\s*1px solid/)
    // The header row is kept for assistive technology, not drawn, and cannot widen the page.
    expect(rule(phone, '  .table thead')).toMatch(/position:\s*absolute/)
    expect(rule(phone, '  .table thead')).toMatch(/overflow:\s*hidden/)
    expect(rule(phone, '  .table thead')).not.toMatch(/display:\s*none/)
    // The table gives up its desktop minimum width and its sideways scroll.
    expect(rule(phone, '  .table')).toMatch(/min-width:\s*0/)
    expect(rule(phone, '  .tableWrap')).toMatch(/overflow-x:\s*visible/)
    // Values wrap; numbers are no longer pushed to the right edge on one line.
    expect(rule(phone, '  .table td')).toMatch(/overflow-wrap:\s*anywhere/)
    expect(rule(phone, '  .number')).toMatch(/white-space:\s*normal/)
  })

  it('keeps long ids and names wrapping and the page on design-system tokens (page stylesheet)', () => {
    expect(rule(css, '.identifier')).toMatch(/overflow-wrap:\s*anywhere/)
    expect(rule(css, '.facts dd')).toMatch(/overflow-wrap:\s*anywhere/)
    expect(rule(css, '.title')).toMatch(/overflow-wrap:\s*anywhere/)
    expect(css).not.toMatch(/text-overflow|line-clamp/)
    expect(css).not.toMatch(/#[0-9a-fA-F]{3,8}\b|rgb\(|hsl\(/)
    // From 768 px the table scrolls inside its own area instead of widening the page.
    expect(rule(css, '.tableWrap')).toMatch(/overflow-x:\s*auto/)
  })
})

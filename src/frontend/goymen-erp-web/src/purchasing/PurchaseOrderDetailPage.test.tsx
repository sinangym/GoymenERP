import { render, screen } from '@testing-library/react'
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
  }
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
  sessionStorage.clear()
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
    expect(screen.getByText('Bağlı değil')).toBeTruthy()
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

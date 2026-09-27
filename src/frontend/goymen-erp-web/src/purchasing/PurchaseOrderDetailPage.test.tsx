import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PurchaseOrderDetailPage } from './PurchaseOrderDetailPage'

const scenario = vi.hoisted(() => ({
  status: 'Draft' as 'Draft' | 'Approved',
  canApprove: true,
  canReadSourceRequest: true,
  capabilities: [] as string[],
  sourceRequestId: null as string | null,
}))
const COMPANY = '22222222-2222-4222-8222-222222222222'
const ORDER = '11111111-1111-4111-8111-111111111111'
const ACTOR = '33333333-3333-4333-8333-333333333333'

function summary() {
  return {
    id: ORDER,
    companyId: COMPANY,
    status: scenario.status,
    version: scenario.status === 'Approved' ? '3' : '2',
    supplierPartyId: '44444444-4444-4444-8444-444444444444',
    companyAccountId: '55555555-5555-4555-8555-555555555555',
    sourceRequestId: scenario.sourceRequestId,
    effectiveOn: '2026-09-23',
    createdAtUtc: '2026-09-23T09:00:00Z',
    updatedAtUtc: '2026-09-23T09:00:00Z',
    lineCount: 0,
  }
}

vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    phase: 'idle',
    sessionEpoch: 1,
    routeEntryGeneration: 1,
    pendingTarget: null,
    activeContext: {
      userId: ACTOR,
      companyId: COMPANY,
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
    data: { type: 'ok-single', data: { order: summary(), lines: [] } },
    refetch: vi.fn(async () => undefined),
  }),
}))

vi.mock('./api', () => ({ fetchPurchaseOrder: vi.fn() }))

vi.mock('./approvalApi', () => ({
  approvePurchaseOrder: vi.fn(async () => {
    scenario.status = 'Approved'
    return { type: 'applied', replayed: true, detail: { order: summary(), lines: [] }, originalCorrelationId: 'pur01-original' }
  }),
}))

vi.mock('./GoodsReceiptPanel', () => ({
  GoodsReceiptPanel: ({ enabled }: { readonly enabled: boolean }) => enabled
    ? <section aria-label="Mal kabul paneli">Mal kabul</section>
    : null,
}))

function LocationReceipt() {
  const location = useLocation()
  return <output aria-label="Navigation state">{JSON.stringify(location.state)}</output>
}

function renderDetail() {
  return render(
    <MemoryRouter initialEntries={[`/satinalma/siparisler/${ORDER}`]}>
      <Routes>
        <Route path="/satinalma/siparisler/:orderId" element={<PurchaseOrderDetailPage />} />
        <Route path="/satinalma/talepler/:requestId" element={<LocationReceipt />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  scenario.status = 'Draft'
  scenario.canApprove = true
  scenario.canReadSourceRequest = true
  scenario.capabilities = []
  scenario.sourceRequestId = null
  sessionStorage.clear()
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

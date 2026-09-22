import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PurchaseOrderDetailPage } from './PurchaseOrderDetailPage'

const scenario = vi.hoisted(() => ({ status: 'Draft' as 'Draft' | 'Approved', canApprove: true }))
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
    sourceRequestId: null,
    effectiveOn: '2026-09-23',
    createdAtUtc: '2026-09-23T09:00:00Z',
    updatedAtUtc: '2026-09-23T09:00:00Z',
    lineCount: 0,
  }
}

vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    sessionEpoch: 1,
    routeEntryGeneration: 1,
    activeContext: { userId: ACTOR, companyId: COMPANY, scopeMode: 'single', capabilities: [] },
  }),
}))

vi.mock('../shell/shellState', () => ({
  contextKey: () => ['fixture'],
  contextKeyString: () => 'fixture',
  hasCapability: () => scenario.canApprove,
}))

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

function renderDetail() {
  return render(
    <MemoryRouter initialEntries={[`/satinalma/siparisler/${ORDER}`]}>
      <Routes><Route path="/satinalma/siparisler/:orderId" element={<PurchaseOrderDetailPage />} /></Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  scenario.status = 'Draft'
  scenario.canApprove = true
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
})

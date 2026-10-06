import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PurchaseOrderDetailPage } from './PurchaseOrderDetailPage'
import { PurchaseOrderListPage } from './PurchaseOrderListPage'

const state = vi.hoisted(() => {
  let resolveApproval: ((value: unknown) => void) | null = null
  let resolvePage: ((value: unknown) => void) | null = null
  return {
    generation: 1,
    approval: new Promise((resolve) => { resolveApproval = resolve }),
    page: new Promise((resolve) => { resolvePage = resolve }),
    resolveApproval: (value: unknown) => resolveApproval?.(value),
    resolvePage: (value: unknown) => resolvePage?.(value),
  }
})

const COMPANY = '22222222-2222-4222-8222-222222222222'
const ORDER_A = '11111111-1111-4111-8111-111111111111'
const ORDER_B = '88888888-8888-4888-8888-888888888888'
const ACTOR = '33333333-3333-4333-8333-333333333333'

function summary(id: string) {
  return {
    id, companyId: COMPANY, status: 'Draft' as const, version: '1',
    supplierPartyId: '44444444-4444-4444-8444-444444444444',
    companyAccountId: '55555555-5555-4555-8555-555555555555', sourceRequestId: null,
    effectiveOn: '2026-09-22', createdAtUtc: '2026-09-22T10:00:00Z',
    updatedAtUtc: '2026-09-22T10:00:00Z', lineCount: 0,
  }
}

vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    sessionEpoch: 1,
    routeEntryGeneration: state.generation,
    activeContext: { userId: ACTOR, companyId: COMPANY, scopeMode: 'single', capabilities: [] },
  }),
}))

vi.mock('../shell/shellState', () => ({
  contextKey: () => ['fixture'],
  contextKeyString: () => 'fixture',
  hasCapability: () => true,
}))

vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: state.generation }), report: vi.fn() }),
}))

vi.mock('@tanstack/react-query', () => ({
  useQuery: (options: { queryKey: readonly unknown[] }) => {
    const detail = options.queryKey.includes('purchase-order')
    return {
      isPending: false,
      isFetching: false,
      data: detail
        ? { type: 'ok-single', data: { order: summary(ORDER_A), lines: [], acceptances: [] } }
        : {
            type: 'ok-single',
            data: state.generation === 1
              ? { items: [summary(ORDER_A)], nextCursor: 'cursor-a' }
              : { items: [summary(ORDER_B)], nextCursor: 'cursor-b' },
          },
      refetch: vi.fn(async () => undefined),
    }
  },
}))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return {
    ...actual,
    fetchPurchaseOrders: vi.fn(() => state.page),
    fetchPurchaseOrder: vi.fn(),
  }
})

vi.mock('./approvalApi', () => ({
  approvePurchaseOrder: vi.fn(() => state.approval),
}))

beforeEach(() => {
  state.generation = 1
  sessionStorage.clear()
})

describe('Purchasing route generation fence', () => {
  it('remounts a pending approval as an explicit same-snapshot retry', async () => {
    const user = userEvent.setup()
    const element = () => (
      <MemoryRouter initialEntries={[`/satinalma/siparisler/${ORDER_A}`]}>
        <Routes><Route path="/satinalma/siparisler/:orderId" element={<PurchaseOrderDetailPage />} /></Routes>
      </MemoryRouter>
    )
    const view = render(element())
    await user.click(screen.getByRole('button', { name: 'Onayla' }))
    expect((screen.getByRole('button', { name: 'Gönderiliyor…' }) as HTMLButtonElement).disabled).toBe(true)

    state.generation = 2
    view.rerender(element())
    expect((await screen.findByRole('button', { name: 'Aynı onayı yeniden gönder' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('drops a delayed continuation and remounts usable pagination for the new generation', async () => {
    const user = userEvent.setup()
    const element = () => <MemoryRouter><PurchaseOrderListPage /></MemoryRouter>
    const view = render(element())
    await screen.findByText('…11111111')
    await user.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    expect(screen.getByText('Sonraki sayfa yükleniyor…')).toBeTruthy()

    state.generation = 2
    view.rerender(element())
    expect(await screen.findByText('…88888888')).toBeTruthy()
    expect(screen.queryByText('…11111111')).toBeNull()
    expect((screen.getByRole('button', { name: 'Daha fazla göster' }) as HTMLButtonElement).disabled).toBe(false)

    await act(async () => state.resolvePage({ type: 'ok-single', data: { items: [], nextCursor: null } }))
    expect(screen.getByText('…88888888')).toBeTruthy()
  })
})

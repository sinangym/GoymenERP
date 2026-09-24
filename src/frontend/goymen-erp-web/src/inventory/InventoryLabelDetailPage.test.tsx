import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryLabelDetailPage } from './InventoryLabelDetailPage'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const PRODUCT = '33333333-3333-3333-3333-333333333333'
const LOT = '44444444-4444-4444-4444-444444444444'
const PARTY = '55555555-5555-5555-5555-555555555555'
const LOCATION = '66666666-6666-6666-6666-666666666666'

const api = vi.hoisted(() => ({ detail: vi.fn() }))
vi.mock('./labelDetailApi', () => ({ fetchInventoryLabelDetail: api.detail }))
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    sessionEpoch: 1, routeEntryGeneration: 1,
    activeContext: { userId: 'actor-a', companyId: COMPANY, scopeMode: 'single', capabilities: [] },
  }),
}))
vi.mock('../shell/shellState', () => ({ contextKey: () => ['fixture'], contextKeyString: () => 'fixture' }))

function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[`/stok/etiket/${LABEL}`]}>
    <Routes><Route path="/stok/etiket/:labelId" element={<InventoryLabelDetailPage />} /></Routes>
  </MemoryRouter></QueryClientProvider>)
}

beforeEach(() => {
  api.detail.mockReset().mockResolvedValue({
    type: 'ok-single',
    data: {
      identity: {
        labelId: LABEL, companyId: COMPANY, code: 'LBL-100', productId: PRODUCT, lotId: LOT,
        heatNumber: null, coilNumber: 'COIL-9', documentStatus: 'Complete', requiredDocuments: ['MTC'],
        documents: [{ kind: 'MTC', attachedAtUtc: '2026-09-24T08:00:00Z' }],
        prints: [{ sequence: 1, printedAtUtc: '2026-09-24T08:01:00Z', reason: null }],
        lot: { id: LOT, heatNumber: 'HEAT-1', coilNumber: null },
      },
      balances: {
        labelId: LABEL, physicalKg: '1250.5', freeKg: '1200', blockedKg: '50.5',
        positions: [{
          ownerKind: 'Company', ownerId: PARTY, custodianKind: 'Company', custodianId: PARTY,
          locationId: LOCATION, productId: PRODUCT, block: 'Free', quantityKg: '1200',
        }],
      },
    },
  })
})

describe('InventoryLabelDetailPage', () => {
  it('reopens one actual label identity together with its ownership, location and quantities', async () => {
    mount()
    expect(await screen.findByRole('heading', { name: 'LBL-100' })).not.toBeNull()
    expect(screen.getByText(LABEL)).not.toBeNull()
    expect(screen.getByText(PRODUCT)).not.toBeNull()
    expect(screen.getByText(LOCATION)).not.toBeNull()
    expect(screen.getByText('1250.5 kg')).not.toBeNull()
    expect(screen.getByText('1200 kg', { selector: 'td' })).not.toBeNull()
    expect(screen.getByText('Gerekli belgeler: MTC')).not.toBeNull()
    expect(api.detail).toHaveBeenCalledWith(COMPANY, LABEL, expect.any(AbortSignal))
  })

  it('keeps a forbidden read in the shared terminal error state', async () => {
    api.detail.mockResolvedValueOnce({ type: 'http', status: 403, body: { status: 403 } })
    mount()
    expect(await screen.findByText(/yetkiniz yok/i)).not.toBeNull()
    expect(screen.queryByRole('heading', { name: 'LBL-100' })).toBeNull()
  })
})

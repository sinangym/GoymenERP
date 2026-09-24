import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryStockPositionPage } from './InventoryStockPositionPage'

const COMPANY = '22222222-2222-4222-8222-222222222222'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const LABEL = '66666666-6666-4666-8666-666666666666'
const LOT = '77777777-7777-4777-8777-777777777777'
const PRODUCT = '88888888-8888-4888-8888-888888888888'
const api = vi.hoisted(() => ({ positions: vi.fn() }))
const auth = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))

vi.mock('./api', async (importOriginal) => ({
  ...await importOriginal<typeof import('./api')>(),
  fetchStockPositions: api.positions,
}))
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    sessionEpoch: 1, routeEntryGeneration: 1,
    activeContext: { userId: 'actor-a', companyId: COMPANY, scopeMode: 'single', capabilities: [] },
  }),
}))
vi.mock('../shell/shellState', () => ({ contextKey: () => ['fixture'], contextKeyString: () => 'fixture' }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => auth }))

const item = {
  companyId: COMPANY, labelId: LABEL, labelVersion: '3', labelCode: 'LBL-1', productId: PRODUCT,
  lot: { id: LOT, heatNumber: 'HEAT-1', coilNumber: null }, heatNumber: null, coilNumber: 'COIL-1',
  documentStatus: 'Complete', ownerKind: 'Company', ownerId: COMPANY,
  custodianKind: 'Company', custodianId: COMPANY, locationId: LOCATION,
  physicalKg: '100', freePhysicalKg: '90', blockedPhysicalKg: '10',
  activeAllocationKg: '30', availableKg: '60',
}

beforeEach(() => {
  api.positions.mockReset().mockResolvedValue({
    type: 'ok-single', data: { items: [item], searchWindowFull: false },
  })
})

describe('InventoryStockPositionPage', () => {
  it('reopens real position components and links to canonical product and location records without raw ids', async () => {
    render(<MemoryRouter><InventoryStockPositionPage /></MemoryRouter>)

    const row = (await screen.findByText('LBL-1')).closest('tr')
    expect(row).not.toBeNull()
    expect(row?.textContent).toContain('Şirket malı')
    expect(screen.getByText('60')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Ürün kartı' }).getAttribute('href')).toBe(`/urun/${PRODUCT}`)
    expect(screen.getByRole('link', { name: 'Lokasyon kartı' }).getAttribute('href'))
      .toBe(`/ayarlar/organizasyon/lokasyon/${LOCATION}`)
    expect(document.body.textContent).not.toContain(PRODUCT)
    expect(document.body.textContent).not.toContain(LOCATION)
  })

  it('searches by label code and shows bounded-window guidance', async () => {
    const user = userEvent.setup()
    api.positions.mockResolvedValueOnce({ type: 'ok-single', data: { items: [item], searchWindowFull: false } })
      .mockResolvedValueOnce({ type: 'ok-single', data: { items: [item], searchWindowFull: true } })
    render(<MemoryRouter><InventoryStockPositionPage /></MemoryRouter>)
    await screen.findByText('LBL-1')
    await user.type(screen.getByLabelText('Etiket kodu'), 'LBL')
    await user.click(screen.getByRole('button', { name: 'Ara' }))

    await waitFor(() => expect(api.positions).toHaveBeenLastCalledWith(COMPANY, 'LBL', expect.any(AbortSignal)))
    expect(await screen.findByText(/Arama penceresi doldu/)).toBeTruthy()
  })

  it('renders verified empty and failure states without mock stock', async () => {
    api.positions.mockResolvedValueOnce({ type: 'ok-single', data: { items: [], searchWindowFull: false } })
    const app = render(<MemoryRouter><InventoryStockPositionPage /></MemoryRouter>)
    expect(await screen.findByText('Bu şirkette eşleşen fiziksel stok yok.')).toBeTruthy()
    app.unmount()

    api.positions.mockResolvedValueOnce({ type: 'http', status: 503, body: null })
    render(<MemoryRouter><InventoryStockPositionPage /></MemoryRouter>)
    expect((await screen.findByRole('alert')).textContent).toBe('Stok pozisyonları okunamadı (503).')
  })
})

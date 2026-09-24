import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useParams } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryOpeningDetailPage } from './InventoryOpeningDetailPage'
import { fetchOpeningCount } from './api'
import type { OpeningCountView } from './contract'
import type { ViewOutcome } from '../shell/uiState'
import { inventoryLabelDetailPath } from './labelDetailRoute'

const scenario = vi.hoisted(() => ({
  companyId: '11111111-1111-1111-1111-111111111111', namespace: 'SENTETIK-a' as string | null,
  generation: 1, canReadLabel: true, report: vi.fn(),
}))
const COMPANY = '11111111-1111-1111-1111-111111111111'
const OTHER_COMPANY = '99999999-9999-9999-9999-999999999999'
const COUNT = '22222222-2222-2222-2222-222222222222'
const LABEL = '33333333-3333-3333-3333-333333333333'
const PRODUCT = '44444444-4444-4444-4444-444444444444'
const clients: QueryClient[] = []
vi.mock('./api', () => ({ fetchOpeningCount: vi.fn() }))
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({ sessionEpoch: 1, routeEntryGeneration: scenario.generation,
    activeContext: { userId: 'SENTETIK-actor', companyId: scenario.companyId, scopeMode: 'single', capabilities: [] } }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => scenario.namespace === null ? null : [scenario.namespace],
  contextKeyString: (key: readonly string[]) => key[0],
  hasCapability: (_shell: unknown, capability: string) => capability === 'inventory.stock-positions.list' && scenario.canReadLabel,
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ generation: scenario.generation }), report: scenario.report }),
}))
function fixture(): OpeningCountView {
  return { countId: COUNT, companyId: COMPANY, locationId: '55555555-5555-5555-5555-555555555555',
    countedOn: '2026-09-18', sheetReference: 'SENTETIK opening', lines: [{
      lineId: '66666666-6666-6666-6666-666666666666', labelId: LABEL, labelCode: 'SENTETIK-LABEL',
      productId: PRODUCT, lotId: '77777777-7777-7777-7777-777777777777',
      heatNumber: null, coilNumber: null, quantityKg: '12.5',
    }] }
}
function LabelRouteReceipt() { const { labelId } = useParams(); return <h1>Etiket: {labelId}</h1> }
function mount() {
  // A long cache lifetime intentionally proves that a new entry does not paint the old record.
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 60_000 } } })
  clients.push(client)
  const element = () => <QueryClientProvider client={client}><MemoryRouter initialEntries={[`/stok/acilis/${COUNT}`]}>
    <Routes>
      <Route path="/stok/acilis/:countId" element={<InventoryOpeningDetailPage />} />
      <Route path="/stok/etiket/:labelId" element={<LabelRouteReceipt />} />
    </Routes>
  </MemoryRouter></QueryClientProvider>
  const view = render(element()); return { ...view, refresh: () => view.rerender(element()) }
}
const ready = () => screen.findByRole('heading', { name: 'Açılış sayımı' })
const labelLink = () => screen.queryByRole('link', { name: 'SENTETIK-LABEL' })
beforeEach(() => {
  scenario.companyId = COMPANY; scenario.namespace = 'SENTETIK-a'; scenario.generation = 1
  scenario.canReadLabel = true; scenario.report.mockReset()
  vi.mocked(fetchOpeningCount).mockReset().mockResolvedValue({ type: 'ok-single', data: fixture() })
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()) })

describe('InventoryOpeningDetailPage label drill-down', () => {
  it('opens the exact stored label through the existing canonical route', async () => {
    const user = userEvent.setup(); mount(); await ready()
    expect(labelLink()?.getAttribute('href')).toBe(inventoryLabelDetailPath(LABEL))
    expect(fetchOpeningCount).toHaveBeenCalledWith(COMPANY, COUNT, expect.any(AbortSignal))
    await user.click(screen.getByRole('link', { name: 'SENTETIK-LABEL' }))
    expect(await screen.findByRole('heading', { name: `Etiket: ${LABEL}` })).toBeTruthy()
  })
  it('keeps the exact label code as inert text when the read capability is absent', async () => {
    scenario.canReadLabel = false; mount(); await ready()
    expect(labelLink()).toBeNull(); expect(screen.getByText('SENTETIK-LABEL')).toBeTruthy()
  })
  it('removes the label link immediately when the current capability is revoked', async () => {
    const view = mount(); await ready(); scenario.canReadLabel = false; view.refresh()
    expect(labelLink()).toBeNull(); expect(screen.getByText('SENTETIK-LABEL')).toBeTruthy()
  })
  it('starts a fresh read on a new route generation without painting the cached label', async () => {
    const view = mount(); await ready()
    let resolve!: (result: ViewOutcome<OpeningCountView>) => void
    vi.mocked(fetchOpeningCount).mockImplementationOnce(() => new Promise((done) => { resolve = done }))
    scenario.generation += 1; view.refresh()
    expect(labelLink()).toBeNull(); expect(screen.queryByText('SENTETIK-LABEL')).toBeNull()
    await waitFor(() => expect(fetchOpeningCount).toHaveBeenCalledTimes(2))
    await act(async () => resolve({ type: 'ok-single', data: fixture() })); await ready()
    expect(labelLink()).toBeTruthy()
  })
  it('discards a delayed previous-company reply', async () => {
    let resolve!: (result: ViewOutcome<OpeningCountView>) => void
    vi.mocked(fetchOpeningCount).mockImplementationOnce(() => new Promise((done) => { resolve = done }))
    const view = mount(); await waitFor(() => expect(fetchOpeningCount).toHaveBeenCalledTimes(1))
    scenario.namespace = 'SENTETIK-b'; scenario.companyId = OTHER_COMPANY
    vi.mocked(fetchOpeningCount).mockResolvedValue({ type: 'http', status: 404 })
    view.refresh(); await screen.findByText('Bulunamadı — açılış sayımı')
    await act(async () => resolve({ type: 'ok-single', data: fixture() }))
    expect(labelLink()).toBeNull(); expect(screen.queryByText('SENTETIK-LABEL')).toBeNull()
  })
  it.each([401, 403])('reports HTTP %s to the existing boundary without showing links', async (status) => {
    vi.mocked(fetchOpeningCount).mockResolvedValue({ type: 'http', status }); mount()
    await waitFor(() => expect(scenario.report).toHaveBeenCalledWith(status, { generation: 1 }))
    expect(labelLink()).toBeNull()
  })
  it.each(['network-error', 'malformed'] as const)('retries %s before allowing navigation', async (type) => {
    vi.mocked(fetchOpeningCount).mockResolvedValueOnce({ type }); const user = userEvent.setup(); mount()
    await screen.findByRole('button', { name: 'Tekrar dene' }); expect(labelLink()).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Tekrar dene' })); await ready(); expect(labelLink()).toBeTruthy()
  })
  it.each([{ ...fixture(), companyId: OTHER_COMPANY }, { ...fixture(), countId: LABEL }])(
    'refuses a decoded record for another identity before deriving links', async (data) => {
      vi.mocked(fetchOpeningCount).mockResolvedValue({ type: 'ok-single', data }); mount()
      await screen.findByText('Beklenmeyen yanıt — açılış sayımı'); expect(labelLink()).toBeNull()
    },
  )
  it('does not read or render records with an unresolved namespace', () => {
    scenario.namespace = null; mount(); expect(fetchOpeningCount).not.toHaveBeenCalled(); expect(labelLink()).toBeNull()
  })
})

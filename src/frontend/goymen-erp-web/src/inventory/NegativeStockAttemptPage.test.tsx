/** Synthetic response and component sources only. Live HTTP, SQL, authorization and browser acceptance are NOT_RUN. */
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { initialShellState, type ShellState } from '../shell/shellState'
import type { ViewOutcome } from '../shell/uiState'
import { INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE, INVENTORY_STOCK_POSITIONS_CAPABILITY, inventoryNegativeStockAttemptPath } from './capabilities'
import { fetchNegativeStockAttempts, NEGATIVE_STOCK_ATTEMPT_INVALID_QUERY } from './NegativeStockAttemptApi'
import type { NegativeStockAttempt, NegativeStockAttemptPage as AttemptPage } from './NegativeStockAttemptContract'
import { NegativeStockAttemptPage } from './NegativeStockAttemptPage'
import { NEGATIVE_STOCK_ATTEMPT_TEXT as T } from './NegativeStockAttemptText'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const PRODUCT = '22222222-2222-4222-8222-222222222222'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const LABEL = '44444444-4444-4444-8444-444444444444'
const OTHER = '55555555-5555-4555-8555-555555555555'
let currentShell: ShellState
const security = vi.hoisted(() => ({ capture: vi.fn(() => ({ synthetic: 'security-snapshot' })), report: vi.fn() }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => currentShell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => security }))
vi.mock('./NegativeStockAttemptApi', async (original) => ({ ...await original<typeof import('./NegativeStockAttemptApi')>(), fetchNegativeStockAttempts: vi.fn() }))
const row = (overrides: Partial<NegativeStockAttempt> = {}): NegativeStockAttempt => ({
  attemptId: '66666666-6666-4666-8666-666666666666', companyId: COMPANY, movementId: '77777777-7777-4777-8777-777777777777',
  commandName: 'inventory.stock-transfer.dispatch.v1', actorId: 'synthetic-actor', occurredAtUtc: '2026-11-17T10:00:00Z',
  productId: PRODUCT, locationId: LOCATION, labelId: LABEL, labelCode: 'SENTETIK-0001', movementKind: 'TransferDispatch',
  sourceKind: 'inventory.transfer', sourceId: '88888888-8888-4888-8888-888888888888', sourceLineId: null,
  requestedKg: '500.001', availableKg: '480', missingKg: '20.001', resolution: null, ...overrides,
})
const ok = (items: readonly NegativeStockAttempt[] = [row()], companyId = COMPANY, windowFull = false): ViewOutcome<AttemptPage> =>
  ({ type: 'ok-single', data: { companyId, items, windowFull } })
function mount(path = INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE as string) {
  const element = () => <MemoryRouter initialEntries={[path]}><Routes>
    <Route path={INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE} element={<NegativeStockAttemptPage />} />
  </Routes></MemoryRouter>
  const result = render(element())
  return { ...result, refresh: () => result.rerender(element()) }
}
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}
beforeEach(() => {
  vi.clearAllMocks(); vi.mocked(fetchNegativeStockAttempts).mockReset().mockResolvedValue(ok())
  currentShell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
    activeContext: { userId: 'synthetic-actor', companyId: COMPANY, scopeMode: 'single', capabilityGeneration: 'g1', capabilities: [INVENTORY_STOCK_POSITIONS_CAPABILITY] } }
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('negative stock attempt report', () => {
  it('shows the complete recorded refusal without recomputing a value or offering a stock command', async () => {
    mount()
    const label = await screen.findByRole('link', { name: 'SENTETIK-0001' })
    const cells = within(label.closest('tr')!).getAllByRole('cell').map((cell) => cell.textContent)
    expect(cells).toContain('500.001 kg'); expect(cells).toContain('480 kg'); expect(cells).toContain('20.001 kg')
    expect(cells).toContain(LOCATION)
    expect(cells).toContain('synthetic-actor'); expect(cells).toContain(T.open)
    expect(label.getAttribute('href')).toContain(LABEL)
    expect(fetchNegativeStockAttempts).toHaveBeenCalledWith(COMPANY, { productId: null, locationId: null, openOnly: true }, expect.any(AbortSignal))
    expect(screen.getByText(T.retention)).toBeTruthy()
    expect(screen.queryByRole('button', { name: /sil|onayla|stok oluştur/i })).toBeNull()
  })
  it('shows legacy negative available kg exactly as recorded', async () => {
    vi.mocked(fetchNegativeStockAttempts).mockResolvedValue(ok([row({ requestedKg: '0.001', availableKg: '-2.002', missingKg: '2.003' })]))
    mount()
    expect(await screen.findByText('-2.002 kg')).toBeTruthy()
    expect(screen.getByText('2.003 kg')).toBeTruthy()
  })
  it('shows an immutable real count resolution when all records are explicitly requested', async () => {
    vi.mocked(fetchNegativeStockAttempts).mockResolvedValue(ok([row({ resolution: { stockCountId: OTHER,
      decidedAtUtc: '2026-11-17T11:00:00Z', decidedBy: 'synthetic-checker' } })]))
    mount(`${INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE}?openOnly=false`)
    expect(await screen.findByText(T.resolved)).toBeTruthy()
    expect(screen.getByText(`${T.count}: ${OTHER}`)).toBeTruthy()
    expect(screen.getByText(`${T.decidedBy}: synthetic-checker`)).toBeTruthy()
    expect(screen.getByText('2026-11-17T11:00:00Z')).toBeTruthy()
  })
  it('carries real applied product and location ids from the stock-position entry path', async () => {
    mount(inventoryNegativeStockAttemptPath(PRODUCT, LOCATION))
    await screen.findByRole('link', { name: 'SENTETIK-0001' })
    expect((screen.getByLabelText(T.product) as HTMLInputElement).value).toBe(PRODUCT)
    expect((screen.getByLabelText(T.location) as HTMLInputElement).value).toBe(LOCATION)
    expect(fetchNegativeStockAttempts).toHaveBeenCalledWith(COMPANY, { productId: PRODUCT, locationId: LOCATION, openOnly: true }, expect.any(AbortSignal))
  })
  it('applies only explicit optional filters and does not read while a draft is typed', async () => {
    mount(); await screen.findByRole('link', { name: 'SENTETIK-0001' })
    const user = userEvent.setup()
    await user.type(screen.getByLabelText(T.product), PRODUCT)
    await user.type(screen.getByLabelText(T.location), LOCATION)
    await user.click(screen.getByRole('checkbox', { name: T.openOnly }))
    expect(fetchNegativeStockAttempts).toHaveBeenCalledTimes(1)
    await user.click(screen.getByRole('button', { name: T.apply }))
    await waitFor(() => expect(fetchNegativeStockAttempts).toHaveBeenCalledTimes(2))
    expect(fetchNegativeStockAttempts).toHaveBeenLastCalledWith(COMPANY, { productId: PRODUCT, locationId: LOCATION, openOnly: false }, expect.any(AbortSignal))
  })
  it('rejects an invalid draft without issuing a malformed owner query', async () => {
    mount(); await screen.findByRole('link', { name: 'SENTETIK-0001' })
    const user = userEvent.setup(); await user.type(screen.getByLabelText(T.product), 'SENTETIK invalid')
    await user.click(screen.getByRole('button', { name: T.apply }))
    expect(screen.getByText(T.invalidId)).toBeTruthy(); expect(fetchNegativeStockAttempts).toHaveBeenCalledTimes(1)
  })
  it.each(['productId=invalid', `productId=${PRODUCT}&productId=${OTHER}`, 'openOnly=maybe', 'unknown=1'])('refuses invalid URL filters %s before a request', async (search) => {
    mount(`${INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE}?${search}`)
    expect(screen.getByText(T.invalidId)).toBeTruthy(); expect(fetchNegativeStockAttempts).not.toHaveBeenCalled()
    await userEvent.setup().click(screen.getByRole('button', { name: T.clear }))
    expect(await screen.findByRole('link', { name: 'SENTETIK-0001' })).toBeTruthy()
  })
  it('separates no open matches from a real empty all-records source', async () => {
    vi.mocked(fetchNegativeStockAttempts).mockResolvedValue(ok([]))
    mount(); expect((await screen.findByText(T.emptyFiltered)).getAttribute('data-state')).toBe('empty-filtered')
    const user = userEvent.setup(); await user.click(screen.getByRole('checkbox', { name: T.openOnly }))
    await user.click(screen.getByRole('button', { name: T.apply }))
    expect((await screen.findByText(T.empty)).getAttribute('data-state')).toBe('empty-no-data')
  })
  it('shows a bounded-window notice without presenting a total or fetching ahead', async () => {
    vi.mocked(fetchNegativeStockAttempts).mockResolvedValue(ok([row()], COMPANY, true))
    mount(); expect(await screen.findByText(T.windowFull)).toBeTruthy()
    expect(fetchNegativeStockAttempts).toHaveBeenCalledTimes(1)
  })
  it('shows loading before any source is available', () => {
    vi.mocked(fetchNegativeStockAttempts).mockReturnValue(new Promise(() => {}))
    mount(); expect(screen.getByText(/Yükleniyor/)).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })
  it.each<readonly [ViewOutcome<AttemptPage>, string]>([
    [{ type: 'http', status: 401 }, 'unauthenticated'], [{ type: 'http', status: 403 }, 'forbidden'],
    [{ type: 'http', status: 404 }, 'not-found'], [{ type: 'http', status: 409 }, 'conflict'],
    [{ type: 'http', status: 503 }, 'service-unavailable'], [{ type: 'offline' }, 'offline'],
    [{ type: 'network-error' }, 'network-error'], [{ type: 'malformed' }, 'malformed-response'],
  ])('shows a distinct failed state without stale quantities %j', async (outcome, expected) => {
    vi.mocked(fetchNegativeStockAttempts).mockResolvedValue(outcome)
    mount(); const alert = await screen.findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe(expected); expect(screen.queryByRole('table')).toBeNull()
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      expect(security.report).toHaveBeenCalledWith(outcome.status, { synthetic: 'security-snapshot' })
    }
  })
  it('accepts only the closed invalid-query problem code as a query error', async () => {
    vi.mocked(fetchNegativeStockAttempts).mockResolvedValueOnce({ type: 'http', status: 400, body: { code: NEGATIVE_STOCK_ATTEMPT_INVALID_QUERY } })
      .mockResolvedValueOnce({ type: 'http', status: 400, body: { code: 'other' } })
    mount(); expect(await screen.findByText(T.invalidQuery)).toBeTruthy()
    await userEvent.setup().click(screen.getByRole('button', { name: T.refresh }))
    expect((await screen.findByRole('alert')).getAttribute('data-state')).toBe('malformed-response')
  })
  it('drops previous rows while refreshing and permits a read-only retry after 503', async () => {
    const late = deferred<ViewOutcome<AttemptPage>>()
    vi.mocked(fetchNegativeStockAttempts).mockResolvedValueOnce(ok()).mockReturnValueOnce(late.promise).mockResolvedValueOnce(ok())
    mount(); await screen.findByRole('link', { name: 'SENTETIK-0001' })
    const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: T.refresh }))
    expect(screen.queryByRole('table')).toBeNull()
    await act(async () => { late.resolve({ type: 'http', status: 503 }) })
    await user.click(await screen.findByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByRole('link', { name: 'SENTETIK-0001' })).toBeTruthy()
  })
  it('does not replace a newer filtered answer with a late old read', async () => {
    const late = deferred<ViewOutcome<AttemptPage>>()
    vi.mocked(fetchNegativeStockAttempts).mockReturnValueOnce(late.promise).mockResolvedValueOnce(ok([]))
    mount(); const user = userEvent.setup()
    await user.type(screen.getByLabelText(T.product), PRODUCT); await user.click(screen.getByRole('button', { name: T.apply }))
    await screen.findByText(T.emptyFiltered)
    const originalSignal = vi.mocked(fetchNegativeStockAttempts).mock.calls[0][2]!
    expect(originalSignal.aborted).toBe(true)
    await act(async () => { late.resolve(ok()) })
    expect(screen.queryByRole('table')).toBeNull(); expect(screen.getByText(T.emptyFiltered)).toBeTruthy()
  })
  it.each(['company', 'actor', 'session', 'authority', 'route'])('isolates an old async response after %s changes', async (change) => {
    const late = deferred<ViewOutcome<AttemptPage>>()
    vi.mocked(fetchNegativeStockAttempts).mockReturnValueOnce(late.promise).mockResolvedValueOnce(ok([], change === 'company' ? OTHER : COMPANY))
    const mounted = mount()
    if (change === 'company') currentShell = { ...currentShell, activeContext: { ...currentShell.activeContext!, companyId: OTHER } }
    if (change === 'actor') currentShell = { ...currentShell, activeContext: { ...currentShell.activeContext!, userId: 'synthetic-replacement' } }
    if (change === 'session') currentShell = { ...currentShell, sessionEpoch: 2 }
    if (change === 'authority') currentShell = { ...currentShell, requestFenceGeneration: currentShell.requestFenceGeneration + 1 }
    if (change === 'route') currentShell = { ...currentShell, routeEntryGeneration: currentShell.routeEntryGeneration + 1 }
    mounted.refresh(); await screen.findByText(T.emptyFiltered)
    expect(vi.mocked(fetchNegativeStockAttempts).mock.calls[0][2]!.aborted).toBe(true)
    await act(async () => { late.resolve(ok()) })
    expect(screen.queryByRole('table')).toBeNull(); expect(screen.getByText(T.emptyFiltered)).toBeTruthy()
  })
  it('does not fetch without the existing stock-report capability or a committed company', () => {
    currentShell = { ...currentShell, activeContext: { ...currentShell.activeContext!, capabilities: [] } }
    const mounted = mount(); expect(screen.getByText(/Yetkiniz yok/)).toBeTruthy(); expect(fetchNegativeStockAttempts).not.toHaveBeenCalled()
    currentShell = { ...currentShell, activeContext: null }; mounted.refresh()
    expect(screen.getByText(/Yetkili şirket yok/)).toBeTruthy(); expect(fetchNegativeStockAttempts).not.toHaveBeenCalled()
  })
})

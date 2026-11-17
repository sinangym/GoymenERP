/** Synthetic HTTP/DOM sources only; no actual SQL, grant, device or browser acceptance is asserted. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { initialShellState, type ShellState } from '../shell/shellState'
import { WarehouseTransferScalePage } from './WarehouseTransferScalePage'
import { resetWarehouseScaleAttemptsForTests, WAREHOUSE_TRANSFER_SCALE_CAPABILITY as CAP,
  WAREHOUSE_TRANSFER_SCALE_SOURCE as SOURCE } from './warehouseTransferScaleApi'
import { WAREHOUSE_TRANSFER_SCALE_TEXT as T } from './warehouseTransferScaleMessages.tr'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const LOCATION = '22222222-2222-4222-8222-222222222222'
const OTHER = '33333333-3333-4333-8333-333333333333'
const SITE = '44444444-4444-4444-8444-444444444444'
let shell: ShellState
const security = vi.hoisted(() => ({ capture: vi.fn(() => ({ companyId: '11111111-1111-4111-8111-111111111111', sessionEpoch: 1 })), report: vi.fn() }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => security }))
const clients: QueryClient[] = []
const json = (raw: unknown, status = 200) => new Response(JSON.stringify(raw), { status })
const choices = (companyId = COMPANY) => ({ companyId, q: null, limit: 25, items: [{ locationId: LOCATION,
  locationCode: 'DEP-01', locationName: 'SENTETIK depo', siteId: SITE, siteCode: 'TES-01', siteName: 'SENTETIK tesis' }], searchWindowFull: false })
const virgin = () => ({ companyId: COMPANY, locationId: LOCATION, policy: { status: 1, companyId: COMPANY, locationId: LOCATION,
  requireExitWeighing: false, recordedVersion: null, sourceVersion: `${SOURCE}/not-configured` }, versions: [] })
interface Request { readonly url: string; readonly init?: RequestInit }
let requests: Request[]
let candidateReply: () => Promise<Response>
let policyReply: () => Promise<Response>
let commandReply: () => Promise<Response>
beforeEach(() => {
  requests = []; let key = 0
  shell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
    activeContext: { userId: 'SENTETIK-actor', scopeMode: 'single', companyId: COMPANY, capabilityGeneration: 'g1', capabilities: [CAP] } }
  candidateReply = async () => json(choices()); policyReply = async () => json(virgin()); commandReply = async () => new Response(null, { status: 204 })
  vi.stubGlobal('crypto', { randomUUID: () => `synthetic-${++key}` })
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input); requests.push({ url, init })
    return init?.method === 'POST' ? commandReply() : url.includes('/warehouses?') ? candidateReply() : policyReply()
  }))
  vi.clearAllMocks()
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()); resetWarehouseScaleAttemptsForTests(); vi.unstubAllGlobals() })
function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, refetchOnWindowFocus: false } } }); clients.push(client)
  const element = () => <QueryClientProvider client={client}><WarehouseTransferScalePage /></QueryClientProvider>
  const result = render(element()); return { ...result, refresh: () => result.rerender(element()) }
}
async function selectWarehouse() {
  const user = userEvent.setup(); await screen.findByLabelText(T.warehouse)
  await user.selectOptions(screen.getByLabelText(T.warehouse), LOCATION)
  await screen.findByLabelText(T.value); return user
}
const commands = () => requests.filter((request) => request.init?.method === 'POST')
describe('warehouse transfer scale real selector and source states', () => {
  it('requires the existing narrow capability before reading either owner', () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [] } }
    mount(); expect(screen.getByText(/Yetkiniz yok/)).toBeTruthy(); expect(requests).toHaveLength(0)
  })
  it('shows real candidate loading and does not offer a technical ID form', () => {
    candidateReply = () => new Promise(() => {})
    mount(); expect(screen.getByText(/Yükleniyor/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    expect(screen.queryByLabelText(T.warehouse)).toBeNull()
  })
  it('shows a real empty discovery instead of manufacturing a warehouse', async () => {
    candidateReply = async () => json({ ...choices(), items: [] })
    mount(); expect(await screen.findByText(T.empty)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    expect(requests.every((request) => request.url.includes('/warehouses?'))).toBe(true)
  })
  it('shows the actual warehouse and its site, then distinguishes virgin Off without prefilling a record', async () => {
    mount(); await selectWarehouse()
    expect(screen.getByRole('option', { name: /DEP-01.*SENTETIK depo.*TES-01.*SENTETIK tesis/ })).toBeTruthy()
    expect(screen.getByText(T.notConfigured)).toBeTruthy()
    expect((screen.getByLabelText(T.value) as HTMLSelectElement).value).toBe('')
    expect(screen.getByText(T.noHistory)).toBeTruthy(); expect(commands()).toHaveLength(0)
    expect(requests.some((request) => request.url.includes('/organization/'))).toBe(false)
  })
  it.each<readonly [number | 'malformed' | 'offline', string]>([
    [401, 'Oturum sonlandı'], [403, 'Yetkiniz yok'], [404, 'Bulunamadı'], [409, 'Çakışma'],
    [503, 'Servis geçici olarak kullanılamıyor'], ['malformed', 'Beklenmeyen yanıt'], ['offline', 'Bağlantı yok'],
  ])('keeps candidate failure %s distinct without an editable policy', async (kind, message) => {
    if (kind === 'offline') { vi.stubGlobal('navigator', { onLine: false }); candidateReply = async () => { throw new TypeError('Synthetic offline') } }
    else candidateReply = async () => json(kind === 'malformed' ? { ...choices(), companyId: OTHER } : {}, typeof kind === 'number' ? kind : 200)
    mount(); expect(await screen.findByText(new RegExp(message))).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    if (kind === 401 || kind === 403) expect(security.report).toHaveBeenCalledWith(kind, expect.anything())
  })
  it('shows policy failure instead of converting unavailable into virgin default Off', async () => {
    policyReply = async () => json({}, 503)
    mount(); const user = userEvent.setup(); await screen.findByLabelText(T.warehouse)
    await user.selectOptions(screen.getByLabelText(T.warehouse), LOCATION)
    expect(await screen.findByText(/Servis geçici olarak kullanılamıyor/)).toBeTruthy()
    expect(screen.queryByText(T.notConfigured)).toBeNull()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })
})
describe('warehouse scale explicit versioned commands and isolation', () => {
  it('records explicit Off at version zero under the chosen actual warehouse with a mandatory reason', async () => {
    mount(); const user = await selectWarehouse()
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(screen.getByText(T.required)).toBeTruthy(); expect(commands()).toHaveLength(0)
    await user.selectOptions(screen.getByLabelText(T.value), 'off'); await user.type(screen.getByLabelText(T.reason), 'SENTETIK kapalı')
    await user.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.saved)
    const command = commands()[0]
    expect(command.url).toBe(`/api/v1/settings/warehouse-transfer-scale/${LOCATION}/versions`)
    expect(JSON.parse(String(command.init?.body))).toEqual({ expectedVersion: '0', requireExitWeighing: false, reason: 'SENTETIK kapalı' })
    expect(new Headers(command.init?.headers).get('X-Company-Id')).toBe(COMPANY)
    expect(new Headers(command.init?.headers).get('Idempotency-Key')).toBe('warehouse-scale-synthetic-1')
  })
  it('uses the actual recorded Off version for a later explicit On request', async () => {
    policyReply = async () => json({ ...virgin(), policy: { ...virgin().policy, status: 2, recordedVersion: 1, sourceVersion: `${SOURCE}/1` }, versions: [{
      recordId: OTHER, version: '1', requireExitWeighing: false, locationVersion: 7, recordedBy: 'SENTETIK-actor', recordedAtUtc: '2026-11-17T10:00:00+00:00', reason: 'SENTETIK ilk seçim',
    }] })
    mount(); const user = await selectWarehouse()
    expect(screen.getByText(T.configured)).toBeTruthy()
    await user.selectOptions(screen.getByLabelText(T.value), 'on'); await user.type(screen.getByLabelText(T.reason), 'SENTETIK aç')
    await user.click(screen.getByRole('button', { name: T.save }))
    await waitFor(() => expect(commands()).toHaveLength(1))
    expect(JSON.parse(String(commands()[0].init?.body))).toEqual({ expectedVersion: '1', requireExitWeighing: true, reason: 'SENTETIK aç' })
  })
  it('preserves an unknown exact request and key across route departure, then retries without requiring another selection', async () => {
    let call = 0; commandReply = async () => ++call === 1 ? json({}, 503)
      : new Response(null, { status: 204, headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original' } })
    const first = mount(); const user = await selectWarehouse()
    await user.selectOptions(screen.getByLabelText(T.value), 'off'); await user.type(screen.getByLabelText(T.reason), 'SENTETIK aynı istek')
    await user.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.unknown)
    expect((screen.getByLabelText(T.warehouse) as HTMLSelectElement).disabled).toBe(true)
    first.unmount(); shell = { ...shell, routeEntryGeneration: 2 }; mount()
    await screen.findByText(T.unknown); await user.click(screen.getByRole('button', { name: T.retry }))
    await screen.findByText(T.replayed)
    expect(commands()).toHaveLength(2)
    expect(commands()[1].init?.body).toBe(commands()[0].init?.body)
    expect(new Headers(commands()[1].init?.headers).get('Idempotency-Key')).toBe(new Headers(commands()[0].init?.headers).get('Idempotency-Key'))
  })
  it('refreshes after an immutable version conflict and keeps its refusal visible', async () => {
    commandReply = async () => json({ code: 'settings_warehouse_transfer_scale_version_conflict' }, 409)
    mount(); const user = await selectWarehouse()
    await user.selectOptions(screen.getByLabelText(T.value), 'off'); await user.type(screen.getByLabelText(T.reason), 'SENTETIK değişiklik')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(/Çakışma/)).toBeTruthy()
    await waitFor(() => expect(requests.filter((request) => request.url.endsWith(LOCATION))).toHaveLength(2))
  })
  it('does not release a former company policy read into the new company screen', async () => {
    let resolve!: (value: Response) => void; policyReply = () => new Promise((done) => { resolve = done })
    const page = mount(); const user = userEvent.setup(); await screen.findByLabelText(T.warehouse)
    await user.selectOptions(screen.getByLabelText(T.warehouse), LOCATION)
    const signal = requests.find((request) => request.url.endsWith(LOCATION))?.init?.signal
    shell = { ...shell, committedContextRevision: 2, activeContext: { ...shell.activeContext!, companyId: OTHER } }
    candidateReply = async () => json({ ...choices(OTHER), items: [] })
    page.refresh(); await screen.findByText(T.empty)
    await act(async () => { resolve(json(virgin())) })
    expect(signal?.aborted).toBe(true); expect(screen.queryByText(T.notConfigured)).toBeNull(); expect(commands()).toHaveLength(0)
  })
  it('does not display a late former-session command result in a new actor session', async () => {
    let resolve!: (value: Response) => void; commandReply = () => new Promise((done) => { resolve = done })
    const page = mount(); const user = await selectWarehouse()
    await user.selectOptions(screen.getByLabelText(T.value), 'off'); await user.type(screen.getByLabelText(T.reason), 'SENTETIK değişiklik')
    await user.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.pending)
    shell = { ...shell, sessionEpoch: 2, activeContext: { ...shell.activeContext!, userId: 'SENTETIK-other' } }
    page.refresh(); await screen.findByLabelText(T.warehouse)
    await act(async () => { resolve(new Response(null, { status: 204 })) })
    expect(screen.queryByText(T.saved)).toBeNull(); expect(screen.queryByText(T.unknown)).toBeNull()
  })
})

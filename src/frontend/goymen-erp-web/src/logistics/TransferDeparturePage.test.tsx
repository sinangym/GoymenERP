/** Synthetic HTTP/DOM source tests; no real Inventory, GIB, SQL, browser or permission grant is asserted. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { initialShellState, type ShellState } from '../shell/shellState'
import { TransferDeparturePage } from './TransferDeparturePage'
import { resetTransferDepartureAttemptsForTests, TRANSFER_DEPARTURE_CAPABILITY as CAP, TRANSFER_DEPARTURE_SOURCE as SOURCE } from './transferDepartureApi'
import { TRANSFER_DEPARTURE_TEXT as T } from './transferDepartureMessages.tr'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const TRANSFER = '22222222-2222-4222-8222-222222222222'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const OTHER = '44444444-4444-4444-8444-444444444444'
let shell: ShellState
const security = vi.hoisted(() => ({ capture: vi.fn(() => ({ companyId: '11111111-1111-4111-8111-111111111111', sessionEpoch: 1 })), report: vi.fn() }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => security }))
const clients: QueryClient[] = []
const json = (raw: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(raw), { status, headers })
const choices = () => ({ companyId: COMPANY, items: [{ transferId: TRANSFER, version: '9', status: 'InTransit', sourceLocationId: LOCATION,
  sourceLocationCode: 'DEP-01', sourceLocationName: 'SENTETIK kaynak', destinationLocationId: OTHER,
  destinationLocationCode: 'DEP-02', destinationLocationName: 'SENTETIK hedef', dispatchedAtUtc: '2026-11-17T08:00:00Z' }], windowFull: false })
const facts = () => ({ companyId: COMPANY, transferId: TRANSFER, sourceLocationId: LOCATION, association: 1, version: 1,
  exitWeighingAtUtc: null as string | null, vehicleExitAtUtc: null as string | null, noFurtherExit: false, officialDocument: null as unknown, sourceVersion: `${SOURCE}/1` })
interface Request { readonly url: string; readonly init?: RequestInit }
let requests: Request[]
let candidateReply: (request: Request) => Promise<Response>
let factsReply: (request: Request) => Promise<Response>
let commandReply: (request: Request) => Promise<Response>
function success(request: Request, replay = false): Response {
  const body = JSON.parse(String(request.init?.body)) as { kind: number; occurredAtUtc: string }
  return json({ facts: { ...facts(), version: 2, sourceVersion: `${SOURCE}/2`,
    exitWeighingAtUtc: body.kind === 1 ? body.occurredAtUtc : null, vehicleExitAtUtc: body.kind === 2 ? body.occurredAtUtc : null },
  isReplay: replay, originalCorrelationId: 'synthetic-original' }, 200,
  replay ? { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original' } : {})
}
beforeEach(() => {
  requests = []; let key = 0
  shell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
    activeContext: { userId: 'SENTETIK-actor', scopeMode: 'single', companyId: COMPANY, capabilityGeneration: 'g1', capabilities: [CAP] } }
  candidateReply = async () => json(choices()); factsReply = async () => json(facts()); commandReply = async (request) => success(request)
  vi.stubGlobal('crypto', { randomUUID: () => `synthetic-${++key}` })
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = { url: String(input), init }; requests.push(request)
    return init?.method === 'POST' ? commandReply(request) : request.url.endsWith('/candidates') ? candidateReply(request) : factsReply(request)
  }))
  vi.clearAllMocks()
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()); resetTransferDepartureAttemptsForTests(); vi.unstubAllGlobals() })
function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, refetchOnWindowFocus: false } } }); clients.push(client)
  const element = () => <QueryClientProvider client={client}><TransferDeparturePage /></QueryClientProvider>
  const result = render(element()); return { ...result, refresh: () => result.rerender(element()) }
}
async function selectTransfer() {
  const user = userEvent.setup(); await screen.findByLabelText(T.transfer)
  await user.selectOptions(screen.getByLabelText(T.transfer), TRANSFER)
  await screen.findByText(T.notLinked); return user
}
async function vehicleForm() {
  const user = await selectTransfer(); await user.selectOptions(screen.getByLabelText(T.kind), '2')
  fireEvent.change(screen.getByLabelText(T.occurred), { target: { value: '2026-11-17T13:10:00' } })
  await user.type(screen.getByLabelText(T.reason), 'SENTETIK araç çıkışı'); return user
}
const commands = () => requests.filter((request) => request.init?.method === 'POST')

describe('transfer departure genuine candidates and closed source states', () => {
  it('requires the narrow departure capability before either owner read', () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [] } }
    mount(); expect(screen.getByText(/Yetkiniz yok/)).toBeTruthy(); expect(requests).toHaveLength(0)
  })
  it('shows a pending real candidate read with no GUID-entry replacement', () => {
    candidateReply = () => new Promise(() => {})
    mount(); expect(screen.getByText(/Yükleniyor/)).toBeTruthy()
    expect(screen.queryByRole('textbox')).toBeNull(); expect(commands()).toHaveLength(0)
  })
  it('shows an actual empty source without offering a fabricated transfer', async () => {
    candidateReply = async () => json({ ...choices(), items: [] })
    mount(); expect(await screen.findByText(T.empty)).toBeTruthy()
    expect(screen.queryByLabelText(T.transfer)).toBeNull(); expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })
  it('selects real warehouse names, exposes recorded facts and offers only physical event recording', async () => {
    mount(); await selectTransfer()
    expect(screen.getByRole('option', { name: /DEP-01.*SENTETIK kaynak.*DEP-02.*SENTETIK hedef/ })).toBeTruthy()
    expect(screen.getAllByText(T.notRecorded)).toHaveLength(2)
    expect((screen.getByLabelText(T.kind) as HTMLSelectElement).value).toBe('')
    expect(screen.queryByRole('button', { name: /GİB|gönderildi|iptal/i })).toBeNull()
    expect(requests.every((request) => request.url.startsWith('/api/v1/logistics/transfer-departures'))).toBe(true)
  })
  it.each<readonly [number | 'malformed' | 'offline', string]>([
    [401, 'Oturum sonlandı'], [403, 'Yetkiniz yok'], [404, 'Bulunamadı'], [409, 'Çakışma'],
    [503, 'Servis geçici olarak kullanılamıyor'], ['malformed', 'Beklenmeyen yanıt'], ['offline', 'Bağlantı yok'],
  ])('keeps candidate failure %s distinct and prevents a write', async (kind, message) => {
    if (kind === 'offline') { vi.stubGlobal('navigator', { onLine: false }); candidateReply = async () => { throw new TypeError('SENTETIK offline') } }
    else candidateReply = async () => json(kind === 'malformed' ? { ...choices(), companyId: OTHER } : {}, typeof kind === 'number' ? kind : 200)
    mount(); expect(await screen.findByText(new RegExp(message))).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    if (kind === 401 || kind === 403) expect(security.report).toHaveBeenCalledWith(kind, expect.anything())
  })
  it.each(['legacy', 'closed'] as const)('blocks %s without inventing an explicit not-linked association', async (kind) => {
    factsReply = async () => json(kind === 'legacy' ? { ...facts(), association: 0, version: null, sourceVersion: `${SOURCE}/legacy-unknown` } : { ...facts(), noFurtherExit: true })
    mount(); const user = userEvent.setup(); await screen.findByLabelText(T.transfer)
    await user.selectOptions(screen.getByLabelText(T.transfer), TRANSFER)
    expect(await screen.findByText(kind === 'legacy' ? T.legacy : T.closed)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull(); expect(commands()).toHaveLength(0)
  })
  it.each(['unavailable', 'invalid-official'] as const)('keeps %s proof closed instead of presenting a usable departure', async (kind) => {
    factsReply = async () => kind === 'unavailable' ? json({}, 503) : json({ ...facts(), association: 2 })
    mount(); const user = userEvent.setup(); await screen.findByLabelText(T.transfer)
    await user.selectOptions(screen.getByLabelText(T.transfer), TRANSFER)
    expect(await screen.findByText(kind === 'unavailable' ? /Servis geçici olarak kullanılamıyor/ : /Beklenmeyen yanıt/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })
  it('displays real official evidence without offering a GIB-send command', async () => {
    factsReply = async () => json({ ...facts(), association: 2, officialDocument: { companyId: COMPANY, transferId: TRANSFER,
      officialDocumentId: OTHER, version: 7, isLinked: true, linkedAtUtc: '2026-11-17T08:00:00Z', isSentToGib: true,
      sentAtUtc: '2026-11-17T08:30:00Z', cancelConfirmationVersion: null } })
    mount(); const user = userEvent.setup(); await screen.findByLabelText(T.transfer)
    await user.selectOptions(screen.getByLabelText(T.transfer), TRANSFER)
    expect(await screen.findByText(new RegExp(T.gibSent))).toBeTruthy()
    expect(screen.queryByRole('button', { name: /GİB/ })).toBeNull()
  })
  it('disables only the already recorded event and stops offering a form when both physical facts exist', async () => {
    factsReply = async () => json({ ...facts(), exitWeighingAtUtc: '2026-11-17T09:00:00Z' })
    mount(); await selectTransfer()
    expect((screen.getByRole('option', { name: T.weighing }) as HTMLOptionElement).disabled).toBe(true)
    expect((screen.getByRole('option', { name: T.vehicle }) as HTMLOptionElement).disabled).toBe(false)
    factsReply = async () => json({ ...facts(), exitWeighingAtUtc: '2026-11-17T09:00:00Z', vehicleExitAtUtc: '2026-11-17T09:05:00Z' })
    await userEvent.setup().click(screen.getByRole('button', { name: T.refresh }))
    await screen.findByText(T.bothRecorded)
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })
})

describe('transfer departure explicit events and retained outcome', () => {
  it('posts the actual departure revision and explicit vehicle occurrence with no weighing payload or computed quantity', async () => {
    mount(); const user = await vehicleForm(); await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.saved)).toBeTruthy()
    const request = commands()[0]; const body = JSON.parse(String(request.init?.body))
    expect(body).toEqual({ expectedVersion: '1', kind: 2, occurredAtUtc: new Date('2026-11-17T13:10:00').toISOString(), reason: 'SENTETIK araç çıkışı', weighing: null })
    expect(request.url).toBe(`/api/v1/logistics/transfer-departures/${TRANSFER}`)
    expect(new Headers(request.init?.headers).get('Idempotency-Key')).toBe('transfer-departure-synthetic-1')
    expect(new Headers(request.init?.headers).get('X-Company-Id')).toBe(COMPANY)
    expect(body).not.toHaveProperty('netKg')
  })
  it('requires both raw weighings and direction and sends them without client net calculation', async () => {
    mount(); const user = await selectTransfer(); await user.selectOptions(screen.getByLabelText(T.kind), '1')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(screen.getByText(T.invalid)).toBeTruthy(); expect(commands()).toHaveLength(0)
    fireEvent.change(screen.getByLabelText(T.occurred), { target: { value: '2026-11-17T13:10:00' } })
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK tartı')
    await user.selectOptions(screen.getByLabelText(T.direction), '2')
    await user.type(screen.getByLabelText(T.firstKg), '15.250'); await user.type(screen.getByLabelText(T.secondKg), '12.125')
    await user.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.saved)
    expect(JSON.parse(String(commands()[0].init?.body)).weighing).toEqual({ direction: 2, firstKg: '15.250', secondKg: '12.125' })
    expect(JSON.parse(String(commands()[0].init?.body))).not.toHaveProperty('netKg')
  })
  it('retains an unavailable request and key across route departure and retries without another transfer selection', async () => {
    let sends = 0; commandReply = async (request) => ++sends === 1 ? json({}, 503) : success(request, true)
    const first = mount(); const user = await vehicleForm(); await user.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.unknown)
    expect((screen.getByLabelText(T.transfer) as HTMLSelectElement).disabled).toBe(true)
    first.unmount(); shell = { ...shell, routeEntryGeneration: 2 }; mount()
    await screen.findByText(T.unknown); await user.click(screen.getByRole('button', { name: T.retry }))
    await screen.findByText(T.replayed)
    expect(commands()).toHaveLength(2); expect(commands()[1].init?.body).toBe(commands()[0].init?.body)
    expect(new Headers(commands()[1].init?.headers).get('Idempotency-Key')).toBe(new Headers(commands()[0].init?.headers).get('Idempotency-Key'))
  })
  it('keeps a prior unknown frozen when a later permission or unproven version refusal arrives', async () => {
    let sends = 0; commandReply = async () => ++sends === 1 ? json({}, 503) : json({ code: 'logistics_transfer_departure_version_conflict' }, 409)
    mount(); const user = await vehicleForm(); await user.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.unknown); await user.click(screen.getByRole('button', { name: T.retry }))
    await screen.findByText(/Çakışma/)
    expect(screen.getByText(T.unknown)).toBeTruthy(); expect((screen.getByLabelText(T.transfer) as HTMLSelectElement).disabled).toBe(true)
  })
  it('refetches facts on a fresh definite version conflict without claiming a saved record', async () => {
    commandReply = async () => json({ code: 'logistics_transfer_departure_version_conflict' }, 409)
    mount(); const user = await vehicleForm(); await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(/Çakışma/)).toBeTruthy(); expect(screen.queryByText(T.saved)).toBeNull()
    await waitFor(() => expect(requests.filter((request) => request.url.endsWith(TRANSFER))).toHaveLength(3))
  })
  it('does not reveal a former company fact after an authority/context change and aborts the old read', async () => {
    let resolve!: (value: Response) => void; factsReply = () => new Promise((done) => { resolve = done })
    const page = mount(); const user = userEvent.setup(); await screen.findByLabelText(T.transfer)
    await user.selectOptions(screen.getByLabelText(T.transfer), TRANSFER)
    const signal = requests.find((request) => request.url.endsWith(TRANSFER))?.init?.signal
    candidateReply = async () => json({ companyId: OTHER, items: [], windowFull: false })
    shell = { ...shell, committedContextRevision: 2, activeContext: { ...shell.activeContext!, companyId: OTHER } }; page.refresh()
    await screen.findByText(T.empty)
    await act(async () => { resolve(json({ ...facts(), noFurtherExit: true })) })
    expect(signal?.aborted).toBe(true); expect(screen.queryByText(T.closed)).toBeNull(); expect(screen.queryByLabelText(T.kind)).toBeNull()
  })
  it('hides a late former-session write outcome while retaining its original attempt for that session', async () => {
    let resolve!: (value: Response) => void; commandReply = () => new Promise((done) => { resolve = done })
    const page = mount(); const user = await vehicleForm(); await user.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.pending); const original = commands()[0]
    shell = { ...shell, sessionEpoch: 2 }; page.refresh()
    await act(async () => { resolve(success(original)) })
    expect(screen.queryByText(T.saved)).toBeNull(); expect(screen.queryByText(T.pending)).toBeNull(); expect(screen.queryByText(T.unknown)).toBeNull()
  })
})

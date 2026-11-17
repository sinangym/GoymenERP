/** Synthetic HTTP/DOM source tests; no Inventory SQL, real permission or browser acceptance is claimed. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { initialShellState, type ShellState } from '../shell/shellState'
import { TransferPlanPanel } from './TransferPlanPanel'
import { InventoryTransferPage } from './InventoryTransferPage'
import { resetTransferPlanAttemptsForTests, TRANSFER_PLAN_CAPABILITY, type PlanTransferRequest,
  type TransferPlanDestination, type TransferPlanLabel } from './transferPlanApi'
import { TRANSFER_PLAN_TEXT as T } from './transferPlanText'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const LOCATION = '22222222-2222-4222-8222-222222222222'
const DESTINATION = '33333333-3333-4333-8333-333333333333'
const LABEL = '44444444-4444-4444-8444-444444444444'
const LABEL2 = '55555555-5555-4555-8555-555555555555'
const PRODUCT = '66666666-6666-4666-8666-666666666666'
const PARTY = '77777777-7777-4777-8777-777777777777'
const SITE = '88888888-8888-4888-8888-888888888888'
const ACTOR = 'SENTETIK-transfer-actor'
const security = vi.hoisted(() => ({ capture: vi.fn(() => ({ companyId: '11111111-1111-4111-8111-111111111111', sessionEpoch: 1 })), report: vi.fn() }))
let shell: ShellState
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => security }))
const label = (): TransferPlanLabel => ({ labelId: LABEL, labelCode: 'SENTETIK-001', labelVersion: '7', productId: PRODUCT, productVersion: null,
  sourceLocationId: LOCATION, sourceCustodianKind: 'Company', sourceCustodianId: COMPANY,
  physicalKg: '10.125', blockedPhysicalKg: '2.1', activeAllocationKg: '3', documentStatus: 'PendingDocuments' })
const target = (): TransferPlanDestination => ({ locationId: DESTINATION, locationCode: 'SENTETIK-HEDEF', locationName: 'Sentetik hedef depo',
  siteId: SITE, siteCode: 'SENTETIK-TESIS', siteName: 'Sentetik tesis', locationVersion: 9, custodianKind: 'Counterparty', custodianId: PARTY })
const json = (raw: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(raw), { status, headers })
interface Request { readonly url: string; readonly init?: RequestInit }
const responseFrame = (request: Request, items: readonly unknown[]) => ({ companyId: new Headers(request.init?.headers).get('X-Company-Id'),
  query: new URL(request.url, 'https://synthetic.invalid').searchParams.get('q'), limit: 25, items, searchWindowFull: false })
const planned = (request: PlanTransferRequest) => ({ transferId: request.transferId, companyId: COMPANY, version: '1', status: 'Planned',
  sourceLocationId: request.sourceLocationId, sourceCustodianKind: request.sourceCustodianKind, sourceCustodianId: request.sourceCustodianId,
  destinationLocationId: request.destinationLocationId, destinationCustodianKind: request.destinationCustodianKind, destinationCustodianId: request.destinationCustodianId,
  plannedAtUtc: request.plannedAtUtc, dispatchedAtUtc: null, lines: request.lines.map((line) => ({ ...line, actualShippedKg: null, differenceKg: null, status: 'Planned' })) })
let requests: Request[]; let sourceRows: TransferPlanLabel[]; let targetRows: TransferPlanDestination[]
let labelReply: (request: Request) => Promise<Response>; let destinationReply: (request: Request) => Promise<Response>
let commandReply: (request: Request) => Promise<Response>; let listReply: (request: Request) => Promise<Response>
let onPlanned: ReturnType<typeof vi.fn<(transferId: string) => void>>
const clients: QueryClient[] = []
beforeEach(() => {
  requests = []; sourceRows = [label()]; targetRows = [target()]; onPlanned = vi.fn<(transferId: string) => void>(); let nextId = 0
  shell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
    activeContext: { userId: ACTOR, scopeMode: 'single', companyId: COMPANY, capabilityGeneration: 'g1', capabilities: [TRANSFER_PLAN_CAPABILITY] } }
  labelReply = async (request) => json(responseFrame(request, sourceRows))
  destinationReply = async (request) => json(responseFrame(request, targetRows))
  commandReply = async (request) => json(planned(JSON.parse(String(request.init?.body)) as PlanTransferRequest), 201)
  listReply = async () => json({}, 403)
  vi.stubGlobal('crypto', { randomUUID: () => `aaaaaaaa-aaaa-4aaa-8aaa-${String(++nextId).padStart(12, '0')}` })
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = { url: String(input), init }; requests.push(request)
    if (init?.method === 'POST') return commandReply(request)
    if (request.url.includes('/transfer-plan-candidates/destinations')) return destinationReply(request)
    if (request.url.includes('/transfer-plan-candidates')) return labelReply(request)
    return listReply(request)
  }))
  vi.clearAllMocks()
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()); resetTransferPlanAttemptsForTests(); vi.unstubAllGlobals() })
function mount(page = false) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, refetchOnWindowFocus: false } } }); clients.push(client)
  const element = () => <QueryClientProvider client={client}><MemoryRouter>{page ? <InventoryTransferPage /> : <TransferPlanPanel onPlanned={onPlanned} />}</MemoryRouter></QueryClientProvider>
  const result = render(element()); return { ...result, refresh: () => result.rerender(element()) }
}
const commands = () => requests.filter((request) => request.init?.method === 'POST')
async function choosePlan(codes = ['SENTETIK-001']) {
  const user = userEvent.setup()
  for (const code of codes) await user.click(await screen.findByRole('checkbox', { name: `${T.select}: ${code}` }))
  await waitFor(() => expect(screen.getByRole('combobox', { name: T.destination }).matches(':disabled')).toBe(false))
  await user.selectOptions(screen.getByRole('combobox', { name: T.destination }), DESTINATION)
  fireEvent.change(screen.getByLabelText(T.plannedAt), { target: { value: '2026-11-17T12:00:00' } })
}
async function submit() { await userEvent.setup().click(screen.getByRole('button', { name: T.submit })) }

describe('actual transfer plan candidates and explicit write capability', () => {
  it('does not infer transfer-write from report read and makes no candidate request without the exact capability', () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: ['inventory.stock-positions.list'] } }
    mount(); expect(screen.queryByRole('heading', { name: T.heading })).toBeNull(); expect(requests).toHaveLength(0)
  })
  it('does not read or write without a single authorized company', () => {
    shell = { ...shell, activeContext: null }; mount()
    expect(screen.getByText(/Yetkili şirket yok/)).toBeTruthy(); expect(requests).toHaveLength(0)
  })
  it('keeps source loading distinct and blocks submission until both genuine owner reads return', () => {
    labelReply = () => new Promise(() => {}); destinationReply = () => new Promise(() => {})
    mount(); expect(screen.getAllByText(/Yükleniyor/)).toHaveLength(2)
    expect(screen.getByRole('button', { name: T.submit }).matches(':disabled')).toBe(true)
    expect(commands()).toHaveLength(0)
  })
  it.each<readonly [number | 'malformed' | 'offline', string]>([
    [401, 'Oturum sonlandı'], [403, 'Yetkiniz yok'], [404, 'Bulunamadı'], [409, 'Çakışma'], [503, 'Servis geçici olarak kullanılamıyor'],
    ['malformed', 'Beklenmeyen yanıt'], ['offline', 'Bağlantı yok'],
  ])('does not substitute an empty source or enable writing on label read %s', async (kind, message) => {
    if (kind === 'offline') { vi.stubGlobal('navigator', { onLine: false }); labelReply = async () => { throw new TypeError('SENTETIK offline') } }
    else labelReply = async (request) => json(kind === 'malformed' ? responseFrame(request, [{ ...label(), labelVersion: 7 }]) : {}, typeof kind === 'number' ? kind : 200)
    mount(); expect(await screen.findByText(new RegExp(message))).toBeTruthy()
    expect(screen.getByRole('button', { name: T.submit }).matches(':disabled')).toBe(true)
    expect(screen.queryByText(T.noLabels)).toBeNull(); expect(commands()).toHaveLength(0)
    if (kind === 401 || kind === 403) expect(security.report).toHaveBeenCalledWith(kind, expect.anything())
  })
  it('shows real empty source and empty destination separately without accepting a typed GUID substitute', async () => {
    sourceRows = []; targetRows = []; mount()
    expect(await screen.findByText(T.noLabels)).toBeTruthy(); expect(await screen.findByText(T.noDestinations)).toBeTruthy()
    await submit(); expect(await screen.findByText(T.invalid)).toBeTruthy(); expect(commands()).toHaveLength(0)
    expect(screen.queryByRole('textbox')).toBeNull()
  })
  it('does not enable a plan from genuine labels when the destination owner is unavailable', async () => {
    destinationReply = async () => json({}, 503)
    mount(); await screen.findByRole('checkbox', { name: `${T.select}: SENTETIK-001` })
    expect(await screen.findByText(/Servis geçici olarak kullanılamıyor/)).toBeTruthy()
    expect(screen.getByRole('button', { name: T.submit }).matches(':disabled')).toBe(true)
    expect(screen.queryByText(T.noDestinations)).toBeNull(); expect(commands()).toHaveLength(0)
  })
  it('uses actual label selection full physical text source versions and the server-owned destination custodian', async () => {
    sourceRows.push({ ...label(), labelId: LABEL2, labelCode: 'SENTETIK-002', labelVersion: '8', physicalKg: '4.025' })
    mount(); await choosePlan(['SENTETIK-001', 'SENTETIK-002']); await submit(); await screen.findByText(T.saved)
    const body = JSON.parse(String(commands()[0].init?.body)) as PlanTransferRequest
    expect(body.lines.map((line) => [line.labelId, line.expectedLabelVersion, line.plannedKg])).toEqual([[LABEL, '7', '10.125'], [LABEL2, '8', '4.025']])
    expect(body).toMatchObject({ sourceLocationId: LOCATION, sourceCustodianKind: 'Company', sourceCustodianId: COMPANY,
      destinationLocationId: DESTINATION, destinationCustodianKind: 'Counterparty', destinationCustodianId: PARTY })
    expect(body).not.toHaveProperty('locationVersion'); expect(body).not.toHaveProperty('totalKg')
    expect(onPlanned).toHaveBeenCalledWith(body.transferId); expect(commands()).toHaveLength(1)
    expect(screen.getByText(body.transferId)).toBeTruthy()
    expect(screen.queryByRole('spinbutton')).toBeNull()
  })
  it.each(['location', 'custodian'] as const)('does not combine a second label from another %s proof', async (change) => {
    sourceRows.push({ ...label(), labelId: LABEL2, labelCode: 'SENTETIK-002', ...(change === 'location' ? { sourceLocationId: DESTINATION } : { sourceCustodianId: PARTY }) })
    mount(); const user = userEvent.setup()
    await user.click(await screen.findByRole('checkbox', { name: `${T.select}: SENTETIK-001` }))
    await user.click(screen.getByRole('checkbox', { name: `${T.select}: SENTETIK-002` }))
    expect(await screen.findByText(T.sourceMismatch)).toBeTruthy()
    expect((screen.getByRole('checkbox', { name: `${T.select}: SENTETIK-002` }) as HTMLInputElement).checked).toBe(false)
    expect(commands()).toHaveLength(0)
  })
  it('requires explicit source target and plan time before any IDs or command are submitted', async () => {
    mount(); await screen.findByRole('checkbox', { name: `${T.select}: SENTETIK-001` })
    await waitFor(() => expect(screen.getByRole('button', { name: T.submit }).matches(':disabled')).toBe(false)); await submit()
    expect(await screen.findByText(T.invalid)).toBeTruthy(); expect(commands()).toHaveLength(0)
  })
  it('searches actual labels and destinations and clears a formerly selected row rather than carrying stale proof across windows', async () => {
    mount(); const user = userEvent.setup(); await user.click(await screen.findByRole('checkbox', { name: `${T.select}: SENTETIK-001` }))
    await user.type(screen.getByRole('searchbox', { name: T.labelQuery }), 'SENTETIK-002')
    sourceRows = [{ ...label(), labelId: LABEL2, labelCode: 'SENTETIK-002' }]
    const sourceForm = screen.getByRole('searchbox', { name: T.labelQuery }).closest('form')!
    fireEvent.submit(sourceForm); await screen.findByRole('checkbox', { name: `${T.select}: SENTETIK-002` })
    expect(requests.some((request) => request.url.includes('q=SENTETIK-002'))).toBe(true)
    expect((screen.getByRole('checkbox', { name: `${T.select}: SENTETIK-002` }) as HTMLInputElement).checked).toBe(false)
    await user.type(screen.getByRole('searchbox', { name: T.destinationQuery }), 'Sentetik hedef')
    fireEvent.submit(screen.getByRole('searchbox', { name: T.destinationQuery }).closest('form')!)
    await waitFor(() => expect(requests.some((request) => request.url.includes('/destinations?limit=25&q=Sentetik+hedef'))).toBe(true))
    expect(commands()).toHaveLength(0)
  })
  it('detects changed selected label versions on an owner refresh and requires reselection', async () => {
    mount(); await choosePlan(); sourceRows = [{ ...label(), labelVersion: '8' }]
    await act(async () => { await clients[0].invalidateQueries({ predicate: (query) => query.queryKey.includes('transfer-plan-labels') }) })
    expect(await screen.findByText(T.stale)).toBeTruthy(); expect(screen.getByRole('button', { name: T.submit }).matches(':disabled')).toBe(true)
    await submit(); expect(commands()).toHaveLength(0)
    await userEvent.setup().click(screen.getByRole('button', { name: T.refresh }))
    await waitFor(() => expect((screen.getByRole('checkbox', { name: `${T.select}: SENTETIK-001` }) as HTMLInputElement).checked).toBe(false))
  })
  it('does not silently replace a selected destination proof with its changed location version', async () => {
    mount(); await choosePlan(); targetRows = [{ ...target(), locationVersion: 10 }]
    await act(async () => { await clients[0].invalidateQueries({ predicate: (query) => query.queryKey.includes('transfer-plan-destinations') }) })
    expect(await screen.findByText(T.stale)).toBeTruthy()
    expect(screen.getByRole('button', { name: T.submit }).matches(':disabled')).toBe(true); expect(commands()).toHaveLength(0)
  })
})

describe('unknown transfer plan outcome and context-safe retry', () => {
  it('retains exactly the transfer IDs body and key over route unmount and resolves only positive original replay', async () => {
    let calls = 0; commandReply = async (request) => ++calls === 1 ? json({}, 503) : json(planned(JSON.parse(String(request.init?.body)) as PlanTransferRequest), 200,
      { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original' })
    const page = mount(); await choosePlan(); await submit(); await screen.findByText(T.unknown)
    expect(screen.getByRole('button', { name: T.submit }).matches(':disabled')).toBe(true)
    page.unmount(); shell = { ...shell, routeEntryGeneration: 2 }; mount()
    await screen.findByText(T.unknown); await userEvent.setup().click(screen.getByRole('button', { name: T.retry })); await screen.findByText(T.replayed)
    expect(commands()).toHaveLength(2); expect(commands()[1].init?.body).toBe(commands()[0].init?.body)
    expect(new Headers(commands()[1].init?.headers).get('Idempotency-Key')).toBe(new Headers(commands()[0].init?.headers).get('Idempotency-Key'))
    expect(onPlanned).toHaveBeenCalledTimes(1)
  })
  it('does not unlock an uncertain original plan after a later conflict', async () => {
    let sends = 0; commandReply = async () => ++sends === 1 ? json({}, 503) : json({ code: 'idempotency_conflict' }, 409)
    mount(); await choosePlan(); await submit(); await screen.findByText(T.unknown)
    await userEvent.setup().click(screen.getByRole('button', { name: T.retry })); await screen.findByText(/Çakışma/)
    expect(screen.getByText(T.unknown)).toBeTruthy(); expect(onPlanned).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: T.submit }).matches(':disabled')).toBe(true)
  })
  it('keeps a malformed success with another company unknown rather than refreshing the actual list', async () => {
    commandReply = async (request) => json({ ...planned(JSON.parse(String(request.init?.body)) as PlanTransferRequest), companyId: PARTY }, 201)
    mount(); await choosePlan(); await submit(); await screen.findByText(T.unknown)
    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy(); expect(onPlanned).not.toHaveBeenCalled()
  })
  it('shows a fresh version conflict without claiming plan success or silently retrying', async () => {
    commandReply = async () => json({ code: 'inventory_transfer_label_version_conflict' }, 409)
    mount(); await choosePlan(); await submit(); await screen.findByText(/Çakışma/)
    expect(screen.queryByText(T.saved)).toBeNull(); expect(screen.queryByText(T.unknown)).toBeNull()
    expect(onPlanned).not.toHaveBeenCalled(); expect(commands()).toHaveLength(1)
  })
  it('aborts former-company reads and never renders their late candidate under the next company', async () => {
    let resolve!: (response: Response) => void; labelReply = () => new Promise((done) => { resolve = done })
    const page = mount(); const old = requests.find((request) => request.url.includes('/transfer-plan-candidates?'))!
    labelReply = async (request) => json(responseFrame(request, [{ ...label(), labelCode: 'SENTETIK-YENI', sourceCustodianId: PARTY }]))
    shell = { ...shell, committedContextRevision: 2, activeContext: { ...shell.activeContext!, companyId: PARTY } }; page.refresh()
    await screen.findByRole('checkbox', { name: `${T.select}: SENTETIK-YENI` })
    await act(async () => { resolve(json(responseFrame(old, [label()]))) })
    expect(old.init?.signal?.aborted).toBe(true); expect(screen.queryByRole('checkbox', { name: `${T.select}: SENTETIK-001` })).toBeNull()
  })
  it.each(['actor', 'session', 'company'] as const)('does not apply a former %s successful write to a newly bound workspace', async (change) => {
    let resolve!: (response: Response) => void; commandReply = () => new Promise((done) => { resolve = done })
    const page = mount(); await choosePlan(); await submit(); await screen.findByText(T.pending)
    const payload = JSON.parse(String(commands()[0].init?.body)) as PlanTransferRequest
    shell = change === 'session' ? { ...shell, sessionEpoch: 2 } : { ...shell, committedContextRevision: 2,
      activeContext: { ...shell.activeContext!, ...(change === 'actor' ? { userId: 'SENTETIK-next-actor' } : { companyId: PARTY }) } }
    page.refresh(); await act(async () => { resolve(json(planned(payload), 201)) })
    expect(screen.queryByText(T.saved)).toBeNull(); expect(screen.queryByText(T.unknown)).toBeNull(); expect(onPlanned).not.toHaveBeenCalled()
    expect(commands()).toHaveLength(1)
  })
})

describe('transfer page preserves write flow independently of report read', () => {
  it('mounts actual planning while the transfer list is still loading', async () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [TRANSFER_PLAN_CAPABILITY, 'inventory.stock-positions.list'] } }
    listReply = () => new Promise(() => {}); mount(true)
    expect(screen.getByRole('heading', { name: T.heading })).toBeTruthy()
    expect(await screen.findByRole('checkbox', { name: `${T.select}: SENTETIK-001` })).toBeTruthy()
  })
  it('retains actual success summary even when list and new detail reads are forbidden', async () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [TRANSFER_PLAN_CAPABILITY, 'inventory.stock-positions.list'] } }
    mount(true); await choosePlan(); await submit(); await screen.findByText(T.saved)
    const payload = JSON.parse(String(commands()[0].init?.body)) as PlanTransferRequest
    expect(screen.getByText(payload.transferId)).toBeTruthy()
    await waitFor(() => expect(requests.some((request) => request.url.endsWith(`/inventory/transfers/${payload.transferId}`))).toBe(true))
    expect(screen.getAllByText(/Yetkiniz yok/).length).toBeGreaterThan(0)
    expect(screen.getByText(T.saved)).toBeTruthy(); expect(commands()).toHaveLength(1)
  })
})

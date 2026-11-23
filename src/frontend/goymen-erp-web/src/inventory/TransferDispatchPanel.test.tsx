/** Synthetic HTTP/DOM source tests. No real authorization, Inventory movement, GIB or browser acceptance. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { initialShellState, type ShellState } from '../shell/shellState'
import { TransferDispatchPanel } from './TransferDispatchPanel'
import { resetTransferDispatchAttemptsForTests, TRANSFER_DISPATCH_CAPABILITIES as CAP, type TransferOutgoingDocument, type TransferOutgoingRead } from './transferDispatchApi'
import type { TransferDetail } from './transferContract'
import { TRANSFER_DISPATCH_TEXT as T } from './transferDispatchText'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const TRANSFER = '22222222-2222-4222-8222-222222222222'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const OTHER = '44444444-4444-4444-8444-444444444444'
const DOCUMENT = '55555555-5555-4555-8555-555555555555'
const LINE = '66666666-6666-4666-8666-666666666666'
const LINE2 = '77777777-7777-4777-8777-777777777777'
const LABEL = '88888888-8888-4888-8888-888888888888'
const ACTOR = 'SENTETIK-actor'
const security = vi.hoisted(() => ({ capture: vi.fn(() => ({ companyId: '11111111-1111-4111-8111-111111111111', sessionEpoch: 1 })), report: vi.fn() }))
let shell: ShellState
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => security }))
const sourceTransfer = (): TransferDetail => ({ companyId: COMPANY, transferId: TRANSFER, version: '1', status: 'Planned', sourceLocationId: LOCATION,
  destinationLocationId: OTHER, sourceCustodianKind: 'Company', sourceCustodianId: COMPANY, destinationCustodianKind: 'Company', destinationCustodianId: COMPANY,
  plannedAtUtc: '2026-11-17T08:00:00Z', dispatchedAtUtc: null, lines: [
    { lineId: LINE, labelId: LABEL, expectedLabelVersion: '3', plannedKg: '5', actualShippedKg: null, differenceKg: null, status: 'Planned' },
    { lineId: LINE2, labelId: OTHER, expectedLabelVersion: '4', plannedKg: '7', actualShippedKg: null, differenceKg: null, status: 'Planned' },
  ] })
const draft = (documentId = DOCUMENT): TransferOutgoingDocument => ({ companyId: COMPANY, documentId, transferId: TRANSFER,
  orderedTransferLineIds: [LINE, LINE2], eDispatchNumber: null, status: 'Draft', version: 1, createdBy: ACTOR, createdAtUtc: '2026-11-17T09:00:00Z',
  effectiveBy: null, effectiveAtUtc: null, cancelledBy: null, cancelledAtUtc: null, cancellationReason: null })
const effective = (): TransferOutgoingDocument => ({ ...draft(), version: 2, status: 'Effective', effectiveBy: ACTOR, effectiveAtUtc: '2026-11-17T09:05:00Z' })
const json = (raw: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(raw), { status, headers })
const recorded = (document: TransferOutgoingDocument, replay = false) => ({ document, isReplay: replay, originalCorrelationId: 'synthetic-original' })
interface Request { readonly url: string; readonly init?: RequestInit }
let requests: Request[]; let owner: TransferOutgoingRead | null; let changed: ReturnType<typeof vi.fn<() => void>>
let currentTransfer: TransferDetail
let readReply: (request: Request) => Promise<Response>; let commandReply: (request: Request) => Promise<Response>
const clients: QueryClient[] = []
beforeEach(() => {
  requests = []; owner = null; changed = vi.fn<() => void>(); currentTransfer = sourceTransfer(); let id = 0
  shell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
    activeContext: { userId: ACTOR, scopeMode: 'single', companyId: COMPANY, capabilityGeneration: 'g1', capabilities: Object.values(CAP) } }
  readReply = async () => owner === null ? json({}, 404) : json(owner)
  commandReply = async (request) => {
    const payload = JSON.parse(String(request.init?.body))
    if (request.url.endsWith('/dispatch')) return json({ ...currentTransfer, version: '2', status: 'InTransit', dispatchedAtUtc: payload.dispatchedAtUtc,
      lines: currentTransfer.lines.map((line) => ({ ...line, actualShippedKg: line.plannedKg, differenceKg: '0', status: 'InTransit' })) }, 201)
    if (request.url.endsWith('/finalize')) owner = { document: { ...owner!.document, version: 2, status: 'Effective', effectiveBy: ACTOR, effectiveAtUtc: '2026-11-17T09:05:00Z' }, association: 1, departureVersion: 1 }
    else if (request.url.endsWith('/cancel')) owner = { ...owner!, document: { ...owner!.document, version: owner!.document.version + 1,
      status: 'Cancelled', cancelledBy: ACTOR, cancelledAtUtc: '2026-11-17T09:10:00Z', cancellationReason: payload.reason } }
    else owner = { document: draft(payload.documentId), association: 1, departureVersion: 1 }
    return json(recorded(owner.document), request.url.endsWith('/transfer-outgoing-documents') ? 201 : 200)
  }
  vi.stubGlobal('crypto', { randomUUID: () => `aaaaaaaa-aaaa-4aaa-8aaa-${String(++id).padStart(12, '0')}` })
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = { url: String(input), init }; requests.push(request)
    return init?.method === 'POST' ? commandReply(request) : readReply(request)
  }))
  vi.clearAllMocks()
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()); resetTransferDispatchAttemptsForTests(); vi.unstubAllGlobals() })
function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, refetchOnWindowFocus: false } } }); clients.push(client)
  const element = () => <QueryClientProvider client={client}><TransferDispatchPanel transfer={currentTransfer} onChanged={changed} /></QueryClientProvider>
  const result = render(element()); return { ...result, refresh: () => result.rerender(element()) }
}
const commands = () => requests.filter((request) => request.init?.method === 'POST')
async function create() { await screen.findByText(T.noDocument); await userEvent.setup().click(screen.getByRole('button', { name: T.create })); await screen.findByText(T.saved) }

describe('transfer outgoing document genuine read and separate capability fences', () => {
  it('does not read the document under an Inventory read or write grant alone', () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [CAP.dispatch] } }
    mount(); expect(screen.getByText(T.noRead)).toBeTruthy(); expect(requests).toHaveLength(0)
  })
  it('keeps read-only access separate from all three Logistics mutations and Inventory dispatch', async () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [CAP.read] } }
    mount(); await screen.findByText(T.noDocument)
    expect(screen.queryByRole('button', { name: T.create })).toBeNull(); expect(commands()).toHaveLength(0)
  })
  it('shows actual missing document as 404 and requires an explicit internal no-link create action', async () => {
    mount(); await screen.findByText(T.noDocument)
    expect(screen.getByText(/Bulunamadı/)).toBeTruthy(); expect(screen.getByText(T.createNote)).toBeTruthy()
    expect(screen.queryByRole('textbox')).toBeNull(); expect(commands()).toHaveLength(0)
    await create(); expect(commands()).toHaveLength(1)
    expect(JSON.parse(String(commands()[0].init?.body))).toMatchObject({ transferId: TRANSFER, orderedTransferLineIds: [LINE, LINE2], eDispatchAssociation: 1, eDispatchNumber: null })
    expect(JSON.parse(String(commands()[0].init?.body)).documentId).toMatch(/^[a-f0-9-]{36}$/)
  })
  it('shows pending read and no create button before the owner answers', () => {
    readReply = () => new Promise(() => {})
    mount(); expect(screen.getByText(/Yükleniyor/)).toBeTruthy(); expect(screen.queryByRole('button', { name: T.create })).toBeNull()
  })
  it.each<readonly [number | 'malformed' | 'offline', string]>([
    [401, 'Oturum sonlandı'], [403, 'Yetkiniz yok'], [409, 'Çakışma'], [503, 'Servis geçici olarak kullanılamıyor'],
    ['malformed', 'Beklenmeyen yanıt'], ['offline', 'Bağlantı yok'],
  ])('blocks writes on owner source failure %s', async (kind, message) => {
    if (kind === 'offline') { vi.stubGlobal('navigator', { onLine: false }); readReply = async () => { throw new TypeError('SENTETIK offline') } }
    else readReply = async () => json(kind === 'malformed' ? { document: { ...draft(), transferId: OTHER }, association: 1, departureVersion: 1 } : {}, typeof kind === 'number' ? kind : 200)
    mount(); expect(await screen.findByText(new RegExp(message))).toBeTruthy(); expect(screen.queryByRole('button', { name: T.create })).toBeNull()
    if (kind === 401 || kind === 403) expect(security.report).toHaveBeenCalledWith(kind, expect.anything())
  })
  it.each([0, 2] as const)('never derives not-linked for actual association %i or enables its finalize/dispatch paths', async (association) => {
    owner = { document: effective(), association, departureVersion: association === 0 ? null : 1 }
    mount(); expect(await screen.findByText(association === 0 ? T.legacy : T.linked)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.dispatch })).toBeNull()
    expect(screen.queryByRole('button', { name: T.finalize })).toBeNull()
    expect(screen.queryByRole('button', { name: /GİB|gönderildi/i })).toBeNull()
  })
  it('preserves a cancelled document and never offers it as a new or effective dispatch source', async () => {
    owner = { document: { ...draft(), version: 2, status: 'Cancelled', cancelledBy: ACTOR, cancelledAtUtc: '2026-11-17T09:10:00Z', cancellationReason: 'SENTETIK önceki iptal' }, association: 1, departureVersion: 1 }
    mount(); await screen.findByText(T.cancelled)
    expect(screen.queryByRole('button', { name: T.dispatch })).toBeNull(); expect(screen.queryByRole('button', { name: T.create })).toBeNull()
  })
})

describe('transfer actual explicit create finalize dispatch and document cancellation', () => {
  it('requires separate user actions for create finalize and dispatch using their genuine refreshed owner versions', async () => {
    mount(); await create(); await screen.findByRole('button', { name: T.finalize })
    expect(commands()).toHaveLength(1); expect(screen.queryByRole('button', { name: T.dispatch })).toBeNull()
    await userEvent.setup().click(screen.getByRole('button', { name: T.finalize }))
    await screen.findByLabelText(T.dispatchedAt)
    expect(commands()).toHaveLength(2); expect(JSON.parse(String(commands()[1].init?.body))).toEqual({ expectedVersion: 1 })
    fireEvent.change(screen.getByLabelText(T.dispatchedAt), { target: { value: '2026-11-17T13:10:00' } })
    await userEvent.setup().click(screen.getByRole('button', { name: T.dispatch }))
    await waitFor(() => expect(changed).toHaveBeenCalledTimes(3))
    expect(commands()).toHaveLength(3)
    const documentId = JSON.parse(String(commands()[0].init?.body)).documentId
    expect(JSON.parse(String(commands()[2].init?.body))).toEqual({ expectedTransferVersion: '1', dispatchedAtUtc: new Date('2026-11-17T13:10:00').toISOString(), outgoingDocumentId: documentId, expectedOutgoingDocumentVersion: '2' })
    expect(commands().map((request) => new Headers(request.init?.headers).get('Idempotency-Key')).every((key, index, keys) => keys.indexOf(key) === index)).toBe(true)
    expect(JSON.parse(String(commands()[2].init?.body))).not.toHaveProperty('actualKg')
    expect((screen.getByRole('button', { name: T.dispatch }).closest('fieldset') as HTMLFieldSetElement).disabled).toBe(true)
  })
  it('does not offer finalize or document cancellation to a creator without those separate capabilities', async () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [CAP.read, CAP.create, CAP.dispatch] } }
    mount(); await create(); await screen.findByText(/Taslak/)
    expect(screen.queryByRole('button', { name: T.finalize })).toBeNull(); expect(screen.queryByRole('button', { name: T.cancel })).toBeNull()
    expect(commands()).toHaveLength(1)
  })
  it('records only document cancellation with its reason and version while a genuine transfer remains InTransit', async () => {
    owner = { document: effective(), association: 1, departureVersion: 1 }
    currentTransfer = { ...sourceTransfer(), status: 'InTransit', version: '2', dispatchedAtUtc: '2026-11-17T09:07:00Z',
      lines: sourceTransfer().lines.map((line) => ({ ...line, status: 'InTransit', actualShippedKg: line.plannedKg, differenceKg: '0' })) }
    mount(); await screen.findByText(T.cancelNote)
    expect(screen.queryByRole('button', { name: T.dispatch })).toBeNull()
    const user = userEvent.setup(); await user.type(screen.getByLabelText(T.cancelReason), 'SENTETIK yalnız belge')
    await user.click(screen.getByRole('button', { name: T.cancel })); await screen.findByText(T.cancelled)
    expect(commands()).toHaveLength(1); expect(commands()[0].url).toBe(`/api/v1/logistics/transfer-outgoing-documents/${DOCUMENT}/cancel`)
    expect(JSON.parse(String(commands()[0].init?.body))).toEqual({ expectedVersion: 2, reason: 'SENTETIK yalnız belge' })
    expect(commands().some((request) => request.url.includes('cancel-dispatch'))).toBe(false)
  })
  it('requires an explicit dispatch timestamp and nonempty document cancellation reason', async () => {
    owner = { document: effective(), association: 1, departureVersion: 1 }
    mount(); await screen.findByLabelText(T.dispatchedAt)
    await userEvent.setup().click(screen.getByRole('button', { name: T.dispatch }))
    expect(screen.getByText(T.invalid)).toBeTruthy(); expect(commands()).toHaveLength(0)
    await userEvent.setup().click(screen.getByRole('button', { name: T.cancel }))
    expect(commands()).toHaveLength(0)
  })
})

describe('transfer flow immutable retry and context isolation', () => {
  it('retains the same new document id body and key across route departure and retries without creating another intent', async () => {
    let sends = 0; let original: TransferOutgoingDocument | null = null
    commandReply = async (request) => {
      const payload = JSON.parse(String(request.init?.body)); original ??= draft(payload.documentId)
      if (++sends === 1) return json({}, 503)
      owner = { document: original, association: 1, departureVersion: 1 }
      return json(recorded(original, true), 201, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original' })
    }
    const first = mount(); await screen.findByText(T.noDocument)
    await userEvent.setup().click(screen.getByRole('button', { name: T.create })); await screen.findByText(T.unknown)
    first.unmount(); shell = { ...shell, routeEntryGeneration: 2 }; mount()
    await screen.findByText(T.unknown); await userEvent.setup().click(screen.getByRole('button', { name: T.retry }))
    await screen.findByText(T.replayed)
    expect(commands()).toHaveLength(2); expect(commands()[1].init?.body).toBe(commands()[0].init?.body)
    expect(new Headers(commands()[1].init?.headers).get('Idempotency-Key')).toBe(new Headers(commands()[0].init?.headers).get('Idempotency-Key'))
    expect(changed).toHaveBeenCalledTimes(1)
  })
  it('keeps another selected transfer blocked without sending the old intent under its identities', async () => {
    commandReply = async () => json({}, 503)
    const page = mount(); await screen.findByText(T.noDocument); await userEvent.setup().click(screen.getByRole('button', { name: T.create })); await screen.findByText(T.unknown)
    currentTransfer = { ...sourceTransfer(), transferId: OTHER }; page.refresh()
    expect(await screen.findByText(T.otherPending)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.retry })).toBeNull(); expect(commands()).toHaveLength(1)
  })
  it('keeps an original unknown frozen on a later 409 and does not refetch or call onChanged without positive proof', async () => {
    let sends = 0; commandReply = async () => ++sends === 1 ? json({}, 503) : json({ code: 'idempotency_conflict' }, 409)
    mount(); await screen.findByText(T.noDocument); await userEvent.setup().click(screen.getByRole('button', { name: T.create })); await screen.findByText(T.unknown)
    await userEvent.setup().click(screen.getByRole('button', { name: T.retry })); await screen.findByText(/Çakışma/)
    expect(screen.getByText(T.unknown)).toBeTruthy(); expect(changed).not.toHaveBeenCalled()
    expect(requests.filter((request) => request.init?.method !== 'POST')).toHaveLength(1)
  })
  it('does not resolve a malformed positive create body for another transfer or issue automatic finalize', async () => {
    commandReply = async () => json(recorded({ ...draft(), transferId: OTHER }), 201)
    mount(); await screen.findByText(T.noDocument); await userEvent.setup().click(screen.getByRole('button', { name: T.create }))
    await screen.findByText(T.unknown); expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    expect(changed).not.toHaveBeenCalled(); expect(commands()).toHaveLength(1)
  })
  it('aborts a former company read and prevents its late document from touching the new company panel', async () => {
    let resolve!: (value: Response) => void; readReply = () => new Promise((done) => { resolve = done })
    const page = mount(); const signal = requests[0].init?.signal
    readReply = async () => json({}, 404)
    shell = { ...shell, committedContextRevision: 2, activeContext: { ...shell.activeContext!, companyId: OTHER } }
    currentTransfer = { ...sourceTransfer(), companyId: OTHER, sourceCustodianId: OTHER, destinationCustodianId: OTHER }; page.refresh()
    await screen.findByText(T.noDocument)
    await act(async () => { resolve(json({ document: draft(), association: 1, departureVersion: 1 })) })
    expect(signal?.aborted).toBe(true); expect(screen.queryByText(DOCUMENT)).toBeNull()
  })
  it('hides an old actor session write outcome and does not refresh the new session from it', async () => {
    let resolve!: (value: Response) => void; commandReply = () => new Promise((done) => { resolve = done })
    const page = mount(); await screen.findByText(T.noDocument); await userEvent.setup().click(screen.getByRole('button', { name: T.create }))
    await screen.findByText(T.pending)
    const id = JSON.parse(String(commands()[0].init?.body)).documentId
    shell = { ...shell, sessionEpoch: 2 }; page.refresh()
    await act(async () => { resolve(json(recorded(draft(id)), 201)) })
    expect(screen.queryByText(T.saved)).toBeNull(); expect(screen.queryByText(T.unknown)).toBeNull(); expect(changed).not.toHaveBeenCalled()
  })
})

/** Synthetic HTTP/DOM tests only; actual SQL, permission, maker-checker and browser evidence remain NOT_RUN. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { initialShellState, type ShellState } from '../shell/shellState'
import { InventoryTransferPage } from './InventoryTransferPage'
import { TransferReceiptPanel } from './TransferReceiptPanel'
import { resetReceiptAttemptsForTests, TRANSFER_RECEIPT_CAPABILITIES as CAP, type ReceiptDocument, type ReceiptFact, type ReceiptPurpose, type ReceiptWork } from './transferReceiptApi'
import { TRANSFER_RECEIPT_TEXT as T } from './transferReceiptText'
import { TRANSFER_DISPATCH_TEXT as DISPATCH_TEXT } from './transferDispatchText'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const TRANSFER = '22222222-2222-4222-8222-222222222222'
const SOURCE = '33333333-3333-4333-8333-333333333333'
const TARGET = '44444444-4444-4444-8444-444444444444'
const LINE = '55555555-5555-4555-8555-555555555555'
const LABEL = '66666666-6666-4666-8666-666666666666'
const DOCUMENT = '77777777-7777-4777-8777-777777777777'
const DOCLINE = '88888888-8888-4888-8888-888888888888'
const MOVEMENT = '99999999-9999-4999-8999-999999999999'
const ACTOR = 'SENTETIK-current-actor'
const OTHER = 'SENTETIK-other-actor'
const AT = '2026-11-17T12:00:00Z'
const security = vi.hoisted(() => ({ capture: vi.fn(() => ({ companyId: '11111111-1111-4111-8111-111111111111', sessionEpoch: 1 })), report: vi.fn() }))
let shell: ShellState
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => security }))
const work = (): ReceiptWork => ({ purpose: 'receive', transfer: { companyId: COMPANY, transferId: TRANSFER, version: '2', status: 'InTransit',
  sourceLocationId: SOURCE, sourceCustodianKind: 'Company', sourceCustodianId: COMPANY, destinationLocationId: TARGET, destinationCustodianKind: 'Company', destinationCustodianId: COMPANY,
  plannedAtUtc: '2026-11-17T09:00:00Z', dispatchedAtUtc: '2026-11-17T10:00:00Z', lines: [{ lineId: LINE, labelId: LABEL, expectedLabelVersion: '7', plannedKg: '10', actualShippedKg: '10', differenceKg: '0', status: 'InTransit' }] },
  facts: { companyId: COMPANY, transferId: TRANSFER, version: '2', status: 'InTransit', receipts: [], losses: [], overageRenewals: [] } })
const draft = (documentId = DOCUMENT): ReceiptDocument => ({ companyId: COMPANY, documentId, transferId: TRANSFER, version: '1', status: 'Draft', sourceTransferVersion: '2', receivedAtUtc: AT,
  lines: [{ documentLineId: DOCLINE, transferLineId: LINE, labelId: LABEL, dispatchedKg: '10', receivedKg: '8', overageReason: null }], createdBy: ACTOR, createdAtUtc: '2026-11-17T12:05:00Z', finalizedBy: null, finalizedAtUtc: null, finalizationReason: null, sourceVersion: 'inventory.transfer-receipt-document.v1' })
const receipt = (overage = false): ReceiptFact => ({ lineId: LINE, dispatchedKg: '10', receivedKg: overage ? '12' : '8', shortageKg: overage ? '0' : '2', overageKg: overage ? '2' : '0', shortageMovementId: overage ? null : MOVEMENT,
  overageReason: overage ? 'SENTETIK fazla' : null, receivedAtUtc: AT, receivedBy: OTHER, overageStatus: overage ? 'Pending' : 'None', overageDecisionReason: null, overageDecidedAtUtc: null, overageDecidedBy: null, overageMovementId: null })
const json = (raw: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(raw), { status, headers })
interface Request { readonly url: string; readonly init?: RequestInit }
const url = (request: Request) => new URL(request.url, 'https://synthetic.invalid')
let owner: ReceiptWork; let documents: ReceiptDocument[]; let requests: Request[]; let selectedTransfer: string | null
let onChanged: ReturnType<typeof vi.fn<(transferId: string) => void>>
let candidateReply: (request: Request) => Promise<Response>; let documentReply: (request: Request) => Promise<Response>
let workReply: (request: Request) => Promise<Response>; let commandReply: (request: Request) => Promise<Response>
const clients: QueryClient[] = []
function candidateFrame(request: Request, ids = [TRANSFER], nextCursor: string | null = null) {
  return { companyId: COMPANY, purpose: url(request).searchParams.get('purpose'), query: url(request).searchParams.get('q'), limit: 25,
    items: ids.map((transferId) => ({ transfer: { transferId, companyId: COMPANY, version: owner.transfer.version, status: owner.transfer.status,
      sourceLocationId: SOURCE, destinationLocationId: TARGET, plannedAtUtc: owner.transfer.plannedAtUtc, dispatchedAtUtc: owner.transfer.dispatchedAtUtc },
    labelCodes: ['SENTETIK-ETIKET'], sourceLocationCode: 'KAYNAK', sourceLocationName: 'Sentetik kaynak', destinationLocationCode: 'HEDEF', destinationLocationName: 'Sentetik hedef' })), searchWindowFull: nextCursor !== null, nextCursor }
}
const docFrame = (items = documents, nextCursor: string | null = null) => ({ companyId: COMPANY, transferId: TRANSFER, items, searchWindowFull: nextCursor !== null, nextCursor })
beforeEach(() => {
  owner = work(); documents = []; requests = []; selectedTransfer = TRANSFER; onChanged = vi.fn<(transferId: string) => void>(); let next = 0
  shell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
    activeContext: { userId: ACTOR, scopeMode: 'single', companyId: COMPANY, capabilityGeneration: 'g1', capabilities: Object.values(CAP) } }
  candidateReply = async (request) => json(candidateFrame(request))
  workReply = async (request) => json({ ...owner, purpose: url(request).searchParams.get('purpose') })
  documentReply = async () => json(docFrame())
  commandReply = async (request) => {
    const payload = JSON.parse(String(request.init?.body))
    if (request.url.endsWith('/transfer-receipt-documents')) {
      const document: ReceiptDocument = { ...draft(payload.documentId), sourceTransferVersion: payload.expectedTransferVersion, receivedAtUtc: payload.receivedAtUtc,
        lines: payload.lines.map((line: { documentLineId: string; transferLineId: string; receivedKg: string; overageReason: string | null }) => ({ ...line, labelId: LABEL, dispatchedKg: '10' })) }
      documents = [...documents, document]; return json({ document, isReplay: false, originalCorrelationId: 'SENTETIK-original' }, 201)
    }
    if (request.url.endsWith('/finalize')) {
      const id = request.url.split('/').at(-2); const prior = documents.find((row) => row.documentId === id)!
      const document: ReceiptDocument = { ...prior, version: '2', status: 'Effective', finalizedBy: ACTOR, finalizedAtUtc: '2026-11-17T12:06:00Z', finalizationReason: payload.reason }
      documents = documents.map((row) => row.documentId === id ? document : row); return json({ document, isReplay: false, originalCorrelationId: 'SENTETIK-original' })
    }
    if (request.url.endsWith(`/receive-lines/${LINE}`)) {
      owner = { ...owner, transfer: { ...owner.transfer, version: '3', status: 'Accepted', lines: owner.transfer.lines.map((line) => ({ ...line, status: 'Accepted' })) },
        facts: { ...owner.facts, version: '3', status: 'Accepted', receipts: [{ ...receipt(), receivedBy: ACTOR, receivedAtUtc: payload.receivedAtUtc, receivedKg: payload.receivedKg }] } }
      return json({ transfer: owner.transfer, facts: owner.facts }, 201)
    }
    if (request.url.endsWith('/overage-decision')) {
      owner = { ...owner, facts: { ...owner.facts, receipts: owner.facts.receipts.map((row) => ({ ...row, overageStatus: 'Rejected', overageDecisionReason: payload.reason, overageDecidedAtUtc: payload.decidedAtUtc, overageDecidedBy: ACTOR })) } }
      return json({ transfer: owner.transfer, facts: owner.facts }, 201)
    }
    if (request.url.endsWith('/loss-decision') || request.url.endsWith('/loss-withdrawal')) {
      owner = { ...owner, facts: { ...owner.facts, losses: owner.facts.losses.map((row) => row.requestNumber === payload.requestNumber ? { ...row,
        status: request.url.endsWith('/loss-withdrawal') ? 'Withdrawn' : 'Rejected', decisionReason: payload.reason, decidedBy: ACTOR, decidedAtUtc: payload.withdrawnAtUtc ?? payload.decidedAtUtc } : row) } }
      return json({ transfer: owner.transfer, facts: owner.facts }, 201)
    }
    return json({}, 409)
  }
  vi.stubGlobal('crypto', { randomUUID: () => `aaaaaaaa-aaaa-4aaa-8aaa-${String(++next).padStart(12, '0')}` })
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = { url: String(input), init }; requests.push(request)
    if (init?.method === 'POST') return commandReply(request)
    if (request.url.includes('/transfer-receipt-work/candidates')) return candidateReply(request)
    if (request.url.includes('/receipt-work?')) return workReply(request)
    if (request.url.includes('/transfer-receipt-documents/by-transfer/')) return documentReply(request)
    return json({}, 503)
  }))
  vi.clearAllMocks()
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()); resetReceiptAttemptsForTests(); vi.unstubAllGlobals() })
function mount(page = false) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, refetchOnWindowFocus: false } } }); clients.push(client)
  const element = () => <QueryClientProvider client={client}><MemoryRouter>{page ? <InventoryTransferPage /> : <TransferReceiptPanel transferId={selectedTransfer} onChanged={onChanged} />}</MemoryRouter></QueryClientProvider>
  const result = render(element()); return { ...result, refresh: () => result.rerender(element()) }
}
const commands = () => requests.filter((request) => request.init?.method === 'POST')
async function purpose(value: ReceiptPurpose) { await userEvent.setup().selectOptions(screen.getByLabelText(T.purpose), value); if (selectedTransfer !== null) await screen.findByRole('table', { name: T.receipts }) }
async function createDraft() {
  await screen.findByText(T.emptyDocuments); const form = screen.getByRole('button', { name: T.create }).closest('form')!
  const user = userEvent.setup(); await user.click(within(form).getByRole('checkbox'))
  await user.type(within(form).getByLabelText(`${T.received} — 1`), '8')
  fireEvent.change(within(form).getByLabelText(T.receivedAt), { target: { value: '2026-11-17T15:00:00' } })
  await user.click(within(form).getByRole('button', { name: T.create }))
}
function pendingLoss() { owner = { ...owner, facts: { ...owner.facts, losses: [{ lineId: LINE, requestNumber: 2, explanation: 'SENTETIK kayıp', requestedAtUtc: '2026-11-17T11:00:00Z', requestedBy: OTHER, status: 'Pending', decisionReason: null, decidedAtUtc: null, decidedBy: null, movementId: null }] } } }

describe('explicit purpose and real transfer selection without a broad report grant', () => {
  it('does not fetch any purpose source until the user chooses an actually advertised purpose', () => {
    mount(); expect(requests).toHaveLength(0); expect(commands()).toHaveLength(0)
    expect(screen.getByRole('combobox', { name: T.purpose })).toBeTruthy()
  })
  it('shows only the exact loss-approve purpose under that capability and never reads receipt documents', async () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [CAP['loss-decision']] } }; pendingLoss()
    mount(); await purpose('loss-decision')
    expect(screen.queryByRole('option', { name: T.purposes.receive })).toBeNull(); expect(screen.getByRole('button', { name: T.lossDecision })).toBeTruthy()
    expect(requests.every((request) => !request.url.includes('/transfer-receipt-documents'))).toBe(true)
  })
  it('opens a real friendly candidate and binds the selected purpose work without user-entered GUIDs', async () => {
    selectedTransfer = null; mount(); await purpose('receive')
    await userEvent.setup().click(await screen.findByRole('button', { name: `${T.open}: SENTETIK-ETIKET` }))
    await screen.findByRole('table', { name: T.receipts }); expect(requests.some((request) => request.url.includes(`/transfers/${TRANSFER}/receipt-work?purpose=receive`))).toBe(true)
    expect(screen.queryByLabelText(/GUID/)).toBeNull(); expect(commands()).toHaveLength(0)
  })
  it('mounts the actual outgoing-document panel under explicit dispatch purpose without aliasing its Logistics read right', async () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [CAP.dispatch] } }
    mount(); await purpose('dispatch'); expect(await screen.findByText(DISPATCH_TEXT.noRead)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.create })).toBeNull()
    expect(requests.some((request) => request.url.includes('/receipt-work?purpose=dispatch'))).toBe(true)
    expect(requests.some((request) => request.url.includes('/logistics/transfer-outgoing-documents'))).toBe(false)
  })
  it.each<readonly [number | 'malformed' | 'offline', string]>([[401, 'Oturum sonlandı'], [403, 'Yetkiniz yok'], [404, 'Bulunamadı'], [409, 'Çakışma'], [503, 'Servis geçici olarak kullanılamıyor'], ['malformed', 'Beklenmeyen yanıt'], ['offline', 'Bağlantı yok']])(
    'keeps actual work failure %s distinct and provides no write form', async (failure, message) => {
      if (failure === 'offline') { vi.stubGlobal('navigator', { onLine: false }); workReply = async () => { throw new TypeError('SENTETIK offline') } }
      else workReply = async () => json(failure === 'malformed' ? { ...work(), facts: { ...work().facts, companyId: TARGET } } : {}, typeof failure === 'number' ? failure : 200)
      mount(); await userEvent.setup().selectOptions(screen.getByLabelText(T.purpose), 'receive')
      expect(await screen.findByText(new RegExp(message))).toBeTruthy(); expect(screen.queryByRole('button', { name: T.create })).toBeNull()
      expect(commands()).toHaveLength(0); if (failure === 401 || failure === 403) expect(security.report).toHaveBeenCalledWith(failure, expect.anything())
    })
  it('keeps the page purpose selector mounted and makes no general list read under write-only access', async () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [CAP.receive] } }; selectedTransfer = null
    mount(true); expect(screen.getByRole('combobox', { name: T.purpose })).toBeTruthy()
    await purpose('receive'); await screen.findByRole('button', { name: `${T.open}: SENTETIK-ETIKET` })
    expect(requests.some((request) => /\/inventory\/transfers(?:\?|$)/.test(request.url))).toBe(false)
  })
})

describe('separate document draft finalize and physical receive', () => {
  it('creates only a draft from weighed input then requires an explicit finalize and a separate exact document-line receive', async () => {
    mount(); await purpose('receive'); await createDraft(); await screen.findByText(T.saved)
    expect(commands()).toHaveLength(1); const created = JSON.parse(String(commands()[0].init?.body))
    expect(created.lines[0]).toMatchObject({ transferLineId: LINE, receivedKg: '8', overageReason: null }); expect(created.expectedTransferVersion).toBe('2')
    expect(created.documentId).toMatch(/^[a-f0-9-]{36}$/)
    const select = screen.getByLabelText(T.document)
    await waitFor(() => expect(within(select).getAllByRole('option')).toHaveLength(2)); await userEvent.setup().selectOptions(select, created.documentId)
    expect(screen.queryByRole('button', { name: T.receive })).toBeNull()
    await userEvent.setup().type(screen.getByLabelText(T.reason), 'SENTETIK kesinleştir')
    await userEvent.setup().click(screen.getByRole('button', { name: T.finalize })); await waitFor(() => expect(commands()).toHaveLength(2))
    await waitFor(() => expect(documents[0].status).toBe('Effective')); await waitFor(() => expect(screen.getByLabelText(T.document).matches(':disabled')).toBe(false))
    await userEvent.setup().selectOptions(screen.getByLabelText(T.document), created.documentId)
    await userEvent.setup().selectOptions(screen.getByLabelText(T.documentLine), created.lines[0].documentLineId)
    await userEvent.setup().click(screen.getByRole('button', { name: T.receive })); await waitFor(() => expect(commands()).toHaveLength(3))
    await waitFor(() => expect(owner.transfer.status).toBe('Accepted'))
    expect(JSON.parse(String(commands()[2].init?.body))).toEqual({ expectedTransferVersion: '2', receivedAtUtc: documents[0].receivedAtUtc,
      incomingDocument: { kind: 'inventory.transfer.incoming', id: created.documentId, lineId: created.lines[0].documentLineId }, receivedKg: '8', overageReason: null, expectedIncomingDocumentVersion: '2' })
    expect(onChanged).toHaveBeenCalledTimes(3)
  })
  it('retains the same body generated document IDs timestamp and key after an uncertain draft and route reentry', async () => {
    let original: Record<string, unknown> | null = null; const actual = commandReply
    commandReply = async (request) => {
      if (original === null) { original = await (await actual(request)).json() as Record<string, unknown>; return json({}, 503) }
      return json({ ...original, isReplay: true }, 200, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original' })
    }
    const page = mount(); await purpose('receive'); await createDraft(); await screen.findByText(T.unknown)
    expect(screen.getByLabelText(T.purpose).matches(':disabled')).toBe(true)
    page.unmount(); shell = { ...shell, routeEntryGeneration: 2 }; mount(); await screen.findByText(T.unknown)
    await userEvent.setup().click(screen.getByRole('button', { name: T.retry })); await screen.findByText(T.replayed)
    expect(commands()).toHaveLength(2); expect(commands()[1].init?.body).toBe(commands()[0].init?.body)
    expect(new Headers(commands()[1].init?.headers).get('Idempotency-Key')).toBe(new Headers(commands()[0].init?.headers).get('Idempotency-Key'))
    expect(onChanged).toHaveBeenCalledTimes(1)
  })
  it('keeps an uncertain original command frozen after a later conflict without fabricating success', async () => {
    let count = 0; commandReply = async () => json({}, ++count === 1 ? 503 : 409)
    mount(); await purpose('receive'); await createDraft(); await screen.findByText(T.unknown)
    await userEvent.setup().click(screen.getByRole('button', { name: T.retry })); await screen.findByText(/Çakışma/)
    expect(screen.getByText(T.unknown)).toBeTruthy(); expect(onChanged).not.toHaveBeenCalled(); expect(commands()).toHaveLength(2)
  })
  it('loads more actual documents with the same failed cursor and deduplicates identical overlaps', async () => {
    const first = Array.from({ length: 50 }, (_, n) => draft(`bbbbbbbb-bbbb-4bbb-8bbb-${String(n + 1).padStart(12, '0')}`)); let nextReads = 0
    documentReply = async (request) => url(request).searchParams.get('cursor') === null ? json(docFrame(first, 'SENTETIK-next-docs')) : ++nextReads === 1 ? json({}, 503)
      : json(docFrame([first[49], draft('bbbbbbbb-bbbb-4bbb-8bbb-000000000051')]))
    mount(); await purpose('receive'); await screen.findByRole('button', { name: T.more })
    await userEvent.setup().click(screen.getByRole('button', { name: T.more })); await screen.findByRole('button', { name: T.moreRetry })
    expect(commands()).toHaveLength(0); await userEvent.setup().click(screen.getByRole('button', { name: T.moreRetry }))
    await waitFor(() => expect(within(screen.getByLabelText(T.document)).getAllByRole('option')).toHaveLength(52))
    const subsequent = requests.filter((request) => request.url.includes('/by-transfer/') && request.url.includes('cursor='))
    expect(subsequent).toHaveLength(2); expect(subsequent[1].url).toBe(subsequent[0].url)
  })
  it('loads more actual transfer candidates using the same failed signed cursor', async () => {
    selectedTransfer = null; let nextReads = 0
    const ids = Array.from({ length: 25 }, (_, n) => `cccccccc-cccc-4ccc-8ccc-${String(n + 1).padStart(12, '0')}`)
    candidateReply = async (request) => url(request).searchParams.get('cursor') === null ? json(candidateFrame(request, ids, 'SENTETIK-next-transfers'))
      : ++nextReads === 1 ? json({}, 503) : json(candidateFrame(request, [ids[24]]))
    mount(); await purpose('receive'); await userEvent.setup().click(await screen.findByRole('button', { name: T.candidateMore }))
    await userEvent.setup().click(await screen.findByRole('button', { name: T.moreRetry }))
    await waitFor(() => expect(screen.getAllByRole('button', { name: `${T.open}: SENTETIK-ETIKET` })).toHaveLength(25))
    const subsequent = requests.filter((request) => request.url.includes('/candidates') && request.url.includes('cursor='))
    expect(subsequent).toHaveLength(2); expect(subsequent[0].url).toBe(subsequent[1].url); expect(commands()).toHaveLength(0)
  })
})

describe('actual pending requests and stale context fences', () => {
  it('sends the exact current loss request number expected version and explicit decision', async () => {
    pendingLoss(); mount(); await purpose('loss-decision')
    await userEvent.setup().selectOptions(screen.getByLabelText(T.pendingLoss), `${LINE}:2`)
    await userEvent.setup().selectOptions(screen.getByLabelText(T.decision), 'Reject')
    await userEvent.setup().type(screen.getByLabelText(T.reason), 'SENTETIK karar')
    fireEvent.change(screen.getByLabelText(T.when), { target: { value: '2026-11-17T15:00:00' } })
    await userEvent.setup().click(screen.getByRole('button', { name: T.lossDecision })); await screen.findByText(T.saved)
    expect(JSON.parse(String(commands()[0].init?.body))).toMatchObject({ expectedTransferVersion: '2', requestNumber: 2, decision: 'Reject', reason: 'SENTETIK karar' })
    expect(commands()[0].url).toBe(`/api/v1/inventory/transfers/${TRANSFER}/lines/${LINE}/loss-decision`)
    expect(screen.getByText(T.statuses.Rejected)).toBeTruthy()
  })
  it('shows actual pending overage and requires an explicit choice instead of assuming approval', async () => {
    const source = work(); owner = { ...source, transfer: { ...source.transfer, version: '3', status: 'Accepted', lines: source.transfer.lines.map((line) => ({ ...line, status: 'Accepted' })) },
      facts: { ...source.facts, version: '3', status: 'Accepted', receipts: [receipt(true)] } }
    mount(); await purpose('overage-decision'); await userEvent.setup().selectOptions(screen.getByLabelText(T.pendingOverage), `${LINE}:1`)
    await userEvent.setup().type(screen.getByLabelText(T.reason), 'SENTETIK karar')
    fireEvent.change(screen.getByLabelText(T.when), { target: { value: '2026-11-17T15:00:00' } })
    await userEvent.setup().click(screen.getByRole('button', { name: T.decideOverage })); expect(await screen.findByText(T.invalid)).toBeTruthy(); expect(commands()).toHaveLength(0)
    await userEvent.setup().selectOptions(screen.getByLabelText(T.decision), 'Reject'); await userEvent.setup().click(screen.getByRole('button', { name: T.decideOverage }))
    await screen.findByText(T.saved); expect(JSON.parse(String(commands()[0].init?.body))).toMatchObject({ decision: 'Reject', requestNumber: 1 })
  })
  it('withdraws the actual numbered pending loss without converting withdrawal into a physical write-off', async () => {
    pendingLoss(); mount(); await purpose('loss-request')
    const form = screen.getByRole('button', { name: T.lossWithdraw }).closest('form')!; const user = userEvent.setup()
    await user.selectOptions(within(form).getByLabelText(T.pendingLoss), `${LINE}:2`)
    await user.type(within(form).getByLabelText(T.reason), 'SENTETIK geri çek')
    fireEvent.change(within(form).getByLabelText(T.when), { target: { value: '2026-11-17T15:00:00' } })
    await user.click(within(form).getByRole('button', { name: T.lossWithdraw })); await screen.findByText(T.saved)
    expect(JSON.parse(String(commands()[0].init?.body))).toMatchObject({ expectedTransferVersion: '2', requestNumber: 2, reason: 'SENTETIK geri çek' })
    expect(owner.transfer.version).toBe('2'); expect(owner.transfer.lines[0].status).toBe('InTransit')
    expect(owner.facts.losses[0].status).toBe('Withdrawn')
  })
  it('opens a new overage request from actual rejected history while preserving old numbered requests', async () => {
    const source = work(); const rejection = { ...receipt(true), overageStatus: 'Rejected' as const, overageDecisionReason: 'SENTETIK ret', overageDecidedAtUtc: AT, overageDecidedBy: 'SENTETIK-reviewer' }
    owner = { ...source, transfer: { ...source.transfer, version: '3', status: 'Accepted', lines: source.transfer.lines.map((line) => ({ ...line, status: 'Accepted' })) },
      facts: { ...source.facts, version: '3', status: 'Accepted', receipts: [rejection], overageRenewals: [{ lineId: LINE, requestNumber: 2, overageKg: '1.5', reason: 'SENTETIK eski talep', requestedAtUtc: AT, requestedBy: OTHER, status: 'Rejected', decisionReason: 'SENTETIK ret', decidedAtUtc: AT, decidedBy: 'SENTETIK-reviewer', movementId: null }] } }
    commandReply = async (request) => { const payload = JSON.parse(String(request.init?.body)); owner = { ...owner, facts: { ...owner.facts, overageRenewals: [...owner.facts.overageRenewals,
      { lineId: LINE, requestNumber: 3, overageKg: payload.overageKg, reason: payload.reason, requestedAtUtc: payload.requestedAtUtc, requestedBy: ACTOR, status: 'Pending', decisionReason: null, decidedAtUtc: null, decidedBy: null, movementId: null }] } }; return json({ transfer: owner.transfer, facts: owner.facts }, 201) }
    mount(); await purpose('receive'); const form = screen.getByRole('button', { name: T.renewOverage }).closest('form')!; const user = userEvent.setup()
    await user.selectOptions(within(form).getByLabelText(T.line), LINE); await user.type(within(form).getByLabelText(T.renewalKg), '1')
    await user.type(within(form).getByLabelText(T.reason), 'SENTETIK yeni talep'); fireEvent.change(within(form).getByLabelText(T.when), { target: { value: '2026-11-17T15:00:00' } })
    await user.click(within(form).getByRole('button', { name: T.renewOverage })); await screen.findByText(T.saved)
    expect(JSON.parse(String(commands()[0].init?.body))).toMatchObject({ expectedTransferVersion: '3', overageKg: '1', reason: 'SENTETIK yeni talep' })
    expect(JSON.parse(String(commands()[0].init?.body))).not.toHaveProperty('requestNumber')
    expect(owner.facts.overageRenewals.map((row) => [row.requestNumber, row.status])).toEqual([[2, 'Rejected'], [3, 'Pending']])
  })
  it('aborts an old company work read and does not render its late facts in the new workspace', async () => {
    let resolve!: (response: Response) => void; workReply = () => new Promise((done) => { resolve = done })
    const page = mount(); await userEvent.setup().selectOptions(screen.getByLabelText(T.purpose), 'receive')
    const request = requests.find((item) => item.url.includes('/receipt-work?'))!
    shell = { ...shell, committedContextRevision: 2, activeContext: { ...shell.activeContext!, companyId: TARGET } }; selectedTransfer = null; page.refresh()
    await act(async () => { resolve(json(work())) })
    expect(request.init?.signal?.aborted).toBe(true); expect(screen.queryByRole('table', { name: T.receipts })).toBeNull(); expect(onChanged).not.toHaveBeenCalled()
  })
  it('does not refresh a new session or display old success from a late committed document response', async () => {
    let resolve!: (response: Response) => void; commandReply = () => new Promise((done) => { resolve = done })
    const page = mount(); await purpose('receive'); await createDraft(); await screen.findByText(T.pending)
    const payload = JSON.parse(String(commands()[0].init?.body))
    shell = { ...shell, sessionEpoch: 2 }; page.refresh()
    const document: ReceiptDocument = { ...draft(payload.documentId), receivedAtUtc: payload.receivedAtUtc, lines: payload.lines.map((line: { documentLineId: string; transferLineId: string; receivedKg: string; overageReason: string | null }) => ({ ...line, labelId: LABEL, dispatchedKg: '10' })) }
    await act(async () => { resolve(json({ document, isReplay: false, originalCorrelationId: 'SENTETIK-original' }, 201)) })
    expect(screen.queryByText(T.saved)).toBeNull(); expect(screen.queryByText(T.unknown)).toBeNull(); expect(onChanged).not.toHaveBeenCalled()
  })
})

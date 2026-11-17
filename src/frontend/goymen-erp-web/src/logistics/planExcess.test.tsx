/** SENTETIK HTTP/DOM source tests. No runtime, SQL, browser, stock, grant or production acceptance is asserted. */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState, type ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { initialShellState, type ShellState } from '../shell/shellState'
import { PlanExcessApprovalPage } from './PlanExcessApprovalPage'
import { PlanExcessPreparationPanel, resetPlanExcessAttempts } from './PlanExcessPreparationPanel'
import { PlanExcessUseEditor, planExcessUsePayload } from './PlanExcessUseEditor'
import { PLAN_EXCESS_CAPABILITY as CAP, approvePlanExcessPath, decodePlanExcessPage, decodePlanExcessResult, decodePlanExcessView,
  fetchPlanExcess, proposePlanExcessPath, sendPlanExcess, type PlanExcessView, type PlanExcessUse } from './planExcessApi'
import { PLAN_EXCESS_TEXT as T } from './planExcessText'
import { decodeInstructionDetail } from './contract'
import { instructionDetail, instructionLine, INSTRUCTION, COMPANY_A, COMPANY_B, LINE, DRAFT, DRAFT_LINE, KG } from './testHarness'

const EXCEPTION = '71000000-0000-4000-8000-000000000001'
const OTHER = '71000000-0000-4000-8000-000000000002'
const clients: QueryClient[] = []
let shell: ShellState
const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ synthetic: true })), report: vi.fn() }))
vi.mock('../shell/shellContext', async importOriginal => ({ ...await importOriginal<typeof import('../shell/shellContext')>(), useShellState: () => shell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
const record = (overrides: Partial<PlanExcessView> = {}): PlanExcessView => ({ companyId: COMPANY_A, exceptionId: EXCEPTION,
  instructionId: INSTRUCTION, lineId: LINE, salesOrderId: DRAFT, salesOrderLineId: DRAFT_LINE, unitId: KG,
  version: '1', instructionVersion: '5', status: 'Pending', instructed: '100', quantity: '20', loadedUsed: '0', shippedUsed: '0',
  availableToLoad: '0', availableToShip: '0', reason: 'SENTETIK ek miktar', proposedBy: 'SENTETIK actor', proposedAtUtc: '2026-11-18T09:00:00Z',
  approvedBy: null, approvedAtUtc: null, ...overrides })
const approved = (overrides: Partial<PlanExcessView> = {}) => record({ status: 'Approved', version: '2', approvedBy: 'SENTETIK actor', approvedAtUtc: '2026-11-18T09:01:00Z', availableToLoad: '20', ...overrides })
const json = (body: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
function mount(node: ReactNode = <PlanExcessApprovalPage />) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } }); clients.push(client)
  return render(<QueryClientProvider client={client}>{node}</QueryClientProvider>)
}
beforeEach(() => {
  shell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
    activeContext: { userId: 'SENTETIK actor', companyId: COMPANY_A, scopeMode: 'single', capabilityGeneration: 'SENTETIK', capabilities: [CAP.approve, CAP.prepare] } }
})
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); resetPlanExcessAttempts(); vi.unstubAllGlobals(); vi.restoreAllMocks(); boundary.report.mockClear() })

describe('SAT_D417 strict price free source and command receipts', () => {
  it('preserves server quantities and rejects foreign identities without computing the instruction plus exception', () => {
    expect(decodePlanExcessView(record(), COMPANY_A, INSTRUCTION)).toEqual(record())
    expect(decodePlanExcessView(record(), COMPANY_B)).toBeNull()
    expect(decodePlanExcessView(record(), COMPANY_A, OTHER)).toBeNull()
    expect(decodePlanExcessPage({ items: [record(), record()], nextCursor: null }, COMPANY_A)).toBeNull()
    expect(decodePlanExcessPage({ items: [], nextCursor: null }, COMPANY_A)).toEqual({ items: [], nextCursor: null })
  })
  it.each([
    { quantity: 20 }, { quantity: '0' }, { quantity: '-1' }, { quantity: null }, { availableToLoad: undefined },
    { approvedBy: undefined }, { unitId: '00000000-0000-0000-0000-000000000000' }, { version: '9223372036854775808' },
    { approvedBy: 'SENTETIK unexpected approval' }, { unexpectedPrice: '1' }, { proposedAtUtc: 'yesterday' },
  ])('refuses missing extra mistyped and contradictory wire fields %j', patch => {
    expect(decodePlanExcessView({ ...record(), ...patch }, COMPANY_A)).toBeNull()
  })
  it('keeps legacy line shape and accepts actual separate capacity without recalculating or changing instructed quantity', () => {
    const legacy = instructionDetail({ status: 'Issued' }, [instructionLine({ instructed: '100', loaded: '120', shipped: '120', remainingToLoad: '0', loadedNotShipped: '0' })])
    expect(decodeInstructionDetail(legacy, INSTRUCTION)?.lines[0]).not.toHaveProperty('approvedPlanExcess')
    const source = instructionDetail({ status: 'Issued' }, [instructionLine({ instructed: '100', loaded: '120', shipped: '120', remainingToLoad: '0', loadedNotShipped: '0', approvedPlanExcess: '20' })])
    expect(decodeInstructionDetail(source, INSTRUCTION)?.lines[0]).toMatchObject({ instructed: '100', loaded: '120', shipped: '120', approvedPlanExcess: '20' })
    for (const invalid of [null, 20, '0', '-20']) {
      expect(decodeInstructionDetail(instructionDetail({}, [instructionLine({ approvedPlanExcess: invalid })]), INSTRUCTION)).toBeNull()
    }
  })
  it('omits an unused optional selection and never converts selected quantities to JS numbers', () => {
    expect(JSON.stringify(planExcessUsePayload([]))).toBe('{}')
    expect(planExcessUsePayload([{ exceptionId: EXCEPTION, expectedVersion: '2', quantity: '20,125' }])).toEqual({ planExcessUses: [{ exceptionId: EXCEPTION, expectedVersion: '2', quantity: '20.125' }] })
    expect(planExcessUsePayload([{ exceptionId: EXCEPTION, expectedVersion: '2', quantity: '0' }])).toBeNull()
    expect(planExcessUsePayload([{ exceptionId: EXCEPTION, expectedVersion: '2', quantity: '1' }, { exceptionId: EXCEPTION, expectedVersion: '2', quantity: '2' }])).toBeNull()
  })
  it('uses the narrow selected purpose and company header without a general Logistics read', async () => {
    const fetch = vi.fn(async (_url: unknown, _init?: RequestInit) => json({ items: [approved()], nextCursor: null })); vi.stubGlobal('fetch', fetch)
    expect((await fetchPlanExcess(COMPANY_A, 'ship', INSTRUCTION)).type).toBe('ok-single')
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toContain(`purpose=ship&instructionId=${INSTRUCTION}`)
    expect(new Headers(init.headers).get('X-Company-Id')).toBe(COMPANY_A)
  })
  it.each<Readonly<Record<string, string>>>([{}, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'wrong' }, { 'Idempotency-Replayed': 'invalid' }])('keeps mismatched replay receipt unknown %j', async headers => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ exception: approved(), isReplay: true, originalCorrelationId: 'SENTETIK original' }, 200, headers)))
    const result = await sendPlanExcess({ companyId: COMPANY_A, path: approvePlanExcessPath(EXCEPTION), binding: 'SENTETIK', key: 'SENTETIK key', payload: { expectedVersion: '1', expectedInstructionVersion: '5' } })
    expect(result.type).toBe('malformed')
  })
  it('requires the actual submitted exception line quantity reason and approval version in success', async () => {
    const snapshot = { companyId: COMPANY_A, path: proposePlanExcessPath(INSTRUCTION, LINE), binding: 'SENTETIK', key: 'SENTETIK key',
      payload: { exceptionId: EXCEPTION, expectedInstructionVersion: '5', quantity: '20', reason: 'SENTETIK ek miktar' } }
    for (const patch of [{}, { exceptionId: OTHER }, { lineId: OTHER }, { quantity: '21' }, { reason: 'different' }]) {
      vi.stubGlobal('fetch', vi.fn(async () => json({ exception: record(patch), isReplay: false, originalCorrelationId: 'SENTETIK original' })))
      expect((await sendPlanExcess(snapshot)).type).toBe(Object.keys(patch).length === 0 ? 'succeeded' : 'malformed')
    }
    expect(decodePlanExcessResult({ exception: record(), isReplay: false, originalCorrelationId: '' }, COMPANY_A)).toBeNull()
  })
})

describe('SAT_D417 explicit preparation and independent approval screen', () => {
  it('requires the staged approval mount before reading and does not require load or general read capability', async () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [CAP.approve] } }
    const fetch = vi.fn(async (_url: unknown, _init?: RequestInit) => json({ items: [record()], nextCursor: null })); vi.stubGlobal('fetch', fetch)
    mount(); expect(await screen.findByRole('button', { name: T.approve })).toBeTruthy()
    expect(fetch.mock.calls.every(call => String(call[0]).includes('/logistics/plan-excess?purpose=approve'))).toBe(true)
  })
  it('refuses missing approval capability before fetching', () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [] } }
    const fetch = vi.fn(); vi.stubGlobal('fetch', fetch); mount()
    expect(screen.getByText(/Yetkiniz yok/)).toBeTruthy(); expect(fetch).not.toHaveBeenCalled()
  })
  it('approves the actual pending record under only approval capability even when the same actor prepared it', async () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [CAP.approve] } }
    const posts: { url: string; init: RequestInit }[] = []; let item = record()
    vi.stubGlobal('fetch', vi.fn(async (url: unknown, init?: RequestInit) => {
      if (init?.method !== 'POST') return json({ items: [item], nextCursor: null })
      posts.push({ url: String(url), init }); item = approved()
      return json({ exception: item, isReplay: false, originalCorrelationId: 'SENTETIK original' })
    }))
    mount(); await userEvent.click(await screen.findByRole('button', { name: T.approve }))
    expect(await screen.findByText(T.completed)).toBeTruthy(); expect(posts).toHaveLength(1)
    expect(posts[0].url).toContain(approvePlanExcessPath(EXCEPTION))
    expect(JSON.parse(String(posts[0].init.body))).toEqual({ expectedVersion: '1', expectedInstructionVersion: '5' })
    expect(new Headers(posts[0].init.headers).get('X-Company-Id')).toBe(COMPANY_A)
  })
  it('keeps a pending source distinct from actual empty records', async () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {}))); const first = mount()
    expect(screen.getByText(/Yükleniyor/)).toBeTruthy(); first.unmount()
    vi.stubGlobal('fetch', vi.fn(async () => json({ items: [], nextCursor: null }))); mount()
    expect(await screen.findByText(/Kayıt yok/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
  })
  it.each<readonly [number | 'malformed' | 'offline', string]>([[401, 'Oturum sonlandı'], [403, 'Yetkiniz yok'], [404, 'Bulunamadı'], [409, 'Çakışma'],
    [503, 'Servis geçici olarak kullanılamıyor'], ['malformed', 'Beklenmeyen yanıt'], ['offline', 'Bağlantı yok']])('distinguishes source state %s', async (kind, message) => {
    if (kind === 'offline') { vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false); vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('SENTETIK offline') })) }
    else vi.stubGlobal('fetch', vi.fn(async () => json(kind === 'malformed' ? { items: [record({ companyId: COMPANY_B })], nextCursor: null } : {}, typeof kind === 'number' ? kind : 200)))
    mount(); expect(await screen.findByText(new RegExp(message))).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
    if (kind === 401 || kind === 403) expect(boundary.report).toHaveBeenCalledWith(kind, { synthetic: true })
  })
  it('prepares a separate exception and retries the frozen payload and key after response loss', async () => {
    const posts: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (_url: unknown, init?: RequestInit) => {
      posts.push(init!); if (posts.length === 1) throw new TypeError('SENTETIK lost response')
      const payload = JSON.parse(String(init!.body))
      return json({ exception: record({ exceptionId: payload.exceptionId, quantity: payload.quantity, reason: payload.reason }), isReplay: true, originalCorrelationId: 'SENTETIK original' }, 200,
        { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK original' })
    }))
    mount(<PlanExcessPreparationPanel instructionId={INSTRUCTION} instructionVersion="5" lineId={LINE} lineNo={1} instructed="100" onRecorded={vi.fn()} />)
    const user = userEvent.setup(); await user.type(screen.getByLabelText(T.quantity), '20'); await user.type(screen.getByLabelText(T.reason), 'SENTETIK extra')
    await user.click(screen.getByRole('button', { name: T.save })); expect(await screen.findByText(T.unknown)).toBeTruthy()
    expect(screen.getByRole('button', { name: T.save }).matches(':disabled')).toBe(true)
    await user.click(screen.getByRole('button', { name: T.retry })); expect(await screen.findByText(T.replayed)).toBeTruthy()
    expect(posts).toHaveLength(2); expect(posts[1].body).toBe(posts[0].body)
    expect(new Headers(posts[1].headers).get('Idempotency-Key')).toBe(new Headers(posts[0].headers).get('Idempotency-Key'))
    expect(JSON.parse(String(posts[0].body))).toMatchObject({ quantity: '20', expectedInstructionVersion: '5' })
    expect(String(posts[0].body)).not.toMatch(/instructed|companyId|approvedBy|permission/)
  })
  it('shows actual server availability and requires explicit record and entered quantity without FIFO or arithmetic', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ items: [approved({ availableToLoad: '9.875', loadedUsed: '10.125' }), approved({ exceptionId: OTHER, lineId: OTHER })], nextCursor: null })))
    const changes = vi.fn()
    function Controlled() { const [value, setValue] = useState<readonly PlanExcessUse[]>([]); return <PlanExcessUseEditor instructionId={INSTRUCTION} lineId={LINE} purpose="load" value={value} onChange={next => { changes(next); setValue(next) }} /> }
    mount(<Controlled />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: T.use }))
    expect(await screen.findByText('9.875')).toBeTruthy(); expect(screen.queryByText(OTHER)).toBeNull(); expect(changes).not.toHaveBeenCalled()
    await user.click(screen.getByRole('checkbox')); expect(changes).toHaveBeenLastCalledWith([{ exceptionId: EXCEPTION, expectedVersion: '2', quantity: '' }])
    await user.type(screen.getByLabelText(T.useQuantity), '2,5')
    expect(changes).toHaveBeenLastCalledWith([{ exceptionId: EXCEPTION, expectedVersion: '2', quantity: '2,5' }])
    expect(screen.getByText('9.875')).toBeTruthy(); expect(screen.queryByText('7.375')).toBeNull()
  })
  it('reopens an unknown preparation with the same frozen key and payload', async () => {
    const posts: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (_url: unknown, init?: RequestInit) => { posts.push(init!); return json({}, 503) }))
    const panel = () => <PlanExcessPreparationPanel instructionId={INSTRUCTION} instructionVersion="5" lineId={LINE} lineNo={1} instructed="100" onRecorded={vi.fn()} />
    const first = mount(panel()); const user = userEvent.setup()
    await user.type(screen.getByLabelText(T.quantity), '20'); await user.type(screen.getByLabelText(T.reason), 'SENTETIK extra')
    await user.click(screen.getByRole('button', { name: T.save })); await screen.findByText(T.unknown); first.unmount()
    mount(panel()); expect(screen.getByLabelText(T.quantity)).toHaveProperty('value', '20')
    await user.click(screen.getByRole('button', { name: T.retry })); await waitFor(() => expect(posts).toHaveLength(2))
    expect(posts[1].body).toBe(posts[0].body); expect(new Headers(posts[1].headers).get('Idempotency-Key')).toBe(new Headers(posts[0].headers).get('Idempotency-Key'))
  })
  it('does not expose a late source after the approval page unmounts', async () => {
    let release!: (response: Response) => void
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => { release = resolve })))
    const view = mount(); view.unmount(); release(json({ items: [record()], nextCursor: null }))
    await waitFor(() => expect(screen.queryByText(record().reason)).toBeNull())
  })
})

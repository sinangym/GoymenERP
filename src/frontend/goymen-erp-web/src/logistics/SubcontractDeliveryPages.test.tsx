import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { initialShellState, type ShellState } from '../shell/shellState'
import { SubcontractDeliveryCreatePage } from './SubcontractDeliveryPages'
import { SUBCONTRACT_CAPABILITY as CAP, SUBCONTRACT_DELIVERY_PATH as PATH } from './subcontractDeliveryContract'
import { SUBCONTRACT_DELIVERY_TEXT as T } from './subcontractDeliveryMessages.tr'
import { beginSubcontractAttempt, finishSubcontractAttempt, resetSubcontractAttemptsForTests } from './subcontractDeliveryApi'
let shell: ShellState
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({}), report: vi.fn() }) }))
const company = '11111111-1111-4111-8111-111111111111'; const clients: QueryClient[] = []
beforeEach(() => { shell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
  activeContext: { userId: 'SENTETIK', scopeMode: 'single', companyId: company, capabilityGeneration: 'g1', capabilities: [CAP.create] } } })
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); resetSubcontractAttemptsForTests(); vi.unstubAllGlobals() })
function mount() { const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } }); clients.push(client)
  const tree = () => <MemoryRouter><QueryClientProvider client={client}><SubcontractDeliveryCreatePage /></QueryClientProvider></MemoryRouter>
  const view = render(tree()); return { ...view, refresh: () => view.rerender(tree()) } }
describe('subcontract delivery real selector states', () => {
  it('rejects missing capability before any source request and never offers GUID-entry substitutes', () => {
    const fetch = vi.fn(); vi.stubGlobal('fetch', fetch); shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [] } }; mount()
    expect(fetch).not.toHaveBeenCalled(); expect(screen.getByText(/Yetkiniz yok/)).toBeTruthy(); expect(screen.queryByRole('textbox')).toBeNull()
  })
  it('keeps a pending read distinct from an empty source and cannot submit while loading', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {}))); mount(); expect(screen.getByText(/Yükleniyor/)).toBeTruthy()
    expect((screen.getByRole('button', { name: T.save }) as HTMLButtonElement).disabled).toBe(true)
  })
  it('shows a genuine empty list without inventing a service line or output', async () => {
    const fetch = vi.fn(async () => new Response(JSON.stringify({ companyId: company, items: [], nextCursor: null }), { status: 200 })); vi.stubGlobal('fetch', fetch); mount()
    expect((await screen.findAllByText(T.empty)).length).toBeGreaterThan(0); expect(screen.queryByLabelText(T.salesLine)).toBeNull(); expect(screen.queryByLabelText(T.output)).toBeNull()
    expect(fetch.mock.calls.length).toBe(1)
  })
  it.each<readonly [number | 'malformed', string]>([[403, 'Yetkiniz yok'], [404, 'Bulunamadı'], [409, 'Çakışma'], [503, 'Servis geçici olarak kullanılamıyor'], ['malformed', 'Beklenmeyen yanıt']])('keeps %s source failure visible without a write', async (status, message) => {
    const fetch = vi.fn(async () => new Response('{}', { status: status === 'malformed' ? 200 : status })); vi.stubGlobal('fetch', fetch); mount()
    expect(await screen.findByText(new RegExp(message))).toBeTruthy(); expect(fetch.mock.calls.length).toBe(1)
    expect(screen.queryByLabelText(T.output)).toBeNull()
  })
})

describe('subcontract terminal physical kilograms stay in their authorized context', () => {
  const detail = 'Sistemde 20 kg var, 80 kg istendi.'
  function recordedRefusal(replayed = false, status = 409) {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [CAP.create, CAP.ship] } }
    const binding = JSON.stringify([shell.activeContext!.userId, company, shell.sessionEpoch])
    const instruction = '22222222-2222-4222-8222-222222222222'
    const snapshot = beginSubcontractAttempt(binding, company, instruction, `${PATH}/${instruction}/ship`, 'subcontract', {})!
    finishSubcontractAttempt(snapshot, { type: 'refused', status, code: 'inventory_physical_stock_insufficient', detail,
      replayed, originalCorrelationId: replayed ? 'SENTETIK-original' : null })
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ companyId: shell.activeContext!.companyId, items: [], nextCursor: null }))))
  }
  it.each([false, true])('shows the exact owner text for the original refusal and replay=%s without computing kg', async replayed => {
    recordedRefusal(replayed); mount()
    expect(await screen.findByText(detail)).toBeTruthy()
    expect(screen.queryByText(T.replayedRefusal) !== null).toBe(replayed)
    expect(screen.queryByText('SENTETIK-original')).toBeNull()
  })
  it.each(['company', 'actor', 'session', 'permission'])('hides a completed physical refusal after the %s fence changes', async fault => {
    recordedRefusal(true); const page = mount(); expect(await screen.findByText(detail)).toBeTruthy()
    if (fault === 'company') shell = { ...shell, activeContext: { ...shell.activeContext!, companyId: '33333333-3333-4333-8333-333333333333' } }
    else if (fault === 'actor') shell = { ...shell, activeContext: { ...shell.activeContext!, userId: 'SENTETIK-other-actor' } }
    else if (fault === 'session') shell = { ...shell, sessionEpoch: 2 }
    else shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [CAP.create] } }
    page.refresh()
    expect(screen.queryByText(detail)).toBeNull(); expect(screen.queryByText(T.replayedRefusal)).toBeNull()
  })
  it.each([401, 403, 503])('never renders physical kg or replay text on a nondisclosable status %s', status => {
    recordedRefusal(true, status); mount()
    expect(screen.queryByText(detail)).toBeNull(); expect(screen.queryByText(T.replayedRefusal)).toBeNull()
  })
})

describe('K3 P2-6: leaving an unresolved subcontract create', () => {
  it('reads the instruction without sending a command and releases the slot when the owner does not know it', async () => {
    // The way out is a read: no command is sent, and 404 proves the create did not apply.
    const binding = JSON.stringify([shell.activeContext!.userId, company, shell.sessionEpoch])
    const instruction = '22222222-2222-4222-8222-222222222222'
    const snapshot = beginSubcontractAttempt(binding, company, instruction, PATH, 'subcontract', { instructionId: instruction, effectiveOn: '2026-11-17', lines: [] })!
    finishSubcontractAttempt(snapshot, { type: 'unknown', reason: 'network-error' })
    const calls: { url: string; method: string }[] = []
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      calls.push({ url: String(input), method: init?.method ?? 'GET' })
      if (String(input).endsWith(`/${instruction}`)) return new Response('{}', { status: 404 })
      return new Response(JSON.stringify({ companyId: company, items: [], nextCursor: null }))
    }))
    mount()
    expect(await screen.findByText(T.unknown)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: T.finalCheck }))
    expect(await screen.findByText(T.released)).toBeTruthy()
    expect(screen.queryByText(T.unknown)).toBeNull()
    await waitFor(() => expect(calls.some(call => call.url.endsWith(`/${instruction}`))).toBe(true))
    expect(calls.filter(call => call.method === 'POST')).toHaveLength(0)
  })
  it('keeps the attempt open when the instruction cannot be read', async () => {
    const binding = JSON.stringify([shell.activeContext!.userId, company, shell.sessionEpoch])
    const instruction = '22222222-2222-4222-8222-222222222222'
    const snapshot = beginSubcontractAttempt(binding, company, instruction, PATH, 'subcontract', { instructionId: instruction, effectiveOn: '2026-11-17', lines: [] })!
    finishSubcontractAttempt(snapshot, { type: 'unknown', reason: 'network-error' })
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => String(input).endsWith(`/${instruction}`)
      ? new Response('{}', { status: 503 }) : new Response(JSON.stringify({ companyId: company, items: [], nextCursor: null }))))
    mount()
    fireEvent.click(await screen.findByRole('button', { name: T.finalCheck }))
    expect(await screen.findByText(T.finalOpen)).toBeTruthy()
    expect(screen.getByText(T.unknown)).toBeTruthy()
  })
})

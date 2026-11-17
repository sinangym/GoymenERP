import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { initialShellState, type ShellState } from '../shell/shellState'
import { SubcontractDeliveryCreatePage } from './SubcontractDeliveryPages'
import { SUBCONTRACT_CAPABILITY as CAP } from './subcontractDeliveryContract'
import { SUBCONTRACT_DELIVERY_TEXT as T } from './subcontractDeliveryMessages.tr'
import { resetSubcontractAttemptsForTests } from './subcontractDeliveryApi'
let shell: ShellState
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({}), report: vi.fn() }) }))
const company = '11111111-1111-4111-8111-111111111111'; const clients: QueryClient[] = []
beforeEach(() => { shell = { ...initialShellState, phase: 'idle', sessionEpoch: 1, committedContextRevision: 1,
  activeContext: { userId: 'SENTETIK', scopeMode: 'single', companyId: company, capabilityGeneration: 'g1', capabilities: [CAP.create] } } })
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); resetSubcontractAttemptsForTests(); vi.unstubAllGlobals() })
function mount() { const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } }); clients.push(client)
  return render(<MemoryRouter><QueryClientProvider client={client}><SubcontractDeliveryCreatePage /></QueryClientProvider></MemoryRouter>) }
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

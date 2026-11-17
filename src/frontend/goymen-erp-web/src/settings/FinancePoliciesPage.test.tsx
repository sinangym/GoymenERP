import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { FinancePoliciesPage } from './FinancePoliciesPage'
import { FINANCE_POLICY_TEXT as T } from './financePolicyText'
import { clearPendingFinancePolicies } from './pendingFinancePolicies'

const state = vi.hoisted(() => ({ actor: '11111111-1111-1111-1111-111111111110', company: '11111111-1111-1111-1111-111111111111', generation: 'g1', revision: 1, epoch: 0, allowed: true }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ phase: 'idle', routeEntryGeneration: 1, sessionEpoch: state.epoch,
  committedContextRevision: state.revision, requestFenceGeneration: state.revision, activeContext: { companyId: state.company, userId: state.actor, scopeMode: 'single', capabilityGeneration: state.generation,
    capabilities: state.allowed ? ['settings.finance-policies.prepare'] : [] } }) }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => 'Synthetic', report: vi.fn() }) }))
const cash = '22222222-2222-2222-2222-222222222222'; const currency = '33333333-3333-3333-3333-333333333333'
const workspace = () => ({ companyId: state.company, businessDate: '2026-11-18', references: { companyId: state.company,
  operations: [], roles: [], cashAccounts: [{ cashAccountId: cash, currencyId: currency, name: 'Sentetik kasa', version: '2' }], currencies: [{ currencyId: currency, code: 'TRY' }] }, approvalPolicies: [], cashCountDifferencePolicies: [] })
const json = (raw: unknown, status = 200) => new Response(JSON.stringify(raw), { status, headers: { 'Content-Type': 'application/json' } })
function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const content = () => <QueryClientProvider client={client}><FinancePoliciesPage family="cash-count-differences" /></QueryClientProvider>
  const view = render(content()); return { ...view, refresh: () => view.rerender(content()) }
}
afterEach(() => { cleanup(); vi.unstubAllGlobals(); clearPendingFinancePolicies(); state.allowed = true; state.actor = '11111111-1111-1111-1111-111111111110'; state.company = '11111111-1111-1111-1111-111111111111'; state.generation = 'g1'; state.revision = 1; state.epoch = 0 })
describe('KIM-S19 explicit financial policy authoring', () => {
  it('starts with no assumed amount and sends only selected owner IDs and actual versions, retaining an unknown key across real capability revision changes', async () => {
    const writes: { body: string; key: string | null }[] = []
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') { writes.push({ body: String(init.body), key: new Headers(init.headers).get('Idempotency-Key') }); return writes.length === 1 ? json({}, 503) : new Response(null, { status: 204 }) }
      return json(workspace())
    }))
    const view = mount(); await screen.findByText(T.empty)
    expect(screen.getByLabelText(T.amount)).toHaveProperty('value', '')
    fireEvent.change(screen.getByLabelText(T.cashAccount), { target: { value: cash } }); fireEvent.change(screen.getByLabelText(T.amount), { target: { value: '5.00' } }); fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'Sentetik gerekçe' } })
    fireEvent.click(screen.getByRole('button', { name: T.prepare })); await screen.findByRole('button', { name: T.retry })
    expect(JSON.parse(writes[0].body)).toEqual({ expectedVersion: '0', cashAccountId: cash, expectedCashAccountVersion: '2', currencyId: currency, amountThreshold: '5.00', validFrom: '2026-11-18', validThrough: null, reason: 'Sentetik gerekçe' })
    state.allowed = false; state.generation = 'g2'; state.revision = 2; view.refresh()
    expect(screen.queryByRole('button', { name: T.retry })).toBeNull()
    state.allowed = true; state.generation = 'g3'; state.revision = 3; view.refresh()
    fireEvent.click(await screen.findByRole('button', { name: T.retry }))
    await waitFor(() => expect(writes).toHaveLength(2)); expect(writes[1]).toEqual(writes[0])
  })
  it('does not show a prior actor unknown command to a different actor or company', async () => {
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => init?.method === 'POST' ? json({}, 503) : json(workspace())))
    const view = mount(); await screen.findByText(T.empty)
    fireEvent.change(screen.getByLabelText(T.cashAccount), { target: { value: cash } }); fireEvent.change(screen.getByLabelText(T.amount), { target: { value: '0' } }); fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'Açık sıfır' } })
    fireEvent.click(screen.getByRole('button', { name: T.prepare })); await screen.findByRole('button', { name: T.retry })
    state.actor = '44444444-4444-4444-4444-444444444444'; state.revision++; view.refresh(); await screen.findByText(T.empty)
    expect(screen.queryByRole('button', { name: T.retry })).toBeNull()
    state.company = '55555555-5555-5555-5555-555555555555'; state.revision++; view.refresh(); await screen.findByText(T.empty)
    expect(screen.queryByRole('button', { name: T.retry })).toBeNull()
  })
})

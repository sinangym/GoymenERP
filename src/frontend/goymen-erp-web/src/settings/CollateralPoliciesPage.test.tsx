import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CollateralRatesPage } from './CollateralPoliciesPage'
import { COLLATERAL_POLICY_TEXT as K } from './collateralPolicyText'
import { FINANCE_POLICY_TEXT as T } from './financePolicyText'
import { clearPendingFinancePolicies } from './pendingFinancePolicies'

const state = vi.hoisted(() => ({ actor: '11111111-1111-4111-8111-111111111110', company: '11111111-1111-4111-8111-111111111111', generation: 'g1', revision: 1, allowed: true }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ phase: 'idle', sessionEpoch: 0, routeEntryGeneration: 1,
  committedContextRevision: state.revision, requestFenceGeneration: state.revision, activeContext: { userId: state.actor,
    companyId: state.company, scopeMode: 'single', capabilityGeneration: state.generation,
    capabilities: state.allowed ? ['settings.finance-policies.prepare'] : [] } }) }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => 'Synthetic', report: vi.fn() }) }))
const type = '22222222-2222-4222-8222-222222222222', bank = '33333333-3333-4333-8333-333333333333'
const source = 'settings.bank-institutions:synthetic:3'
const workspace = () => ({ companyId: state.company, businessDate: '2026-11-18', versions: [],
  types: { companyId: state.company, entries: [{ id: type, code: 'SYN', name: 'Sentetik teminat', kind: 'GuaranteeLetter',
    active: true, ratePercent: '100', version: '7', recordedBy: 'Synthetic', recordedAtUtc: '2026-11-18T09:00:00Z' }] },
  banks: { outcome: 'Available', companyId: state.company, entries: [{ bankInstitutionId: bank, name: 'Sentetik banka', eftCode: '0007', active: true, sourceVersion: source }], sourceVersion: source, reason: null } })
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
afterEach(() => { cleanup(); vi.unstubAllGlobals(); clearPendingFinancePolicies(); state.allowed = true; state.generation = 'g1'; state.revision = 1 })

describe('K6 explicit policy authoring', () => {
  it('starts without assumed rate or days and keeps exact source versions and an unknown key after real capability revision changes', async () => {
    const writes: { body: string; key: string | null }[] = []
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        writes.push({ body: String(init.body), key: new Headers(init.headers).get('Idempotency-Key') })
        return writes.length === 1 ? json({}, 503) : new Response(null, { status: 204 })
      }
      return json(workspace())
    }))
    const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
    const page = () => <QueryClientProvider client={client}><CollateralRatesPage /></QueryClientProvider>
    const view = render(page()); await screen.findByText(K.noRates)
    expect(screen.getByLabelText(K.percentage)).toHaveProperty('value', '')
    fireEvent.change(screen.getByLabelText(K.scope), { target: { value: 'TypeBankRemainingDays' } })
    expect(screen.getByLabelText(K.days)).toHaveProperty('value', '')
    fireEvent.change(screen.getByLabelText(K.type), { target: { value: type } })
    fireEvent.change(screen.getByLabelText(K.bank), { target: { value: bank } })
    fireEvent.change(screen.getByLabelText(K.days), { target: { value: '30' } })
    fireEvent.change(screen.getByLabelText(K.percentage), { target: { value: '40' } })
    fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'Açık sentetik politika' } })
    fireEvent.click(screen.getByRole('button', { name: T.prepare })); await screen.findByRole('button', { name: T.retry })
    expect(JSON.parse(writes[0].body)).toMatchObject({ expectedCollateralTypeVersion: '7', expectedBankSourceVersion: source,
      maximumRemainingDays: 30, ratePercent: '40', bankInstitutionId: bank, collateralTypeId: type })
    state.allowed = false; state.generation = 'g2'; state.revision = 2; view.rerender(page())
    expect(screen.queryByRole('button', { name: T.retry })).toBeNull()
    state.allowed = true; state.generation = 'g3'; state.revision = 3; view.rerender(page())
    fireEvent.click(await screen.findByRole('button', { name: T.retry }))
    await waitFor(() => expect(writes).toHaveLength(2)); expect(writes[1]).toEqual(writes[0])
  })
})

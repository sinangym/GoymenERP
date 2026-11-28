import {afterEach, describe, expect, it, vi} from 'vitest'
import {act, cleanup, fireEvent, render, screen, waitFor} from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {createMemoryRouter, RouterProvider} from 'react-router'
import {QueryClient} from '@tanstack/react-query'
import {AppShell} from '../shell/AppShell'
import {ShellProvider} from '../shell/ShellProvider'
import {COMPANY_A, COMPANY_B, IDENTITY, installFetch, json, problem} from '../settings/testHarness'
import {workRoutes} from './workRoutes'
import {WORK_DECISION_TEXT as W} from './workDecisionText'
import {SALES_CONTRACT_DISCOUNT_WORK_TEXT as T} from './workSalesContractDiscountText'
import {SALES_CONTRACT_DISCOUNT_WORK_KIND as kind, SALES_CONTRACT_DISCOUNT_WORK_CAPABILITY as cap} from './workSalesContractDiscountDecision'
import {salesContractDiscountFixture as fixture, salesContractDiscountId as id, salesContractMaker} from './salesContractDiscountDecisionFixture'
import {decodeWorkDecisionPreview, isWorkDecisionRequest, sendWorkDecision, WORK_DECISION_KINDS} from './workDecisionApi'
const originalActor = IDENTITY.userId, ownerPath = `/work/decisions/${kind}/${id}`
afterEach(() => {cleanup(); sessionStorage.clear(); vi.unstubAllGlobals(); vi.restoreAllMocks(); Object.assign(IDENTITY, {authenticationSessionBinding: null, userId: originalActor})})
function mount(client = new QueryClient({defaultOptions: {queries: {retry: false, gcTime: 0, staleTime: 0}}})) {
  const router = createMemoryRouter([{path: '/', element: <AppShell/>, children: workRoutes}], {initialEntries: [`/onaylarim/${kind}/${id}`]})
  render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>); return client
}
function result(approve = true, replayed = false) {return json({companyId: COMPANY_A, kind, itemId: id, state: approve ? 'Active' : 'DiscountRejected', replayed, originalCorrelationId: 'SENTETIK-original-contract'}, 200, replayed ? {'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original-contract'} : undefined)}
describe('Actual Contract purpose route, canonical prepared reason and immutable original-key recovery', () => {
  it('publishes one purpose beside all62 previous handlers and decodes only its own complete human wire', () => {
    expect(WORK_DECISION_KINDS).toHaveLength(63); expect(WORK_DECISION_KINDS.filter(k => k === kind)).toEqual([kind])
    expect(workRoutes.filter(r => r.path === `onaylarim/${kind}/:approvalId`)).toHaveLength(1)
    const value = fixture(COMPANY_A); expect(decodeWorkDecisionPreview(value, COMPANY_A, kind, id)).not.toBeNull()
    expect(decodeWorkDecisionPreview({...value, salesRiskException: {}}, COMPANY_A, kind, id)).toBeNull()
    expect(isWorkDecisionRequest({expectedVersion: '1', approve: false, reason: ' padded ', delegationId: null}, kind)).toBe(false)
  })
  it.each(['click', 'keyboard'] as const)('prepares visible canonical reason before the first %s confirmation and posts precisely that body', async method => {
    const calls = installFetch({capabilities: [cap], respond: (url, init) => url.includes(ownerPath) ? init?.method === 'POST' ? result(false) : json(fixture(COMPANY_A)) : undefined})
    mount(); await screen.findByRole('region', {name: T.title}); const user = userEvent.setup(), raw = '  SENTETIK sözleşme ret  '
    const input = screen.getByRole('textbox', {name: W.reason}); await user.type(input, raw)
    expect(screen.getByLabelText(T.preparedReason).textContent).toBe(raw.trim())
    if (method === 'click') await user.click(screen.getByRole('button', {name: W.reject}))
    else {await user.tab(); await user.tab(); await user.tab(); expect(document.activeElement).toBe(screen.getByRole('button', {name: W.reject})); await user.keyboard('{Enter}')}
    await screen.findByText(W.done)
    expect(calls.filter(c => c.method === 'POST').map(c => c.body)).toEqual([{expectedVersion: '9007199254740993', approve: false, reason: raw.trim(), delegationId: null}])
    expect(calls.filter(c => c.url.includes(ownerPath) && c.method === 'GET')).toHaveLength(1)
  })
  it.each([true, false])('accepts only native original result/correlation approve=%s and never labels Active as generic Approved', async approve => {
    const body = {expectedVersion: '1', approve, reason: approve ? null : 'canonical', delegationId: null}
    vi.stubGlobal('fetch', vi.fn(async () => result(approve, true))); const outcome = await sendWorkDecision(COMPANY_A, kind, id, body, 'SENTETIK-key')
    expect(outcome.type).toBe('applied'); if (outcome.type === 'applied') expect(outcome.data.originalCorrelationId).toBe('SENTETIK-original-contract')
    for (const state of ['Approved', 'Rejected', approve ? 'DiscountRejected' : 'Active']) {
      vi.stubGlobal('fetch', vi.fn(async () => json({companyId: COMPANY_A, kind, itemId: id, state, replayed: false, originalCorrelationId: 'other'})))
      expect((await sendWorkDecision(COMPANY_A, kind, id, body, 'SENTETIK-key')).type).toBe('unknown')
    }
  })
  it.each(['same-mount', 'remount'] as const)('recovers unknown with the exact canonical body/key through %s, without current GET or cached human substitution', async mode => {
    Object.assign(IDENTITY, {authenticationSessionBinding: 'SENTETIK-contract-recovery'})
    let posts = 0
    const calls = installFetch({capabilities: [cap], respond: (url, init) => url.includes(ownerPath) ? init?.method === 'POST' ? ++posts === 1 ? problem(503) : result(false, true) : json(fixture(COMPANY_A)) : undefined})
    const client = mount(); await screen.findByRole('region', {name: T.title})
    fireEvent.change(screen.getByRole('textbox', {name: W.reason}), {target: {value: '  canonical frozen  '}}); fireEvent.click(screen.getByRole('button', {name: W.reject})); await screen.findByRole('button', {name: W.retry})
    expect(screen.queryByRole('region', {name: T.title})).toBeNull()
    act(() => client.setQueriesData({predicate: q => q.queryKey.includes('work-decision')}, {type: 'ok-single', data: fixture(COMPANY_A, '9007199254740995', 'Active')}))
    expect(screen.queryByRole('region', {name: T.title})).toBeNull()
    if (mode === 'remount') {cleanup(); mount(); await screen.findByRole('button', {name: W.retry})}
    fireEvent.click(screen.getByRole('button', {name: W.retry})); await screen.findByText(W.done)
    const writes = calls.filter(c => c.method === 'POST'); expect(writes).toHaveLength(2); expect(writes[0].headers['Idempotency-Key']).toBe(writes[1].headers['Idempotency-Key']); expect(writes[0].body).toEqual(writes[1].body)
    expect(writes[1].body).toEqual({expectedVersion: '9007199254740993', approve: false, reason: 'canonical frozen', delegationId: null})
    expect(calls.filter(c => c.url.includes(ownerPath) && c.method === 'GET')).toHaveLength(1); expect(screen.queryByRole('region', {name: T.title})).toBeNull()
    expect(Object.values(sessionStorage).join(' ')).not.toContain('SENTETIK sözleşme maddesi'); expect(Object.values(sessionStorage).join(' ')).not.toContain('1500.00')
  })
  it('refreshes409 once with exact two GETs, clears previous intent and requires the new human binding', async () => {
    let reads = 0
    const calls = installFetch({capabilities: [cap], respond: (url, init) => url.includes(ownerPath) ? init?.method === 'POST' ? problem(409) : json(fixture(COMPANY_A, ++reads === 1 ? '1' : '3')) : undefined})
    mount(); await screen.findByRole('region', {name: T.title}); fireEvent.change(screen.getByRole('textbox', {name: W.reason}), {target: {value: 'old intent'}}); fireEvent.click(screen.getByRole('button', {name: W.reject}))
    await screen.findByText(`${W.refused} (409)`); await waitFor(() => expect(reads).toBe(2))
    expect((screen.getByRole('textbox', {name: W.reason}) as HTMLTextAreaElement).value).toBe(''); expect(screen.getByRole('button', {name: W.reject}).matches(':disabled')).toBe(true)
    expect(calls.filter(c => c.method === 'POST')).toHaveLength(1)
  })
  it('suppresses an actual late current GET during unknown reconciliation without replacing original version/key', async () => {
    Object.assign(IDENTITY, {authenticationSessionBinding: 'SENTETIK-contract-late'})
    let reads = 0, posts = 0, release!: (response: Response) => void
    const late = new Promise<Response>(resolve => {release = resolve})
    const calls = installFetch({capabilities: [cap], respond: (url, init) => url.includes(ownerPath) ? init?.method === 'POST' ? ++posts === 1 ? problem(503) : result(true, true) : ++reads === 1 ? json(fixture(COMPANY_A, '1')) : late : undefined})
    const client = mount(); await screen.findByRole('region', {name: T.title}); let refetch!: Promise<void>
    act(() => {refetch = client.refetchQueries({predicate: q => q.queryKey.includes('work-decision')})}); await waitFor(() => expect(reads).toBe(2))
    fireEvent.click(screen.getByRole('button', {name: W.approve})); await screen.findByRole('button', {name: W.retry})
    await act(async () => {release(json(fixture(COMPANY_A, '3', 'Active'))); await refetch})
    expect(screen.queryByRole('region', {name: T.title})).toBeNull(); fireEvent.click(screen.getByRole('button', {name: W.retry})); await screen.findByText(W.done)
    const writes = calls.filter(c => c.method === 'POST'); expect(writes).toHaveLength(2); expect(writes[0].body).toEqual(writes[1].body)
    expect(writes[1].body).toEqual({expectedVersion: '1', approve: true, reason: null, delegationId: null}); expect(writes[0].headers['Idempotency-Key']).toBe(writes[1].headers['Idempotency-Key']); expect(reads).toBe(2)
    client.clear()
  })
  it.each(['company', 'actor', 'session'] as const)('does not recover a prior unknown under a changed %s', async change => {
    Object.assign(IDENTITY, {authenticationSessionBinding: 'SENTETIK-contract-old'})
    const calls = installFetch({capabilities: [cap], respond: (url, init) => {
      if (!url.includes(ownerPath)) return undefined
      if (init?.method === 'POST') return problem(503)
      return json(fixture(new Headers(init?.headers).get('X-Company-Id') ?? COMPANY_A))
    }})
    mount(); await screen.findByRole('region', {name: T.title}); fireEvent.click(screen.getByRole('button', {name: W.approve})); await screen.findByRole('button', {name: W.retry})
    if (change === 'company') await userEvent.setup().selectOptions(screen.getByRole('combobox', {name: 'Şirket'}), COMPANY_B)
    else {cleanup(); Object.assign(IDENTITY, change === 'actor' ? {userId: '88888888-8888-4888-8888-888888888888'} : {authenticationSessionBinding: 'SENTETIK-contract-new'}); mount()}
    await screen.findByRole('region', {name: T.title}); expect(screen.queryByRole('button', {name: W.retry})).toBeNull(); expect(calls.filter(c => c.method === 'POST')).toHaveLength(1)
  })
  it('hides cached prices immediately when the fresh owner returns403 and sends no decision', async () => {
    let reads = 0
    const calls = installFetch({capabilities: [cap], respond: url => url.includes(ownerPath) ? ++reads === 1 ? json(fixture(COMPANY_A)) : problem(403) : undefined}), client = mount()
    await screen.findByRole('region', {name: T.title}); await act(async () => {await client.refetchQueries({predicate: q => q.queryKey.includes('work-decision')})})
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull()); expect(screen.queryByRole('region', {name: T.title})).toBeNull(); expect(calls.some(c => c.method === 'POST')).toBe(false); client.clear()
  })
  it('invalidates reason when a fresh same-version human changes instead of reusing stale intent', async () => {
    const calls = installFetch({capabilities: [cap], respond: url => url.includes(ownerPath) ? json(fixture(COMPANY_A)) : undefined}), client = mount()
    await screen.findByRole('region', {name: T.title}); fireEvent.change(screen.getByRole('textbox', {name: W.reason}), {target: {value: 'old intent'}})
    const value = fixture(COMPANY_A); Object.assign(value.salesContractDiscount.contract, {clauses: 'SENTETIK güncel madde'})
    act(() => client.setQueriesData({predicate: q => q.queryKey.includes('work-decision')}, {type: 'ok-single', data: value}))
    await screen.findByText('SENTETIK güncel madde'); expect((screen.getByRole('textbox', {name: W.reason}) as HTMLTextAreaElement).value).toBe(''); expect(screen.getByRole('button', {name: W.reject}).matches(':disabled')).toBe(true); expect(calls.some(c => c.method === 'POST')).toBe(false)
  })
  it.each(['Active', 'DiscountRejected', 'PendingDiscountApproval'] as const)('renders %s at Max read-only with no incrementing command', async state => {
    const calls = installFetch({capabilities: [cap], respond: url => url.includes(ownerPath) ? json(fixture(COMPANY_A, '9223372036854775807', state)) : undefined}); mount(); await screen.findByRole('region', {name: T.title})
    const approve = screen.queryByRole('button', {name: W.approve}); if (approve !== null) expect(approve.matches(':disabled')).toBe(true)
    expect(screen.queryByRole('button', {name: W.reject})).toBeNull(); expect(calls.some(c => c.method === 'POST')).toBe(false)
  })
  it.each([401, 403, 404, 503])('does not disclose selected facts or commands after read refusal%s', async status => {
    const calls = installFetch({capabilities: [cap], respond: url => url.includes(ownerPath) ? problem(status) : undefined}); mount()
    const state = status === 401 ? 'unauthenticated' : status === 403 || status === 404 ? 'not-found' : 'service-unavailable'
    await waitFor(() => expect(document.querySelector(`[data-state="${state}"]`)).not.toBeNull()); expect(screen.queryByRole('region', {name: T.title})).toBeNull(); expect(calls.some(c => c.method === 'POST')).toBe(false)
  })
  it('does not turn creator into a self-approval exception when native authority denies', async () => {
    Object.assign(IDENTITY, {userId: salesContractMaker}); const calls = installFetch({capabilities: [cap], respond: url => url.includes(ownerPath) ? problem(403) : undefined}); mount()
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull()); expect(calls.some(c => c.method === 'POST')).toBe(false)
  })
  it('does not acquire the purpose through general ContractsRead or PriceRead capabilities alone', async () => {
    const calls = installFetch({capabilities: ['sales.contracts.list', 'sales.drafts.detail'], respond: url => url.includes(ownerPath) ? json(fixture(COMPANY_A)) : undefined}); mount()
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull()); expect(calls.some(c => c.url.includes(ownerPath))).toBe(false)
  })
  it('fails closed for wrong-company and malformed amount human responses before any command', async () => {
    const bad = fixture(COMPANY_A); Object.assign(bad.salesContractDiscount.contract.installments[0], {amount: '79228162514264337593543950336.00'})
    expect(decodeWorkDecisionPreview(fixture(COMPANY_B), COMPANY_A, kind, id)).toBeNull()
    const calls = installFetch({capabilities: [cap], respond: url => url.includes(ownerPath) ? json(bad) : undefined}); mount()
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull()); expect(screen.queryByRole('button', {name: W.approve})).toBeNull(); expect(calls.some(c => c.method === 'POST')).toBe(false)
  })
})

import {afterEach, describe, expect, it, vi} from 'vitest'
import {act, cleanup, fireEvent, render, screen, waitFor, within} from '@testing-library/react'
import {createMemoryRouter, RouterProvider} from 'react-router'
import {QueryClient} from '@tanstack/react-query'
import userEvent from '@testing-library/user-event'
import {AppShell} from '../shell/AppShell'
import {ShellProvider} from '../shell/ShellProvider'
import {COMPANY_A, COMPANY_B, IDENTITY, installFetch, json, problem} from '../settings/testHarness'
import {workRoutes} from './workRoutes'
import {WORK_DECISION_TEXT as W} from './workDecisionText'
import {WorkSalesCustomerReturnFacts} from './WorkSalesCustomerReturnFacts'
import {SALES_CUSTOMER_RETURN_WORK_TEXT as T} from './workSalesCustomerReturnText'
import {SALES_CUSTOMER_RETURN_WORK_KIND as kind, SALES_CUSTOMER_RETURN_WORK_CAPABILITY as cap,
  decodeWorkSalesCustomerReturnPreview as decode} from './workSalesCustomerReturnDecision'
import {salesCustomerReturnFixture as fixture, salesCustomerReturnHuman as human, salesCustomerReturnId as id} from './salesCustomerReturnDecisionFixture'
import {decodeWorkDecisionPreview, exactNativeClrAmount as amount, isWorkPackageEffectiveDate as date, sendWorkDecision} from './workDecisionApi'
const originalActor = IDENTITY.userId, ownerPath = `/work/decisions/${kind}/${id}`
afterEach(() => {cleanup(); sessionStorage.clear(); vi.unstubAllGlobals(); vi.restoreAllMocks(); Object.assign(IDENTITY, {authenticationSessionBinding: null, userId: originalActor})})
function mount(client = new QueryClient({defaultOptions: {queries: {retry: false, gcTime: 0, staleTime: 0}}})) {
  // Integration source expects root's exact common codec/Page/purpose route composition; no test-only UI engine.
  const router = createMemoryRouter([{path: '/', element: <AppShell/>, children: workRoutes}], {initialEntries: [`/onaylarim/${kind}/${id}`]})
  render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>); return client
}
function result(approve = true, replayed = false, state = approve ? 'Approved' : 'Rejected') {
  return json({companyId: COMPANY_A, kind, itemId: id, state, replayed, originalCorrelationId: 'SENTETIK-original-return'}, 200,
    replayed ? {'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original-return'} : undefined)
}
describe('Sales return complete readonly human and actual common Work protocol (SOURCE_ONLY, NOT_BROWSER)', () => {
  it('renders all native header, full line and foreign amount fields without private proof or an extra action', () => {
    const h = decode(human('PendingApproval', '9007199254740993', true), COMPANY_A, id, date, amount)
    expect(h).not.toBeNull(); if (h === null) return
    render(<WorkSalesCustomerReturnFacts human={h}/>); const region = screen.getByRole('region', {name: T.title}), c = h.customerReturn
    for (const [field, value] of Object.entries(c)) if (typeof value === 'string' && field !== 'state') expect(region.textContent).toContain(value)
    for (const l of c.lines) {
      for (const value of Object.values(l)) if (typeof value === 'string') expect(region.textContent).toContain(value)
      for (const a of [l.amounts, l.functionalAmounts]) if (a !== null) for (const value of Object.values(a)) expect(region.textContent).toContain(value)
    }
    for (const a of [c.totals, c.functionalTotals]) if (a !== null) for (const value of Object.values(a)) expect(region.textContent).toContain(value)
    for (const label of Object.values(T.fields)) expect(within(region).getAllByText(label, {exact: true}).length).toBeGreaterThan(0)
    for (const role of ['button', 'textbox', 'checkbox', 'combobox'] as const) expect(within(region).queryByRole(role)).toBeNull()
    for (const field of ['sourceFactsDigest', 'previewDigest', 'originalDecisionReceipt', 'currentResponseDigest', 'payloadHash', 'auditIntegrityHash']) expect(region.textContent).not.toContain(field)
  })
  it.each([true, false])('posts only the original four fields, optional RAW note and full text version approve=%s', async approve => {
    const calls = installFetch({capabilities: [cap], respond: (url, init) => url.includes(ownerPath) ? init?.method === 'POST' ? result(approve) : json(fixture(COMPANY_A)) : undefined})
    mount(); await screen.findByRole('region', {name: T.title}); const raw = '\u0085  SENTETIK raw return note  \u0085'
    fireEvent.change(screen.getByRole('textbox', {name: W.reason}), {target: {value: raw}})
    fireEvent.click(screen.getByRole('button', {name: approve ? W.approve : W.reject})); await screen.findByText(W.done)
    expect(calls.filter(c => c.method === 'POST').map(c => c.body)).toEqual([{expectedVersion: '9007199254740993', approve, reason: approve ? null : raw, delegationId: null}])
    expect(calls.filter(c => c.url.includes(ownerPath) && c.method === 'GET')).toHaveLength(1)
  })
  it('offers native rejection without a required reason or any finalization/payment/selection action', async () => {
    const calls = installFetch({capabilities: [cap], respond: (url, init) => url.includes(ownerPath) ? init?.method === 'POST' ? result(false) : json(fixture(COMPANY_A)) : undefined})
    mount(); await screen.findByRole('region', {name: T.title}); expect(screen.getByRole('button', {name: W.reject}).matches(':disabled')).toBe(false)
    fireEvent.click(screen.getByRole('button', {name: W.reject})); await screen.findByText(W.done)
    expect(calls.filter(c => c.method === 'POST').map(c => c.body)).toEqual([{expectedVersion: '9007199254740993', approve: false, reason: '', delegationId: null}])
    expect(screen.queryByRole('button', {name: 'Kesinleştir'})).toBeNull(); expect(screen.queryByRole('combobox')).toBeNull()
  })
  it.each(['same-mount', 'remount'] as const)('reconciles unknown through %s without GET or current Finalized facts replacing the original decision', async mode => {
    Object.assign(IDENTITY, {authenticationSessionBinding: 'SENTETIK-return-unknown'})
    let posts = 0
    const calls = installFetch({capabilities: [cap], respond: (url, init) => url.includes(ownerPath) ? init?.method === 'POST' ? ++posts === 1 ? problem(503) : result(false, true) : json(fixture(COMPANY_A)) : undefined})
    const client = mount(); await screen.findByRole('region', {name: T.title}); const raw = '  SENTETIK frozen raw return reason  '
    fireEvent.change(screen.getByRole('textbox', {name: W.reason}), {target: {value: raw}}); fireEvent.click(screen.getByRole('button', {name: W.reject})); await screen.findByRole('button', {name: W.retry})
    expect(screen.queryByRole('region', {name: T.title})).toBeNull()
    act(() => client.setQueriesData({predicate: q => q.queryKey.includes('work-decision')}, {type: 'ok-single', data: fixture(COMPANY_A, 'Finalized', '9007199254740997')}))
    expect(screen.queryByRole('region', {name: T.title})).toBeNull()
    if (mode === 'remount') {cleanup(); mount(); await screen.findByRole('button', {name: W.retry})}
    fireEvent.click(screen.getByRole('button', {name: W.retry})); await screen.findByText(W.done)
    const writes = calls.filter(c => c.method === 'POST'); expect(writes).toHaveLength(2)
    expect(writes[0].headers['Idempotency-Key']).toBe(writes[1].headers['Idempotency-Key']); expect(writes[1].body).toEqual(writes[0].body)
    expect(writes[1].body).toEqual({expectedVersion: '9007199254740993', approve: false, reason: raw, delegationId: null})
    expect(calls.filter(c => c.url.includes(ownerPath) && c.method === 'GET')).toHaveLength(1)
    expect(screen.queryByRole('region', {name: T.title})).toBeNull()
    for (const field of ['netUnitPrice', 'createdBy', 'sourceFactsDigest', 'originalDecisionReceipt']) expect(Object.values(sessionStorage).join(' ')).not.toContain(field)
  })
  it.each(['Approved', 'Finalized', 'Rejected', 'Cancelled'] as const)('shows native current %s but exposes no new decision', async state => {
    expect(decodeWorkDecisionPreview(fixture(COMPANY_A, state, '5'), COMPANY_A, kind, id)).not.toBeNull()
    const calls = installFetch({capabilities: [cap], respond: url => url.includes(ownerPath) ? json(fixture(COMPANY_A, state, '5')) : undefined})
    mount(); await screen.findByRole('region', {name: T.title})
    expect(screen.queryByRole('button', {name: W.approve})).toBeNull(); expect(screen.queryByRole('button', {name: W.reject})).toBeNull()
    expect(calls.filter(c => c.method === 'POST')).toHaveLength(0)
  })
  it.each([true, false])('requires immutable original result state, never native CURRENT Finalized response approve=%s', async approve => {
    const body = {expectedVersion: '2', approve, reason: approve ? null : '  raw  ', delegationId: null}
    vi.stubGlobal('fetch', vi.fn(async () => result(approve, true)))
    expect((await sendWorkDecision(COMPANY_A, kind, id, body, 'SENTETIK-original')).type).toBe('applied')
    vi.stubGlobal('fetch', vi.fn(async () => result(approve, true, 'Finalized')))
    expect((await sendWorkDecision(COMPANY_A, kind, id, body, 'SENTETIK-original')).type).toBe('unknown')
  })
  it.each([401, 403])('keeps ambiguous postcommit status%s unresolved at the transport boundary', async status => {
    vi.stubGlobal('fetch', vi.fn(async () => problem(status)))
    const report = vi.fn()
    expect((await sendWorkDecision(COMPANY_A, kind, id, {expectedVersion: '2', approve: true, reason: null, delegationId: null}, 'SENTETIK-key', report)).type).toBe('unknown')
    expect(report).toHaveBeenCalledExactlyOnceWith(status)
  })
  it.each([403, 503])('read refusal%s exposes no human data and sends no decision', async status => {
    const calls = installFetch({capabilities: [cap], respond: url => url.includes(ownerPath) ? problem(status) : undefined})
    mount(); await waitFor(() => expect(calls.some(c => c.url.includes(ownerPath))).toBe(true))
    expect(screen.queryByRole('region', {name: T.title})).toBeNull(); expect(calls.filter(c => c.method === 'POST')).toHaveLength(0)
  })
  it.each(['company', 'actor', 'session'] as const)('does not restore the original unknown under changed %s', async change => {
    Object.assign(IDENTITY, {authenticationSessionBinding: 'SENTETIK-return-old-scope'})
    const calls = installFetch({capabilities: [cap], respond: (url, init) => url.includes(ownerPath)
      ? init?.method === 'POST' ? problem(503) : json(fixture(new Headers(init?.headers).get('X-Company-Id') ?? COMPANY_A)) : undefined})
    mount(); await screen.findByRole('region', {name: T.title}); fireEvent.click(screen.getByRole('button', {name: W.approve})); await screen.findByRole('button', {name: W.retry})
    if (change === 'company') await userEvent.setup().selectOptions(screen.getByRole('combobox', {name: 'Şirket'}), COMPANY_B)
    else {cleanup(); Object.assign(IDENTITY, change === 'actor' ? {userId: '88888888-8888-4888-8888-888888888888'} : {authenticationSessionBinding: 'SENTETIK-return-new-scope'}); mount()}
    await screen.findByRole('region', {name: T.title}); expect(screen.queryByRole('button', {name: W.retry})).toBeNull()
    expect(calls.filter(c => c.method === 'POST')).toHaveLength(1)
  })
  it('hides a genuinely in-flight late current Finalized read while retrying the frozen original request', async () => {
    Object.assign(IDENTITY, {authenticationSessionBinding: 'SENTETIK-return-late'})
    let reads = 0, posts = 0, release!: (r: Response) => void
    const late = new Promise<Response>(resolve => {release = resolve})
    const calls = installFetch({capabilities: [cap], respond: (url, init) => url.includes(ownerPath)
      ? init?.method === 'POST' ? ++posts === 1 ? problem(503) : result(true, true)
        : ++reads === 1 ? json(fixture(COMPANY_A)) : late : undefined})
    const client = mount(); await screen.findByRole('region', {name: T.title}); let refetch!: Promise<void>
    act(() => {refetch = client.refetchQueries({predicate: q => q.queryKey.includes('work-decision')})}); await waitFor(() => expect(reads).toBe(2))
    fireEvent.click(screen.getByRole('button', {name: W.approve})); await screen.findByRole('button', {name: W.retry})
    await act(async () => {release(json(fixture(COMPANY_A, 'Finalized', '9007199254740997'))); await refetch})
    expect(screen.queryByRole('region', {name: T.title})).toBeNull(); fireEvent.click(screen.getByRole('button', {name: W.retry})); await screen.findByText(W.done)
    const writes = calls.filter(c => c.method === 'POST'); expect(writes).toHaveLength(2); expect(writes[0].body).toEqual(writes[1].body)
    expect(writes[0].headers['Idempotency-Key']).toBe(writes[1].headers['Idempotency-Key']); expect(reads).toBe(2); client.clear()
  })
  it('cannot recover the unknown decision after the current purpose capability is removed', async () => {
    Object.assign(IDENTITY, {authenticationSessionBinding: 'SENTETIK-return-revoked'})
    const capabilities = [cap], calls = installFetch({capabilities, respond: (url, init) => url.includes(ownerPath) ? init?.method === 'POST' ? problem(503) : json(fixture(COMPANY_A)) : undefined})
    mount(); await screen.findByRole('region', {name: T.title}); fireEvent.click(screen.getByRole('button', {name: W.approve})); await screen.findByRole('button', {name: W.retry})
    cleanup(); capabilities.splice(0); mount(); await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(screen.queryByRole('region', {name: T.title})).toBeNull(); expect(screen.queryByRole('button', {name: W.retry})).toBeNull()
    expect(calls.filter(c => c.method === 'POST')).toHaveLength(1)
  })
  it('does not infer a purpose route from existing native detail or approval controls', async () => {
    const calls = installFetch({capabilities: ['sales.customer-returns.detail', 'sales.customer-returns.approve'], respond: () => undefined})
    mount(); await screen.findByText('Sayfa bulunamadı')
    expect(calls.filter(c => c.url.includes(ownerPath))).toHaveLength(0)
  })
})

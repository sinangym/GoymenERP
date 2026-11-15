import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { creditLimitAttemptStoreKey } from './CreditLimitCommand'
import { CreditLimitPage } from './CreditLimitPage'
import { CREDIT_LIMIT_SUCCESS, CREDIT_LIMIT_TEXT as T, creditLimitAccountName, shortCreditLimitReference } from './CreditLimitText'
import {
  account,
  candidates,
  CL_ACCOUNT,
  CL_COMPANY,
  CL_CURRENCY,
  CL_PARTY,
  historyPage,
  jsonResponse,
  listPage,
  proposedSet,
  approvedSet,
  refusal,
  rejected,
  succeeded,
} from './creditLimitFixtures'

// Synthetic consumer tests, NOT_ENV: they prove neither real grants nor a real browser, API or SQL.
const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))
const shell = vi.hoisted(() => ({
  state: {
    activeContext: null as null | { scopeMode: string; companyId: string | null; userId: string },
    routeEntryGeneration: 0,
    sessionBinding: null as string | null,
  },
}))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('../shell/shellContext', async (original) => ({
  ...await original<typeof import('../shell/shellContext')>(),
  useShellState: () => shell.state,
}))
vi.mock('../shell/shellState', async (original) => ({
  ...await original<typeof import('../shell/shellState')>(),
  contextKey: (state: typeof shell.state) => state.activeContext === null
    ? null : [state.activeContext.userId, state.activeContext.scopeMode, state.activeContext.companyId ?? 'group', 'g', '0'],
}))

const SESSION = 'sentetik-oturum'
const ACTOR = 'actor-a'

interface Call {
  readonly url: string
  readonly method: string
  readonly company: string | undefined
  readonly key: string | undefined
  readonly body: unknown
}

type Answer = (call: Call) => Response | Promise<Response>

/** Reads answer from `reads` by path; every POST goes to `command`. */
function serve(reads: { list?: Answer; account?: Answer; history?: Answer; candidates?: Answer }, command?: Answer) {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init: RequestInit = {}) => {
    const headers = (init.headers ?? {}) as Record<string, string>
    const call: Call = {
      url,
      method: init.method ?? 'GET',
      company: headers['X-Company-Id'],
      key: headers['Idempotency-Key'],
      body: typeof init.body === 'string' ? JSON.parse(init.body) : null,
    }
    calls.push(call)
    if (call.method === 'POST') return command === undefined ? new Response(null, { status: 503 }) : command(call)
    if (url.endsWith('/currency-candidates')) return (reads.candidates ?? (() => jsonResponse(candidates())))(call)
    if (url.endsWith('/history')) return (reads.history ?? (() => jsonResponse(historyPage([approvedSet()]))))(call)
    if (url.includes('/accounts/')) return (reads.account ?? (() => jsonResponse(account())))(call)
    return (reads.list ?? (() => jsonResponse(listPage([account()]))))(call)
  }))
  return calls
}

const posts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'POST')

function signIn(companyId: string | null = CL_COMPANY, sessionBinding: string | null = SESSION) {
  shell.state = {
    activeContext: { scopeMode: companyId === null ? 'group' : 'single', companyId, userId: ACTOR },
    routeEntryGeneration: 0,
    sessionBinding,
  }
}

function renderPage(props: { canPropose?: boolean; canApprove?: boolean } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  const view = render(<QueryClientProvider client={client}>
    <CreditLimitPage canPropose={props.canPropose ?? false} canApprove={props.canApprove ?? false} />
  </QueryClientProvider>)
  return { ...view, client }
}

async function openAccount(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: creditLimitAccountName(shortCreditLimitReference(CL_ACCOUNT)) }))
  return screen.findByTestId('finance-credit-limit-detail')
}

beforeEach(() => {
  boundary.capture.mockClear()
  boundary.report.mockClear()
  sessionStorage.clear()
  signIn()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  sessionStorage.clear()
})

describe('CreditLimitPage reads', () => {
  it('asks for a single company and reads nothing in a group context', () => {
    signIn(null)
    const calls = serve({})
    renderPage({ canApprove: true })
    expect(screen.getByTestId('finance-credit-limits').querySelector('[data-state="no-authorized-company"]')).not.toBeNull()
    expect(calls).toHaveLength(0)
  })

  it('shows loading, then the list with a caption and the limit as given', async () => {
    let release: (value: Response) => void = () => {}
    serve({ list: () => new Promise<Response>((resolve) => { release = resolve }) })
    renderPage()
    expect(screen.getByRole('heading', { level: 1, name: T.title })).not.toBeNull()
    expect(document.querySelector('[data-state="initial-loading"]')).not.toBeNull()
    expect(screen.queryByRole('table')).toBeNull()
    release(jsonResponse(listPage([account()])))
    const table = await screen.findByRole('table', { name: T.tableCaption })
    const row = table.querySelector(`[data-account="${CL_ACCOUNT}"]`)
    expect(row).not.toBeNull()
    expect(row?.textContent).toContain('150.000,50')
    expect(screen.getByText(T.pendingHidden)).not.toBeNull()
  })

  it('shows the empty state for a company without limits', async () => {
    serve({ list: () => jsonResponse(listPage([])) })
    renderPage()
    await waitFor(() => expect(document.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('reports a 403 to the shell boundary and draws no table', async () => {
    serve({ list: () => new Response(null, { status: 403 }) })
    renderPage()
    await waitFor(() => expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 }))
    expect(document.querySelector('[data-state="forbidden"]')).not.toBeNull()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('treats an answer of another company as malformed, not as data', async () => {
    serve({ list: () => jsonResponse(listPage([account()], '99999999-9999-4999-8999-999999999999')) })
    renderPage()
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('shows a removed limit as "limit yok", never zero', async () => {
    const removed = approvedSet({ kind: 'Remove', currencyId: null, amount: null })
    serve({ list: () => jsonResponse(listPage([account({ effective: removed })])) })
    renderPage()
    const table = await screen.findByRole('table', { name: T.tableCaption })
    expect(within(table).getByText(T.noLimitRemoved)).not.toBeNull()
    expect(table.textContent).not.toContain('0,00')
  })
})

const proposeForm = (detail: HTMLElement) => detail.querySelector('form[data-form="propose"]') as HTMLFormElement

async function chooseRemoval(user: ReturnType<typeof userEvent.setup>, form: HTMLFormElement) {
  await user.click(within(form).getByLabelText(T.proposeKindRemove))
}

describe('CreditLimitPage second person (FNS-D45)', () => {
  it('gives an approver alone no set, change or removal path, only a pointer to proposals', async () => {
    const user = userEvent.setup()
    const calls = serve({})
    renderPage({ canApprove: true })
    const detail = await openAccount(user)
    expect(await within(detail).findByRole('table', { name: T.historyCaption })).not.toBeNull()
    expect(within(detail).getByText(T.approverOnly)).not.toBeNull()
    expect(detail.querySelector('form[data-form="set"]')).toBeNull()
    expect(detail.querySelector('[data-form="remove"]')).toBeNull()
    expect(detail.querySelector('form[data-form="propose"]')).toBeNull()
    expect(detail.querySelector('[data-form="decision"]')).toBeNull()
    // No first limit for a new account either: that is a proposal too.
    expect(screen.queryByRole('button', { name: T.newHeading })).toBeNull()
    expect(posts(calls)).toHaveLength(0)
  })

  it('gives a holder of both authorities the proposal form, never a one-step change', async () => {
    const user = userEvent.setup()
    serve({})
    renderPage({ canApprove: true, canPropose: true })
    const detail = await openAccount(user)
    expect(proposeForm(detail)).not.toBeNull()
    expect(detail.querySelector('form[data-form="set"]')).toBeNull()
    expect(detail.querySelector('[data-form="remove"]')).toBeNull()
    expect(within(detail).queryByText(T.approverOnly)).toBeNull()
  })

  it('draws a history without a "recorded how" column: every record is a proposal and a decision', async () => {
    const user = userEvent.setup()
    serve({ history: () => jsonResponse(historyPage([approvedSet(), rejected()])) })
    renderPage({ canApprove: true })
    const detail = await openAccount(user)
    const table = await within(detail).findByRole('table', { name: T.historyCaption })
    expect(within(table).getAllByRole('columnheader')).toHaveLength(6)
    expect(table.querySelectorAll('[data-record]')).toHaveLength(2)
  })

  it('treats a record that claims a one-step change as malformed, not as history', async () => {
    const user = userEvent.setup()
    serve({ history: () => jsonResponse(historyPage([approvedSet({ isDirect: true })])) })
    renderPage({ canApprove: true })
    const detail = await openAccount(user)
    await waitFor(() => expect(detail.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(within(detail).queryByRole('table', { name: T.historyCaption })).toBeNull()
  })
})

describe('CreditLimitPage approver', () => {
  it('approves another person\'s proposal by its version', async () => {
    const user = userEvent.setup()
    const pending = account({ pending: proposedSet() })
    const calls = serve({ list: () => jsonResponse(listPage([pending])), account: () => jsonResponse(pending) },
      () => jsonResponse(succeeded(approvedSet({ id: proposedSet().id, amount: '200000', revision: '2', version: '2' }))))
    renderPage({ canApprove: true })
    const detail = await openAccount(user)
    const decision = await waitFor(() => {
      const element = detail.querySelector('[data-form="decision"]')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })
    await user.click(within(decision).getByRole('button', { name: T.approve }))
    expect(await within(decision).findByText(CREDIT_LIMIT_SUCCESS.approve)).not.toBeNull()
    const [post] = posts(calls)
    expect(post.url).toMatch(new RegExp(`/proposals/${proposedSet().id}/approval$`))
    expect(post.body).toEqual({ expectedVersion: '1' })
  })

  it('requires a reason before a rejection and sends it trimmed', async () => {
    const user = userEvent.setup()
    const pending = account({ pending: proposedSet() })
    const calls = serve({ list: () => jsonResponse(listPage([pending])), account: () => jsonResponse(pending) },
      () => jsonResponse(succeeded(proposedSet({ status: 'Rejected', rejectionReason: 'SENTETIK gerekçe',
        decidedAtUtc: '2026-11-14T10:00:00+00:00', version: '2' }))))
    renderPage({ canApprove: true })
    const detail = await openAccount(user)
    const decision = await waitFor(() => {
      const element = detail.querySelector('[data-form="decision"]')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })
    await user.click(within(decision).getByRole('button', { name: T.reject }))
    expect(within(decision).getByText(T.rejectReasonRequired)).not.toBeNull()
    expect(posts(calls)).toHaveLength(0)

    await user.type(within(decision).getByLabelText(T.rejectReason), '  SENTETIK gerekçe  ')
    await user.click(within(decision).getByRole('button', { name: T.reject }))
    expect(await within(decision).findByText(CREDIT_LIMIT_SUCCESS.reject)).not.toBeNull()
    const [post] = posts(calls)
    expect(post.url).toMatch(new RegExp(`/proposals/${proposedSet().id}/rejection$`))
    expect(post.body).toEqual({ expectedVersion: '1', reason: 'SENTETIK gerekçe' })
  })

  it('hides the decision for the caller\'s own proposal', async () => {
    const user = userEvent.setup()
    const own = account({ pending: proposedSet(), isOwnPending: true })
    serve({ list: () => jsonResponse(listPage([own])), account: () => jsonResponse(own) })
    renderPage({ canApprove: true, canPropose: true })
    const detail = await openAccount(user)
    expect(await within(detail).findByText(new RegExp(T.ownPending))).not.toBeNull()
    expect(detail.querySelector('[data-form="decision"]')).toBeNull()
    expect(within(detail).queryByRole('button', { name: T.approve })).toBeNull()
    // A pending proposal also hides a second proposal form.
    expect(detail.querySelector('form[data-form="propose"]')).toBeNull()
  })
})

describe('CreditLimitPage proposer', () => {
  it('opens the detail with the history table and proposes a limit naming the account revision', async () => {
    const user = userEvent.setup()
    const calls = serve({}, () => jsonResponse(succeeded(proposedSet({ id: 'aaaaaaaa-0000-4000-8000-000000000007',
      amount: '175000.25' }))))
    renderPage({ canPropose: true })
    const detail = await openAccount(user)
    expect(await within(detail).findByRole('table', { name: T.historyCaption })).not.toBeNull()

    const form = proposeForm(detail)
    expect(form).not.toBeNull()
    expect(detail.querySelector('form[data-form="set"]')).toBeNull()
    expect(detail.querySelector('[data-form="remove"]')).toBeNull()
    await user.selectOptions(await within(form).findByLabelText(T.currency), CL_CURRENCY)
    await user.type(within(form).getByLabelText(T.amount), '175000,25')
    await user.click(within(form).getByRole('button', { name: T.proposeSubmit }))

    expect(await within(form).findByText(CREDIT_LIMIT_SUCCESS.propose)).not.toBeNull()
    const [post] = posts(calls)
    expect(post.url).toMatch(/\/finance\/credit-limits\/proposals$/)
    expect(post.company).toBe(CL_COMPANY)
    expect(post.key).toMatch(/^[0-9a-f]{32}$/)
    expect(post.body).toEqual({ partyId: CL_PARTY, companyAccountId: CL_ACCOUNT, kind: 'Set', currencyId: CL_CURRENCY,
      amount: '175000.25', expectedRevision: '1' })
    // A definite answer releases the stored key.
    const storeKey = creditLimitAttemptStoreKey({ sessionBinding: SESSION, actorId: ACTOR, companyId: CL_COMPANY }, 'propose',
      CL_ACCOUNT)
    expect(storeKey).not.toBeNull()
    expect(sessionStorage.getItem(storeKey ?? '')).toBeNull()
  })

  it('refuses to send a proposal without a currency or a valid amount', async () => {
    const user = userEvent.setup()
    const calls = serve({})
    renderPage({ canPropose: true })
    const detail = await openAccount(user)
    const form = proposeForm(detail)
    await within(form).findByLabelText(T.currency)
    await user.type(within(form).getByLabelText(T.amount), '0')
    await user.click(within(form).getByRole('button', { name: T.proposeSubmit }))
    expect(within(form).getByText(T.currencyRequired)).not.toBeNull()
    expect(within(form).getByText(T.amountError['not-positive'])).not.toBeNull()
    expect(posts(calls)).toHaveLength(0)
  })

  it('proposes a removal without currency or amount', async () => {
    const user = userEvent.setup()
    const calls = serve({}, () => jsonResponse(succeeded(proposedSet({ kind: 'Remove', currencyId: null, amount: null }))))
    renderPage({ canPropose: true })
    const detail = await openAccount(user)
    const form = proposeForm(detail)
    await chooseRemoval(user, form)
    expect(within(form).getByText(T.removeHint)).not.toBeNull()
    await user.click(within(form).getByRole('button', { name: T.proposeSubmit }))
    expect(await within(form).findByText(CREDIT_LIMIT_SUCCESS.propose)).not.toBeNull()
    const [post] = posts(calls)
    expect(post.url).toMatch(/\/finance\/credit-limits\/proposals$/)
    expect(post.body).toEqual({ partyId: CL_PARTY, companyAccountId: CL_ACCOUNT, kind: 'Remove', currencyId: null, amount: null,
      expectedRevision: '1' })
  })

  it('shows a version conflict with a reload action, never a success', async () => {
    const user = userEvent.setup()
    serve({}, () => jsonResponse(refusal('VersionConflict', [{ field: 'expectedRevision', code: 'version_conflict' }]), 409))
    renderPage({ canPropose: true })
    const detail = await openAccount(user)
    const form = proposeForm(detail)
    await chooseRemoval(user, form)
    await user.click(within(form).getByRole('button', { name: T.proposeSubmit }))
    expect(await within(form).findByText(T.outcome.VersionConflict)).not.toBeNull()
    expect(within(form).getByRole('button', { name: T.reload })).not.toBeNull()
    expect(within(form).queryByText(CREDIT_LIMIT_SUCCESS.propose)).toBeNull()
  })

  it('keeps the same key after an unknown outcome and resends the same command', async () => {
    const user = userEvent.setup()
    let answer: () => Response = () => new Response(null, { status: 503 })
    const calls = serve({}, () => answer())
    renderPage({ canPropose: true })
    const detail = await openAccount(user)
    const form = proposeForm(detail)
    await chooseRemoval(user, form)
    await user.click(within(form).getByRole('button', { name: T.proposeSubmit }))
    expect(await within(form).findByText(T.unknownTitle)).not.toBeNull()

    const storeKey = creditLimitAttemptStoreKey({ sessionBinding: SESSION, actorId: ACTOR, companyId: CL_COMPANY }, 'propose',
      CL_ACCOUNT)
    expect(storeKey).toBe(`goymen.finance.credit-limit.propose.v1:${SESSION}:${ACTOR}:${CL_COMPANY}:${CL_ACCOUNT}`)
    const stored = JSON.parse(sessionStorage.getItem(storeKey ?? '') ?? 'null') as { key: string } | null
    expect(stored?.key).toBe(posts(calls)[0].key)

    answer = () => jsonResponse(succeeded(proposedSet({ id: 'aaaaaaaa-0000-4000-8000-000000000009', kind: 'Remove',
      currencyId: null, amount: null })))
    await user.click(within(form).getByRole('button', { name: T.resend }))
    expect(await within(form).findByText(CREDIT_LIMIT_SUCCESS.propose)).not.toBeNull()
    const [first, second] = posts(calls)
    expect(second.key).toBe(first.key)
    expect(second.body).toEqual(first.body)
    expect(sessionStorage.getItem(storeKey ?? '')).toBeNull()
  })

  it('stores nothing while the server gave no session binding', async () => {
    signIn(CL_COMPANY, null)
    const user = userEvent.setup()
    serve({}, () => new Response(null, { status: 503 }))
    renderPage({ canPropose: true })
    const detail = await openAccount(user)
    const form = proposeForm(detail)
    await chooseRemoval(user, form)
    await user.click(within(form).getByRole('button', { name: T.proposeSubmit }))
    expect(await within(form).findByText(T.unknownTitle)).not.toBeNull()
    expect(sessionStorage.length).toBe(0)
  })

  it('proposes a limit for a new account at revision "0" and refuses an invalid identity', async () => {
    const user = userEvent.setup()
    const NEW_PARTY = 'dddddddd-0000-4000-8000-000000000010'
    const NEW_ACCOUNT = 'eeeeeeee-0000-4000-8000-000000000011'
    const calls = serve({}, () => jsonResponse(succeeded(proposedSet({ partyId: NEW_PARTY, companyAccountId: NEW_ACCOUNT }))))
    renderPage({ canPropose: true })
    await user.click(await screen.findByRole('button', { name: T.newHeading }))
    const section = screen.getByTestId('finance-credit-limit-new')
    await user.type(within(section).getByLabelText(T.partyId), 'not-an-id')
    await user.type(within(section).getByLabelText(T.companyAccountId), NEW_ACCOUNT)
    await user.click(within(section).getByRole('button', { name: T.open }))
    expect(within(section).getByText(T.identityInvalid)).not.toBeNull()

    await user.clear(within(section).getByLabelText(T.partyId))
    await user.type(within(section).getByLabelText(T.partyId), ` ${NEW_PARTY.toUpperCase()} `)
    await user.click(within(section).getByRole('button', { name: T.open }))
    const form = section.querySelector('form[data-form="propose"]') as HTMLFormElement
    expect(form).not.toBeNull()
    await user.selectOptions(await within(form).findByLabelText(T.currency), CL_CURRENCY)
    await user.type(within(form).getByLabelText(T.amount), '50000')
    await user.click(within(form).getByRole('button', { name: T.proposeSubmit }))
    expect(await within(form).findByText(CREDIT_LIMIT_SUCCESS.propose)).not.toBeNull()
    expect(posts(calls)[0].body).toEqual({ partyId: NEW_PARTY, companyAccountId: NEW_ACCOUNT, kind: 'Set', currencyId: CL_CURRENCY,
      amount: '50000', expectedRevision: '0' })
  })
})

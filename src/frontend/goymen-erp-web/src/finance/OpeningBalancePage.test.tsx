import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { openingBalanceAttemptStoreKey, PROPOSAL_TARGET } from './OpeningBalanceCommand'
import { OpeningBalancePage } from './OpeningBalancePage'
import {
  OPENING_BALANCE_SUCCESS,
  OPENING_BALANCE_TEXT as T,
  OPENING_PERIOD_NOTICE,
  openingBalanceIssueText,
} from './OpeningBalanceText'
import {
  approved,
  currencyCandidates,
  jsonResponse,
  listPage,
  OB_ACCOUNT,
  OB_CASH,
  OB_COMPANY,
  OB_CURRENCY,
  OB_PARTY,
  partyProposed,
  period,
  proposed,
  refusal,
  rejected,
  succeeded,
} from './openingBalanceFixtures'

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
const BINDING = { sessionBinding: SESSION, actorId: ACTOR, companyId: OB_COMPANY }

interface Call {
  readonly url: string
  readonly method: string
  readonly company: string | undefined
  readonly key: string | undefined
  readonly body: unknown
}

type Answer = (call: Call) => Response | Promise<Response>

/** Reads answer from `reads` by path; every POST goes to `command`. */
function serve(reads: { period?: Answer; list?: Answer; currencies?: Answer }, command?: Answer) {
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
    if (url.endsWith('/period')) return (reads.period ?? (() => jsonResponse(period())))(call)
    if (url.endsWith('/currency-candidates')) return (reads.currencies ?? (() => jsonResponse(currencyCandidates())))(call)
    return (reads.list ?? (() => jsonResponse(listPage([proposed()]))))(call)
  }))
  return calls
}

const posts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'POST')
const listReads = (calls: readonly Call[]) =>
  calls.filter((call) => call.method === 'GET' && /\/finance\/opening-balances$/.test(call.url))

function signIn(companyId: string | null = OB_COMPANY, sessionBinding: string | null = SESSION) {
  shell.state = {
    activeContext: { scopeMode: companyId === null ? 'group' : 'single', companyId, userId: ACTOR },
    routeEntryGeneration: 0,
    sessionBinding,
  }
}

function renderPage(canPropose = false, canApprove = false) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  return render(<QueryClientProvider client={client}>
    <OpeningBalancePage canPropose={canPropose} canApprove={canApprove} />
  </QueryClientProvider>)
}

const root = () => screen.getByTestId('finance-opening-balances')
const row = async (id: string) => {
  await screen.findByRole('table', { name: T.listCaption })
  const found = root().querySelector(`[data-opening="${id}"]`) as HTMLElement | null
  expect(found).not.toBeNull()
  return found as HTMLElement
}
const form = (name: string) => {
  const found = root().querySelector(`form[data-form="${name}"]`) as HTMLElement | null
  expect(found).not.toBeNull()
  return found as HTMLElement
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

describe('OpeningBalancePage period gate', () => {
  it('asks for a single company and reads nothing in a group context', () => {
    signIn(null)
    const calls = serve({})
    renderPage(true, true)
    expect(root().querySelector('[data-state="no-authorized-company"]')).not.toBeNull()
    expect(calls).toHaveLength(0)
  })

  it.each(['Closed', 'Unavailable'] as const)('shows only the notice while the period is %s', async (state) => {
    const calls = serve({ period: () => jsonResponse(period(state)) })
    renderPage(true, true)
    const notice = await screen.findByText(OPENING_PERIOD_NOTICE[state])
    expect(notice.getAttribute('data-period')).toBe(state)
    expect(root().querySelector('table')).toBeNull()
    expect(root().querySelector('form')).toBeNull()
    expect(listReads(calls)).toHaveLength(0)
    expect(calls.some((call) => call.url.endsWith('/currency-candidates'))).toBe(false)
  })

  it('shows an unreadable period as an error and reports 403 to the boundary', async () => {
    serve({ period: () => new Response(null, { status: 403 }) })
    renderPage(true, true)
    await waitFor(() => expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 }))
    expect(root().querySelector('table')).toBeNull()
    expect(root().querySelector('form')).toBeNull()
  })

  it('shows a malformed period as malformed, never as open', async () => {
    serve({ period: () => jsonResponse(period('Open', null)) })
    renderPage(true, true)
    await waitFor(() => expect(root().querySelector('[data-state="malformed"]')).not.toBeNull())
    expect(root().querySelector('form')).toBeNull()
  })
})

describe('OpeningBalancePage list', () => {
  it('shows the openings for the verified company and no command to a reader', async () => {
    const calls = serve({ list: () => jsonResponse(listPage([proposed(), partyProposed(), approved(), rejected()])) })
    renderPage()
    const cash = await row(proposed().id)
    expect(cash.getAttribute('data-status')).toBe('Proposed')
    expect(cash.textContent).toContain('150.000,50')
    const rejectedRow = await row(rejected().id)
    expect(rejectedRow.textContent).toContain('SENTETIK red gerekçesi')
    expect(calls.filter((call) => call.method === 'GET').every((call) => call.company === OB_COMPANY)).toBe(true)
    expect(root().querySelector('form')).toBeNull()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
    expect(calls.some((call) => call.url.endsWith('/currency-candidates'))).toBe(false)
  })

  it('shows an empty list as empty', async () => {
    serve({ list: () => jsonResponse(listPage([])) })
    renderPage()
    expect(await screen.findByText(T.listEmpty)).not.toBeNull()
  })

  it('offers no decision on the caller\'s own proposal or on a decided opening', async () => {
    serve({ list: () => jsonResponse(listPage([proposed({ isOwnProposal: true }), approved()])) })
    renderPage(false, true)
    const own = await row(proposed().id)
    expect(own.querySelector('[data-own-proposal="true"]')).not.toBeNull()
    expect(within(own).queryByRole('button', { name: T.approve })).toBeNull()
    expect(within(await row(approved().id)).queryByRole('button', { name: T.approve })).toBeNull()
  })
})

describe('OpeningBalancePage decisions', () => {
  it('approves with the version looked at and reloads the reads after success', async () => {
    const user = userEvent.setup()
    const calls = serve({}, () => jsonResponse(succeeded(approved({ id: proposed().id }))))
    renderPage(false, true)
    const target = await row(proposed().id)
    await user.click(within(target).getByRole('button', { name: T.approve }))
    expect(await screen.findByText(OPENING_BALANCE_SUCCESS.approve)).not.toBeNull()
    const [post] = posts(calls)
    expect(post.url).toMatch(new RegExp(`/finance/opening-balances/${proposed().id}/approve$`))
    expect(post.body).toEqual({ expectedVersion: '1' })
    expect(post.key).toMatch(/^[0-9a-f]{32}$/)
    expect(post.company).toBe(OB_COMPANY)
    await waitFor(() => expect(listReads(calls).length).toBeGreaterThan(1))
  })

  it('shows a version conflict with a reload action', async () => {
    const user = userEvent.setup()
    serve({}, () => jsonResponse(refusal('VersionConflict', [{ field: 'expectedVersion', code: 'version_conflict' }]), 409))
    renderPage(false, true)
    await user.click(within(await row(proposed().id)).getByRole('button', { name: T.approve }))
    expect(await screen.findByText(T.outcome.VersionConflict)).not.toBeNull()
    expect(screen.getByRole('button', { name: T.reload })).not.toBeNull()
  })

  it('shows the server refusing a proposer\'s own decision by its code', async () => {
    const user = userEvent.setup()
    serve({}, () => jsonResponse(refusal('ValidationFailed', [{ field: '', code: 'finance_approver_is_proposer' }]), 422))
    renderPage(false, true)
    await user.click(within(await row(proposed().id)).getByRole('button', { name: T.approve }))
    expect(await screen.findByText(openingBalanceIssueText('finance_approver_is_proposer'))).not.toBeNull()
  })

  it('requires a reason to reject and sends it trimmed with the version', async () => {
    const user = userEvent.setup()
    const calls = serve({}, () => jsonResponse(succeeded(rejected({ id: proposed().id }))))
    renderPage(false, true)
    await user.click(within(await row(proposed().id)).getByRole('button', { name: T.rejectOpen }))
    const reject = form('reject')
    await user.click(within(reject).getByRole('button', { name: T.rejectSubmit }))
    expect(await within(reject).findByText(T.rejectReasonRequired)).not.toBeNull()
    expect(posts(calls)).toHaveLength(0)
    await user.type(within(reject).getByLabelText(T.rejectReason), '  SENTETIK red gerekçesi  ')
    await user.click(within(reject).getByRole('button', { name: T.rejectSubmit }))
    expect(await screen.findByText(OPENING_BALANCE_SUCCESS.reject)).not.toBeNull()
    expect(posts(calls)[0].body).toEqual({ expectedVersion: '1', reason: 'SENTETIK red gerekçesi' })
  })

  it('keeps the key of an unknown approval and resends the same command with it', async () => {
    const user = userEvent.setup()
    let answer: Response = new Response(null, { status: 503 })
    const calls = serve({}, () => answer)
    renderPage(false, true)
    await user.click(within(await row(proposed().id)).getByRole('button', { name: T.approve }))
    expect(await screen.findByText(T.unknownTitle)).not.toBeNull()
    const storeKey = openingBalanceAttemptStoreKey(BINDING, 'approve', proposed().id)
    expect(storeKey).not.toBeNull()
    expect(sessionStorage.getItem(storeKey as string)).not.toBeNull()
    answer = jsonResponse(succeeded(approved({ id: proposed().id })))
    await user.click(screen.getByRole('button', { name: T.resend }))
    expect(await screen.findByText(OPENING_BALANCE_SUCCESS.approve)).not.toBeNull()
    const [first, second] = posts(calls)
    expect(second.key).toBe(first.key)
    expect(second.body).toEqual(first.body)
    expect(sessionStorage.getItem(storeKey as string)).toBeNull()
  })
})

describe('OpeningBalancePage proposal', () => {
  it('validates the form and sends nothing while it is invalid', async () => {
    const user = userEvent.setup()
    const calls = serve({})
    renderPage(true)
    const propose = await waitFor(() => form('propose'))
    await within(propose).findByLabelText(T.currency)
    await user.click(within(propose).getByRole('button', { name: T.proposeSubmit }))
    expect(within(propose).getByText(T.targetKindRequired)).not.toBeNull()
    expect(within(propose).getByText(T.currencyRequired)).not.toBeNull()
    expect(within(propose).getByText(T.amountError.required)).not.toBeNull()
    expect(within(propose).getByText(T.asOfDateRequired)).not.toBeNull()
    expect(posts(calls)).toHaveLength(0)
  })

  it('proposes a cash opening without party or direction, the amount as typed', async () => {
    const user = userEvent.setup()
    const calls = serve({}, () => jsonResponse(succeeded(proposed())))
    renderPage(true)
    const propose = await waitFor(() => form('propose'))
    await user.selectOptions(within(propose).getByLabelText(T.targetKind), 'CashAccount')
    expect(within(propose).queryByLabelText(T.partyId)).toBeNull()
    await user.type(within(propose).getByLabelText(T.targetId), OB_CASH)
    await user.selectOptions(await within(propose).findByLabelText(T.currency), OB_CURRENCY)
    await user.type(within(propose).getByLabelText(T.amount), '150000,50')
    await user.type(within(propose).getByLabelText(T.asOfDate), '2026-11-01')
    await user.click(within(propose).getByRole('button', { name: T.proposeSubmit }))
    expect(await screen.findByText(OPENING_BALANCE_SUCCESS.propose)).not.toBeNull()
    expect(posts(calls)[0].body).toEqual({ targetKind: 'CashAccount', targetId: OB_CASH, partyId: null,
      currencyId: OB_CURRENCY, direction: null, amount: '150000.50', asOfDate: '2026-11-01' })
    expect(sessionStorage.getItem(openingBalanceAttemptStoreKey(BINDING, 'propose', PROPOSAL_TARGET) as string)).toBeNull()
  })

  it('requires party and direction for a party opening and sends them', async () => {
    const user = userEvent.setup()
    const calls = serve({}, () => jsonResponse(succeeded(partyProposed({ amount: '2500' }))))
    renderPage(true)
    const propose = await waitFor(() => form('propose'))
    await user.selectOptions(within(propose).getByLabelText(T.targetKind), 'PartyAccount')
    await user.type(within(propose).getByLabelText(T.targetId), OB_ACCOUNT)
    await user.selectOptions(await within(propose).findByLabelText(T.currency), OB_CURRENCY)
    await user.type(within(propose).getByLabelText(T.amount), '2500')
    await user.type(within(propose).getByLabelText(T.asOfDate), '2026-11-01')
    await user.click(within(propose).getByRole('button', { name: T.proposeSubmit }))
    expect(within(propose).getByText(T.directionRequired)).not.toBeNull()
    expect(within(propose).getByText(T.identityInvalid)).not.toBeNull()
    expect(posts(calls)).toHaveLength(0)
    await user.type(within(propose).getByLabelText(T.partyId), OB_PARTY)
    await user.selectOptions(within(propose).getByLabelText(T.direction), 'Debit')
    await user.click(within(propose).getByRole('button', { name: T.proposeSubmit }))
    expect(await screen.findByText(OPENING_BALANCE_SUCCESS.propose)).not.toBeNull()
    expect(posts(calls)[0].body).toEqual({ targetKind: 'PartyAccount', targetId: OB_ACCOUNT, partyId: OB_PARTY,
      currencyId: OB_CURRENCY, direction: 'Debit', amount: '2500', asOfDate: '2026-11-01' })
  })

  it('names the missing currency candidates and offers no currency', async () => {
    serve({ currencies: () => jsonResponse(currencyCandidates(OB_COMPANY, [])) })
    renderPage(true)
    expect(await screen.findByText(T.noCurrencies)).not.toBeNull()
    expect(screen.queryByLabelText(T.currency)).toBeNull()
  })

  it('shows a duplicate proposal refusal by its code', async () => {
    const user = userEvent.setup()
    serve({}, () => jsonResponse(refusal('ValidationFailed', [{ field: 'targetId', code: 'opening_balance_exists' }]), 422))
    renderPage(true)
    const propose = await waitFor(() => form('propose'))
    await user.selectOptions(within(propose).getByLabelText(T.targetKind), 'CashAccount')
    await user.type(within(propose).getByLabelText(T.targetId), OB_CASH)
    await user.selectOptions(await within(propose).findByLabelText(T.currency), OB_CURRENCY)
    await user.type(within(propose).getByLabelText(T.amount), '10')
    await user.type(within(propose).getByLabelText(T.asOfDate), '2026-11-01')
    await user.click(within(propose).getByRole('button', { name: T.proposeSubmit }))
    expect(await screen.findByText(openingBalanceIssueText('opening_balance_exists'))).not.toBeNull()
  })
})

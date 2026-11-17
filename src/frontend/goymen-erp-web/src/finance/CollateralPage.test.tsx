import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { collateralAttemptStoreKey } from './CollateralCommand'
import { CollateralPage } from './CollateralPage'
import { COLLATERAL_SUCCESS, COLLATERAL_TEXT as T, collateralFigureReasonText, collateralIssueText } from './CollateralText'
import {
  account,
  changed,
  CO_ACCOUNT,
  CO_COLLATERAL,
  CO_COMPANY,
  CO_CURRENCY,
  CO_PARTY,
  CO_TYPE,
  currencyCandidates,
  historyPage,
  jsonResponse,
  recorded,
  refusal,
  released,
  succeeded,
  superseded,
  typeCandidates,
  unavailable,
} from './collateralFixtures'

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
const BINDING = { sessionBinding: SESSION, actorId: ACTOR, companyId: CO_COMPANY }

interface Call {
  readonly url: string
  readonly method: string
  readonly company: string | undefined
  readonly key: string | undefined
  readonly body: unknown
}

type Answer = (call: Call) => Response | Promise<Response>

/** Reads answer from `reads` by path; every POST goes to `command`. */
function serve(reads: { account?: Answer; history?: Answer; types?: Answer; currencies?: Answer; banks?: Answer }, command?: Answer) {
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
    if (url.endsWith('/type-candidates')) return (reads.types ?? (() => jsonResponse(typeCandidates())))(call)
    if (url.endsWith('/currency-candidates')) return (reads.currencies ?? (() => jsonResponse(currencyCandidates())))(call)
    if (url.endsWith('/bank-candidates')) return (reads.banks ?? (() => jsonResponse({ companyId: CO_COMPANY, items: [] })))(call)
    if (url.endsWith('/history')) return (reads.history ?? (() => jsonResponse(historyPage([recorded()]))))(call)
    return (reads.account ?? (() => jsonResponse(account())))(call)
  }))
  return calls
}

const posts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'POST')
const accountReads = (calls: readonly Call[]) => calls.filter((call) => call.url.includes('/accounts/'))

function signIn(companyId: string | null = CO_COMPANY, sessionBinding: string | null = SESSION) {
  shell.state = {
    activeContext: { scopeMode: companyId === null ? 'group' : 'single', companyId, userId: ACTOR },
    routeEntryGeneration: 0,
    sessionBinding,
  }
}

function renderPage(canRecord = false) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  return render(<QueryClientProvider client={client}><CollateralPage canRecord={canRecord} /></QueryClientProvider>)
}

async function openAccount(user: ReturnType<typeof userEvent.setup>, id: string = CO_ACCOUNT) {
  await user.type(screen.getByLabelText(T.companyAccountId), id)
  await user.click(screen.getByRole('button', { name: T.show }))
  return screen.findByTestId('finance-collateral-account')
}

const form = (scope: HTMLElement, name: string) => {
  const found = scope.querySelector(`form[data-form="${name}"]`) as HTMLElement | null
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

describe('CollateralPage reads', () => {
  it('asks for a single company and reads nothing in a group context', () => {
    signIn(null)
    const calls = serve({})
    renderPage(true)
    expect(screen.getByTestId('finance-collaterals').querySelector('[data-state="no-authorized-company"]')).not.toBeNull()
    expect(calls).toHaveLength(0)
  })

  it('refuses an account id that is not an identity and reads no account', async () => {
    const user = userEvent.setup()
    const calls = serve({})
    renderPage()
    await user.type(screen.getByLabelText(T.companyAccountId), 'not-an-account')
    await user.click(screen.getByRole('button', { name: T.show }))
    expect(await screen.findByText(T.identityInvalid)).not.toBeNull()
    expect(accountReads(calls)).toHaveLength(0)
    expect(screen.queryByTestId('finance-collateral-account')).toBeNull()
  })

  it('shows the held collaterals and the weighted collateral and documentary risk as two separate figures', async () => {
    const user = userEvent.setup()
    const calls = serve({})
    renderPage()
    const detail = await openAccount(user)
    const held = await within(detail).findByRole('table', { name: T.heldCaption })
    const row = held.querySelector(`[data-collateral="${CO_COLLATERAL}"]`)
    expect(row?.textContent).toContain('250.000,50 TRY')
    expect(row?.textContent).toContain('SENTETIK-TUR')
    expect(row?.textContent).toContain(T.openEnded)
    expect(within(detail).getByTestId('collateral-weighted').textContent).toBe('187.500,375 TRY')
    expect(within(detail).getByTestId('collateral-documentary').textContent).toBe('40.000 TRY')
    // FNS-D58: the documentary risk is never added to anything on this screen.
    expect(detail.textContent).not.toContain('227.500,375')
    expect(await within(detail).findByRole('table', { name: T.historyCaption })).not.toBeNull()
    expect(accountReads(calls).every((call) => call.company === CO_COMPANY)).toBe(true)
    // A reader sees no command.
    expect(detail.querySelector('form')).toBeNull()
    expect(within(detail).queryByRole('button', { name: T.changeOpen })).toBeNull()
  })

  it('names why a figure cannot be shown and never draws an amount for it', async () => {
    const user = userEvent.setup()
    serve({ account: () => jsonResponse(account({
      weightedCollateral: unavailable('finance_collateral_type_rate_unavailable'),
      documentaryRisk: unavailable('finance_documentary_risk_withheld', 'Withheld'),
    })) })
    renderPage()
    const detail = await openAccount(user)
    const weighted = await within(detail).findByTestId('collateral-weighted')
    expect(weighted.getAttribute('data-figure-state')).toBe('Unavailable')
    expect(weighted.textContent).toBe(collateralFigureReasonText('finance_collateral_type_rate_unavailable'))
    const documentary = within(detail).getByTestId('collateral-documentary')
    expect(documentary.getAttribute('data-figure-state')).toBe('Withheld')
    expect(documentary.textContent).toBe(collateralFigureReasonText('finance_documentary_risk_withheld'))
  })

  it('shows a recorded zero as zero and an account without collateral as empty', async () => {
    const user = userEvent.setup()
    serve({
      account: () => jsonResponse(account({ held: [], weightedCollateral: { state: 'Available', amounts: [], reasonCode: null } })),
      history: () => new Response(null, { status: 404 }),
    })
    renderPage()
    const detail = await openAccount(user)
    expect(await within(detail).findByText(T.heldEmpty)).not.toBeNull()
    expect(within(detail).getByTestId('collateral-weighted').textContent).toBe(T.figureZero)
    // An account without a revision answers 404 for its history: that is empty, not an error.
    expect(await within(detail).findByText(T.historyEmpty)).not.toBeNull()
    expect(detail.querySelector('[data-state="not-found"]')).toBeNull()
  })

  it('shows the history with release reasons and superseded revisions', async () => {
    const user = userEvent.setup()
    serve({ account: () => jsonResponse(account({ held: [] })),
      history: () => jsonResponse(historyPage([released(), superseded()])) })
    renderPage()
    const detail = await openAccount(user)
    const history = await within(detail).findByRole('table', { name: T.historyCaption })
    expect(history.textContent).toContain('SENTETIK çözüm gerekçesi')
    expect(history.querySelectorAll('tbody tr')).toHaveLength(2)
  })

  it('reports a 403 to the shell boundary and draws no account', async () => {
    const user = userEvent.setup()
    serve({ account: () => new Response(null, { status: 403 }) })
    renderPage()
    const detail = await openAccount(user)
    await waitFor(() => expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 }))
    expect(detail.querySelector('[data-state="forbidden"]')).not.toBeNull()
    expect(within(detail).queryByRole('table', { name: T.heldCaption })).toBeNull()
  })
})

describe('CollateralPage record', () => {
  async function fillRecord(user: ReturnType<typeof userEvent.setup>, record: HTMLElement, amount = '250000,50') {
    await user.selectOptions(await within(record).findByLabelText(T.type), CO_TYPE)
    await user.selectOptions(within(record).getByLabelText(T.currency), CO_CURRENCY)
    await user.type(within(record).getByLabelText(T.amount), amount)
    fireEvent.change(within(record).getByLabelText(T.validFrom), { target: { value: '2026-11-01' } })
    await user.type(within(record).getByLabelText(T.documentReference), 'SENTETIK-BELGE-1')
  }

  it('sends only an explicitly selected real bank identity and source version, keeping an unknown attempt unchanged', async () => {
    const user = userEvent.setup()
    const bank = { companyId: CO_COMPANY, bankInstitutionId: '33333333-3333-4333-8333-333333333333',
      name: 'Sentetik banka', eftCode: '0007', sourceVersion: 'settings.bank-institutions:synthetic:3' }
    const calls = serve({ banks: () => jsonResponse({ companyId: CO_COMPANY, items: [bank] }) }, () => new Response(null, { status: 503 }))
    renderPage(true); const record = form(await openAccount(user), 'record'); await fillRecord(user, record)
    expect(within(record).getByLabelText(T.bank)).toHaveProperty('value', '')
    await user.selectOptions(within(record).getByLabelText(T.bank), `${bank.bankInstitutionId}::${bank.sourceVersion}`)
    await user.click(within(record).getByRole('button', { name: T.recordSubmit }))
    await waitFor(() => expect(posts(calls)).toHaveLength(1))
    expect(posts(calls)[0].body).toMatchObject({ bankInstitutionId: bank.bankInstitutionId, bankSourceVersion: bank.sourceVersion })
    expect(posts(calls)[0].body).not.toHaveProperty('bankName')
    expect(posts(calls)[0].body).not.toHaveProperty('bankAccountId')
  })

  it('records a collateral in one step with the company only in the header', async () => {
    const user = userEvent.setup()
    const calls = serve({}, () => jsonResponse(succeeded(recorded({ collateralId: 'cccccccc-0000-4000-8000-000000000009' }))))
    renderPage(true)
    const record = form(await openAccount(user), 'record')
    expect(within(record).getByRole('option', { name: 'SENTETIK-TUR — SENTETIK teminat türü (%75)' })).not.toBeNull()
    await fillRecord(user, record)
    await user.click(within(record).getByRole('button', { name: T.recordSubmit }))

    expect(await within(record).findByText(COLLATERAL_SUCCESS.record)).not.toBeNull()
    const [post] = posts(calls)
    expect(post.url).toMatch(/\/finance\/collaterals$/)
    expect(post.company).toBe(CO_COMPANY)
    expect(post.key).toMatch(/^[0-9a-f]{32}$/)
    expect(post.body).toEqual({ partyId: CO_PARTY, companyAccountId: CO_ACCOUNT, collateralTypeId: CO_TYPE,
      currencyId: CO_CURRENCY, amount: '250000.50', validFrom: '2026-11-01', validUntil: null,
      documentReference: 'SENTETIK-BELGE-1' })
    expect(sessionStorage.getItem(collateralAttemptStoreKey(BINDING, 'record', CO_ACCOUNT) ?? '')).toBeNull()
  })

  it('sends nothing without a type, a currency, a positive amount or an ordered validity', async () => {
    const user = userEvent.setup()
    const calls = serve({})
    renderPage(true)
    const record = form(await openAccount(user), 'record')
    await within(record).findByLabelText(T.type)
    await user.click(within(record).getByRole('button', { name: T.recordSubmit }))
    expect(within(record).getByText(T.typeRequired)).not.toBeNull()
    expect(within(record).getByText(T.currencyRequired)).not.toBeNull()
    expect(within(record).getByText(T.amountError.required)).not.toBeNull()
    expect(within(record).getByText(T.validFromRequired)).not.toBeNull()

    await fillRecord(user, record, '0')
    fireEvent.change(within(record).getByLabelText(T.validUntil), { target: { value: '2026-10-31' } })
    await user.click(within(record).getByRole('button', { name: T.recordSubmit }))
    expect(within(record).getByText(T.amountError['not-positive'])).not.toBeNull()
    expect(within(record).getByText(T.validityInvalid)).not.toBeNull()
    expect(posts(calls)).toHaveLength(0)
  })

  it('cannot record while Settings lists no collateral type', async () => {
    const user = userEvent.setup()
    serve({ types: () => jsonResponse(typeCandidates([])) })
    renderPage(true)
    const record = form(await openAccount(user), 'record')
    expect(await within(record).findByText(T.noTypes)).not.toBeNull()
    expect(within(record).queryByLabelText(T.type)).toBeNull()
  })

  it('names a server refusal in Turkish, such as a cheque type', async () => {
    const user = userEvent.setup()
    serve({}, () => jsonResponse(refusal('ValidationFailed',
      [{ field: 'collateralTypeId', code: 'collateral_type_is_instrument' }]), 422))
    renderPage(true)
    const record = form(await openAccount(user), 'record')
    await fillRecord(user, record)
    await user.click(within(record).getByRole('button', { name: T.recordSubmit }))
    expect(await within(record).findByText(collateralIssueText('collateral_type_is_instrument'))).not.toBeNull()
    expect(within(record).queryByText(COLLATERAL_SUCCESS.record)).toBeNull()
  })

  it('keeps the same key after an unknown record outcome, asks for a check and resends the same command', async () => {
    const user = userEvent.setup()
    let answer: () => Response = () => new Response(null, { status: 503 })
    const calls = serve({}, () => answer())
    renderPage(true)
    const record = form(await openAccount(user), 'record')
    await fillRecord(user, record)
    await user.click(within(record).getByRole('button', { name: T.recordSubmit }))
    expect(await within(record).findByText(T.unknownTitle)).not.toBeNull()
    expect(within(record).getByText(T.unknownRecordWarning)).not.toBeNull()
    expect(within(record).getByRole('button', { name: T.reload })).not.toBeNull()

    const storeKey = collateralAttemptStoreKey(BINDING, 'record', CO_ACCOUNT)
    expect(storeKey).toBe(`goymen.finance.collateral.record.v1:${SESSION}:${ACTOR}:${CO_COMPANY}:${CO_ACCOUNT}`)
    const stored = JSON.parse(sessionStorage.getItem(storeKey ?? '') ?? 'null') as { key: string } | null
    expect(stored?.key).toBe(posts(calls)[0].key)

    answer = () => jsonResponse(succeeded(recorded()))
    await user.click(within(record).getByRole('button', { name: T.resend }))
    expect(await within(record).findByText(COLLATERAL_SUCCESS.record)).not.toBeNull()
    const [first, second] = posts(calls)
    expect(second.key).toBe(first.key)
    expect(second.body).toEqual(first.body)
    expect(sessionStorage.getItem(storeKey ?? '')).toBeNull()
  })

  it('stores nothing while the server gave no session binding', async () => {
    signIn(CO_COMPANY, null)
    const user = userEvent.setup()
    serve({}, () => new Response(null, { status: 503 }))
    renderPage(true)
    const record = form(await openAccount(user), 'record')
    await fillRecord(user, record)
    await user.click(within(record).getByRole('button', { name: T.recordSubmit }))
    expect(await within(record).findByText(T.unknownTitle)).not.toBeNull()
    expect(sessionStorage.length).toBe(0)
  })
})

describe('CollateralPage change and release', () => {
  it('changes a held collateral naming the revision the person looked at', async () => {
    const user = userEvent.setup()
    const calls = serve({}, () => jsonResponse(succeeded(changed())))
    renderPage(true)
    const detail = await openAccount(user)
    await user.click(await within(detail).findByRole('button', { name: T.changeOpen }))
    const change = form(detail, 'change')
    // Prefilled with the held values, the amount in the typed grammar.
    expect((within(change).getByLabelText(T.amount) as HTMLInputElement).value).toBe('250000,50')
    await user.clear(within(change).getByLabelText(T.amount))
    await user.type(within(change).getByLabelText(T.amount), '300000')
    await user.click(within(change).getByRole('button', { name: T.changeSubmit }))

    expect(await within(change).findByText(COLLATERAL_SUCCESS.change)).not.toBeNull()
    const [post] = posts(calls)
    expect(post.url).toMatch(new RegExp(`/finance/collaterals/${CO_COLLATERAL}/changes$`))
    expect(post.body).toEqual({ expectedRevision: '1', collateralTypeId: CO_TYPE, currencyId: CO_CURRENCY, amount: '300000',
      validFrom: '2026-11-01', validUntil: null, documentReference: 'SENTETIK-BELGE-1' })
  })

  it('shows a version conflict with a reload, never a success', async () => {
    const user = userEvent.setup()
    serve({}, () => jsonResponse(refusal('VersionConflict', [{ field: 'expectedRevision', code: 'version_conflict' }]), 409))
    renderPage(true)
    const detail = await openAccount(user)
    await user.click(await within(detail).findByRole('button', { name: T.changeOpen }))
    const change = form(detail, 'change')
    await user.click(within(change).getByRole('button', { name: T.changeSubmit }))
    expect(await within(change).findByText(T.outcome.VersionConflict)).not.toBeNull()
    expect(within(change).getByRole('button', { name: T.reload })).not.toBeNull()
    expect(within(change).queryByText(COLLATERAL_SUCCESS.change)).toBeNull()
  })

  it('releases a collateral only with a reason, as a reverse record', async () => {
    const user = userEvent.setup()
    const calls = serve({}, () => jsonResponse(succeeded(released())))
    renderPage(true)
    const detail = await openAccount(user)
    await user.click(await within(detail).findByRole('button', { name: T.releaseOpen }))
    const release = form(detail, 'release')
    await user.click(within(release).getByRole('button', { name: T.releaseSubmit }))
    expect(within(release).getByText(T.releaseReasonRequired)).not.toBeNull()
    expect(posts(calls)).toHaveLength(0)

    await user.type(within(release).getByLabelText(T.releaseReason), '  SENTETIK çözüm gerekçesi  ')
    await user.click(within(release).getByRole('button', { name: T.releaseSubmit }))
    expect(await within(release).findByText(COLLATERAL_SUCCESS.release)).not.toBeNull()
    const [post] = posts(calls)
    expect(post.url).toMatch(new RegExp(`/finance/collaterals/${CO_COLLATERAL}/release$`))
    expect(post.body).toEqual({ expectedRevision: '1', reason: 'SENTETIK çözüm gerekçesi' })
  })
})

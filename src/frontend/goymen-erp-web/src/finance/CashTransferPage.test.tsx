import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CashTransferPage, type CashTransferPageProps } from './CashTransferPage'
import { clearPendingCashTransferAttempts } from './cashTransferHttp'
import { CASH_TRANSFER_TEXT as T } from './cashTransferText'

// Synthetic consumer tests, NOT_ENV: they prove neither real grants nor a real browser, API or SQL. Data is SENTETIK.
const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))
const shell = vi.hoisted(() => ({
  state: {
    activeContext: null as null | { scopeMode: string; companyId: string | null; userId: string },
    routeEntryGeneration: 0,
    sessionEpoch: 0,
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

const COMPANY = '11111111-1111-4111-8111-111111111111'
const OTHER_COMPANY = '99999999-9999-4999-8999-999999999999'
const TRY = '22222222-2222-4222-8222-222222222222'
const EUR = '77777777-7777-4777-8777-777777777777'
const TRANSFER = '33333333-3333-4333-8333-333333333333'
const SOURCE = '44444444-4444-4444-8444-444444444444'
const TARGET = '55555555-5555-4555-8555-555555555555'
const EUR_ACCOUNT = '88888888-8888-4888-8888-888888888888'
const PROFILE = '66666666-6666-4666-8666-666666666666'
const ACTOR = 'treasurer-a'

const BASE = '/api/v1/finance/cash-transfers'
const PREVIEW_URL = `${BASE}/preview`
const CONTEXT_URL = `${BASE}/account-context?`
const DETAIL_URL = `${BASE}/${TRANSFER}`
const CASH_URL = '/api/v1/finance/cash-accounts'

const preparation = (patch: Record<string, unknown> = {}) => ({
  kind: 'SameCurrency',
  sourceKind: 'Cash',
  sourceAccountId: SOURCE,
  sourceMoney: { amount: '1000.50', currencyId: TRY },
  sourceProfile: { profileId: PROFILE, version: '3' },
  targetKind: 'Cash',
  targetAccountId: TARGET,
  targetMoney: { amount: '1000.50', currencyId: TRY },
  targetProfile: { profileId: PROFILE, version: '3' },
  rate: null,
  occurredOn: '2026-11-10',
  valueOn: null,
  reference: null,
  description: null,
  ...patch,
})
const transfer = (patch: Record<string, unknown> = {}) => ({
  id: TRANSFER, companyId: COMPANY, preparation: preparation(), recordedBy: ACTOR,
  recordedAtUtc: '2026-11-10T09:00:00+00:00', transferConfirmed: true, version: '1', ...patch,
})
const page = (items: readonly unknown[], nextCursor: string | null = null, companyId = COMPANY) => ({ companyId, items, nextCursor })
const succeeded = (resource: unknown, patch: Record<string, unknown> = {}) => ({
  outcome: 'Succeeded', resource, isReplay: false, originalCorrelationId: 'corr-1', issues: [], ...patch,
})
const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })

const cashAccount = (id: string, currencyId: string, displayName: string) => ({
  account: {
    id, companyId: COMPANY, currencyId, displayName, status: 'Approved', isActive: true, rejectionReason: null,
    pendingChange: null, version: '2',
  },
  isOwnProposal: false,
  isOwnPendingChange: false,
})
const cashList = {
  companyId: COMPANY,
  items: [
    cashAccount(SOURCE, TRY, 'SENTETIK Merkez Kasa'),
    cashAccount(TARGET, TRY, 'SENTETIK Şube Kasa'),
    cashAccount(EUR_ACCOUNT, EUR, 'SENTETIK Döviz Kasa'),
  ],
}
const CURRENCY_OF: Record<string, string> = { [SOURCE]: TRY, [TARGET]: TRY, [EUR_ACCOUNT]: EUR }

interface Call {
  readonly url: string
  readonly method: string
  readonly company: string
  readonly key: string | undefined
  readonly body: unknown
}

/** Every answer is chosen by the test; an unexpected request is a 404 so it shows up as a failure. */
function serve(answer: (call: Call) => Response | Promise<Response> | undefined) {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init: RequestInit = {}) => {
    const headers = init.headers as Record<string, string>
    const call: Call = {
      url,
      method: init.method ?? 'GET',
      company: headers['X-Company-Id'],
      key: headers['Idempotency-Key'],
      body: typeof init.body === 'string' ? JSON.parse(init.body) : undefined,
    }
    calls.push(call)
    return await answer(call) ?? json({}, 404)
  }))
  return calls
}

/** The account context answers the asked account with its currency and the one synthetic profile. */
function contextFor(url: string) {
  const query = new URLSearchParams(url.slice(CONTEXT_URL.length))
  const accountId = query.get('accountId') ?? ''
  return json({
    companyId: COMPANY, accountKind: query.get('accountKind'), accountId, currencyId: CURRENCY_OF[accountId],
    profile: { profileId: PROFILE, version: '3' }, occurredOn: query.get('occurredOn'),
  })
}

/** The usual reads: the cash accounts, the leg contexts and the list given. */
function reads(list: () => Response, more: (call: Call) => Response | Promise<Response> | undefined = () => undefined) {
  return serve((call) => {
    if (call.method === 'GET' && call.url === CASH_URL) return json(cashList)
    if (call.method === 'GET' && call.url.startsWith(CONTEXT_URL)) return contextFor(call.url)
    if (call.method === 'GET' && call.url === BASE) return list()
    return more(call)
  })
}

function signIn(userId = ACTOR) {
  shell.state = { activeContext: { scopeMode: 'single', companyId: COMPANY, userId }, routeEntryGeneration: 0, sessionEpoch: 0 }
}

const props = (patch: Partial<CashTransferPageProps> = {}): CashTransferPageProps => ({
  companyId: COMPANY, actorId: ACTOR, canCommit: true, ...patch,
})

function show(patch: Partial<CashTransferPageProps> = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  return render(<QueryClientProvider client={client}>
    <MemoryRouter><CashTransferPage {...props(patch)} /></MemoryRouter>
  </QueryClientProvider>)
}

/** Fills a same-currency transfer between the two TRY cash accounts, or an exchange from the EUR account. */
async function fill(options: { readonly exchange?: boolean } = {}) {
  const user = userEvent.setup()
  await user.click(await screen.findByRole('button', { name: T.newTransfer }))
  const form = screen.getByRole('form', { name: T.formHeading })
  await user.selectOptions(within(form).getByLabelText(T.kind), options.exchange ? 'CurrencyExchange' : 'SameCurrency')
  fireEvent.change(within(form).getByLabelText(T.occurredOn), { target: { value: '2026-11-10' } })
  const source = within(form).getByRole('group', { name: T.sourceLegHeading })
  const target = within(form).getByRole('group', { name: T.targetLegHeading })
  await user.selectOptions(within(source).getByLabelText(T.accountKind), 'Cash')
  await user.selectOptions(await within(source).findByLabelText(T.account), options.exchange ? EUR_ACCOUNT : SOURCE)
  await user.selectOptions(within(target).getByLabelText(T.accountKind), 'Cash')
  await user.selectOptions(await within(target).findByLabelText(T.account), TARGET)
  await within(source).findByText(T.currency)
  await within(target).findByText(T.currency)
  if (options.exchange) {
    await user.type(within(form).getByLabelText(T.sourceAmount), '100')
    await user.type(within(form).getByLabelText(T.targetAmount), '3650')
  } else {
    await user.type(within(form).getByLabelText(T.sameCurrencyAmount), '1000,50')
  }
  return { user, form }
}

beforeEach(() => {
  signIn()
  boundary.capture.mockClear()
  boundary.report.mockClear()
})

afterEach(() => {
  cleanup()
  clearPendingCashTransferAttempts()
  vi.unstubAllGlobals()
})

describe('CashTransferPage reads', () => {
  it('says there is no authorized company without a context', () => {
    shell.state = { activeContext: null, routeEntryGeneration: 0, sessionEpoch: 0 }
    serve(() => undefined)
    show()
    expect(screen.getByText(`Yetkili şirket yok — ${T.subject}`)).toBeTruthy()
  })

  it('shows loading until the list answers', () => {
    serve(() => new Promise<Response>(() => {}))
    show()
    expect(screen.getByText(`Yükleniyor — ${T.subject}`)).toBeTruthy()
  })

  it('shows the empty state for an empty first page, scoped to the company', async () => {
    const calls = reads(() => json(page([])))
    show()
    expect(await screen.findByText(`Kayıt yok — ${T.subject}`)).toBeTruthy()
    expect(calls.every((call) => call.company === COMPANY)).toBe(true)
  })

  it('reports a 401 to the shell and draws the session-ended state', async () => {
    reads(() => json({}, 401))
    show()
    expect(await screen.findByText(`Oturum sonlandı — ${T.subject}`)).toBeTruthy()
    expect(boundary.report).toHaveBeenCalledWith(401, { token: 1 })
  })

  it('reports a 403 and draws the non-disclosing forbidden state', async () => {
    reads(() => json({}, 403))
    show()
    expect(await screen.findByText(`Yetkiniz yok — ${T.subject}`)).toBeTruthy()
    expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 })
  })

  it('offers a retry on 503 and draws the rows by account name once the service answers', async () => {
    let answers = 0
    reads(() => (answers++ === 0 ? json({}, 503) : json(page([transfer()]))))
    show()
    expect(await screen.findByText(`Servis geçici olarak kullanılamıyor — ${T.subject}`)).toBeTruthy()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByText('SENTETIK Merkez Kasa')).toBeTruthy()
    expect(screen.getByText('SENTETIK Şube Kasa')).toBeTruthy()
    expect(screen.getByText('Virman')).toBeTruthy()
  })

  it('treats a page for another company as malformed', async () => {
    reads(() => json(page([], null, OTHER_COMPANY)))
    show()
    expect(await screen.findByText(`Beklenmeyen yanıt — ${T.subject}`)).toBeTruthy()
  })

  it('opens the detail of a stored exchange with its rate', async () => {
    const stored = transfer({
      preparation: preparation({
        kind: 'CurrencyExchange', sourceAccountId: EUR_ACCOUNT, sourceMoney: { amount: '100', currencyId: EUR },
        targetMoney: { amount: '3650', currencyId: TRY }, rate: '36.5',
      }),
    })
    reads(() => json(page([stored])), (call) => (call.method === 'GET' && call.url === DETAIL_URL ? json(stored) : undefined))
    show()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.openName('10.11.2026', 'Döviz bozdurma') }))
    const detail = await screen.findByTestId('finance-cash-transfer-detail')
    expect(await within(detail).findByText(ACTOR)).toBeTruthy()
    expect(within(detail).getByText('36,5')).toBeTruthy()
  })

  it('does not offer a new transfer without the commit capability', async () => {
    reads(() => json(page([])))
    show({ canCommit: false })
    expect(await screen.findByText(`Kayıt yok — ${T.subject}`)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.newTransfer })).toBeNull()
  })
})

describe('CashTransferPage preview and commit', () => {
  it('sends nothing for the same account on both legs', async () => {
    const calls = reads(() => json(page([])))
    show()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.newTransfer }))
    const form = screen.getByRole('form', { name: T.formHeading })
    await user.selectOptions(within(form).getByLabelText(T.kind), 'SameCurrency')
    fireEvent.change(within(form).getByLabelText(T.occurredOn), { target: { value: '2026-11-10' } })
    const source = within(form).getByRole('group', { name: T.sourceLegHeading })
    const target = within(form).getByRole('group', { name: T.targetLegHeading })
    await user.selectOptions(within(source).getByLabelText(T.accountKind), 'Cash')
    await user.selectOptions(await within(source).findByLabelText(T.account), SOURCE)
    await user.selectOptions(within(target).getByLabelText(T.accountKind), 'Cash')
    await user.selectOptions(await within(target).findByLabelText(T.account), SOURCE)
    await user.type(within(form).getByLabelText(T.sameCurrencyAmount), '10')
    await user.click(within(form).getByRole('button', { name: T.preview }))
    expect(await within(form).findByText(T.sameAccount)).toBeTruthy()
    expect(calls.some((call) => call.method === 'POST')).toBe(false)
  })

  it('previews, asks for the confirmation and commits the previewed transfer with a key', async () => {
    const calls = reads(() => json(page([])), (call) => {
      if (call.method === 'POST' && call.url === PREVIEW_URL) {
        return json({ outcome: 'Succeeded', preparation: call.body, issues: [] })
      }
      if (call.method === 'POST' && call.url === BASE) return json(succeeded(transfer()))
      if (call.method === 'GET' && call.url === DETAIL_URL) return json(transfer())
      return undefined
    })
    show()
    const { user, form } = await fill()
    expect(within(form).queryByRole('button', { name: T.commit })).toBeNull()
    await user.click(within(form).getByRole('button', { name: T.preview }))
    expect(await within(form).findByText(T.previewAccepted)).toBeTruthy()
    const previewCall = calls.find((call) => call.url === PREVIEW_URL)
    expect(previewCall?.key).toBeUndefined()
    expect(previewCall?.body).toEqual(preparation())

    await user.click(within(form).getByRole('button', { name: T.commit }))
    expect(await within(form).findByText(T.commitNeedsConfirm)).toBeTruthy()
    expect(calls.some((call) => call.method === 'POST' && call.url === BASE)).toBe(false)

    await user.click(within(form).getByRole('checkbox', { name: T.commitConfirm }))
    await user.click(within(form).getByRole('button', { name: T.commit }))
    await waitFor(() => expect(calls.some((call) => call.method === 'POST' && call.url === BASE)).toBe(true))
    const post = calls.find((call) => call.method === 'POST' && call.url === BASE)
    expect(post?.key).toMatch(/^[0-9a-f]{32}$/)
    expect(post?.company).toBe(COMPANY)
    expect(post?.body).toEqual({ preparation: preparation(), transferConfirmed: true })
    expect(await screen.findByTestId('finance-cash-transfer-detail')).toBeTruthy()
  })

  it('withdraws the commit when the form changes after the preview', async () => {
    reads(() => json(page([])), (call) => (call.method === 'POST' && call.url === PREVIEW_URL
      ? json({ outcome: 'Succeeded', preparation: call.body, issues: [] }) : undefined))
    show()
    const { user, form } = await fill()
    await user.click(within(form).getByRole('button', { name: T.preview }))
    expect(await within(form).findByText(T.previewAccepted)).toBeTruthy()
    await user.type(within(form).getByLabelText(T.sameCurrencyAmount), '1')
    expect(await within(form).findByText(T.previewStale)).toBeTruthy()
    expect(within(form).queryByRole('button', { name: T.commit })).toBeNull()
  })

  it('sends both actual amounts without a client-calculated rate and refuses an unreadable comparison source', async () => {
    const calls = reads(() => json(page([])), (call) => (call.method === 'POST' && call.url === PREVIEW_URL
      ? json({
          outcome: 'ValidationFailed', preparation: null,
          issues: [{ field: 'rate', code: 'finance_exchange_rate_rule_unavailable' }],
        }, 422)
      : undefined))
    show()
    const { user, form } = await fill({ exchange: true })
    await user.click(within(form).getByRole('button', { name: T.preview }))
    expect(await within(form).findByText(T.previewRefused)).toBeTruthy()
    expect(within(form).getByText('Karşılaştırma kuru veya para profili okunamadı. İşlem kaydedilmedi.')).toBeTruthy()
    expect(within(form).queryByRole('button', { name: T.commit })).toBeNull()
    expect(calls.find((call) => call.url === PREVIEW_URL)?.body).toEqual(preparation({
      kind: 'CurrencyExchange',
      sourceAccountId: EUR_ACCOUNT,
      sourceMoney: { amount: '100', currencyId: EUR },
      targetMoney: { amount: '3650', currencyId: TRY },
      rate: null,
    }))
  })

  it('shows the actual server quote and TCMB benchmark without a rate input or a profit claim', async () => {
    const p = preparation({ kind: 'CurrencyExchange', sourceAccountId: EUR_ACCOUNT,
      sourceMoney: { amount: '100', currencyId: EUR }, targetMoney: { amount: '3650', currencyId: TRY }, rate: null })
    const exchange = {
      companyId: COMPANY, occurredOn: p.occurredOn, sourceMoney: p.sourceMoney, targetMoney: p.targetMoney,
      sourceProfile: p.sourceProfile, targetProfile: p.targetProfile,
      transactionRate: '36.50', exactTransactionRate: { numerator: '73', denominator: '2' },
      comparisonRate: '36.00', exactComparisonRate: { numerator: '36', denominator: '1' }, rateDeviation: '0.50',
      sourceComparison: { currencyId: EUR, sellingRate: '36', rateDay: p.occurredOn, source: 'Tcmb',
        recordId: SOURCE, recordVersion: '7', bulletinDate: '2026-11-09', provenance: 'SENTETIK / NOT_ENV' },
      targetComparison: { currencyId: TRY, sellingRate: '1', rateDay: p.occurredOn, source: 'FunctionalCurrency',
        recordId: null, recordVersion: null, bulletinDate: null, provenance: null },
      sourceReferenceTl: { currencyId: TRY, amount: '3600', sourceVersion: 'finance.cash-transfer.tl-reference.v1' },
      targetReferenceTl: { currencyId: TRY, amount: '3650', sourceVersion: 'finance.cash-transfer.tl-reference.v1' },
    }
    const calls = reads(() => json(page([])), (call) => call.method === 'POST' && call.url === PREVIEW_URL
      ? json({ outcome: 'Succeeded', preparation: p, issues: [], exchange }) : undefined)
    show()
    const { user, form } = await fill({ exchange: true })
    expect(within(form).queryByLabelText(T.rate)).toBeNull()
    await user.click(within(form).getByRole('button', { name: T.preview }))
    expect(await within(form).findByText(T.previewAccepted)).toBeTruthy()
    expect(within(form).getByText(T.comparisonIsNotProfit)).toBeTruthy()
    expect(within(form).getByText(T.referenceIsNotBookValue)).toBeTruthy()
    expect(within(form).getByText(T.sourceReferenceTl).nextElementSibling?.textContent).toBe('3.600')
    expect(within(form).getByText(T.targetReferenceTl).nextElementSibling?.textContent).toBe('3.650')
    expect(within(form).getByText('36,50')).toBeTruthy()
    expect(calls.find((call) => call.url === PREVIEW_URL)?.body).toEqual(p)
  })

  it('treats a preview echo that differs from what was sent as unknown', async () => {
    reads(() => json(page([])), (call) => (call.method === 'POST' && call.url === PREVIEW_URL
      ? json({ outcome: 'Succeeded', preparation: preparation({ sourceMoney: { amount: '1000.5', currencyId: TRY },
          targetMoney: { amount: '1000.5', currencyId: TRY } }), issues: [] })
      : undefined))
    show()
    const { user, form } = await fill()
    await user.click(within(form).getByRole('button', { name: T.preview }))
    expect(await within(form).findByText(T.previewUnknown)).toBeTruthy()
    expect(within(form).queryByRole('button', { name: T.commit })).toBeNull()
  })

  it('keeps the same key and body for the retry of an unknown commit and locks the form', async () => {
    let attempts = 0
    const calls = reads(() => json(page([])), (call) => {
      if (call.method === 'POST' && call.url === PREVIEW_URL) return json({ outcome: 'Succeeded', preparation: call.body, issues: [] })
      if (call.method === 'POST' && call.url === BASE) return attempts++ === 0 ? json({}, 503) : json(succeeded(transfer()))
      if (call.method === 'GET' && call.url === DETAIL_URL) return json(transfer())
      return undefined
    })
    show()
    const { user, form } = await fill()
    await user.click(within(form).getByRole('button', { name: T.preview }))
    await user.click(await within(form).findByRole('checkbox', { name: T.commitConfirm }))
    await user.click(within(form).getByRole('button', { name: T.commit }))
    expect(await within(form).findByText(T.unknownHeading)).toBeTruthy()
    expect(within(form).queryByRole('button', { name: T.commit })).toBeNull()
    // The inputs are locked through their enclosing fieldset (the input's own `disabled` property stays false).
    expect(within(form).getByLabelText(T.sameCurrencyAmount).closest('fieldset')?.disabled).toBe(true)
    await user.click(within(form).getByRole('button', { name: T.retrySame }))
    expect(await screen.findByTestId('finance-cash-transfer-detail')).toBeTruthy()
    const posts = calls.filter((call) => call.method === 'POST' && call.url === BASE)
    expect(posts).toHaveLength(2)
    expect(posts[1].key).toBe(posts[0].key)
    expect(posts[1].body).toEqual(posts[0].body)
  })

  it('keeps the key when the retry of an unknown commit is answered 403: the next send is the same attempt, never a second transfer', async () => {
    let attempts = 0
    const calls = reads(() => json(page([])), (call) => {
      if (call.method === 'POST' && call.url === PREVIEW_URL) return json({ outcome: 'Succeeded', preparation: call.body, issues: [] })
      if (call.method === 'POST' && call.url === BASE) {
        attempts += 1
        return attempts === 1 ? json({}, 503) : attempts === 2 ? json({}, 403) : json(succeeded(transfer()))
      }
      if (call.method === 'GET' && call.url === DETAIL_URL) return json(transfer())
      return undefined
    })
    show()
    const { user, form } = await fill()
    await user.click(within(form).getByRole('button', { name: T.preview }))
    await user.click(await within(form).findByRole('checkbox', { name: T.commitConfirm }))
    await user.click(within(form).getByRole('button', { name: T.commit }))
    expect(await within(form).findByText(T.unknownHeading)).toBeTruthy()
    await user.click(within(form).getByRole('button', { name: T.retrySame }))
    await waitFor(() => expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 }))
    expect(await within(form).findByText(T.unknownHeading)).toBeTruthy()
    expect(within(form).queryByRole('button', { name: T.commit })).toBeNull()
    await user.click(within(form).getByRole('button', { name: T.retrySame }))
    expect(await screen.findByTestId('finance-cash-transfer-detail')).toBeTruthy()
    const posts = calls.filter((call) => call.method === 'POST' && call.url === BASE)
    expect(posts).toHaveLength(3)
    expect(new Set(posts.map((post) => post.key)).size).toBe(1)
    expect(posts[2].body).toEqual(posts[0].body)
  })

  it('treats a replay the headers do not confirm as unknown', async () => {
    reads(() => json(page([])), (call) => {
      if (call.method === 'POST' && call.url === PREVIEW_URL) return json({ outcome: 'Succeeded', preparation: call.body, issues: [] })
      if (call.method === 'POST' && call.url === BASE) return json(succeeded(transfer(), { isReplay: true }))
      return undefined
    })
    show()
    const { user, form } = await fill()
    await user.click(within(form).getByRole('button', { name: T.preview }))
    await user.click(await within(form).findByRole('checkbox', { name: T.commitConfirm }))
    await user.click(within(form).getByRole('button', { name: T.commit }))
    expect(await within(form).findByText(T.unknownHeading)).toBeTruthy()
  })

  it('shows the balance refusal as the server gave it (fail-closed, nothing clipped)', async () => {
    reads(() => json(page([])), (call) => {
      if (call.method === 'POST' && call.url === PREVIEW_URL) return json({ outcome: 'Succeeded', preparation: call.body, issues: [] })
      if (call.method === 'POST' && call.url === BASE) {
        return json({
          outcome: 'ValidationFailed', resource: null, isReplay: false, originalCorrelationId: 'corr-1',
          issues: [{ field: 'sourceAccountId', code: 'finance_treasury_balance_unavailable' }],
        }, 422)
      }
      return undefined
    })
    show()
    const { user, form } = await fill()
    await user.click(within(form).getByRole('button', { name: T.preview }))
    await user.click(await within(form).findByRole('checkbox', { name: T.commitConfirm }))
    await user.click(within(form).getByRole('button', { name: T.commit }))
    expect(await within(form).findByText('Kaynak hesabın güncel bakiyesi okunamadı; kesin kayıt yapılamaz.')).toBeTruthy()
    expect(within(form).getByText(T.failedHeading)).toBeTruthy()
  })

  it('reports a forbidden commit to the shell', async () => {
    reads(() => json(page([])), (call) => {
      if (call.method === 'POST' && call.url === PREVIEW_URL) return json({ outcome: 'Succeeded', preparation: call.body, issues: [] })
      if (call.method === 'POST' && call.url === BASE) return json({}, 403)
      return undefined
    })
    show()
    const { user, form } = await fill()
    await user.click(within(form).getByRole('button', { name: T.preview }))
    await user.click(await within(form).findByRole('checkbox', { name: T.commitConfirm }))
    await user.click(within(form).getByRole('button', { name: T.commit }))
    expect(await within(form).findByText(T.forbiddenCommand)).toBeTruthy()
    expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 })
  })
})

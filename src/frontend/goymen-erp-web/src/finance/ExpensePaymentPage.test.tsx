import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ExpensePaymentPage, type ExpensePaymentPageProps } from './ExpensePaymentPage'
import { clearPendingExpenseAttempts } from './expensePaymentHttp'
import { EXPENSE_PAYMENT_TEXT as T } from './expensePaymentText'

// Synthetic consumer tests, NOT_ENV: they prove neither real grants nor a real browser, API or SQL. Data is SENTETIK.
const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))
const shell = vi.hoisted(() => ({
  capabilities: [] as string[],
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
  hasCapability: (_state: unknown, id: string) => shell.capabilities.includes(id),
  contextKey: (state: typeof shell.state) => state.activeContext === null
    ? null : [state.activeContext.userId, state.activeContext.scopeMode, state.activeContext.companyId ?? 'group', 'g', '0'],
}))

const COMPANY = '11111111-1111-4111-8111-111111111111'
const OTHER_COMPANY = '99999999-9999-4999-8999-999999999999'
const CURRENCY = '22222222-2222-4222-8222-222222222222'
const PAYMENT = '33333333-3333-4333-8333-333333333333'
const ACCOUNT = '44444444-4444-4444-8444-444444444444'
const ITEM = '55555555-5555-4555-8555-555555555555'
const PROFILE = '66666666-6666-4666-8666-666666666666'
const PENDING_ACCOUNT = '77777777-7777-4777-8777-777777777777'
const ACTOR = 'checker-b'
const MAKER = 'maker-a'

const BASE = '/api/v1/finance/expense-payments'
const LIST_URL = BASE
const ITEMS_URL = `${BASE}/expense-items`
const DETAIL_URL = `${BASE}/${PAYMENT}`
const APPROVAL_URL = `${DETAIL_URL}/approval`
const RECORD_URL = `${DETAIL_URL}/record`
const CASH_URL = '/api/v1/finance/cash-accounts'

const preparation = (patch: Record<string, unknown> = {}) => ({
  sourceKind: 'Cash', treasuryAccountId: ACCOUNT, expenseItemId: ITEM, money: { amount: '125.50', currencyId: CURRENCY },
  profile: { profileId: PROFILE, version: '3' }, occurredOn: '2026-11-10', valueOn: null, reference: null,
  description: 'SENTETIK kırtasiye gideri', ...patch,
})
const draft = (patch: Record<string, unknown> = {}) => ({
  id: PAYMENT, companyId: COMPANY, status: 'Draft', preparation: preparation(), preparedBy: MAKER,
  preparedAtUtc: '2026-11-10T08:00:00+00:00', approvedBy: null, approvedAtUtc: null, recordedBy: null, recordedAtUtc: null,
  paymentConfirmed: false, version: '1', ...patch,
})
const approved = (patch: Record<string, unknown> = {}) =>
  draft({ status: 'Approved', approvedBy: ACTOR, approvedAtUtc: '2026-11-10T09:00:00+00:00', version: '2', ...patch })
const page = (items: readonly unknown[], nextCursor: string | null = null, companyId = COMPANY) => ({ companyId, items, nextCursor })
const catalog = { companyId: COMPANY, items: [{ id: ITEM, code: 'general-expense' }] }
const succeeded = (resource: unknown, patch: Record<string, unknown> = {}) => ({
  outcome: 'Succeeded', resource, isReplay: false, originalCorrelationId: 'corr-1', issues: [], ...patch,
})
const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })

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

/** The usual reads: one catalog item and the list given. */
function reads(list: () => Response, more: (call: Call) => Response | Promise<Response> | undefined = () => undefined) {
  return serve((call) => {
    if (call.method === 'GET' && call.url === ITEMS_URL) return json(catalog)
    if (call.method === 'GET' && call.url === LIST_URL) return list()
    return more(call)
  })
}

function signIn(userId = ACTOR) {
  shell.state = { activeContext: { scopeMode: 'single', companyId: COMPANY, userId }, routeEntryGeneration: 0, sessionEpoch: 0 }
}

const props = (patch: Partial<ExpensePaymentPageProps> = {}): ExpensePaymentPageProps => ({
  companyId: COMPANY, actorId: ACTOR, canDraft: false, canApprove: true, canRecord: true, ...patch,
})

function show(patch: Partial<ExpensePaymentPageProps> = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  return render(<QueryClientProvider client={client}>
    <MemoryRouter><ExpensePaymentPage {...props(patch)} /></MemoryRouter>
  </QueryClientProvider>)
}

async function openPayment() {
  const user = userEvent.setup()
  await user.click(await screen.findByRole('button', { name: T.openName('10.11.2026', 'SENTETIK kırtasiye gideri') }))
  return { user, detail: await screen.findByTestId('finance-expense-payment-detail') }
}

beforeEach(() => {
  signIn()
  shell.capabilities = []
  boundary.capture.mockClear()
  boundary.report.mockClear()
})

afterEach(() => {
  cleanup()
  clearPendingExpenseAttempts()
  vi.unstubAllGlobals()
})

describe('ExpensePaymentPage reads', () => {
  it('says there is no authorized company without a context', () => {
    shell.state = { activeContext: null, routeEntryGeneration: 0, sessionEpoch: 0 }
    serve(() => undefined)
    show()
    expect(screen.getByText('Yetkili şirket yok')).toBeTruthy()
  })

  it('shows loading until the list answers', () => {
    serve(() => new Promise<Response>(() => {}))
    show()
    expect(screen.getAllByText('Yükleniyor').length).toBeGreaterThan(0)
  })

  it('shows the empty state for an empty first page', async () => {
    const calls = reads(() => json(page([])))
    show()
    expect(await screen.findByText('Kayıt yok')).toBeTruthy()
    expect(calls.every((call) => call.company === COMPANY)).toBe(true)
  })

  it('reports a 401 to the shell and draws the session-ended state', async () => {
    reads(() => json({}, 401))
    show()
    expect(await screen.findByText('Oturum sonlandı')).toBeTruthy()
    expect(boundary.report).toHaveBeenCalledWith(401, { token: 1 })
  })

  it('reports a 403 and draws the non-disclosing forbidden state', async () => {
    reads(() => json({}, 403))
    show()
    expect(await screen.findByText('Yetkiniz yok')).toBeTruthy()
    expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 })
  })

  it('offers a retry on 503 and draws the rows once the service answers', async () => {
    let answers = 0
    reads(() => (answers++ === 0 ? json({}, 503) : json(page([draft()]))))
    show()
    expect(await screen.findByText('Servis geçici olarak kullanılamıyor')).toBeTruthy()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByText('SENTETIK kırtasiye gideri')).toBeTruthy()
    // FNS-D25: the item is shown by the catalog code's label, read by identity.
    expect(screen.getByText('Masraf')).toBeTruthy()
  })

  it('treats a page for another company as malformed', async () => {
    reads(() => json(page([], null, OTHER_COMPANY)))
    show()
    expect(await screen.findByText('Beklenmeyen yanıt')).toBeTruthy()
  })
})

describe('ExpensePaymentPage approval', () => {
  it('does not offer approval to the person who prepared the draft', async () => {
    signIn(MAKER)
    reads(() => json(page([draft()])), (call) => (call.url === DETAIL_URL ? json(draft()) : undefined))
    show({ actorId: MAKER })
    const { detail } = await openPayment()
    expect(await within(detail).findByText(T.ownDraftCannotApprove)).toBeTruthy()
    expect(within(detail).queryByRole('button', { name: T.approve })).toBeNull()
  })

  it('approves with an idempotency key and the expected version', async () => {
    const calls = reads(() => json(page([draft()])), (call) => {
      if (call.method === 'GET' && call.url === DETAIL_URL) return json(draft())
      if (call.method === 'POST' && call.url === APPROVAL_URL) return json(succeeded(approved()))
      return undefined
    })
    show()
    const { user, detail } = await openPayment()
    await user.click(await within(detail).findByRole('button', { name: T.approve }))
    expect(await within(detail).findByText(T.applied)).toBeTruthy()
    const post = calls.find((call) => call.method === 'POST')
    expect(post?.key).toMatch(/^[0-9a-f]{32}$/)
    expect(post?.company).toBe(COMPANY)
    expect(post?.body).toEqual({ paymentId: PAYMENT, expectedVersion: '1' })
  })

  it('keeps the same key for the retry of an unknown outcome', async () => {
    let attempts = 0
    const calls = reads(() => json(page([draft()])), (call) => {
      if (call.method === 'GET' && call.url === DETAIL_URL) return json(draft())
      if (call.method === 'POST' && call.url === APPROVAL_URL) {
        return attempts++ === 0 ? json({}, 503) : json(succeeded(approved()))
      }
      return undefined
    })
    show()
    const { user, detail } = await openPayment()
    await user.click(await within(detail).findByRole('button', { name: T.approve }))
    expect(await within(detail).findByText(T.unknownHeading)).toBeTruthy()
    expect(within(detail).queryByRole('button', { name: T.approve })).toBeNull()
    await user.click(within(detail).getByRole('button', { name: T.retrySame }))
    expect(await within(detail).findByText(T.applied)).toBeTruthy()
    const posts = calls.filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[1].key).toBe(posts[0].key)
    expect(posts[1].body).toEqual(posts[0].body)
  })

  it('treats a replay the headers do not confirm as unknown', async () => {
    reads(() => json(page([draft()])), (call) => {
      if (call.method === 'GET' && call.url === DETAIL_URL) return json(draft())
      if (call.method === 'POST' && call.url === APPROVAL_URL) return json(succeeded(approved(), { isReplay: true }))
      return undefined
    })
    show()
    const { user, detail } = await openPayment()
    await user.click(await within(detail).findByRole('button', { name: T.approve }))
    expect(await within(detail).findByText(T.unknownHeading)).toBeTruthy()
    expect(within(detail).queryByText(T.applied)).toBeNull()
  })

  it('shows a confirmed replay as the stored result', async () => {
    reads(() => json(page([draft()])), (call) => {
      if (call.method === 'GET' && call.url === DETAIL_URL) return json(draft())
      if (call.method === 'POST' && call.url === APPROVAL_URL) {
        return json(succeeded(approved(), { isReplay: true }), 200,
          { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' })
      }
      return undefined
    })
    show()
    const { user, detail } = await openPayment()
    await user.click(await within(detail).findByRole('button', { name: T.approve }))
    expect(await within(detail).findByText(T.appliedReplay)).toBeTruthy()
  })

  it('shows a version conflict with a reload', async () => {
    reads(() => json(page([draft()])), (call) => {
      if (call.method === 'GET' && call.url === DETAIL_URL) return json(draft())
      if (call.method === 'POST' && call.url === APPROVAL_URL) {
        return json({
          outcome: 'VersionConflict', resource: null, isReplay: false, originalCorrelationId: 'corr-1',
          issues: [{ field: 'expectedVersion', code: 'version_conflict' }],
        }, 409)
      }
      return undefined
    })
    show()
    const { user, detail } = await openPayment()
    await user.click(await within(detail).findByRole('button', { name: T.approve }))
    expect(await within(detail).findByText(T.conflict)).toBeTruthy()
    expect(within(detail).getByRole('button', { name: T.reload })).toBeTruthy()
  })
})

describe('ExpensePaymentPage record', () => {
  it('asks for the payment confirmation before sending anything', async () => {
    const calls = reads(() => json(page([approved()])), (call) => (call.url === DETAIL_URL ? json(approved()) : undefined))
    show()
    const { user, detail } = await openPayment()
    await user.click(await within(detail).findByRole('button', { name: T.record }))
    expect(await within(detail).findByText(T.recordNeedsConfirm)).toBeTruthy()
    expect(calls.some((call) => call.method === 'POST')).toBe(false)
  })

  it('shows the server refusal when the balance cannot be read (fail-closed)', async () => {
    const calls = reads(() => json(page([approved()])), (call) => {
      if (call.method === 'GET' && call.url === DETAIL_URL) return json(approved())
      if (call.method === 'POST' && call.url === RECORD_URL) {
        return json({
          outcome: 'VersionConflict', resource: null, isReplay: false, originalCorrelationId: 'corr-1',
          issues: [{ field: 'treasuryAccountId', code: 'finance_treasury_balance_unavailable' }],
        }, 409)
      }
      return undefined
    })
    show()
    const { user, detail } = await openPayment()
    await user.click(await within(detail).findByRole('checkbox', { name: T.recordConfirm }))
    await user.click(within(detail).getByRole('button', { name: T.record }))
    expect(await within(detail).findByText('Hesabın güncel bakiyesi okunamadı; kesin kayıt yapılamaz.')).toBeTruthy()
    expect(calls.find((call) => call.method === 'POST')?.body)
      .toEqual({ paymentId: PAYMENT, expectedVersion: '2', paymentConfirmed: true })
  })

  it('shows the server refusal for a cash account whose passivation waits for approval (FNS-D50)', async () => {
    reads(() => json(page([approved()])), (call) => {
      if (call.method === 'GET' && call.url === DETAIL_URL) return json(approved())
      if (call.method === 'POST' && call.url === RECORD_URL) {
        return json({
          outcome: 'ValidationFailed', resource: null, isReplay: false, originalCorrelationId: 'corr-1',
          issues: [{ field: 'treasuryAccountId', code: 'expense_payment_treasury_account_passivation_pending' }],
        }, 422)
      }
      return undefined
    })
    show()
    const { user, detail } = await openPayment()
    await user.click(await within(detail).findByRole('checkbox', { name: T.recordConfirm }))
    await user.click(within(detail).getByRole('button', { name: T.record }))
    expect(await within(detail).findByText(
      'Seçilen kasanın pasife alma talebi onay bekliyor; bu kasadan yeni ödeme kaydedilemez.')).toBeTruthy()
  })
})

describe('ExpensePaymentPage draft', () => {
  const cashList = {
    companyId: COMPANY,
    items: [{
      account: {
        id: ACCOUNT, companyId: COMPANY, currencyId: CURRENCY, displayName: 'SENTETIK Merkez Kasa', status: 'Approved',
        isActive: true, rejectionReason: null, pendingChange: null, version: '2',
      },
      isOwnProposal: false,
      isOwnPendingChange: false,
    }],
  }
  const context = {
    companyId: COMPANY, sourceKind: 'Cash', treasuryAccountId: ACCOUNT, currencyId: CURRENCY,
    profile: { profileId: PROFILE, version: '3' }, occurredOn: '2026-11-10',
  }

  it('does not offer a cash account whose passivation waits for approval (FNS-D50)', async () => {
    const pending = {
      account: {
        ...cashList.items[0].account, id: PENDING_ACCOUNT, displayName: 'SENTETIK Şube Kasa', pendingChange: 'Passivation',
        version: '3',
      },
      isOwnProposal: false,
      isOwnPendingChange: false,
    }
    reads(() => json(page([])), (call) => {
      if (call.method === 'GET' && call.url === CASH_URL) return json({ ...cashList, items: [...cashList.items, pending] })
      return undefined
    })
    show({ canDraft: true })
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.newPayment }))
    const form = screen.getByRole('form', { name: T.formHeading })
    await user.selectOptions(within(form).getByLabelText(T.sourceKind), 'Cash')
    // The account select is drawn only once the list has answered.
    const account = await within(form).findByLabelText(T.account)
    const offered = within(account).getAllByRole('option').map((option) => (option as HTMLOptionElement).value)
    expect(offered).toContain(ACCOUNT)
    expect(offered).not.toContain(PENDING_ACCOUNT)
  })

  it('requires a description and then sends the item by identity with the server-given currency', async () => {
    const calls = reads(() => json(page([])), (call) => {
      if (call.method === 'GET' && call.url === CASH_URL) return json(cashList)
      if (call.method === 'GET' && call.url.startsWith(`${BASE}/draft-context?`)) return json(context)
      if (call.method === 'POST' && call.url === BASE) return json(succeeded(draft({ preparedBy: ACTOR })))
      if (call.method === 'GET' && call.url === DETAIL_URL) return json(draft({ preparedBy: ACTOR }))
      return undefined
    })
    show({ canDraft: true })
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.newPayment }))
    const form = screen.getByRole('form', { name: T.formHeading })
    await user.selectOptions(within(form).getByLabelText(T.sourceKind), 'Cash')
    await user.selectOptions(await within(form).findByLabelText(T.account), ACCOUNT)
    fireEvent.change(within(form).getByLabelText(T.occurredOn), { target: { value: '2026-11-10' } })
    await user.type(within(form).getByLabelText(T.amount), '125,50')
    const save = await within(form).findByRole('button', { name: T.saveDraft })
    await waitFor(() => expect((save as HTMLButtonElement).disabled).toBe(false))

    await user.click(save)
    expect(await within(form).findByText(T.descriptionRequired)).toBeTruthy()
    expect(calls.some((call) => call.method === 'POST')).toBe(false)

    await user.type(within(form).getByLabelText(T.description), '  SENTETIK kırtasiye gideri  ')
    await user.click(save)
    await waitFor(() => expect(calls.some((call) => call.method === 'POST')).toBe(true))
    const post = calls.find((call) => call.method === 'POST')
    expect(post?.key).toMatch(/^[0-9a-f]{32}$/)
    expect(post?.body).toEqual({
      paymentId: null,
      expectedVersion: null,
      preparation: {
        sourceKind: 'Cash',
        treasuryAccountId: ACCOUNT,
        expenseItemId: ITEM,
        money: { amount: '125.50', currencyId: CURRENCY },
        profile: { profileId: PROFILE, version: '3' },
        occurredOn: '2026-11-10',
        valueOn: null,
        reference: null,
        description: 'SENTETIK kırtasiye gideri',
      },
    })
  })
})

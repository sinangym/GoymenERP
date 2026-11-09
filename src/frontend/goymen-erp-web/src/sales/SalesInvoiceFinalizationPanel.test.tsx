/**
 * API-36 (PR #986) finalize / Finance re-send commands, SENTETIK consumer tests (NOT_ENV): the shell hooks are mocked and the
 * panel is mounted with `canControl` true, as it will be once the shell publishes the control's capability. Proves: the body is
 * `{ expectedVersion }` only with `X-Company-Id` and `Idempotency-Key`; a 200 counts only when the replay headers agree with the
 * body; an unknown outcome (offline, network error, 503, malformed) keeps the SAME key for the retry; a 409 reads the invoice
 * again; a refusal shows its Turkish text and offers no workaround; 401/403 go to the auth boundary; an answer that lands after
 * the session changed is dropped.
 */

import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SalesInvoiceFinalizationPanel } from './SalesInvoiceFinalization'
import { INVOICE_FINALIZATION_TEXT as T } from './invoiceText'
import { SALES_PROBLEM_CODE } from './contract'
import { problemCodeText } from './salesText'
import { INVOICE_ID, finalizedDetail, invoiceDetail, sentWaiting } from './invoiceTestData'

const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))
const shell = vi.hoisted(() => ({
  state: {
    activeContext: null as null | { scopeMode: string; companyId: string | null; userId: string },
    sessionEpoch: 1,
    routeEntryGeneration: 0,
  },
}))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('../shell/shellContext', async (original) => ({
  ...await original<typeof import('../shell/shellContext')>(),
  useShellState: () => shell.state,
}))

const COMPANY = '99999999-9999-9999-9999-999999999999'
const REPLAY = { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-original-1' }

interface Call {
  readonly url: string
  readonly method: string
  readonly company: string | null
  readonly key: string | null
  readonly contentType: string | null
  readonly body: string | null
}

const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
const problem = (status: number, code: string | null) =>
  new Response(JSON.stringify({ status, title: 'Problem', ...(code === null ? {} : { code }) }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })

type Answer = (call: Call) => Response | Promise<Response>

function serve(read: Answer, command: Answer) {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init: RequestInit = {}) => {
    const headers = (init.headers ?? {}) as Record<string, string>
    const call: Call = {
      url,
      method: (init.method ?? 'GET').toUpperCase(),
      company: headers['X-Company-Id'] ?? null,
      key: headers['Idempotency-Key'] ?? null,
      contentType: headers['Content-Type'] ?? null,
      body: typeof init.body === 'string' ? init.body : null,
    }
    calls.push(call)
    return call.method === 'GET' ? read(call) : command(call)
  }))
  return calls
}

const posts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'POST')
const reads = (calls: readonly Call[]) => calls.filter((call) => call.method === 'GET')

function mount(onRecorded = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  const tree = () => (
    <QueryClientProvider client={client}>
      <SalesInvoiceFinalizationPanel
        invoiceId={INVOICE_ID}
        companyId={COMPANY}
        namespace="ns-a"
        entryGeneration={0}
        canControl
        onRecorded={onRecorded}
      />
    </QueryClientProvider>
  )
  const view = render(tree())
  return { ...view, onRecorded, again: () => view.rerender(tree()) }
}

async function panel() {
  const section = await screen.findByTestId('sales-invoice-finalization')
  await within(section).findByTestId('sales-invoice-state')
  return section
}

const notice = () => screen.findByTestId('sales-invoice-finalization-notice')

beforeEach(() => {
  shell.state = { activeContext: { scopeMode: 'single', companyId: COMPANY, userId: 'actor-a' }, sessionEpoch: 1, routeEntryGeneration: 0 }
  boundary.capture.mockClear()
  boundary.report.mockClear()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('API-36 finalize command', () => {
  it('sends only the expected version under the company and a key, then reads the invoice again', async () => {
    let body: unknown = invoiceDetail()
    const calls = serve(() => json(body), () => {
      body = finalizedDetail()
      return json(finalizedDetail())
    })
    const { onRecorded } = mount()
    const section = await panel()
    expect(within(section).queryByTestId('sales-invoice-controls-unavailable')).toBeNull()
    expect(within(section).queryByTestId('sales-invoice-resend')).toBeNull()
    fireEvent.click(within(section).getByTestId('sales-invoice-finalize'))

    expect((await notice()).textContent).toBe(T.completedFinalize)
    const [post] = posts(calls)
    expect(post.url).toBe(`/api/v1/sales/invoices/${INVOICE_ID}/finalization`)
    expect(post.body).toBe('{"expectedVersion":"4"}')
    expect(post.company).toBe(COMPANY)
    expect(post.contentType).toBe('application/json')
    expect(post.key).toMatch(/\S{8,}/)
    expect(onRecorded).toHaveBeenCalledTimes(1)
    await within(section).findByTestId('sales-invoice-resend')
    expect(reads(calls).length).toBeGreaterThanOrEqual(2)
    expect(within(section).queryByTestId('sales-invoice-finalize')).toBeNull()
  })

  it('says a replayed command was recorded before only when the headers name the original attempt', async () => {
    serve(() => json(invoiceDetail()), () => json(finalizedDetail(undefined, { isReplay: true }), 200, REPLAY))
    mount()
    fireEvent.click(within(await panel()).getByTestId('sales-invoice-finalize'))
    expect((await notice()).textContent).toBe(T.replayed)
  })

  it('keeps the same key when a replay body comes without the replay headers, and when the service is unavailable', async () => {
    const answers: Answer[] = [
      () => json(finalizedDetail(undefined, { isReplay: true })),
      () => problem(503, null),
      () => json(finalizedDetail()),
    ]
    const calls = serve(() => json(invoiceDetail()), (call) => answers[posts(calls).indexOf(call)](call))
    mount()
    const section = await panel()
    fireEvent.click(within(section).getByTestId('sales-invoice-finalize'))
    expect((await notice()).textContent).toBe(T.unknownMalformed)
    expect(within(section).getByTestId('sales-invoice-finalize')).toHaveProperty('disabled', true)

    fireEvent.click(within(section).getByTestId('sales-invoice-finalization-retry'))
    // The second answer is a 503 for a retry of an unknown outcome: still unknown, the first reason kept.
    await waitFor(() => expect(posts(calls)).toHaveLength(2))
    await screen.findByTestId('sales-invoice-finalization-retry')

    fireEvent.click(within(section).getByTestId('sales-invoice-finalization-retry'))
    await waitFor(async () => expect((await notice()).textContent).toBe(T.completedFinalize))
    const keys = posts(calls).map((call) => call.key)
    expect(keys).toHaveLength(3)
    expect(new Set(keys).size).toBe(1)
    expect(posts(calls).every((call) => call.body === '{"expectedVersion":"4"}')).toBe(true)
  })

  it('says a 503 plainly and retries with the same key', async () => {
    const answers: Answer[] = [() => problem(503, null), () => json(finalizedDetail())]
    const calls = serve(() => json(invoiceDetail()), (call) => answers[posts(calls).indexOf(call)](call))
    mount()
    const section = await panel()
    fireEvent.click(within(section).getByTestId('sales-invoice-finalize'))
    expect((await notice()).textContent).toBe(T.unknownServiceUnavailable)
    fireEvent.click(within(section).getByTestId('sales-invoice-finalization-retry'))
    await waitFor(async () => expect((await notice()).textContent).toBe(T.completedFinalize))
    expect(posts(calls)[0].key).toBe(posts(calls)[1].key)
  })

  it('tells offline from an unreachable server, keeping the key for both', async () => {
    let online = false
    vi.spyOn(navigator, 'onLine', 'get').mockImplementation(() => online)
    const calls = serve(() => json(invoiceDetail()), () => {
      throw new TypeError('Failed to fetch')
    })
    // The read must succeed: answer it before going offline.
    online = true
    mount()
    const section = await panel()
    online = false
    fireEvent.click(within(section).getByTestId('sales-invoice-finalize'))
    expect((await notice()).textContent).toBe(T.unknownOffline)

    online = true
    fireEvent.click(within(section).getByTestId('sales-invoice-finalization-retry'))
    await waitFor(() => expect(posts(calls)).toHaveLength(2))
    // A retry of an unknown outcome keeps the first reason; the key is the same.
    expect(posts(calls)[0].key).toBe(posts(calls)[1].key)
    cleanup()

    const fresh = serve(() => json(invoiceDetail()), () => {
      throw new TypeError('Failed to fetch')
    })
    mount()
    fireEvent.click(within(await panel()).getByTestId('sales-invoice-finalize'))
    expect((await notice()).textContent).toBe(T.unknownNetwork)
    expect(posts(fresh)).toHaveLength(1)
  })

  it('reads the invoice again after a version conflict and says so', async () => {
    const calls = serve(() => json(invoiceDetail()), () => problem(409, SALES_PROBLEM_CODE.versionConflict))
    const { onRecorded } = mount()
    const section = await panel()
    const readsBefore = reads(calls).length
    fireEvent.click(within(section).getByTestId('sales-invoice-finalize'))
    const text = (await notice()).textContent
    expect(text).toBe(`${T.conflict} ${T.versionConflict}`)
    await waitFor(() => expect(reads(calls).length).toBeGreaterThan(readsBefore))
    expect(onRecorded).toHaveBeenCalledTimes(1)
  })

  it('refuses while the match difference is open, offers no workaround, and starts a new key for the next command', async () => {
    const answers: Answer[] = [
      () => problem(422, SALES_PROBLEM_CODE.invoiceMatchDifferenceOpen),
      () => problem(422, SALES_PROBLEM_CODE.invoiceMatchRequired),
    ]
    const calls = serve(() => json(invoiceDetail()), (call) => answers[posts(calls).indexOf(call)](call))
    const { onRecorded } = mount()
    const section = await panel()
    fireEvent.click(within(section).getByTestId('sales-invoice-finalize'))
    const shown = await notice()
    expect(shown.textContent).toBe(`${T.validation} ${problemCodeText(SALES_PROBLEM_CODE.invoiceMatchDifferenceOpen)}`)
    expect(shown.getAttribute('role')).toBe('alert')
    expect(screen.queryByTestId('sales-invoice-finalization-retry')).toBeNull()
    expect(onRecorded).not.toHaveBeenCalled()

    fireEvent.click(within(section).getByTestId('sales-invoice-finalize'))
    await waitFor(async () =>
      expect((await notice()).textContent).toBe(`${T.validation} ${problemCodeText(SALES_PROBLEM_CODE.invoiceMatchRequired)}`))
    const [first, second] = posts(calls)
    expect(first.key).not.toBe(second.key)
  })

  it('reports 401 and 403 to the auth boundary and records nothing', async () => {
    for (const status of [401, 403] as const) {
      serve(() => json(invoiceDetail()), () => problem(status, null))
      mount()
      fireEvent.click(within(await panel()).getByTestId('sales-invoice-finalize'))
      expect((await notice()).textContent).toBe(status === 401 ? T.unauthenticated : T.forbidden)
      expect(boundary.report).toHaveBeenLastCalledWith(status, { token: 1 })
      cleanup()
      vi.unstubAllGlobals()
    }
  })

  it('drops an answer that lands after the session changed', async () => {
    let release: (response: Response) => void = () => undefined
    const calls = serve(() => json(invoiceDetail()), () => new Promise<Response>((resolve) => {
      release = resolve
    }))
    const { again } = mount()
    const section = await panel()
    fireEvent.click(within(section).getByTestId('sales-invoice-finalize'))
    expect((await notice()).textContent).toBe(T.submittingFinalize)
    await waitFor(() => expect(posts(calls)).toHaveLength(1))

    shell.state = { ...shell.state, sessionEpoch: 2 }
    again()
    release(json(finalizedDetail()))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(screen.getByTestId('sales-invoice-finalization-notice').textContent).toBe(T.submittingFinalize)
  })

  it('sends nothing without a single bound company', async () => {
    shell.state = { ...shell.state, activeContext: { scopeMode: 'group', companyId: null, userId: 'actor-a' } }
    const calls = serve(() => json(invoiceDetail()), () => json(finalizedDetail()))
    mount()
    fireEvent.click(within(await panel()).getByTestId('sales-invoice-finalize'))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(posts(calls)).toHaveLength(0)
    expect(screen.queryByTestId('sales-invoice-finalization-notice')).toBeNull()
  })
})

describe('API-36 Finance re-send command', () => {
  it('re-sends a finalized invoice Finance has not accepted to the admission route with its version', async () => {
    const calls = serve(() => json(finalizedDetail(sentWaiting())), () => json(finalizedDetail(sentWaiting())))
    mount()
    const section = await panel()
    expect(within(section).getByTestId('sales-invoice-redrive-note').textContent).toBe(T.redriveNote)
    fireEvent.click(within(section).getByTestId('sales-invoice-resend'))
    expect((await notice()).textContent).toBe(T.completedResend)
    const [post] = posts(calls)
    expect(post.url).toBe(`/api/v1/sales/invoices/${INVOICE_ID}/finance-admission`)
    expect(post.body).toBe('{"expectedVersion":"5"}')
    expect(post.company).toBe(COMPANY)
  })

  it('treats a 200 that is not a finalized invoice as an unknown outcome', async () => {
    serve(() => json(finalizedDetail(sentWaiting())), () => json(invoiceDetail()))
    mount()
    fireEvent.click(within(await panel()).getByTestId('sales-invoice-resend'))
    expect((await notice()).textContent).toBe(T.unknownMalformed)
    expect(screen.getByTestId('sales-invoice-finalization-retry')).toBeTruthy()
  })
})

describe('API-36 invoice read states', () => {
  it('shows the forbidden state for a 403 read and reports it', async () => {
    serve(() => problem(403, null), () => json(finalizedDetail()))
    mount()
    const section = await screen.findByTestId('sales-invoice-finalization')
    await waitFor(() => expect(within(section).getByRole('alert').getAttribute('data-state')).toBe('forbidden'))
    expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 })
    expect(within(section).queryByTestId('sales-invoice-finalize')).toBeNull()
  })

  it('shows offline and network-error reads distinctly', async () => {
    let online = false
    vi.spyOn(navigator, 'onLine', 'get').mockImplementation(() => online)
    serve(() => {
      throw new TypeError('Failed to fetch')
    }, () => json(finalizedDetail()))
    mount()
    let section = await screen.findByTestId('sales-invoice-finalization')
    await waitFor(() => expect(within(section).getByRole('alert').getAttribute('data-state')).toBe('offline'))
    cleanup()
    online = true
    mount()
    section = await screen.findByTestId('sales-invoice-finalization')
    await waitFor(() => expect(within(section).getByRole('alert').getAttribute('data-state')).toBe('network-error'))
  })
})

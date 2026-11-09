/**
 * UI-64 (API-33) draft submit / approve / reject / cancel, SENTETIK consumer tests (NOT_ENV): the shell hooks are mocked and the
 * panel is mounted with `canWrite` / `canApprove` as the shell will pass them once it publishes `sales.invoices.write` /
 * `sales.invoices.approve`. Proves: a command shows only when the server's state offers it AND the control is held; each send
 * carries `{ expectedVersion }` (plus a trimmed or null `reason` for reject / cancel) with `X-Company-Id` and an Idempotency-Key;
 * a refusal shows its Turkish text; a conflict reads the invoice again; an unknown outcome keeps the SAME key for the retry;
 * 401/403 go to the auth boundary.
 */

import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SalesInvoiceFinalizationPanel } from './SalesInvoiceFinalization'
import { INVOICE_DRAFT_ACTIONS_TEXT as A } from './invoiceDraftText'
import { INVOICE_FINALIZATION_TEXT as T } from './invoiceText'
import { LIMITS } from './contract'
import { INVOICE_ID, invoiceDetail } from './invoiceTestData'

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
      body: typeof init.body === 'string' ? init.body : null,
    }
    calls.push(call)
    return call.method === 'GET' ? read(call) : command(call)
  }))
  return calls
}

const posts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'POST')
const reads = (calls: readonly Call[]) => calls.filter((call) => call.method === 'GET')

const draft = (over: Record<string, unknown> = {}) => invoiceDetail({ state: 'Draft', version: '2', match: null, ...over })
const pending = (over: Record<string, unknown> = {}) =>
  invoiceDetail({
    state: 'PendingApproval',
    version: '3',
    match: null,
    decision: {
      submittedBy: 'company-a-user',
      submittedAtUtc: '2026-11-01T09:00:00Z',
      decidedBy: null,
      decidedAtUtc: null,
      decisionReason: null,
      cancelledBy: null,
      cancelledAtUtc: null,
      cancellationReason: null,
    },
    ...over,
  })

function mount({ canWrite = true, canApprove = true } = {}) {
  const onRecorded = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  render(
    <QueryClientProvider client={client}>
      <SalesInvoiceFinalizationPanel
        invoiceId={INVOICE_ID}
        companyId={COMPANY}
        namespace="ns-a"
        entryGeneration={0}
        canControl={false}
        canWrite={canWrite}
        canApprove={canApprove}
        onRecorded={onRecorded}
      />
    </QueryClientProvider>,
  )
  return { onRecorded }
}

async function panel() {
  const section = await screen.findByTestId('sales-invoice-finalization')
  await within(section).findByTestId('sales-invoice-state')
  return section
}

const notice = () => screen.findByTestId('sales-invoice-draft-notice')

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

describe('UI-64 offered commands follow the server state and the held controls', () => {
  it('offers submit and cancel on a draft, and approve, reject and cancel on a pending draft', async () => {
    serve(() => json(draft()), () => problem(500, null))
    mount()
    const section = await panel()
    expect(within(section).getByTestId('sales-invoice-draft-submit')).toBeTruthy()
    expect(within(section).getByTestId('sales-invoice-draft-cancel')).toBeTruthy()
    expect(within(section).queryByTestId('sales-invoice-draft-approve')).toBeNull()
    expect(within(section).queryByTestId('sales-invoice-draft-reject')).toBeNull()
    cleanup()

    serve(() => json(pending()), () => problem(500, null))
    mount()
    const second = await panel()
    expect(within(second).queryByTestId('sales-invoice-draft-submit')).toBeNull()
    expect(within(second).getByTestId('sales-invoice-draft-approve')).toBeTruthy()
    expect(within(second).getByTestId('sales-invoice-draft-reject')).toBeTruthy()
    expect(within(second).getByTestId('sales-invoice-draft-cancel')).toBeTruthy()
    expect(within(second).getByTestId('sales-invoice-draft-decision').textContent).toContain('company-a-user')
  })

  it('hides every command of an approved invoice and the commands whose control is not held', async () => {
    serve(() => json(invoiceDetail()), () => problem(500, null))
    mount()
    expect(within(await panel()).queryByTestId('sales-invoice-draft-actions')).toBeNull()
    cleanup()

    serve(() => json(pending()), () => problem(500, null))
    mount({ canWrite: false, canApprove: false })
    const section = await panel()
    for (const kind of ['submit', 'approve', 'reject', 'cancel']) {
      expect(within(section).queryByTestId(`sales-invoice-draft-${kind}`)).toBeNull()
    }
    expect(within(section).getByTestId('sales-invoice-draft-write-unavailable').textContent).toBe(A.writeUnavailable)
    expect(within(section).getByTestId('sales-invoice-draft-approve-unavailable').textContent).toBe(A.approveUnavailable)
  })
})

describe('UI-64 commands', () => {
  it('submits with only the expected version under the company and a key, then reads the invoice again', async () => {
    let body: unknown = draft()
    const calls = serve(() => json(body), () => {
      body = pending()
      return json(pending())
    })
    const { onRecorded } = mount()
    fireEvent.click(within(await panel()).getByTestId('sales-invoice-draft-submit'))
    expect((await notice()).textContent).toBe(A.completed.submit)
    const [post] = posts(calls)
    expect(post.url).toBe(`/api/v1/sales/invoices/${INVOICE_ID}/submission`)
    expect(post.body).toBe('{"expectedVersion":"2"}')
    expect(post.company).toBe(COMPANY)
    expect(post.key).toMatch(/\S{8,}/)
    expect(onRecorded).toHaveBeenCalledTimes(1)
    await screen.findByTestId('sales-invoice-draft-approve')
    expect(reads(calls).length).toBeGreaterThanOrEqual(2)
  })

  it('says a replayed approval was recorded before', async () => {
    serve(() => json(pending()), () => json(invoiceDetail({ isReplay: true }), 200, REPLAY))
    mount()
    fireEvent.click(within(await panel()).getByTestId('sales-invoice-draft-approve'))
    expect((await notice()).textContent).toBe(A.replayed)
  })

  it('sends a trimmed reject reason, a blank cancel reason as null, and refuses a too long reason locally', async () => {
    const calls = serve(() => json(pending()), (call) =>
      json(invoiceDetail({ state: call.url.endsWith('/rejection') ? 'Rejected' : 'Cancelled', version: '4', match: null })))
    mount()
    const section = await panel()
    fireEvent.click(within(section).getByTestId('sales-invoice-draft-reject'))
    const reason = within(section).getByTestId('sales-invoice-draft-reason')
    fireEvent.change(reason, { target: { value: 'a'.repeat(LIMITS.note + 1) } })
    fireEvent.click(within(section).getByTestId('sales-invoice-draft-confirm'))
    expect(within(section).getByTestId('sales-invoice-draft-reason-error').textContent).toBe(A.reasonTooLong(LIMITS.note))
    expect(posts(calls)).toHaveLength(0)

    fireEvent.change(reason, { target: { value: '  Fiyat hatali  ' } })
    fireEvent.click(within(section).getByTestId('sales-invoice-draft-confirm'))
    expect((await notice()).textContent).toBe(A.completed.reject)
    expect(posts(calls)[0].url).toBe(`/api/v1/sales/invoices/${INVOICE_ID}/rejection`)
    expect(posts(calls)[0].body).toBe('{"expectedVersion":"3","reason":"Fiyat hatali"}')
    cleanup()

    const second = serve(() => json(draft()), () => json(invoiceDetail({ state: 'Cancelled', version: '3', match: null })))
    mount()
    const again = await panel()
    fireEvent.click(within(again).getByTestId('sales-invoice-draft-cancel'))
    fireEvent.change(within(again).getByTestId('sales-invoice-draft-reason'), { target: { value: '   ' } })
    fireEvent.click(within(again).getByTestId('sales-invoice-draft-confirm'))
    expect((await notice()).textContent).toBe(A.completed.cancel)
    expect(posts(second)[0].url).toBe(`/api/v1/sales/invoices/${INVOICE_ID}/cancellation`)
    expect(posts(second)[0].body).toBe('{"expectedVersion":"2","reason":null}')
  })

  it('shows the self approval refusal and reads the invoice again', async () => {
    const calls = serve(() => json(pending()), () => problem(409, 'sales_invoice_self_approval'))
    const { onRecorded } = mount()
    fireEvent.click(within(await panel()).getByTestId('sales-invoice-draft-approve'))
    expect((await notice()).textContent).toBe(`${T.conflict} ${A.selfApproval}`)
    await waitFor(() => expect(onRecorded).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))
  })

  it('says a concurrent change plainly and a server-side reason limit plainly', async () => {
    serve(() => json(draft()), () => problem(409, 'concurrent_change'))
    mount()
    fireEvent.click(within(await panel()).getByTestId('sales-invoice-draft-submit'))
    expect((await notice()).textContent).toBe(`${T.conflict} ${A.concurrentChange}`)
    cleanup()

    serve(() => json(pending()), () => problem(422, 'sales_approval_reason_too_long'))
    mount()
    const section = await panel()
    fireEvent.click(within(section).getByTestId('sales-invoice-draft-reject'))
    fireEvent.change(within(section).getByTestId('sales-invoice-draft-reason'), { target: { value: 'Uzun' } })
    fireEvent.click(within(section).getByTestId('sales-invoice-draft-confirm'))
    expect((await notice()).textContent).toContain(A.reasonTooLongServer)
  })

  it('keeps the same key across an unavailable service and a malformed answer, and freezes the controls meanwhile', async () => {
    const answers: Answer[] = [
      () => problem(503, null),
      () => json(invoiceDetail({ state: 'Draft', version: '2', match: null })),
      () => json(pending()),
    ]
    const calls = serve(() => json(draft()), (call) => answers[posts(calls).indexOf(call)](call))
    mount()
    const section = await panel()
    fireEvent.click(within(section).getByTestId('sales-invoice-draft-submit'))
    expect((await notice()).textContent).toBe(T.unknownServiceUnavailable)
    expect(within(section).getByTestId('sales-invoice-draft-submit')).toHaveProperty('disabled', true)

    fireEvent.click(within(section).getByTestId('sales-invoice-draft-retry'))
    await waitFor(() => expect(posts(calls)).toHaveLength(2))
    await screen.findByTestId('sales-invoice-draft-retry')

    fireEvent.click(within(section).getByTestId('sales-invoice-draft-retry'))
    await waitFor(async () => expect((await notice()).textContent).toBe(A.completed.submit))
    const keys = posts(calls).map((call) => call.key)
    expect(keys).toHaveLength(3)
    expect(new Set(keys).size).toBe(1)
  })

  it('lets the person leave an unknown outcome without a new attempt; a later retry still uses the same key', async () => {
    const answers: Answer[] = [() => problem(503, null), () => json(pending())]
    const calls = serve(() => json(draft()), (call) => answers[posts(calls).indexOf(call)](call))
    mount()
    const section = await panel()
    fireEvent.click(within(section).getByTestId('sales-invoice-draft-submit'))
    await screen.findByTestId('sales-invoice-draft-retry')
    fireEvent.click(within(section).getByTestId('sales-invoice-draft-abandon'))
    expect((await notice()).textContent).toBe(T.abandoned)
    // The write may have happened: no second command starts under a new key.
    expect(within(section).getByTestId('sales-invoice-draft-submit')).toHaveProperty('disabled', true)
    fireEvent.click(within(section).getByTestId('sales-invoice-draft-submit'))
    expect(posts(calls)).toHaveLength(1)

    fireEvent.click(within(section).getByTestId('sales-invoice-draft-retry'))
    await waitFor(async () => expect((await notice()).textContent).toBe(A.completed.submit))
    expect(posts(calls)[0].key).toBe(posts(calls)[1].key)
  })

  it.each([401, 403])('reports a %s to the auth boundary', async (status) => {
    serve(() => json(draft()), () => problem(status, null))
    mount()
    fireEvent.click(within(await panel()).getByTestId('sales-invoice-draft-submit'))
    await waitFor(() => expect(boundary.report).toHaveBeenCalledWith(status, { token: 1 }))
  })
})

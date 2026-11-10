/**
 * UI-64b / API-35 (PR #981) official e-invoice match, SENTETIK consumer tests (NOT_ENV): the shell hooks are mocked and the
 * finalization panel is mounted with `canMatch` true, as it will be once the shell publishes the form's capability. Proves: the
 * body carries the version, the number, the lower-cased ETTN and no approval by default, with `X-Company-Id` and
 * `Idempotency-Key`; the server's difference and tolerance are shown verbatim; a 200 counts only when it carries the sent match
 * and the replay headers agree; an unknown outcome keeps the SAME key; a data refusal keeps the form and reads nothing again;
 * `difference_approval_required` opens the approval fields only to a caller who may approve; 401/403 go to the auth boundary;
 * an answer that lands after the session changed is dropped; the routed tab shows no form without a published capability.
 */

import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SalesInvoiceFinalizationPanel } from './SalesInvoiceFinalization'
import { INVOICE_FINALIZATION_TEXT as F } from './invoiceText'
import { INVOICE_MATCH_PROBLEM_TEXT, INVOICE_MATCH_TEXT as T } from './invoiceMatchText'
import { invoiceMatchNotice, validateMatchForm } from './invoiceMatch'
import { INVOICE_ID, invoiceDetail, matchOf } from './invoiceTestData'

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
const OFFICIAL_NO = 'GIB2026000000123'
const ETTN = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

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
const problem = (status: number, code: string | null, errors: readonly unknown[] = []) =>
  new Response(JSON.stringify({ status, title: 'Problem', errors, ...(code === null ? {} : { code }) }), {
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

const unmatched = (over: Record<string, unknown> = {}) => invoiceDetail({ match: null, ...over })
const matched = (match: Record<string, unknown> = {}, over: Record<string, unknown> = {}) =>
  invoiceDetail({ version: '5', match: matchOf(match), ...over })

function mount({ canMatch = true, canApproveDifference = false, onRecorded = vi.fn() } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  const tree = () => (
    <QueryClientProvider client={client}>
      <SalesInvoiceFinalizationPanel
        invoiceId={INVOICE_ID}
        companyId={COMPANY}
        namespace="ns-a"
        entryGeneration={0}
        canControl={false}
        canMatch={canMatch}
        canApproveDifference={canApproveDifference}
        onRecorded={onRecorded}
      />
    </QueryClientProvider>
  )
  const view = render(tree())
  return { ...view, onRecorded, again: () => view.rerender(tree()) }
}

async function form() {
  return screen.findByTestId('sales-invoice-match-form')
}

function fill(section: HTMLElement, officialNo = OFFICIAL_NO, ettn = ETTN) {
  fireEvent.change(within(section).getByTestId('sales-invoice-match-official-no'), { target: { value: officialNo } })
  fireEvent.change(within(section).getByTestId('sales-invoice-match-ettn'), { target: { value: ettn } })
}

const submit = (section: HTMLElement) => fireEvent.click(within(section).getByTestId('sales-invoice-match-submit'))
const notice = () => screen.findByTestId('sales-invoice-match-notice')

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

describe('UI-64b official e-invoice match form', () => {
  it('sends the version, the number and the lower-cased ETTN without approval, then shows the server result verbatim', async () => {
    let body: unknown = unmatched()
    const result = matched({ difference: '-0.03', tolerance: '0.05', officialAmount: '1180.37' })
    const calls = serve(() => json(body), () => {
      body = result
      return json(result)
    })
    const { onRecorded } = mount()
    const section = await form()
    fill(section, `  ${OFFICIAL_NO} `, ETTN.toUpperCase())
    submit(section)

    expect((await notice()).textContent).toBe(T.completed)
    const [post] = posts(calls)
    expect(post.url).toBe(`/api/v1/sales/invoices/${INVOICE_ID}/external-match`)
    expect(JSON.parse(post.body ?? 'null')).toEqual({
      expectedVersion: '4',
      officialInvoiceNo: OFFICIAL_NO,
      ettn: ETTN,
      approveDifference: false,
      differenceReason: null,
    })
    expect(post.company).toBe(COMPANY)
    expect(post.contentType).toBe('application/json')
    expect(post.key).toMatch(/^[0-9a-f]{32}$/)
    expect(onRecorded).toHaveBeenCalledTimes(1)
    expect(screen.getByTestId('sales-invoice-match-result-difference').textContent).toBe('-0.03')
    expect(screen.getByTestId('sales-invoice-match-result-tolerance').textContent).toBe('0.05')
    // The invoice is read again: the facts show the recorded match and the form is gone.
    expect((await screen.findByTestId('sales-invoice-match-difference')).textContent).toBe('-0.03')
    expect(screen.queryByTestId('sales-invoice-match-form')).toBeNull()
    expect(reads(calls).length).toBeGreaterThanOrEqual(2)
  })

  it('shows no form for a matched invoice, and shows who approved a difference with the reason', async () => {
    serve(() => json(matched({ difference: '0.40', differenceApprovedBy: 'approver-b', differenceReason: 'Kur yuvarlaması' })), () => json(null))
    mount()
    expect((await screen.findByTestId('sales-invoice-match-approved-by')).textContent).toBe('approver-b')
    expect(screen.getByTestId('sales-invoice-match-reason-fact').textContent).toBe('Kur yuvarlaması')
    expect(screen.getByTestId('sales-invoice-match-difference').textContent).toBe('0.40')
    expect(screen.queryByTestId('sales-invoice-match-panel')).toBeNull()
  })

  it('says a match recorded before SAT-D241 was never compared on VAT and withholding instead of showing zero differences', async () => {
    serve(() => json(matched({
      invoiceVatAmount: null, officialVatAmount: null, vatDifference: null,
      invoiceWithholdingAmount: null, officialWithholdingAmount: null, withholdingDifference: null,
    })), () => json(null))
    mount()
    const notCompared = await screen.findByTestId('sales-invoice-match-tax-not-compared')
    expect(notCompared.textContent).toBe(T.taxNotCompared)
    // The row covers VAT and withholding alike, so its label is the general tax difference, not the VAT one.
    expect(notCompared.previousElementSibling?.textContent).toBe(T.taxDifference)
    expect(screen.queryByTestId('sales-invoice-match-vat-difference')).toBeNull()
    expect(screen.queryByTestId('sales-invoice-match-withholding-difference')).toBeNull()
  })

  it('shows no form for an invoice that is not approved', async () => {
    serve(() => json(unmatched({ state: 'Draft', decision: null })), () => json(null))
    mount()
    await screen.findByTestId('sales-invoice-state')
    expect(screen.queryByTestId('sales-invoice-match-form')).toBeNull()
  })

  it('says neutrally that the form waits for a published capability', async () => {
    serve(() => json(unmatched()), () => json(null))
    mount({ canMatch: false })
    expect((await screen.findByTestId('sales-invoice-match-unavailable')).textContent).toBe(T.formUnavailable)
    expect(screen.queryByTestId('sales-invoice-match-form')).toBeNull()
  })

  it('refuses a blank number and a malformed ETTN on the client and sends nothing', async () => {
    const calls = serve(() => json(unmatched()), () => json(null))
    mount()
    const section = await form()
    fill(section, '   ', 'not-a-guid')
    submit(section)
    expect(await within(section).findByText(T.invalidOfficialNo(64))).toBeTruthy()
    expect(within(section).getByText(T.invalidEttn)).toBeTruthy()
    fill(section, OFFICIAL_NO, '00000000-0000-0000-0000-000000000000')
    submit(section)
    expect(await within(section).findByText(T.invalidEttn)).toBeTruthy()
    expect(posts(calls)).toHaveLength(0)
  })

  it('says a replayed match was recorded before only when the headers name the original attempt', async () => {
    serve(() => json(unmatched()), () => json(matched({}, { isReplay: true }), 200, REPLAY))
    mount()
    const section = await form()
    fill(section)
    submit(section)
    expect((await notice()).textContent).toBe(T.replayed)
  })

  it('keeps the same key when the answer is malformed, without replay headers, or the provider is unavailable', async () => {
    const answers: Answer[] = [
      () => json(matched({}, { isReplay: true })),
      () => json(matched({ officialInvoiceNo: 'OTHER-1' })),
      () => problem(503, 'official_invoice_provider_unavailable'),
      () => json(matched()),
    ]
    const calls = serve(() => json(unmatched()), (call) => answers[posts(calls).indexOf(call)](call))
    mount()
    const section = await form()
    fill(section)
    submit(section)
    expect((await notice()).textContent).toBe(F.unknownMalformed)
    expect((within(section).getByTestId('sales-invoice-match-submit') as HTMLButtonElement).disabled).toBe(true)

    fireEvent.click(screen.getByTestId('sales-invoice-match-retry'))
    await waitFor(() => expect(posts(calls)).toHaveLength(2))
    expect((await notice()).textContent).toBe(F.unknownMalformed)

    fireEvent.click(screen.getByTestId('sales-invoice-match-retry'))
    await waitFor(() => expect(posts(calls)).toHaveLength(3))
    await waitFor(() => expect(screen.getByTestId('sales-invoice-match-notice').textContent).toBe(F.unknownMalformed))

    fireEvent.click(screen.getByTestId('sales-invoice-match-retry'))
    expect(await screen.findByText(T.completed)).toBeTruthy()
    const keys = posts(calls).map((call) => call.key)
    expect(new Set(keys).size).toBe(1)
    expect(new Set(posts(calls).map((call) => call.body)).size).toBe(1)
  })

  it('names the provider when the first answer is a 503', async () => {
    serve(() => json(unmatched()), () => problem(503, 'official_invoice_provider_unavailable'))
    mount()
    const section = await form()
    fill(section)
    submit(section)
    expect((await notice()).textContent).toBe(T.unknownServiceUnavailable)
  })

  it('keeps the typed data after a mismatch refusal, names the fields and reads nothing again', async () => {
    const calls = serve(
      () => json(unmatched()),
      () => problem(409, 'official_match_mismatch', [{ field: 'customer', code: 'mismatch' }, { field: 'currency', code: 'mismatch' }]),
    )
    const { onRecorded } = mount()
    const section = await form()
    fill(section)
    submit(section)
    const text = (await notice()).textContent ?? ''
    expect(text).toContain(T.refused)
    expect(text).toContain(INVOICE_MATCH_PROBLEM_TEXT.official_match_mismatch)
    expect(text).toContain(`${T.mismatchFields}: Müşteri, Para birimi.`)
    expect(screen.getByTestId('sales-invoice-match-notice').getAttribute('role')).toBe('alert')
    expect(onRecorded).not.toHaveBeenCalled()
    expect(reads(calls)).toHaveLength(1)
    expect((within(section).getByTestId('sales-invoice-match-official-no') as HTMLInputElement).value).toBe(OFFICIAL_NO)
    expect((within(section).getByTestId('sales-invoice-match-submit') as HTMLButtonElement).disabled).toBe(false)
  })

  it('reads the invoice again after a version conflict', async () => {
    const calls = serve(() => json(unmatched()), () => problem(409, 'version_conflict'))
    const { onRecorded } = mount()
    const section = await form()
    fill(section)
    submit(section)
    const text = (await notice()).textContent ?? ''
    expect(text).toContain(T.conflict)
    expect(text).toContain(INVOICE_MATCH_PROBLEM_TEXT.version_conflict)
    expect(onRecorded).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))
  })

  it('opens the approval fields after difference_approval_required and resends with a reason under a new key', async () => {
    const answers: Answer[] = [
      () => problem(409, 'difference_approval_required', [{ field: 'difference', code: 'exceeds_tolerance' }]),
      () => json(matched({ difference: '0.40', differenceApprovedBy: 'actor-a', differenceReason: 'Kur farkı' })),
    ]
    const calls = serve(() => json(unmatched()), (call) => answers[posts(calls).indexOf(call)](call))
    mount({ canApproveDifference: true })
    const section = await form()
    expect(within(section).queryByTestId('sales-invoice-match-approval')).toBeNull()
    fill(section)
    submit(section)
    expect((await notice()).textContent).toContain(INVOICE_MATCH_PROBLEM_TEXT.difference_approval_required)
    const approval = await within(section).findByTestId('sales-invoice-match-approval')

    fireEvent.click(within(approval).getByTestId('sales-invoice-match-approve'))
    submit(section)
    expect(await within(section).findByText(T.invalidReason(4000))).toBeTruthy()
    expect(posts(calls)).toHaveLength(1)

    fireEvent.change(within(section).getByTestId('sales-invoice-match-reason'), { target: { value: '  Kur farkı ' } })
    expect(within(section).getByTestId('sales-invoice-match-submit').textContent).toBe(T.submitApproval)
    submit(section)
    expect(await screen.findByText(T.completed)).toBeTruthy()
    const [first, second] = posts(calls)
    expect(JSON.parse(second.body ?? 'null')).toEqual({
      expectedVersion: '4',
      officialInvoiceNo: OFFICIAL_NO,
      ettn: ETTN,
      approveDifference: true,
      differenceReason: 'Kur farkı',
    })
    expect(second.key).not.toBe(first.key)
    expect(screen.getByTestId('sales-invoice-match-result-approved-by').textContent).toBe('actor-a')
    expect(screen.getByTestId('sales-invoice-match-result-difference').textContent).toBe('0.40')
  })

  it('names the VAT and withholding differences the server found above the tolerance (SAT-D241) and shows them once recorded', async () => {
    const answers: Answer[] = [
      () => problem(409, 'difference_approval_required', [
        { field: 'vatDifference', code: 'exceeds_tolerance' },
        { field: 'withholdingDifference', code: 'exceeds_tolerance' },
      ]),
      () => json(matched({ differenceApprovedBy: 'actor-a', differenceReason: 'KDV yuvarlama', vatDifference: '0.10', withholdingDifference: '0.06' })),
    ]
    const calls = serve(() => json(unmatched()), (call) => answers[posts(calls).indexOf(call)](call))
    mount({ canApproveDifference: true })
    const section = await form()
    fill(section)
    submit(section)
    const text = (await notice()).textContent ?? ''
    expect(text).toContain(`${T.exceededFields}: KDV, Tevkifat.`)
    expect(text).not.toContain('Ödenecek tutar')
    const approval = await within(section).findByTestId('sales-invoice-match-approval')
    fireEvent.click(within(approval).getByTestId('sales-invoice-match-approve'))
    fireEvent.change(within(section).getByTestId('sales-invoice-match-reason'), { target: { value: 'KDV yuvarlama' } })
    submit(section)
    expect(await screen.findByText(T.completed)).toBeTruthy()
    expect(screen.getByTestId('sales-invoice-match-result-vat-difference').textContent).toBe('0.10')
    expect(screen.getByTestId('sales-invoice-match-result-withholding-difference').textContent).toBe('0.06')
  })

  it('hides the approval fields again when the number or ETTN changes', async () => {
    serve(() => json(unmatched()), () => problem(409, 'difference_approval_required'))
    mount({ canApproveDifference: true })
    const section = await form()
    fill(section)
    submit(section)
    await within(section).findByTestId('sales-invoice-match-approval')
    fill(section, 'GIB2026000000999')
    expect(within(section).queryByTestId('sales-invoice-match-approval')).toBeNull()
  })

  it('offers no approval to a caller who may not approve, and says so neutrally', async () => {
    serve(() => json(unmatched()), () => problem(409, 'difference_approval_required'))
    mount({ canApproveDifference: false })
    const section = await form()
    fill(section)
    submit(section)
    expect((await within(section).findByTestId('sales-invoice-match-approval-unavailable')).textContent).toBe(T.approvalNotAvailable)
    expect(within(section).queryByTestId('sales-invoice-match-approval')).toBeNull()
  })

  it('shows the 422 reason refusal and the self-approval refusal with their Turkish text', async () => {
    for (const [status, code] of [[422, 'sales_invoice_match_difference_reason_required'], [409, 'sales_invoice_self_approval']] as const) {
      serve(() => json(unmatched()), () => problem(status, code))
      mount()
      const section = await form()
      fill(section)
      submit(section)
      expect((await notice()).textContent).toContain(INVOICE_MATCH_PROBLEM_TEXT[code])
      cleanup()
      vi.unstubAllGlobals()
    }
  })

  it('reports 401 and 403 to the auth boundary', async () => {
    for (const status of [401, 403]) {
      serve(() => json(unmatched()), () => problem(status, null))
      mount()
      const section = await form()
      fill(section)
      submit(section)
      expect((await notice()).textContent).toBe(status === 401 ? F.unauthenticated : F.forbidden)
      expect(boundary.report).toHaveBeenLastCalledWith(status, { token: 1 })
      cleanup()
      vi.unstubAllGlobals()
    }
  })

  it('says a missing invoice was not matched', async () => {
    serve(() => json(unmatched()), () => problem(404, null))
    mount()
    const section = await form()
    fill(section)
    submit(section)
    expect((await notice()).textContent).toBe(F.notFound)
  })

  it('drops an answer that lands after the session changed', async () => {
    let release: (response: Response) => void = () => undefined
    const calls = serve(() => json(unmatched()), () => new Promise<Response>((resolve) => {
      release = resolve
    }))
    const { again, onRecorded } = mount()
    const section = await form()
    fill(section)
    submit(section)
    expect((await notice()).textContent).toBe(T.submitting)
    await waitFor(() => expect(posts(calls)).toHaveLength(1))

    shell.state = { ...shell.state, sessionEpoch: 2 }
    again()
    release(json(matched()))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(screen.getByTestId('sales-invoice-match-notice').textContent).toBe(T.submitting)
    expect(onRecorded).not.toHaveBeenCalled()
  })

  it('sends nothing without a single bound company', async () => {
    shell.state = { ...shell.state, activeContext: { scopeMode: 'group', companyId: null, userId: 'actor-a' } }
    const calls = serve(() => json(unmatched()), () => json(matched()))
    mount()
    const section = await form()
    fill(section)
    submit(section)
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(posts(calls)).toHaveLength(0)
    expect(screen.queryByTestId('sales-invoice-match-notice')).toBeNull()
  })
})

describe('UI-64b match helpers', () => {
  it('sends no reason and no approval unless the server asked for one', () => {
    const result = validateMatchForm({ officialInvoiceNo: OFFICIAL_NO, ettn: ETTN, approveDifference: true, differenceReason: 'x' }, false)
    expect(result).toEqual({
      ok: true,
      body: { officialInvoiceNo: OFFICIAL_NO, ettn: ETTN, approveDifference: false, differenceReason: null },
    })
  })

  it('refuses a number longer than the contract allows', () => {
    const result = validateMatchForm({ officialInvoiceNo: 'A'.repeat(65), ettn: ETTN, approveDifference: false, differenceReason: '' }, false)
    expect(result.ok).toBe(false)
  })

  it('says nothing before an attempt', () => {
    expect(invoiceMatchNotice({ phase: 'idle' })).toBeNull()
  })
})

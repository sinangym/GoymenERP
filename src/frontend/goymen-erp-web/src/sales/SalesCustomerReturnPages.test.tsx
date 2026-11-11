/**
 * UI-70b S-10 "Müşteri iade faturaları" in the real shell (`AppShell`, `CapabilityRoute`) against SENTETIK wire bodies pinned to
 * API-40. Component evidence, NOT_ENV: each route opens only on its own published id; every read and write is company-bound;
 * the list keeps the server order and narrows on the server; the create form sends the recorded body under a key and keeps the
 * key when the outcome is unknown; the commands re-read the return; the 503 of a missing Finance port (SAT-D294) says why.
 */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  SALES_CAPABILITY,
  SALES_CUSTOMER_RETURN_APPROVE_CAPABILITY,
  SALES_CUSTOMER_RETURN_FINALIZE_CAPABILITY,
  SALES_CUSTOMER_RETURN_SUBMIT_CAPABILITY,
} from './capabilities'
import { CUSTOMER_RETURN_REASON_MAX } from './customerReturnContract'
import { SALES_PROBLEM_CODE } from './contract'
import { CUSTOMER_RETURN_PROBLEM_CODE, CUSTOMER_RETURN_TEXT, customerReturnProblemText } from './customerReturnText'
import { INVOICE_ID, INVOICE_NO, OTHER_INVOICE_ID } from './invoiceTestData'
import {
  CANCELLATION_REASON,
  INVOICE_LINE_1,
  INVOICE_LINE_2,
  REJECTION_REASON,
  RETURN_2,
  RETURN_DOCUMENT_NO,
  RETURN_ID,
  SHIPMENT_2,
  finalizedInvoiceWithLines,
  returnSummary,
  returnView,
} from './customerReturnTestData'
import { ACCOUNT, COMPANY_A, CURRENCY, PARTY, installPoolFetch, json, type PoolCall } from './poolFixtures'
import { POOL_TEXT } from './poolText'
import { renderPool } from './poolTestHarness'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

type Wire = Record<string, unknown>
const T = CUSTOMER_RETURN_TEXT
const LIST_URL = '/api/v1/sales/customer-returns'
const DETAIL_URL = `${LIST_URL}/${RETURN_ID}`
const INVOICES_URL = '/api/v1/sales/invoices?pageSize=50'
const INVOICE_URL = `/api/v1/sales/invoices/${INVOICE_ID}`
const READ = [SALES_CAPABILITY.customerReturnsList, SALES_CAPABILITY.customerReturnsDetail, SALES_CAPABILITY.customerReturnsCreate]

const problem = (status: number, body: Wire = {}) =>
  json({ status, ...body }, status, { 'Content-Type': 'application/problem+json' })
const unexpected = (call: PoolCall): Response => {
  throw new Error(`unexpected ${call.method} ${call.url}`)
}
const invoiceSummary = (over: Wire = {}): Wire => ({
  id: INVOICE_ID,
  documentNo: INVOICE_NO,
  state: 'Finalized',
  version: '5',
  businessDate: '2026-11-01',
  partyId: PARTY,
  companyAccountId: ACCOUNT,
  currencyId: CURRENCY,
  amountDisclosure: 'Visible',
  payableAmount: '1180.40',
  functionalPayableAmount: null,
  lineCount: 2,
  createdAtUtc: '2026-11-01T08:00:00Z',
  ...over,
})
const replayHeaders = { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' }

describe('UI-70b route gates (published S-10 ids)', () => {
  it.each([
    ['/satis/iadeler', [SALES_CAPABILITY.customerReturnsDetail, SALES_CAPABILITY.customerReturnsCreate]],
    ['/satis/iadeler/yeni', [SALES_CAPABILITY.customerReturnsList, SALES_CAPABILITY.customerReturnsDetail]],
    [`/satis/iadeler/${RETURN_ID}`, [SALES_CAPABILITY.customerReturnsList, SALES_CAPABILITY.customerReturnsCreate]],
  ])('%s closes onto the neutral block and reads nothing without its own id', async (route, granted) => {
    const fetch = installPoolFetch(unexpected, granted)
    renderPool(route)
    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    expect(fetch.calls).toHaveLength(0)
  })
})

describe('UI-70b S-10 list', () => {
  it('reads the company list once, keeps the server order and opens a row', async () => {
    const fetch = installPoolFetch((call) => {
      if (call.url === LIST_URL) return json({ items: [returnSummary({ id: RETURN_2, customerDocumentNo: 'MIF-2' }), returnSummary()] })
      return unexpected(call)
    }, READ)
    renderPool('/satis/iadeler')
    const table = await screen.findByRole('table', { name: T.list.caption })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((row) => row.getAttribute('data-customer-return-id'))).toEqual([RETURN_2, RETURN_ID])
    expect(within(table).getByRole('link', { name: RETURN_DOCUMENT_NO }).getAttribute('href')).toBe(`/satis/iadeler/${RETURN_ID}`)
    expect(screen.getByTestId(`customer-return-state-${RETURN_ID}`).textContent).toBe('Taslak')
    expect(within(table).getAllByText('240.00').length).toBe(2)
    expect(fetch.calls.map((call) => [call.url, call.companyId])).toEqual([[LIST_URL, COMPANY_A]])
  })

  it('narrows to one invoice on the server with ?fatura and says the filtered list is empty', async () => {
    const fetch = installPoolFetch((call) => {
      if (call.url === `${LIST_URL}?invoiceId=${INVOICE_ID}`) return json({ items: [] })
      return unexpected(call)
    }, READ)
    renderPool(`/satis/iadeler?fatura=${INVOICE_ID}`)
    expect(await screen.findByText(/Sonuç yok/)).toBeTruthy()
    expect(screen.getByTestId('customer-returns-filter').textContent).toBe(T.list.filteredBy(INVOICE_ID))
    expect(fetch.calls).toHaveLength(1)
  })

  it('shows the empty list, and a malformed filter is not sent', async () => {
    const fetch = installPoolFetch((call) => (call.url === LIST_URL ? json({ items: [] }) : unexpected(call)), READ)
    renderPool('/satis/iadeler?fatura=not-an-id')
    expect(await screen.findByText(/Kayıt yok/)).toBeTruthy()
    expect(screen.queryByTestId('customer-returns-filter')).toBeNull()
    expect(fetch.calls.map((call) => call.url)).toEqual([LIST_URL])
  })

  it.each([
    [403, /Yetkiniz yok|Bulunamadı — sayfa/],
    [503, /Servis geçici olarak kullanılamıyor/],
  ])('shows the %s answer as its own state', async (status, title) => {
    installPoolFetch((call) => (call.url === LIST_URL ? problem(status) : unexpected(call)), READ)
    renderPool('/satis/iadeler')
    expect(await screen.findByText(title)).toBeTruthy()
  })

  it.each([
    ['a row of another invoice under the filter', `${LIST_URL}?invoiceId=${INVOICE_ID}`, `?fatura=${INVOICE_ID}`, { items: [returnSummary({ originalInvoiceId: OTHER_INVOICE_ID })] }],
    ['a stray member', LIST_URL, '', { items: [returnSummary({ isReplay: false })] }],
  ])('refuses %s as a malformed answer', async (_label, url, query, body) => {
    installPoolFetch((call) => (call.url === url ? json(body) : unexpected(call)), READ)
    renderPool(`/satis/iadeler${query}`)
    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
  })
})

/* ── create ─────────────────────────────────────────────────────────────────────────────────────────────────────── */

function serveCreate(onCreate: (call: PoolCall, n: number) => Response, capabilities: readonly string[] = READ) {
  let creates = 0
  return installPoolFetch((call) => {
    if (call.method === 'GET' && call.url === INVOICES_URL) {
      return json({ items: [invoiceSummary(), invoiceSummary({ id: OTHER_INVOICE_ID, documentNo: 'SF-2026-0008', state: 'Approved' })], nextCursor: null })
    }
    if (call.method === 'GET' && call.url === INVOICE_URL) return json(finalizedInvoiceWithLines())
    if (call.method === 'POST' && call.url === LIST_URL) return onCreate(call, creates++)
    if (call.method === 'GET' && call.url === DETAIL_URL) return json(returnView('Draft'))
    return unexpected(call)
  }, capabilities)
}

async function fillForm(user: ReturnType<typeof userEvent.setup>) {
  const invoice = await screen.findByLabelText(new RegExp(T.create.invoice))
  await waitFor(() => expect((screen.getByRole('option', { name: /SF-2026-0008/ }) as HTMLOptionElement).disabled).toBe(true))
  await user.selectOptions(invoice, INVOICE_ID)
  fireEvent.change(screen.getByLabelText(new RegExp(T.common.customerDocumentNo)), { target: { value: RETURN_DOCUMENT_NO } })
  fireEvent.change(screen.getByLabelText(new RegExp(T.common.customerDocumentDate)), { target: { value: '2026-11-05' } })
  const quantity = await screen.findByLabelText(T.create.returnQuantityLabel(1))
  fireEvent.change(quantity, { target: { value: '2' } })
}

const expectedBody = {
  originalInvoiceId: INVOICE_ID,
  customerDocumentNo: RETURN_DOCUMENT_NO,
  customerDocumentDate: '2026-11-05',
  shipmentId: null,
  lines: [{ originalInvoiceLineId: INVOICE_LINE_1, quantity: '2' }],
}

describe('UI-70b S-10 create', () => {
  it('sends the return under the company and a key, then opens the recorded draft', async () => {
    const user = userEvent.setup()
    const fetch = serveCreate(() => json(returnView('Draft'), 201))
    renderPool('/satis/iadeler/yeni')
    await fillForm(user)
    await user.click(screen.getByRole('button', { name: T.create.submit }))
    expect(await screen.findByTestId('customer-return-state')).toBeTruthy()
    const [post] = fetch.posts()
    expect(post.companyId).toBe(COMPANY_A)
    expect(post.idempotencyKey).toMatch(/\S{8,}/)
    expect(post.body).toEqual(expectedBody)
  })

  it('says a replay is the recorded earlier answer', async () => {
    const user = userEvent.setup()
    serveCreate(() => json(returnView('Draft'), 201, replayHeaders), [SALES_CAPABILITY.customerReturnsCreate])
    renderPool('/satis/iadeler/yeni')
    await fillForm(user)
    await user.click(screen.getByRole('button', { name: T.create.submit }))
    const notice = await screen.findByTestId('customer-return-create-notice')
    expect(notice.textContent).toBe(`${T.create.completed(RETURN_DOCUMENT_NO)} ${POOL_TEXT.attempt.replayed}`)
  })

  it('refuses a replay header without its original correlation as malformed and keeps the key', async () => {
    const user = userEvent.setup()
    const fetch = serveCreate(
      (_call, n) => (n === 0 ? json(returnView('Draft'), 201, { 'Idempotency-Replayed': 'true' }) : json(returnView('Draft'), 201)),
      [SALES_CAPABILITY.customerReturnsCreate],
    )
    renderPool('/satis/iadeler/yeni')
    await fillForm(user)
    await user.click(screen.getByRole('button', { name: T.create.submit }))
    await user.click(await screen.findByRole('button', { name: T.common.retry }))
    await screen.findByText(T.create.completed(RETURN_DOCUMENT_NO))
    const keys = fetch.posts().map((call) => call.idempotencyKey)
    expect(keys).toHaveLength(2)
    expect(keys[1]).toBe(keys[0])
  })

  it('keeps the key on a 503 and sends the same body again only on retry', async () => {
    const user = userEvent.setup()
    const fetch = serveCreate((_call, n) => (n === 0 ? problem(503) : json(returnView('Draft'), 201)), [SALES_CAPABILITY.customerReturnsCreate])
    renderPool('/satis/iadeler/yeni')
    await fillForm(user)
    await user.click(screen.getByRole('button', { name: T.create.submit }))
    expect((await screen.findByTestId('customer-return-create-notice')).getAttribute('role')).toBe('alert')
    expect(fetch.posts()).toHaveLength(1)
    await user.click(screen.getByRole('button', { name: T.common.retry }))
    await screen.findByText(T.create.completed(RETURN_DOCUMENT_NO))
    const [first, second] = fetch.posts()
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toEqual(first.body)
  })

  it('puts a 422 reason on its field and lists it in the summary', async () => {
    const user = userEvent.setup()
    serveCreate(() =>
      problem(422, {
        code: CUSTOMER_RETURN_PROBLEM_CODE.unshippedBalanceExceeded,
        errors: [{ field: 'lines[0].quantity', code: CUSTOMER_RETURN_PROBLEM_CODE.unshippedBalanceExceeded }],
      }),
    )
    renderPool('/satis/iadeler/yeni')
    await fillForm(user)
    await user.click(screen.getByRole('button', { name: T.create.submit }))
    const summary = await screen.findByTestId('pool-error-summary')
    const reason = customerReturnProblemText(CUSTOMER_RETURN_PROBLEM_CODE.unshippedBalanceExceeded) ?? ''
    expect(within(summary).getByRole('link', { name: reason }).getAttribute('href')).toBe('#pool-lines')
    expect(document.getElementById('pool-lines')?.textContent).toBe(reason)
  })

  it('refuses an incomplete form locally and sends nothing', async () => {
    const user = userEvent.setup()
    const fetch = serveCreate(() => problem(500))
    renderPool('/satis/iadeler/yeni')
    await screen.findByRole('option', { name: /SF-2026-0008/ })
    await user.click(screen.getByRole('button', { name: T.create.submit }))
    const summary = await screen.findByTestId('pool-error-summary')
    expect(within(summary).getByText(T.create.invoiceRequired)).toBeTruthy()
    expect(within(summary).getByText(T.create.documentNoRequired)).toBeTruthy()
    expect(within(summary).getByText(T.create.documentDateRequired)).toBeTruthy()

    await fillForm(user)
    fireEvent.change(screen.getByLabelText(T.create.returnQuantityLabel(1)), { target: { value: '-1' } })
    await user.click(screen.getByRole('button', { name: T.create.submit }))
    expect(await screen.findAllByText(T.create.quantityInvalid)).not.toHaveLength(0)
    expect(fetch.posts()).toHaveLength(0)
  })

  it('offers only the lines of the chosen shipment and sends that shipment', async () => {
    const user = userEvent.setup()
    const fetch = serveCreate(() => json(returnView('Draft', { shipmentId: SHIPMENT_2, lines: [] }), 201), [SALES_CAPABILITY.customerReturnsCreate])
    renderPool(`/satis/iadeler/yeni?fatura=${INVOICE_ID}`)
    await screen.findByTestId('customer-return-create-line-2')
    await user.selectOptions(screen.getByLabelText(new RegExp(T.create.shipment.replace(/[()]/g, '\\$&'))), SHIPMENT_2)
    expect(screen.queryByTestId('customer-return-create-line-1')).toBeNull()
    fireEvent.change(screen.getByLabelText(new RegExp(T.common.customerDocumentNo)), { target: { value: RETURN_DOCUMENT_NO } })
    fireEvent.change(screen.getByLabelText(new RegExp(T.common.customerDocumentDate)), { target: { value: '2026-11-05' } })
    fireEvent.change(screen.getByLabelText(T.create.returnQuantityLabel(2)), { target: { value: '1.5' } })
    await user.click(screen.getByRole('button', { name: T.create.submit }))
    await waitFor(() => expect(fetch.posts()).toHaveLength(1))
    expect(fetch.posts()[0].body).toEqual({
      ...expectedBody,
      shipmentId: SHIPMENT_2,
      lines: [{ originalInvoiceLineId: INVOICE_LINE_2, quantity: '1.5' }],
    })
  })
})

/* ── detail commands ────────────────────────────────────────────────────────────────────────────────────────────── */

const COMMANDS = [
  SALES_CAPABILITY.customerReturnsDetail,
  SALES_CUSTOMER_RETURN_SUBMIT_CAPABILITY,
  SALES_CUSTOMER_RETURN_APPROVE_CAPABILITY,
  SALES_CUSTOMER_RETURN_FINALIZE_CAPABILITY,
]

function serveDetail(states: readonly string[], onCommand: (call: PoolCall) => Response, capabilities: readonly string[] = COMMANDS) {
  let reads = 0
  return installPoolFetch((call) => {
    if (call.method === 'GET' && call.url === DETAIL_URL) return json(returnView(states[Math.min(reads++, states.length - 1)]))
    if (call.method === 'POST' && call.url.startsWith(`${DETAIL_URL}/`)) return onCommand(call)
    return unexpected(call)
  }, capabilities)
}

describe('UI-70b S-10 detail and commands', () => {
  it('shows the recorded return and offers no command without its id', async () => {
    serveDetail(['Draft'], unexpected, [SALES_CAPABILITY.customerReturnsDetail])
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    expect((await screen.findByTestId('customer-return-state')).textContent).toBe('Taslak')
    expect(within(screen.getByTestId('customer-return-totals')).getAllByText('240.00')).toHaveLength(2)
    expect(screen.getByTestId('customer-return-line-1')).toBeTruthy()
    expect(screen.queryByTestId('customer-return-commands')).toBeNull()
  })

  it('submits with the version it read under a key, then reads the return again', async () => {
    const user = userEvent.setup()
    const fetch = serveDetail(['Draft', 'PendingApproval'], () => json(returnView('PendingApproval')))
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    await user.click(await screen.findByRole('button', { name: T.detail.submit }))
    expect((await screen.findByTestId('customer-return-notice')).textContent).toBe(T.detail.completed.submission)
    await waitFor(() => expect(screen.getByTestId('customer-return-state').textContent).toBe('Onay bekliyor'))
    const [post] = fetch.posts()
    expect(post.url).toBe(`${DETAIL_URL}/submission`)
    expect(post.body).toEqual({ expectedVersion: '1' })
    expect(post.companyId).toBe(COMPANY_A)
    expect(post.idempotencyKey).toMatch(/\S{8,}/)
    expect(fetch.calls.filter((call) => call.method === 'GET').length).toBeGreaterThanOrEqual(2)
  })

  it('says why the server refused a self-approval and keeps the return as it is', async () => {
    const user = userEvent.setup()
    serveDetail(['PendingApproval'], () =>
      problem(422, { code: CUSTOMER_RETURN_PROBLEM_CODE.selfApproval, errors: [{ field: 'approval', code: CUSTOMER_RETURN_PROBLEM_CODE.selfApproval }] }),
    )
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    expect(await screen.findByText(T.detail.approveHint)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.detail.approve }))
    const notice = await screen.findByTestId('customer-return-notice')
    expect(notice.textContent).toContain(customerReturnProblemText(CUSTOMER_RETURN_PROBLEM_CODE.selfApproval))
    expect(screen.getByTestId('customer-return-state').textContent).toBe('Onay bekliyor')
  })

  it('keeps the key on the 503 of a missing Finance port, says why and offers only the same send', async () => {
    const user = userEvent.setup()
    const fetch = serveDetail(['Approved'], () =>
      problem(503, { code: CUSTOMER_RETURN_PROBLEM_CODE.financePortUnavailable }),
    )
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    await user.click(await screen.findByRole('button', { name: T.detail.finalize }))
    const notice = await screen.findByTestId('customer-return-notice')
    expect(notice.textContent).toBe(T.detail.financePortUnavailable)
    expect(notice.getAttribute('role')).toBe('alert')
    await user.click(screen.getByRole('button', { name: T.common.retry }))
    await waitFor(() => expect(fetch.posts()).toHaveLength(2))
    const [first, second] = fetch.posts()
    expect(first.url).toBe(`${DETAIL_URL}/finalization`)
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toEqual({ expectedVersion: '3' })
    expect(screen.getByTestId('customer-return-state').textContent).toBe('Onaylandı')
  })
})

describe('UI-70b S-10 rejection and cancellation (SAT-D290 (2, 3))', () => {
  it('rejects a pending return with its reason after the confirm step, then shows who rejected and why', async () => {
    const user = userEvent.setup()
    const fetch = serveDetail(['PendingApproval', 'Rejected'], () => json(returnView('Rejected')))
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    await user.click(await screen.findByRole('button', { name: T.detail.reject }))
    // Opening the form sends nothing; only the confirm does.
    expect(fetch.posts()).toHaveLength(0)
    await user.type(screen.getByLabelText(T.detail.reasonLabel), `  ${REJECTION_REASON}  `)
    await user.click(screen.getByRole('button', { name: T.detail.rejectConfirm }))
    expect((await screen.findByTestId('customer-return-notice')).textContent).toBe(T.detail.completed.rejection)
    await waitFor(() => expect(screen.getByTestId('customer-return-state').textContent).toBe('Reddedildi'))
    expect(screen.getByTestId('customer-return-rejection-reason').textContent).toBe(REJECTION_REASON)
    expect(screen.queryByTestId('customer-return-reason-form')).toBeNull()
    const [post] = fetch.posts()
    expect(post.url).toBe(`${DETAIL_URL}/rejection`)
    expect(post.body).toEqual({ expectedVersion: '2', reason: REJECTION_REASON })
    expect(post.companyId).toBe(COMPANY_A)
    expect(post.idempotencyKey).toMatch(/\S{8,}/)
    expect(fetch.posts()).toHaveLength(1)
  })

  it('cancels a draft with no reason as null, then shows who cancelled it', async () => {
    const user = userEvent.setup()
    const fetch = serveDetail(['Draft', 'Cancelled'], () => json(returnView('Cancelled', { cancellationReason: null })))
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    await user.click(await screen.findByRole('button', { name: T.detail.cancel }))
    await user.type(screen.getByLabelText(T.detail.reasonLabel), '   ')
    await user.click(screen.getByRole('button', { name: T.detail.cancelConfirm }))
    expect((await screen.findByTestId('customer-return-notice')).textContent).toBe(T.detail.completed.cancellation)
    await waitFor(() => expect(screen.getByTestId('customer-return-state').textContent).toBe('İptal edildi'))
    const [post] = fetch.posts()
    expect(post.url).toBe(`${DETAIL_URL}/cancellation`)
    expect(post.body).toEqual({ expectedVersion: '1', reason: null })
    expect(post.idempotencyKey).toMatch(/\S{8,}/)
  })

  it('shows a cancellation recorded without a reason as such', async () => {
    installPoolFetch((call) => {
      if (call.method === 'GET' && call.url === DETAIL_URL) return json(returnView('Cancelled', { cancellationReason: null }))
      return unexpected(call)
    }, COMMANDS)
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    expect((await screen.findByTestId('customer-return-cancellation-reason')).textContent).toBe(T.detail.reasonNone)
    expect(screen.getByTestId('customer-return-state').textContent).toBe('İptal edildi')
    expect(screen.queryByTestId('customer-return-commands')).toBeNull()
  })

  it('offers cancellation on a pending return too, and going back closes the form without a send', async () => {
    const user = userEvent.setup()
    const fetch = serveDetail(['PendingApproval'], unexpected)
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    await user.click(await screen.findByRole('button', { name: T.detail.cancel }))
    expect(screen.getByTestId('customer-return-reason-form')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.detail.reasonBack }))
    expect(screen.queryByTestId('customer-return-reason-form')).toBeNull()
    expect(fetch.posts()).toHaveLength(0)
  })

  it('refuses a reason over the bound locally and sends nothing', async () => {
    const user = userEvent.setup()
    const fetch = serveDetail(['PendingApproval'], unexpected)
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    await user.click(await screen.findByRole('button', { name: T.detail.reject }))
    fireEvent.change(screen.getByLabelText(T.detail.reasonLabel), { target: { value: 'x'.repeat(CUSTOMER_RETURN_REASON_MAX + 1) } })
    await user.click(screen.getByRole('button', { name: T.detail.rejectConfirm }))
    expect((await screen.findByTestId('customer-return-reason-error')).textContent).toBe(T.detail.reasonTooLong(CUSTOMER_RETURN_REASON_MAX))
    expect(fetch.posts()).toHaveLength(0)
  })

  it('says why the server refused a self-rejection (409) and keeps the return as it is', async () => {
    const user = userEvent.setup()
    serveDetail(['PendingApproval'], () => problem(409, { code: CUSTOMER_RETURN_PROBLEM_CODE.selfApproval }))
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    await user.click(await screen.findByRole('button', { name: T.detail.reject }))
    await user.click(screen.getByRole('button', { name: T.detail.rejectConfirm }))
    const notice = await screen.findByTestId('customer-return-notice')
    expect(notice.textContent).toContain(customerReturnProblemText(CUSTOMER_RETURN_PROBLEM_CODE.selfApproval))
    expect(screen.getByTestId('customer-return-state').textContent).toBe('Onay bekliyor')
  })

  it('says the server refused a reason as too long', async () => {
    const user = userEvent.setup()
    serveDetail(['Draft'], () =>
      problem(422, { code: SALES_PROBLEM_CODE.approvalReasonTooLong, errors: [{ field: 'reason', code: SALES_PROBLEM_CODE.approvalReasonTooLong }] }),
    )
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    await user.click(await screen.findByRole('button', { name: T.detail.cancel }))
    await user.type(screen.getByLabelText(T.detail.reasonLabel), CANCELLATION_REASON)
    await user.click(screen.getByRole('button', { name: T.detail.cancelConfirm }))
    expect((await screen.findByTestId('customer-return-notice')).textContent).toBe(T.detail.reasonTooLongServer)
    expect(screen.getByTestId('customer-return-state').textContent).toBe('Taslak')
  })

  it.each(['Approved', 'Finalized', 'Rejected', 'Cancelled'])('offers neither decision on a %s return', async (state) => {
    serveDetail([state], unexpected)
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    await screen.findByTestId('customer-return-state')
    expect(screen.queryByTestId('customer-return-rejection')).toBeNull()
    expect(screen.queryByTestId('customer-return-cancellation')).toBeNull()
  })

  it('offers rejection only with the approve id and cancellation only with the submit id', async () => {
    serveDetail(['PendingApproval'], unexpected, [SALES_CAPABILITY.customerReturnsDetail, SALES_CUSTOMER_RETURN_SUBMIT_CAPABILITY])
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    expect(await screen.findByTestId('customer-return-cancellation')).toBeTruthy()
    expect(screen.queryByTestId('customer-return-rejection')).toBeNull()
    cleanup()
    vi.unstubAllGlobals()
    serveDetail(['PendingApproval'], unexpected, [SALES_CAPABILITY.customerReturnsDetail, SALES_CUSTOMER_RETURN_APPROVE_CAPABILITY])
    renderPool(`/satis/iadeler/${RETURN_ID}`)
    expect(await screen.findByTestId('customer-return-rejection')).toBeTruthy()
    expect(screen.queryByTestId('customer-return-cancellation')).toBeNull()
  })
})

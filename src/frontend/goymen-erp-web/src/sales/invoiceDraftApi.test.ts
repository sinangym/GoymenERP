/**
 * API-32 / API-33 transport, SENTETIK consumer tests (NOT_ENV): the create body names the customer account and the whole shares
 * only; each draft command posts `{ expectedVersion }` (plus `reason` for reject / cancel) under the frozen company and the
 * attempt's key; a 2xx counts only as the pinned status with a body of the asked invoice, in the state the command leads to,
 * whose replay flag agrees with the headers — anything else is malformed, so the caller keeps the key.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  COMMAND_RESULT_STATE,
  INVOICE_DRAFT_COMMAND,
  SALES_INVOICES_PATH,
  commandTakesReason,
  createSalesInvoice,
  invoiceDraftCommandBody,
  invoiceDraftCommandPath,
  sendInvoiceDraftCommand,
  type InvoiceDraftCommand,
} from './invoiceDraftApi'
import { INVOICE_ID, invoiceDetail } from './invoiceTestData'

const COMPANY = '99999999-9999-9999-9999-999999999999'
const ACCOUNT = '55555555-5555-5555-5555-555555555555'
const SHIPMENT_1 = 'f9f9f9f9-0000-4000-8000-000000000001'
const SHIPMENT_2 = 'f9f9f9f9-0000-4000-8000-000000000003'
const REPLAY = { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-original-1' }

interface Seen {
  readonly url: string
  readonly method: string
  readonly headers: Record<string, string>
  readonly body: string | null
}

const json = (body: unknown, status: number, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })

function answer(response: () => Response) {
  const seen: Seen[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init: RequestInit = {}) => {
    seen.push({
      url,
      method: (init.method ?? 'GET').toUpperCase(),
      headers: (init.headers ?? {}) as Record<string, string>,
      body: typeof init.body === 'string' ? init.body : null,
    })
    return response()
  }))
  return seen
}

const context = { idempotencyKey: 'key-0001-abcdef', companyId: COMPANY }
const draft = (over: Record<string, unknown> = {}) => invoiceDetail({ state: 'Draft', version: '1', match: null, ...over })

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('API-32 create invoice draft', () => {
  it('posts the account and the chosen shares under the company and the key, and accepts a 201 Draft', async () => {
    const seen = answer(() => json(draft(), 201))
    const outcome = await createSalesInvoice({ companyAccountId: ACCOUNT, shipmentIds: [SHIPMENT_1, SHIPMENT_2] }, context)
    expect(outcome.type).toBe('succeeded')
    expect(seen).toHaveLength(1)
    expect(seen[0].url).toBe(`/api/v1${SALES_INVOICES_PATH}`)
    expect(seen[0].method).toBe('POST')
    expect(seen[0].headers['X-Company-Id']).toBe(COMPANY)
    expect(seen[0].headers['Idempotency-Key']).toBe('key-0001-abcdef')
    expect(seen[0].headers['Content-Type']).toBe('application/json')
    expect(JSON.parse(seen[0].body ?? 'null')).toEqual({
      companyAccountId: ACCOUNT,
      shares: [{ shipmentId: SHIPMENT_1 }, { shipmentId: SHIPMENT_2 }],
    })
  })

  it('accepts a replay only when the headers name the original attempt', async () => {
    answer(() => json(draft({ isReplay: true }), 201, REPLAY))
    expect((await createSalesInvoice({ companyAccountId: ACCOUNT, shipmentIds: [SHIPMENT_1] }, context)).type).toBe('succeeded')
    answer(() => json(draft({ isReplay: true }), 201))
    expect((await createSalesInvoice({ companyAccountId: ACCOUNT, shipmentIds: [SHIPMENT_1] }, context)).type).toBe('malformed')
    answer(() => json(draft(), 201, REPLAY))
    expect((await createSalesInvoice({ companyAccountId: ACCOUNT, shipmentIds: [SHIPMENT_1] }, context)).type).toBe('malformed')
  })

  it('treats another state, another account, a broken body or another 2xx as malformed', async () => {
    const payload = { companyAccountId: ACCOUNT, shipmentIds: [SHIPMENT_1] }
    answer(() => json(draft({ state: 'Approved' }), 201))
    expect((await createSalesInvoice(payload, context)).type).toBe('malformed')
    answer(() => json(draft({ companyAccountId: '66666666-6666-6666-6666-666666666666' }), 201))
    expect((await createSalesInvoice(payload, context)).type).toBe('malformed')
    answer(() => json({ ...draft(), extra: 1 }, 201))
    expect((await createSalesInvoice(payload, context)).type).toBe('malformed')
    answer(() => json(draft(), 200))
    expect((await createSalesInvoice(payload, context)).type).toBe('malformed')
  })

  it('returns an error status with its problem, unchanged', async () => {
    answer(() => json({ status: 409, title: 'Problem', code: 'invoice_not_ready', errors: { 'shares[0]': ['rate_unresolved'] } }, 409))
    const outcome = await createSalesInvoice({ companyAccountId: ACCOUNT, shipmentIds: [SHIPMENT_1] }, context)
    expect(outcome.type).toBe('http')
    if (outcome.type !== 'http') return
    expect(outcome.status).toBe(409)
    expect(outcome.problem?.code).toBe('invoice_not_ready')
  })
})

describe('API-33 draft commands', () => {
  const kinds = Object.values(INVOICE_DRAFT_COMMAND) as InvoiceDraftCommand[]

  it('names the declared routes and sends a reason member only for reject and cancel', () => {
    expect(invoiceDraftCommandPath('submit', INVOICE_ID)).toBe(`/sales/invoices/${INVOICE_ID}/submission`)
    expect(invoiceDraftCommandPath('approve', INVOICE_ID)).toBe(`/sales/invoices/${INVOICE_ID}/approval`)
    expect(invoiceDraftCommandPath('reject', INVOICE_ID)).toBe(`/sales/invoices/${INVOICE_ID}/rejection`)
    expect(invoiceDraftCommandPath('cancel', INVOICE_ID)).toBe(`/sales/invoices/${INVOICE_ID}/cancellation`)
    expect(kinds.filter(commandTakesReason)).toEqual(['reject', 'cancel'])
    expect(invoiceDraftCommandBody('submit', { expectedVersion: '3', reason: 'x' })).toEqual({ expectedVersion: '3' })
    expect(invoiceDraftCommandBody('approve', { expectedVersion: '3', reason: null })).toEqual({ expectedVersion: '3' })
    expect(invoiceDraftCommandBody('reject', { expectedVersion: '3', reason: 'Fiyat yanlis' })).toEqual({ expectedVersion: '3', reason: 'Fiyat yanlis' })
    expect(invoiceDraftCommandBody('cancel', { expectedVersion: '3', reason: null })).toEqual({ expectedVersion: '3', reason: null })
  })

  it.each(kinds)('accepts %s only as a 200 in the state the command leads to', async (kind) => {
    const seen = answer(() => json(invoiceDetail({ state: COMMAND_RESULT_STATE[kind], version: '2' }), 200))
    const ok = await sendInvoiceDraftCommand(kind, { expectedVersion: '1', reason: null }, { ...context, invoiceId: INVOICE_ID })
    expect(ok.type).toBe('succeeded')
    expect(seen[0].url).toBe(`/api/v1${invoiceDraftCommandPath(kind, INVOICE_ID)}`)
    expect(seen[0].headers['X-Company-Id']).toBe(COMPANY)
    expect(seen[0].headers['Idempotency-Key']).toBe('key-0001-abcdef')

    answer(() => json(invoiceDetail({ state: 'Draft', version: '2', match: null }), 200))
    const wrong = await sendInvoiceDraftCommand(kind, { expectedVersion: '1', reason: null }, { ...context, invoiceId: INVOICE_ID })
    expect(wrong.type).toBe('malformed')
  })

  it('refuses another invoice and a replay without headers, and passes a refusal through', async () => {
    const ctx = { ...context, invoiceId: INVOICE_ID }
    answer(() => json(invoiceDetail({ id: 'f1000000-0000-4000-8000-000000000002', state: 'PendingApproval' }), 200))
    expect((await sendInvoiceDraftCommand('submit', { expectedVersion: '1', reason: null }, ctx)).type).toBe('malformed')
    answer(() => json(invoiceDetail({ state: 'PendingApproval', isReplay: true }), 200))
    expect((await sendInvoiceDraftCommand('submit', { expectedVersion: '1', reason: null }, ctx)).type).toBe('malformed')
    answer(() => json({ status: 409, title: 'Problem', code: 'sales_invoice_self_approval' }, 409))
    const refused = await sendInvoiceDraftCommand('approve', { expectedVersion: '1', reason: null }, ctx)
    expect(refused.type).toBe('http')
    if (refused.type === 'http') expect(refused.problem?.code).toBe('sales_invoice_self_approval')
  })
})

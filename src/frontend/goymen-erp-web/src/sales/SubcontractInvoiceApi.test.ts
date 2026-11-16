/**
 * F2 subcontract service invoice transport, SENTETIK consumer tests (NOT_ENV): the create body names the order, the loading
 * instruction and the weighing only (no kilogram, price or amount), under the frozen company and the attempt's key; a 2xx
 * counts only as a 201 Draft of the asked order, instruction and weighing whose replay flag agrees with the headers — anything
 * else is malformed, so the caller keeps the key. The read decodes the asked invoice only.
 */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createSubcontractInvoice, fetchSubcontractInvoice, subcontractInvoiceBody } from './SubcontractInvoiceApi'
import { SUBCONTRACT_SERVICE_INVOICES_PATH } from './SubcontractInvoiceContract'
import {
  LOADING_INSTRUCTION_ID,
  SUBCONTRACT_INVOICE_ID,
  SUBCONTRACT_ORDER_ID,
  WEIGHING_ID,
  subcontractInvoiceDetail,
} from './subcontractInvoiceTestData'

const COMPANY = '99999999-9999-9999-9999-999999999999'
const OTHER = 'e7000000-0000-4000-8000-000000000009'
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

const payload = { salesOrderId: SUBCONTRACT_ORDER_ID, loadingInstructionId: LOADING_INSTRUCTION_ID, weighingId: WEIGHING_ID }
const context = { idempotencyKey: 'key-0001-abcdef', companyId: COMPANY }

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('F2 create subcontract service invoice draft', () => {
  it('posts the order, the instruction and the weighing only, under the company and the key, and accepts a 201 Draft', async () => {
    const seen = answer(() => json(subcontractInvoiceDetail(), 201))
    const outcome = await createSubcontractInvoice(payload, context)
    expect(outcome.type).toBe('succeeded')
    if (outcome.type === 'succeeded') expect(outcome.data.netKg).toBe('1234.560')
    expect(seen).toHaveLength(1)
    expect(seen[0].url).toBe(`/api/v1${SUBCONTRACT_SERVICE_INVOICES_PATH}`)
    expect(seen[0].method).toBe('POST')
    expect(seen[0].headers['X-Company-Id']).toBe(COMPANY)
    expect(seen[0].headers['Idempotency-Key']).toBe('key-0001-abcdef')
    expect(seen[0].headers['Content-Type']).toBe('application/json')
    expect(JSON.parse(seen[0].body ?? 'null')).toEqual({
      salesOrderId: SUBCONTRACT_ORDER_ID,
      loadingInstructionId: LOADING_INSTRUCTION_ID,
      weighingId: WEIGHING_ID,
    })
    expect(Object.keys(subcontractInvoiceBody(payload))).toEqual(['salesOrderId', 'loadingInstructionId', 'weighingId'])
  })

  it('accepts a replay only when the headers name the original attempt', async () => {
    answer(() => json(subcontractInvoiceDetail({ isReplay: true }), 201, REPLAY))
    expect((await createSubcontractInvoice(payload, context)).type).toBe('succeeded')
    answer(() => json(subcontractInvoiceDetail({ isReplay: true }), 201))
    expect((await createSubcontractInvoice(payload, context)).type).toBe('malformed')
    answer(() => json(subcontractInvoiceDetail(), 201, REPLAY))
    expect((await createSubcontractInvoice(payload, context)).type).toBe('malformed')
  })

  it('treats another order, instruction or weighing, another status or an unknown member as malformed', async () => {
    for (const over of [{ salesOrderId: OTHER }, { loadingInstructionId: OTHER }, { weighingId: OTHER }, { extra: 1 }]) {
      answer(() => json(subcontractInvoiceDetail(over), 201))
      expect((await createSubcontractInvoice(payload, context)).type).toBe('malformed')
    }
    answer(() => json(subcontractInvoiceDetail(), 200))
    expect((await createSubcontractInvoice(payload, context)).type).toBe('malformed')
  })

  it('matches the echoed ids case-insensitively', async () => {
    answer(() => json(subcontractInvoiceDetail({ weighingId: WEIGHING_ID.toUpperCase() }), 201))
    expect((await createSubcontractInvoice(payload, context)).type).toBe('succeeded')
  })

  it('returns an error status with its problem, unchanged', async () => {
    answer(() => json({
      status: 409,
      title: 'Problem',
      code: 'invoice_not_ready',
      errors: [{ field: 'weighingId', code: 'weighing_missing' }],
    }, 409))
    const outcome = await createSubcontractInvoice(payload, context)
    expect(outcome.type).toBe('http')
    if (outcome.type !== 'http') return
    expect(outcome.status).toBe(409)
    expect(outcome.problem?.code).toBe('invoice_not_ready')
  })
})

describe('F2 read subcontract service invoice', () => {
  it('reads the asked invoice under the company and refuses another one', async () => {
    const seen = answer(() => json(subcontractInvoiceDetail(), 200))
    const outcome = await fetchSubcontractInvoice(SUBCONTRACT_INVOICE_ID, COMPANY)
    expect(outcome.type).toBe('ok-single')
    expect(seen[0].url).toBe(`/api/v1${SUBCONTRACT_SERVICE_INVOICES_PATH}/${SUBCONTRACT_INVOICE_ID}`)
    expect(seen[0].method).toBe('GET')
    expect(seen[0].headers['X-Company-Id']).toBe(COMPANY)

    answer(() => json(subcontractInvoiceDetail(), 200))
    expect((await fetchSubcontractInvoice('f5000000-0000-4000-8000-000000000009', COMPANY)).type).toBe('malformed')
    answer(() => json({ status: 404 }, 404))
    expect((await fetchSubcontractInvoice(SUBCONTRACT_INVOICE_ID, COMPANY)).type).toBe('http')
  })
})

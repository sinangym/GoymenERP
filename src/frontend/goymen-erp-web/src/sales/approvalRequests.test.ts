/**
 * WP4 (SAT-D475 / SAT-D476): the approval request wire — the exact decoders (no unknown member, the kind's facts, the decision
 * that fits the status) and the transport (the path, the company header, the key, the pinned status, the answer bound to the
 * asked subject). Synthetic data (SENTETIK).
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  APPROVAL_REQUEST_KIND,
  decideOverDeliveryRequest,
  decodeApprovalRequest,
  decodeApprovalRequestList,
  fetchOverDeliveryRequests,
  requestQuoteConversion,
  type ApprovalRequest,
} from './approvalRequests'
import { APPROVAL_REQUEST_PROBLEM_TEXT, APPROVAL_REQUEST_STATUS_TEXT, approvalRequestProblemText } from './approvalRequestText'
import { LOGISTICS_PROBLEM_CODE } from '../logistics/contract'
import { PROBLEM_CODE_TEXT as LOGISTICS_PROBLEM_TEXT } from '../logistics/logisticsText'

const COMPANY = '5a1e0000-0000-4000-8000-00000000c0a1'
const ORDER = '5a1e0000-0000-4000-8000-0000000000d1'
const QUOTE = '5a1e0000-0000-4000-8000-0000000000e1'
const LINE = '5a1e0000-0000-4000-8000-0000000000f1'

const overDelivery = (overrides: Partial<Record<keyof ApprovalRequest, unknown>> = {}) => ({
  kind: 'OverDelivery',
  subjectId: ORDER,
  requestNo: 1,
  subjectRevision: '0',
  status: 'Pending',
  version: '1',
  lineId: LINE,
  ordered: '100',
  quantity: '2.5',
  requestedTotal: '102.5',
  loadingInstructionId: '5a1e0000-0000-4000-8000-000000000101',
  shipmentId: '5a1e0000-0000-4000-8000-000000000102',
  note: null,
  requestedBy: 'SENTETIK-shipper',
  requestedAtUtc: '2026-11-23T07:00:00+00:00',
  decidedBy: null,
  decidedAtUtc: null,
  decisionReason: null,
  ...overrides,
})

const quoteRequest = (overrides: Partial<Record<keyof ApprovalRequest, unknown>> = {}) => ({
  ...overDelivery({
    kind: 'QuoteExpiredConversion', subjectId: QUOTE, subjectRevision: '2', lineId: null, ordered: null, quantity: null,
    requestedTotal: null, loadingInstructionId: null, shipmentId: null, note: 'SENTETIK not', requestedBy: 'SENTETIK-seller',
  }),
  ...overrides,
})

const json = (body: unknown, status: number, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('approval request decoder', () => {
  it('reads an over-delivery request and a quote request exactly as the server describes them', () => {
    expect(decodeApprovalRequest(overDelivery())).toMatchObject({ kind: 'OverDelivery', requestedTotal: '102.5', requestNo: 1 })
    expect(decodeApprovalRequest(quoteRequest())).toMatchObject({ kind: 'QuoteExpiredConversion', note: 'SENTETIK not' })
    expect(decodeApprovalRequest(overDelivery({
      status: 'Rejected', version: '2', decidedBy: 'SENTETIK-approver', decidedAtUtc: '2026-11-23T08:00:00Z', decisionReason: 'SENTETIK',
    }))?.status).toBe('Rejected')
  })

  it.each([
    ['an unknown member', { ...overDelivery(), extra: 1 }],
    ['a missing member', (() => { const { note: _, ...rest } = overDelivery(); return rest })()],
    ['an unknown kind', overDelivery({ kind: 'Other' })],
    ['an unknown status', overDelivery({ status: 'Waiting' })],
    ['a zero request number', overDelivery({ requestNo: 0 })],
    ['a quantity in Turkish digits', overDelivery({ requestedTotal: '102,5' })],
    ['an over-delivery without its line', overDelivery({ lineId: null })],
    ['a quote request with over-delivery facts', quoteRequest({ ordered: '1' })],
    ['a rejection without its reason', overDelivery({ status: 'Rejected', decidedBy: 'x', decidedAtUtc: '2026-11-23T08:00:00Z' })],
    ['a pending request with a decision', overDelivery({ decidedBy: 'x', decidedAtUtc: '2026-11-23T08:00:00Z' })],
    ['a blank requester', overDelivery({ requestedBy: ' ' })],
  ])('refuses %s', (_, body) => {
    expect(decodeApprovalRequest(body)).toBeNull()
  })

  it('reads a list only when every item is of the asked kind and subject, newest first', () => {
    const second = overDelivery({ requestNo: 2 })
    expect(decodeApprovalRequestList({ items: [second, overDelivery()] }, APPROVAL_REQUEST_KIND.overDelivery, ORDER)).toHaveLength(2)
    expect(decodeApprovalRequestList({ items: [overDelivery(), second] }, APPROVAL_REQUEST_KIND.overDelivery, ORDER)).toBeNull()
    expect(decodeApprovalRequestList({ items: [overDelivery()] }, APPROVAL_REQUEST_KIND.overDelivery, QUOTE)).toBeNull()
    expect(decodeApprovalRequestList({ items: [quoteRequest()] }, APPROVAL_REQUEST_KIND.overDelivery, QUOTE)).toBeNull()
    expect(decodeApprovalRequestList({ items: [], more: true }, APPROVAL_REQUEST_KIND.overDelivery, ORDER)).toBeNull()
  })
})

describe('approval request transport', () => {
  it('reads the order\'s over-delivery requests under the company and refuses another order\'s list', async () => {
    const seen: string[] = []
    vi.stubGlobal('fetch', vi.fn(async (url: string, init: RequestInit = {}) => {
      seen.push(`${url} ${(init.headers as Record<string, string>)['X-Company-Id']}`)
      return json({ items: [overDelivery()] }, 200)
    }))
    const outcome = await fetchOverDeliveryRequests(ORDER, COMPANY)
    expect(outcome.type).toBe('ok-single')
    expect(seen).toEqual([`/api/v1/sales/drafts/${ORDER}/over-delivery-requests ${COMPANY}`])

    vi.stubGlobal('fetch', vi.fn(async () => json({ items: [overDelivery({ subjectId: QUOTE })] }, 200)))
    expect((await fetchOverDeliveryRequests(ORDER, COMPANY)).type).toBe('malformed')
  })

  it('posts the conversion request with its key and accepts only a 201 naming the quote', async () => {
    const bodies: unknown[] = []
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init: RequestInit = {}) => {
      bodies.push({ body: JSON.parse(init.body as string), key: (init.headers as Record<string, string>)['Idempotency-Key'] })
      return json(quoteRequest(), 201)
    }))
    const outcome = await requestQuoteConversion(QUOTE, { expectedVersion: '3', note: null }, { idempotencyKey: 'k-1', companyId: COMPANY })
    expect(outcome.type).toBe('succeeded')
    expect(bodies).toEqual([{ body: { expectedVersion: '3', note: null }, key: 'k-1' }])

    vi.stubGlobal('fetch', vi.fn(async () => json(quoteRequest(), 200)))
    expect((await requestQuoteConversion(QUOTE, { expectedVersion: '3', note: null }, { idempotencyKey: 'k-2', companyId: COMPANY })).type)
      .toBe('malformed')
  })

  it('a decision answer of another request number is malformed; a refusal carries its problem code', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(overDelivery({ requestNo: 2 }), 200)))
    expect((await decideOverDeliveryRequest(ORDER, 1, 'approval', { expectedVersion: '1' }, { idempotencyKey: 'k', companyId: COMPANY })).type)
      .toBe('malformed')

    vi.stubGlobal('fetch', vi.fn(async () => json({ status: 409, title: 'x', code: 'sales_approval_request_self_decision', errors: [] }, 409)))
    const refused = await decideOverDeliveryRequest(ORDER, 1, 'approval', { expectedVersion: '1' }, { idempotencyKey: 'k', companyId: COMPANY })
    expect(refused).toMatchObject({ type: 'http', status: 409 })
    if (refused.type === 'http') expect(approvalRequestProblemText(refused.problem?.code)).toContain('kendi fazla teslim talebini')
  })
})

describe('approval request words', () => {
  it('names every status and refusal in Turkish and the Logistics shipment says why it stopped', () => {
    expect(Object.values(APPROVAL_REQUEST_STATUS_TEXT)).toEqual(['Onay bekliyor', 'Onaylandı', 'Reddedildi', 'Geçersiz (teklif değişti)'])
    expect(Object.keys(APPROVAL_REQUEST_PROBLEM_TEXT)).toContain('sales_approval_request_blocked')
    expect(approvalRequestProblemText('unknown_code')).toBe('İşlem tamamlanamadı. Tekrar deneyin.')
    expect(LOGISTICS_PROBLEM_TEXT[LOGISTICS_PROBLEM_CODE.overDeliveryApprovalPending]).toContain('fazla teslim onay talebi açıldı')
    expect(LOGISTICS_PROBLEM_TEXT[LOGISTICS_PROBLEM_CODE.overDeliveryBlocked]).toContain('blok bandına')
  })
})

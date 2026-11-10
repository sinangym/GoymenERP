/**
 * API-07 (EK21 SAT-D153…D162): the risk exception wire decoder accepts only an exact, state-consistent answer of this order and
 * refuses everything else as a whole. Every identity is synthetic.
 */
import { describe, expect, it } from 'vitest'
import { decodeRiskException, decodeRiskExceptionList, RISK_EXCEPTION_REASON_MAX, riskExceptionDecisionPath } from './riskException'

const ORDER = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const SHIPMENT = 'e1e1e1e1-e1e1-4e1e-8e1e-e1e1e1e1e1e1'

type Body = Record<string, unknown>

const requestedException = (overrides: Body = {}): Body => ({
  orderId: ORDER, exceptionNo: 1, loadingInstructionId: SHIPMENT, state: 'Requested', version: '1',
  reason: 'SENTETIK müşteri ödeme sözü verdi', requestedBy: 'synthetic-shipper', requestedAtUtc: '2026-11-10T07:00:00Z',
  decidedBy: null, decidedAtUtc: null, rejectionReason: null, validOn: null, isValidToday: false, ...overrides,
})
const approvedException = (overrides: Body = {}): Body => requestedException({
  state: 'Approved', version: '2', decidedBy: 'synthetic-manager', decidedAtUtc: '2026-11-10T08:00:00Z', validOn: '2026-11-10',
  isValidToday: true, ...overrides,
})
const rejectedException = (overrides: Body = {}): Body => requestedException({
  state: 'Rejected', version: '2', decidedBy: 'synthetic-manager', decidedAtUtc: '2026-11-10T08:00:00Z',
  rejectionReason: 'SENTETIK risk çok yüksek', ...overrides,
})

describe('API-07 risk exception decoder', () => {
  it('accepts a request, an approval and a rejection that agree with their state', () => {
    expect(decodeRiskException(requestedException(), ORDER)?.state).toBe('Requested')
    expect(decodeRiskException(approvedException(), ORDER.toUpperCase())?.validOn).toBe('2026-11-10')
    expect(decodeRiskException(approvedException({ isValidToday: false }), ORDER)?.isValidToday).toBe(false)
    expect(decodeRiskException(rejectedException(), ORDER)?.rejectionReason).toBe('SENTETIK risk çok yüksek')
    expect(decodeRiskException(requestedException({ reason: 'x'.repeat(RISK_EXCEPTION_REASON_MAX) }), ORDER)).not.toBeNull()
  })

  it.each([
    ['another order', requestedException({ orderId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' })],
    ['an extra key (an amount)', requestedException({ amount: '100.00' })],
    ['a missing key', (() => { const body = requestedException(); delete body.validOn; return body })()],
    ['an unknown state', requestedException({ state: 'Withdrawn' })],
    ['a non-positive number', requestedException({ exceptionNo: 0 })],
    ['a fractional number', requestedException({ exceptionNo: 1.5 })],
    ['a blank reason', requestedException({ reason: '  ' })],
    ['an over-long reason', requestedException({ reason: 'x'.repeat(RISK_EXCEPTION_REASON_MAX + 1) })],
    ['a bad shipment id', requestedException({ loadingInstructionId: 'not-a-uuid' })],
    ['a request carrying a decision', requestedException({ decidedBy: 'synthetic-manager', decidedAtUtc: '2026-11-10T08:00:00Z' })],
    ['a request valid today', requestedException({ isValidToday: true })],
    ['a request at version 2', requestedException({ version: '2' })],
    ['an approval without its business date', approvedException({ validOn: null })],
    ['an approval with a rejection reason', approvedException({ rejectionReason: 'r' })],
    ['an approval at version 1', approvedException({ version: '1' })],
    ['an approval with a date-time validOn', approvedException({ validOn: '2026-11-10T00:00:00Z' })],
    ['a self-approval (SAT-D153)', approvedException({ decidedBy: ' synthetic-shipper ' })],
    ['a rejection without a reason', rejectedException({ rejectionReason: null })],
    ['a rejection valid today', rejectedException({ isValidToday: true })],
    ['a rejection with a business date', rejectedException({ validOn: '2026-11-10' })],
    ['a decision without its time', rejectedException({ decidedAtUtc: 'yesterday' })],
  ])('refuses %s as a whole', (_, body) => {
    expect(decodeRiskException(body, ORDER)).toBeNull()
  })

  it('reads the list envelope and refuses a duplicate number, an extra key or one bad item', () => {
    expect(decodeRiskExceptionList({ items: [] }, ORDER)).toEqual([])
    expect(decodeRiskExceptionList({ items: [approvedException({ exceptionNo: 2 }), requestedException()] }, ORDER)?.length).toBe(2)
    expect(decodeRiskExceptionList({ items: [requestedException(), rejectedException()] }, ORDER)).toBeNull()
    expect(decodeRiskExceptionList({ items: [], hasMore: false }, ORDER)).toBeNull()
    expect(decodeRiskExceptionList({ items: [requestedException(), { exceptionNo: 2 }] }, ORDER)).toBeNull()
    expect(decodeRiskExceptionList([], ORDER)).toBeNull()
  })

  it('names the decision routes under the order', () => {
    expect(riskExceptionDecisionPath(ORDER, 3, 'rejection')).toBe(`/sales/drafts/${ORDER}/risk-exceptions/3/rejection`)
  })
})

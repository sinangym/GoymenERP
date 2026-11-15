/**
 * URT-S19 cost trace decoder: strict keys, lossless text figures, the order asked for, and a cost part that carries
 * figures only when `available` — never a zero beside a reason.
 */

import { describe, expect, it } from 'vitest'
import { decodeCostTrace } from './costTraceContract'
import { availableCost, costTrace, costTraceLot, unavailableCost } from './costTraceFixture'
import { ORDER, ROLL_1 } from './testHarness'

function withoutCost(): Record<string, unknown> {
  const body: Record<string, unknown> = { ...costTrace() }
  delete body.cost
  return body
}

describe('decodeCostTrace', () => {
  it('decodes the server shape and keeps every figure as the server text', () => {
    const trace = decodeCostTrace(costTrace(), ORDER)

    expect(trace).not.toBeNull()
    expect(trace?.methodCode).toBe('weighted-average-raw-material-kg')
    expect(trace?.lots.map((lot) => lot.consumedKg)).toEqual(['600.5', '0'])
    expect(trace?.outputs).toEqual([{ outputClass: 'GoodProduct', netKg: '590', declarationCount: 1, reversalCount: 0 }])
    expect(trace?.cost).toEqual(unavailableCost())
  })

  it('accepts an available cost with its figures and a sales order link', () => {
    const trace = decodeCostTrace(
      costTrace({
        cost: availableCost(),
        salesOrderLink: {
          salesOrderId: 'a0000000-0000-4000-8000-000000000001',
          salesOrderLineId: 'a0000000-0000-4000-8000-000000000002',
          customerPartyId: 'a0000000-0000-4000-8000-000000000003',
          productId: 'a0000000-0000-4000-8000-000000000004',
          lineRevision: 2,
          linkedAtUtc: '2026-09-17T08:00:00+00:00',
        },
      }),
      ORDER,
    )

    expect(trace?.cost.costPerKgTl).toBe('31.4159')
    expect(trace?.salesOrderLink?.lineRevision).toBe(2)
  })

  it.each([
    ['another order', costTrace({ orderId: 'd0000000-0000-4000-8000-0000000000ff' })],
    ['an unknown contract version', costTrace({ contractVersion: 2 })],
    ['an extra key', { ...costTrace(), extra: true }],
    ['a missing key', withoutCost()],
    ['a kg as a JSON number', costTrace({ totalIssuedKg: 600.5 })],
    ['a kg with a trailing zero', costTrace({ totalIssuedKg: '600.50' })],
    ['a negative kg', costTrace({ totalConsumedKg: '-1' })],
    ['a duplicate lot', costTrace({ lots: [costTraceLot(ROLL_1), costTraceLot(ROLL_1)] })],
    ['an issued lot without its moment', costTrace({ lots: [costTraceLot(ROLL_1, { issueCount: 1 })] })],
    ['an owner without its account', costTrace({
      lots: [costTraceLot(ROLL_1, { commercialPartyId: 'a0000000-0000-4000-8000-000000000003' })],
    })],
    ['an unknown cost status', costTrace({ cost: { ...unavailableCost(), status: 'estimated' } })],
    ['a zero beside an unavailable reason', costTrace({ cost: { ...unavailableCost(), costPerKgTl: '0' } })],
    ['an unavailable cost without a reason', costTrace({ cost: { ...unavailableCost(), reasonCode: null } })],
    ['an available cost without its figure', costTrace({ cost: availableCost({ costPerKgTl: null }) })],
    ['an available cost with a reason', costTrace({ cost: availableCost({ reasonCode: 'mass_unbalanced' }) })],
    ['a duplicate output class', costTrace({
      outputs: [
        { outputClass: 'Scrap', netKg: '1', declarationCount: 1, reversalCount: 0 },
        { outputClass: 'Scrap', netKg: '2', declarationCount: 1, reversalCount: 0 },
      ],
    })],
    ['a timeline with only a first moment', costTrace({
      timeline: {
        firstFactAtUtc: '2026-09-18T09:30:00+00:00',
        lastFactAtUtc: null,
        closedAtUtc: null,
        stoppageCode: 'time_records_not_recorded',
        durationCode: 'time_records_not_recorded',
      },
    })],
  ])('refuses %s', (_name, body) => {
    expect(decodeCostTrace(body, ORDER)).toBeNull()
  })

  it('refuses a body that is not an object', () => {
    expect(decodeCostTrace(null, ORDER)).toBeNull()
    expect(decodeCostTrace([costTrace()], ORDER)).toBeNull()
  })
})

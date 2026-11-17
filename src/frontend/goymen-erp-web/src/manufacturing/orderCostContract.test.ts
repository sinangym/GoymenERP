/**
 * URT-S17 order cost decoder: strict keys, lossless text figures, the order asked for, figures only when
 * `available` (giren TL = dağıtılan TL, the remainder on the line it names) and never a zero beside a reason.
 */

import { describe, expect, it } from 'vitest'
import { decodeOrderCost } from './orderCostContract'
import { COST_LABEL_1, COST_LABEL_2, availableOrderCost, costLine, orderCost } from './orderCostFixture'
import { ORDER } from './testHarness'

const OTHER_ORDER = 'd0000000-0000-4000-8000-000000000099'
const ISSUE = 'b0000000-0000-4000-8000-000000000002'

function withoutKey(body: Record<string, unknown>, key: string): Record<string, unknown> {
  const copy: Record<string, unknown> = { ...body }
  delete copy[key]
  return copy
}

describe('decodeOrderCost', () => {
  it('preserves an exact foreign unit and amount ratio without a rounded scalar or UI calculation', () => {
    const original = { issueMovementId: ISSUE, currencyId: 'c0000000-0000-4000-8000-000000000001', foreignUnitCostPerKg: '0.35',
      exchangeRate: '35.25', tlUnitCostPerKg: '12.3375', keptKilograms: '2', foreignAmount: '0.7' }
    const exact = { ...original, foreignUnitCostPerKg: null, foreignAmount: null,
      foreignUnitCostRatio: { numerator: '1', denominator: '3' }, foreignAmountRatio: { numerator: '2', denominator: '3' } }
    const decoded = decodeOrderCost(availableOrderCost({ currencyLines: [exact] }), ORDER)
    expect(decoded?.currencyLines[0]?.foreignUnitCostPerKg).toBeNull()
    expect(decoded?.currencyLines[0]?.foreignAmountRatio).toEqual({ numerator: '2', denominator: '3' })
    for (const malformed of [ { ...exact, foreignUnitCostPerKg: '0.3333' }, { ...exact, foreignUnitCostRatio: { numerator: '1', denominator: '0' } },
      { ...exact, foreignAmountRatio: null } ]) expect(decodeOrderCost(availableOrderCost({ currencyLines: [malformed] }), ORDER)).toBeNull()
  })
  it('decodes the unavailable answer with its reason and no figure', () => {
    const cost = decodeOrderCost(orderCost(), ORDER)

    expect(cost).not.toBeNull()
    expect(cost?.status).toBe('unavailable')
    expect(cost?.reasonCode).toBe('cost_source_unavailable')
    expect(cost?.costPerKgTl).toBeNull()
    expect(cost?.lines).toEqual([])
  })

  it('decodes an available cost and keeps every figure as the server text', () => {
    const cost = decodeOrderCost(availableOrderCost(), ORDER)

    expect(cost).not.toBeNull()
    expect(cost?.costPerKgTl).toBe('12.3457')
    expect(cost?.rawMaterialCostPerKgTl).toBe('12.1')
    expect(cost?.inputTotalTl).toBe('1000')
    expect(cost?.distributedTotalTl).toBe('1000')
    expect(cost?.remainderTl).toBe('-0.01')
    expect(cost?.remainderLabelId).toBe(COST_LABEL_1)
    expect(cost?.lines.map((line) => line.amountTl)).toEqual(['740.74', '259.26', null])
    expect(cost?.lines.map((line) => line.remainderTl)).toEqual(['-0.01', '0', '0'])
    expect(cost?.currencyLines[0]?.exchangeRate).toBe('35.25')
    expect(cost?.noteCodes).toEqual(['paint_cost_not_included'])
  })

  it('decodes a pending answer of an open order with its reason (SAT-D317 (1))', () => {
    const cost = decodeOrderCost(orderCost({ status: 'pending', reasonCode: 'order_not_closed' }), ORDER)

    expect(cost?.status).toBe('pending')
    expect(cost?.reasonCode).toBe('order_not_closed')
  })

  it('decodes an unreadable answer with the issues that keep the cost from being computed', () => {
    const cost = decodeOrderCost(
      orderCost({
        status: 'unreadable',
        reasonCode: 'input_cost_zero',
        issueReasons: [{ issueMovementId: ISSUE, reasonCode: 'input_cost_zero' }],
      }),
      ORDER,
    )

    expect(cost?.issueReasons).toEqual([{ issueMovementId: ISSUE, reasonCode: 'input_cost_zero' }])
  })

  it('accepts an available cost without a remainder that names no line', () => {
    const body = availableOrderCost({
      remainderTl: '0',
      remainderLabelId: null,
      lines: [costLine(COST_LABEL_1, { kilograms: '60', amountTl: '740.74' }), costLine(COST_LABEL_2)],
    })

    expect(decodeOrderCost(body, ORDER)?.remainderLabelId).toBeNull()
  })

  it.each([
    ['another order', () => orderCost({ orderId: OTHER_ORDER })],
    ['another contract version', () => orderCost({ contractVersion: 2 })],
    ['an unknown status', () => orderCost({ status: 'estimated' })],
    ['an extra key', () => ({ ...orderCost(), costPerKg: null })],
    ['a missing key', () => withoutKey(orderCost(), 'noteCodes')],
    ['an order version of zero', () => orderCost({ orderVersion: 0 })],
    ['a figure as a JSON number', () => availableOrderCost({ costPerKgTl: 12.3457 })],
    ['a figure with grouping', () => availableOrderCost({ inputTotalTl: '1,000', distributedTotalTl: '1,000' })],
    ['a trailing fractional zero', () => availableOrderCost({ rawMaterialCostPerKgTl: '12.10' })],
    ['a negative cost per kg', () => availableOrderCost({ costPerKgTl: '-12.3457' })],
    ['a remainder of -0', () => availableOrderCost({ remainderTl: '-0', remainderLabelId: null })],
    ['a zero figure beside a reason', () => orderCost({ costPerKgTl: '0' })],
    ['lines on an unavailable answer', () => orderCost({ lines: [costLine(COST_LABEL_2)] })],
    ['an unavailable answer without a reason', () => orderCost({ reasonCode: null })],
    ['an available answer with a reason', () => availableOrderCost({ reasonCode: 'order_not_closed' })],
    ['an available answer missing a figure', () => availableOrderCost({ costPerKgTl: null })],
    ['an available answer with issue reasons', () =>
      availableOrderCost({ issueReasons: [{ issueMovementId: ISSUE, reasonCode: 'input_cost_zero' }] })],
    ['distributed differing from input', () => availableOrderCost({ distributedTotalTl: '999.99' })],
    ['a remainder naming no line', () => availableOrderCost({ remainderLabelId: null })],
    ['a remainder naming another line', () => availableOrderCost({ remainderLabelId: COST_LABEL_2 })],
    ['a zero remainder naming a line', () =>
      availableOrderCost({
        remainderTl: '0',
        lines: [costLine(COST_LABEL_1, { kilograms: '60', amountTl: '740.74' }), costLine(COST_LABEL_2)],
      })],
    ['a remainder on two lines', () =>
      availableOrderCost({
        lines: [
          costLine(COST_LABEL_1, { kilograms: '60', amountTl: '740.74', remainderTl: '-0.01' }),
          costLine(COST_LABEL_2, { remainderTl: '-0.01' }),
        ],
      })],
    ['a duplicate label', () =>
      availableOrderCost({
        remainderTl: '0',
        remainderLabelId: null,
        lines: [costLine(COST_LABEL_2), costLine(COST_LABEL_2)],
      })],
    ['an amount without its unit cost', () =>
      availableOrderCost({
        remainderTl: '0',
        remainderLabelId: null,
        lines: [costLine(COST_LABEL_2, { unitCostPerKgTl: null })],
      })],
    ['a line not allocated without a reason', () =>
      availableOrderCost({
        remainderTl: '0',
        remainderLabelId: null,
        lines: [costLine(COST_LABEL_2, { unitCostPerKgTl: null, amountTl: null })],
      })],
    ['a nil order id', () => orderCost({ orderId: '00000000-0000-0000-0000-000000000000' })],
    ['an output class that is not an enum name', () =>
      availableOrderCost({
        remainderTl: '0',
        remainderLabelId: null,
        lines: [costLine(COST_LABEL_2, { outputClass: 'good product' })],
      })],
    ['a note that is not a code', () => orderCost({ noteCodes: ['Boya yok'] })],
  ])('rejects %s', (_case, body) => {
    expect(decodeOrderCost(body(), ORDER)).toBeNull()
  })

  it('rejects a body that is not an object', () => {
    expect(decodeOrderCost(null, ORDER)).toBeNull()
    expect(decodeOrderCost([], ORDER)).toBeNull()
    expect(decodeOrderCost('cost', ORDER)).toBeNull()
  })
})

/**
 * WP11 (DALGA_PLANI_v2 1.12) "bu sayıyı açıkla" — component/DOM tests through the real shell, gate and fence over synthetic
 * fixtures (URT-D75 (4) golden figures). NOT real-API, real-SQL or browser acceptance.
 *
 * The explanation is read only when "Açıkla" is opened; its last step is the figure on screen (the decoder refuses anything
 * else); a 403 is the forbidden state, never an empty trace.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, within } from '@testing-library/react'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import { costTrace } from './costTraceFixture'
import { availableOrderCost } from './orderCostFixture'
import { decodeMassBalanceExplanation, decodeProductionCostExplanation } from './calculationExplanation'
import { ORDER, ROLL_1, ROLL_2, installFetch, json, massBalance, order, problem, renderAt, requestsFor, rollBalance } from './testHarness'

const PATH = `/uretim/emir/${ORDER}`
const COST_CAPABILITIES = [MANUFACTURING_CAPABILITY.orderDetail, MANUFACTURING_CAPABILITY.orderCostTrace]
const ISSUE = 'b0000000-0000-4000-8000-0000000d75a4'
const SCRAP = 'a3000000-0000-4000-8000-0000000d75a4'

const step = (code: string, operation: string, operands: string[], value: string, unit: string, scale: number | null = null) =>
  ({ code, operation, operands, value, unit, scale })

/** URT-D75 (4): 1000 kg × 40 TL; 900 good, 50 remnant, 50 scrap × 10 TL; (40000 − 2000 − 500) / 900. */
function costExplanation(overrides: Record<string, unknown> = {}) {
  return {
    contractVersion: 1, orderId: ORDER, orderVersion: 3, status: 'available', reasonCode: null,
    costBasis: 'production-input-average-plus-scrap', calculatorVersion: 'production-receipt-cost/1',
    displayedCostPerKgTl: '41.6667',
    steps: [
      step('net_input_cost', 'sum', ['issue_amounts'], '40000', 'TL'),
      step('consumed_kg', 'sum', ['issue_amounts'], '1000', 'kg'),
      step('raw_material_cost_per_kg', 'divide', ['net_input_cost', 'consumed_kg'], '40', 'TL/kg'),
      step('usable_remnant_kg', 'input', [], '50', 'kg'),
      step('usable_remnant_value', 'multiply', ['usable_remnant_kg', 'raw_material_cost_per_kg'], '2000', 'TL'),
      step('weighed_scrap_value', 'sum', [], '500', 'TL'),
      step('good_product_kg', 'input', [], '900', 'kg'),
      step('second_quality_kg', 'input', [], '0', 'kg'),
      step('produced_kg', 'add', ['good_product_kg', 'second_quality_kg'], '900', 'kg'),
      step('cost_numerator', 'subtract', ['net_input_cost', 'usable_remnant_value', 'weighed_scrap_value'], '37500', 'TL'),
      step('cost_per_kg_exact', 'divide', ['cost_numerator', 'produced_kg'], '41.666666666666666666666666667', 'TL/kg'),
      step('cost_per_kg_recorded', 'round_half_away_from_zero', ['cost_per_kg_exact'], '41.6667', 'TL/kg', 4),
    ],
    issues: [{ issueMovementId: ISSUE, issuedKg: '1000', returnedKg: '0', keptKg: '1000', tlUnitCostPerKg: '40', amountTl: '40000' }],
    scrapWeighings: [{ scrapOutputId: SCRAP, kilograms: '50', tlPerKg: '10', valueTl: '500' }],
    currencyLines: [], ruleReferences: ['URT-D75 (4) golden'],
    ...overrides,
  }
}

function balanceExplanation() {
  const roll = (parentRollId: string) => ({
    parentRollId, displayedDifferenceKg: '0', issueIds: [], returnIds: [], outputIds: [],
    steps: [
      step('issued_kg', 'sum', [], '0', 'kg'), step('returned_kg', 'sum', [], '0', 'kg'),
      step('actual_input_kg', 'subtract', ['issued_kg', 'returned_kg'], '0', 'kg'),
      step('accounted_kg', 'add', [], '0', 'kg'),
      step('difference_kg', 'subtract', ['actual_input_kg', 'accounted_kg'], '0', 'kg'),
    ],
  })
  return { contractVersion: 1, orderId: ORDER, version: 1, formulaVersion: 'GDC-BAL/1 (master v3 §28.9)',
    rolls: [roll(ROLL_1), roll(ROLL_2)], ruleReferences: ['GDC-BAL/1 BAL-003 (master v3 §28.9)'] }
}

function stub(explanation: () => Response) {
  return installFetch({
    capabilities: COST_CAPABILITIES,
    respond: (url) => {
      if (!url.includes(`/manufacturing/orders/${ORDER}`)) return undefined
      if (url.endsWith('/mass-balance/explanation')) return json(balanceExplanation())
      if (url.endsWith('/production-cost/explanation')) return explanation()
      if (url.endsWith('/mass-balance')) return json(massBalance({ rolls: [rollBalance(ROLL_1), rollBalance(ROLL_2)] }))
      if (url.endsWith('/cost-trace')) return json(costTrace())
      if (url.endsWith('/production-cost')) return json(availableOrderCost({ costPerKgTl: '41.6667' }))
      return json(order())
    },
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('WP11 production cost explanation', () => {
  it('reads nothing until opened, then shows the server steps ending on the figure on screen', async () => {
    const recorded = stub(() => json(costExplanation()))
    renderAt(PATH)
    const panel = await screen.findByRole('region', { name: 'Emir maliyeti' })
    const shown = await within(panel).findByTestId('order-cost-per-kg')
    expect(requestsFor(recorded, `/manufacturing/orders/${ORDER}/production-cost/explanation`, 'GET')).toHaveLength(0)

    fireEvent.click(within(panel).getByTestId('order-cost-explain-toggle'))

    const steps = await within(panel).findByTestId('order-cost-explain-steps')
    expect(within(steps).getByTestId('order-cost-explain-steps-cost_per_kg_recorded').textContent).toBe(shown.textContent)
    expect(within(steps).getByTestId('order-cost-explain-steps-net_input_cost').textContent).toBe('40000')
    expect(within(panel).getByTestId('order-cost-explain-issues').textContent).toContain(ISSUE)
    expect(within(panel).getByTestId('order-cost-explain-rules').textContent).toContain('URT-D75 (4) golden')
    expect(requestsFor(recorded, `/manufacturing/orders/${ORDER}/production-cost/explanation`, 'GET')).toHaveLength(1)
  })

  it('shows the forbidden state on 403, never an empty trace', async () => {
    stub(() => problem(403))
    renderAt(PATH)
    const panel = await screen.findByRole('region', { name: 'Emir maliyeti' })
    await within(panel).findByTestId('order-cost-per-kg')

    fireEvent.click(within(panel).getByTestId('order-cost-explain-toggle'))

    const opened = await within(panel).findByTestId('order-cost-explain-panel')
    expect(within(opened).queryByTestId('order-cost-explain-steps')).toBeNull()
  })
})

describe('WP11 explanation decoders', () => {
  it('refuses a cost explanation whose last step is not the shown figure or whose operand is not an earlier step', () => {
    expect(decodeProductionCostExplanation(costExplanation(), ORDER)).not.toBeNull()
    expect(decodeProductionCostExplanation(costExplanation({ displayedCostPerKgTl: '41.6668' }), ORDER)).toBeNull()
    const steps = costExplanation().steps
    expect(decodeProductionCostExplanation(costExplanation({ steps: [steps[2], ...steps.slice(0, 2), ...steps.slice(3)] }), ORDER)).toBeNull()
    expect(decodeProductionCostExplanation(costExplanation({ extra: 1 }), ORDER)).toBeNull()
    expect(decodeProductionCostExplanation(costExplanation(), 'd0000000-0000-4000-8000-000000000009')).toBeNull()
  })

  it('accepts a cost explanation without figures only with its reason and no step', () => {
    const empty = costExplanation({ status: 'unavailable', reasonCode: 'cost_source_unavailable', costBasis: null,
      displayedCostPerKgTl: null, steps: [], issues: [], scrapWeighings: [] })
    expect(decodeProductionCostExplanation(empty, ORDER)?.status).toBe('unavailable')
    expect(decodeProductionCostExplanation({ ...empty, steps: costExplanation().steps }, ORDER)).toBeNull()
  })

  it('refuses a mass balance explanation whose last step is not the shown difference', () => {
    expect(decodeMassBalanceExplanation(balanceExplanation(), ORDER)?.rolls).toHaveLength(2)
    const broken = balanceExplanation()
    expect(decodeMassBalanceExplanation({ ...broken, rolls: [{ ...broken.rolls[0], displayedDifferenceKg: '1' }] }, ORDER)).toBeNull()
  })
})

describe('WP11 mass balance explanation', () => {
  it('opens beside a roll difference and ends on the difference shown', async () => {
    stub(() => json(costExplanation()))
    renderAt(PATH)
    const panel = await screen.findByRole('region', { name: 'Kütle dengesi (GDC-BAL/1)' })
    const toggle = await within(panel).findByTestId(`mass-balance-explain-${ROLL_1}-toggle`)

    fireEvent.click(toggle)

    const steps = await within(panel).findByTestId(`mass-balance-explain-${ROLL_1}-steps`)
    expect(within(steps).getByTestId(`mass-balance-explain-${ROLL_1}-steps-difference_kg`).textContent).toBe('0')
  })
})

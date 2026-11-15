/**
 * Test-only synthetic `ProductionCostViewV1` bodies in the server's JSON shape (URT-S17). Component data, never real
 * fixture evidence.
 */

import { ORDER } from './testHarness'

export const COST_LABEL_1 = 'f0000000-0000-4000-8000-000000000001'
export const COST_LABEL_2 = 'f0000000-0000-4000-8000-000000000002'
export const COST_SCRAP_LABEL = 'f0000000-0000-4000-8000-000000000003'
export const COST_CURRENCY = 'c0000000-0000-4000-8000-000000000001'
export const COST_ISSUE = 'b0000000-0000-4000-8000-000000000001'

/** The answer while no issue-cost source is bound: unavailable, no figure. */
export function orderCost(overrides: Record<string, unknown> = {}) {
  return {
    contractVersion: 1,
    orderId: ORDER,
    orderStatus: 'Open',
    orderVersion: 1,
    status: 'unavailable',
    costBasis: null,
    costPerKgTl: null,
    rawMaterialCostPerKgTl: null,
    inputTotalTl: null,
    weighedScrapExpenseTl: null,
    distributedTotalTl: null,
    remainderTl: null,
    remainderLabelId: null,
    lines: [],
    currencyLines: [],
    issueReasons: [],
    noteCodes: [],
    reasonCode: 'cost_source_unavailable',
    ...overrides,
  }
}

export function costLine(outputLabelId: string, overrides: Record<string, unknown> = {}) {
  return {
    outputLabelId,
    outputClass: 'GoodProduct',
    kilograms: '21',
    unitCostPerKgTl: '12.3457',
    amountTl: '259.26',
    separateScrapExpenseTl: null,
    remainderTl: '0',
    allocationBasis: 'allocated-at-product-cost-per-kg',
    notAllocatedCode: null,
    ...overrides,
  }
}

/** A closed order's recorded cost: 740.74 + 259.26 = 1000 distributed, remainder -0.01 on the 60 kg line. */
export function availableOrderCost(overrides: Record<string, unknown> = {}) {
  return orderCost({
    orderStatus: 'Closed',
    orderVersion: 3,
    status: 'available',
    costBasis: 'production-input-average-plus-scrap',
    costPerKgTl: '12.3457',
    rawMaterialCostPerKgTl: '12.1',
    inputTotalTl: '1000',
    weighedScrapExpenseTl: '0',
    distributedTotalTl: '1000',
    remainderTl: '-0.01',
    remainderLabelId: COST_LABEL_1,
    lines: [
      costLine(COST_LABEL_1, { kilograms: '60', amountTl: '740.74', remainderTl: '-0.01' }),
      costLine(COST_LABEL_2),
      costLine(COST_SCRAP_LABEL, {
        outputClass: 'Scrap',
        kilograms: '2.5',
        unitCostPerKgTl: null,
        amountTl: null,
        allocationBasis: null,
        notAllocatedCode: 'scrap_label_cost_not_decided',
      }),
    ],
    currencyLines: [
      {
        issueMovementId: COST_ISSUE,
        currencyId: COST_CURRENCY,
        foreignUnitCostPerKg: '0.35',
        exchangeRate: '35.25',
        tlUnitCostPerKg: '12.3375',
        keptKilograms: '81',
        foreignAmount: '28.35',
      },
    ],
    noteCodes: ['paint_cost_not_included'],
    reasonCode: null,
    ...overrides,
  })
}

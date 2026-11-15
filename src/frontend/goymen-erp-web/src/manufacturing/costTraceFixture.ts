/**
 * Test-only synthetic `CostTraceViewV1` bodies in the server's JSON shape (URT-S19). Component data, never real
 * fixture evidence.
 */

import { ORDER, PLAN, ROLL_1, ROLL_2, WORK_CENTER } from './testHarness'

export function costTraceLot(parentRollId: string, overrides: Record<string, unknown> = {}) {
  return {
    parentRollId,
    plannedKg: '1000',
    issuedKg: '0',
    returnedKg: '0',
    consumedKg: '0',
    issueCount: 0,
    returnCount: 0,
    firstIssuedAtUtc: null,
    lastIssuedAtUtc: null,
    commercialPartyId: null,
    commercialPartyAccountId: null,
    ...overrides,
  }
}

export function unavailableCost(reasonCode = 'cost_source_unavailable') {
  return {
    status: 'unavailable',
    costBasis: null,
    costPerKgTl: null,
    rawMaterialCostPerKgTl: null,
    weighedScrapExpenseTl: null,
    noteCodes: [],
    reasonCode,
  }
}

export function availableCost(overrides: Record<string, unknown> = {}) {
  return {
    status: 'available',
    costBasis: 'production-input-average-plus-scrap',
    costPerKgTl: '31.4159',
    rawMaterialCostPerKgTl: '30.5',
    weighedScrapExpenseTl: '120.75',
    noteCodes: ['paint_cost_not_included'],
    reasonCode: null,
    ...overrides,
  }
}

export function costTrace(overrides: Record<string, unknown> = {}) {
  return {
    contractVersion: 1,
    methodCode: 'weighted-average-raw-material-kg',
    orderId: ORDER,
    planId: PLAN,
    workCenterId: WORK_CENTER,
    process: 'Slitting',
    status: 'Open',
    version: 1,
    lots: [
      costTraceLot(ROLL_1, {
        issuedKg: '600.5',
        consumedKg: '600.5',
        issueCount: 1,
        firstIssuedAtUtc: '2026-09-18T09:30:00+00:00',
        lastIssuedAtUtc: '2026-09-18T09:30:00+00:00',
      }),
      costTraceLot(ROLL_2),
    ],
    totalIssuedKg: '600.5',
    totalReturnedKg: '0',
    totalConsumedKg: '600.5',
    outputs: [{ outputClass: 'GoodProduct', netKg: '590', declarationCount: 1, reversalCount: 0 }],
    timeline: {
      firstFactAtUtc: '2026-09-18T09:30:00+00:00',
      lastFactAtUtc: '2026-09-18T09:31:00+00:00',
      closedAtUtc: null,
      stoppageCode: 'time_records_not_recorded',
      durationCode: 'time_records_not_recorded',
    },
    salesOrderLink: null,
    cost: unavailableCost(),
    ...overrides,
  }
}

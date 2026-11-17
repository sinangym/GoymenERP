import { describe, expect, it } from 'vitest'
import { decodeAdditionalCost, decodeAdditionalCostSource, decodeAdditionalCostReferences, decodeAdditionalCostReceipt, decodeAdditionalCosts } from './additionalCostContract'
import { expenseFact, expenseId as id, expenseReferences, expenseSource } from './additionalCostFixtures.testSupport'
describe('ALS-D23/D27/D107 expense wire sources', () => {
  it('keeps untyped historical descriptions and authoritative amounts without computing totals', () => {
    const fact = expenseFact()
    expect(decodeAdditionalCost(fact, id(1), 'GoodsOrder', id(2))).toEqual(fact)
    expect(decodeAdditionalCost({ ...fact, grossAmount: '120.02' }, id(1), 'GoodsOrder', id(2))?.grossAmount).toBe('120.02')
    expect(decodeAdditionalCost({ ...fact, expenseTypeId: id(8) }, id(1), 'GoodsOrder', id(2))).toBeNull()
    expect(decodeAdditionalCosts([fact, fact], id(1), 'GoodsOrder', id(2))).toBeNull()
  })
  it('rejects foreign company, source kind, missing source version and extra physical fields', () => {
    const fact = expenseFact()
    expect(decodeAdditionalCost(fact, id(99), 'GoodsOrder', id(2))).toBeNull()
    expect(decodeAdditionalCost(fact, id(1), 'ServiceOrder', id(2))).toBeNull()
    expect(decodeAdditionalCost({ ...fact, quantity: '1' }, id(1), 'GoodsOrder', id(2))).toBeNull()
    expect(decodeAdditionalCostSource({ ...expenseSource(), orderVersion: '0' }, id(1), 'GoodsOrder', id(2))).toBeNull()
    expect(decodeAdditionalCostSource({ ...expenseSource(), acceptances: [{ acceptanceId: id(7), lineId: id(8), quantity: '0', recordedAtUtc: '2026-11-17T00:00:00Z' }] }, id(1), 'GoodsOrder', id(2))).toBeNull()
  })
  it('distinguishes an actual empty type catalog from a malformed required-type catalog or stale date', () => {
    const refs = expenseReferences()
    expect(decodeAdditionalCostReferences(refs, id(1), refs.businessDate)).toEqual(refs)
    expect(decodeAdditionalCostReferences({ ...refs, typeRequired: true }, id(1), refs.businessDate)).toBeNull()
    expect(decodeAdditionalCostReferences({ ...refs, vatRates: [refs.vatRates[0], refs.vatRates[0]] }, id(1), refs.businessDate)).toBeNull()
    expect(decodeAdditionalCostReferences(refs, id(1), '2026-11-18')).toBeNull()
  })
  it('requires the exact receipt identity and replay agreement', () => {
    const wire = { cost: expenseFact(), isReplay: true, originalCorrelationId: 'SENTETIK-original' }
    expect(decodeAdditionalCostReceipt(wire, id(1), 'GoodsOrder', id(2), id(4), true)).not.toBeNull()
    expect(decodeAdditionalCostReceipt(wire, id(1), 'GoodsOrder', id(2), id(4), false)).toBeNull()
    expect(decodeAdditionalCostReceipt(wire, id(1), 'GoodsOrder', id(2), id(99), true)).toBeNull()
  })
})

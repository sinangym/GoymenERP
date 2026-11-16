import { describe, expect, it } from 'vitest'
import { decodeCostAttempt, decodeCostGeneration, decodeCostHistory } from './costGenerationApi'
const company = 'a0000000-0000-0000-0000-00000000000a'; const order = 'b0000000-0000-0000-0000-00000000000b'
const generation = 'c0000000-0000-0000-0000-00000000000c'
const row = () => ({ companyId: company, orderId: order, generationId: generation, closedOrderVersion: 4, revision: 1,
  inventoryVersion: '1', state: 'Unreadable', inputFingerprint: 'a'.repeat(64), calculatorVersion: 'rounding:v1',
  recordedAtUtc: '2026-11-17T01:00:00Z', reason: 'Synthetic source', correlationId: 'synthetic-correlation', costDifferenceEventId: null,
  outputs: [{ outputId: generation, labelId: generation, netKg: '100', remainingKg: '70', consumedKg: '30', originalTlAmount: null,
    remainingTlValue: null, consumedTlDifference: null, usdUnitCostPerKg: null }] })
describe('manufacturing immutable cost generations', () => {
  it('preserves exact server decimal text and rejects fictional money on an unreadable result', () => {
    const value = row(); expect(decodeCostGeneration(value, company, order)).not.toBeNull()
    expect(decodeCostGeneration({ ...value, outputs: [{ ...value.outputs[0], remainingTlValue: '0' }] }, company, order)).toBeNull()
    const actual = { ...value, state: 'Recognized', outputs: [{ ...value.outputs[0], originalTlAmount: '999999999999999.12345678' }] }
    expect(decodeCostGeneration(actual, company, order)?.outputs[0].originalTlAmount).toBe('999999999999999.12345678')
    expect(decodeCostGeneration(value, order, order)).toBeNull()
  })
  it('rejects duplicated or reversed revisions and freezes a validated replay body', () => {
    const value = row(); expect(decodeCostHistory({ orderId: order, orderVersion: 4, generations: [value, value] }, company, order)).toBeNull()
    expect(decodeCostAttempt({ idempotencyKey: generation, expectedVersion: 4, expectedGenerationRevision: 1, reason: 'Synthetic source' })).not.toBeNull()
    expect(decodeCostAttempt({ idempotencyKey: generation, expectedVersion: 4, expectedGenerationRevision: -1, reason: 'Synthetic source' })).toBeNull()
  })
})

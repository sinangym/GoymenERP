/**
 * `OrderViewV1` decoding and the fact body. Unit tests on synthetic JSON: a claim about
 * this decoder, not about the real API.
 */

import { describe, expect, it } from 'vitest'
import { decodeOrder, factBody, isKilogramText, type FactSnapshot } from './contract'
import { ROLL_1, order, roll } from './testHarness'

describe('decodeOrder', () => {
  it('keeps every kilogram as the server wrote it, the signed difference included', () => {
    const decoded = decodeOrder(
      order({ rolls: [roll(ROLL_1, { issuedKg: '1000.125', unexplainedKg: '-2.5', isFullyAccounted: false })] }),
    )
    expect(decoded?.rolls[0]).toMatchObject({ issuedKg: '1000.125', unexplainedKg: '-2.5', isFullyAccounted: false })
    expect(decoded?.costStatus).toBe('NotCalculated')
    expect(decoded?.remainingTarget.basis).toBeNull()
  })

  it.each([
    ['a number where text is required', order({ rolls: [roll(ROLL_1, { issuedKg: 10 })] })],
    ['a negative issued mass', order({ rolls: [roll(ROLL_1, { issuedKg: '-1' })] })],
    ['a padded mass', order({ rolls: [roll(ROLL_1, { issuedKg: '10.50' })] })],
    ['a missing output class', order({ rolls: [roll(ROLL_1, { outputsKg: { GoodProduct: '0' } })] })],
    [
      'an unknown output class',
      order({
        rolls: [
          roll(ROLL_1, {
            outputsKg: { GoodProduct: '0', SecondQuality: '0', UsableRemnant: '0', Scrap: '0', ProcessLoss: '0', Fire: '0' },
          }),
        ],
      }),
    ],
    ['a cost amount instead of a status', order({ costStatus: '0' })],
    ['an unknown status', order({ status: 'Draft' })],
    ['a zero version', order({ version: 0 })],
    ['a fractional version', order({ version: 1.5 })],
    ['a half-filled unavailable target', order({ remainingTarget: { basis: null, targetKg: '0', achievedKg: null, remainingKg: null } })],
    ['a non-guid order id', order({ orderId: 'x' })],
  ])('fails closed on %s', (_, body) => {
    expect(decodeOrder(body)).toBeNull()
  })

  it('reads a declared target and a remaining that went past it', () => {
    const decoded = decodeOrder(
      order({
        target: { basis: 'GoodOutputMass', kg: '900' },
        remainingTarget: { basis: 'GoodOutputMass', targetKg: '900', achievedKg: '950', remainingKg: '-50' },
      }),
    )
    expect(decoded?.target).toEqual({ basis: 'GoodOutputMass', kg: '900' })
    expect(decoded?.remainingTarget.remainingKg).toBe('-50')
  })
})

describe('isKilogramText (the server grammar, not the scale)', () => {
  it.each(['0', '10', '10.5', '1000.125', '0.0001'])('accepts %s', (text) => {
    expect(isKilogramText(text)).toBe(true)
  })

  it.each(['', ' 10', '10 ', '+10', '-1', '1e3', '1,5', '10.', '.5', '0010'])('refuses %j', (text) => {
    expect(isKilogramText(text)).toBe(false)
  })
})

describe('factBody', () => {
  const base: FactSnapshot = {
    kind: 'input-issues',
    factId: 'f0000000-0000-4000-8000-000000000001',
    idempotencyKey: 'k',
    expectedVersion: 3,
    parentRollId: ROLL_1,
    kg: '10.5',
    occurredAtUtc: '2026-09-18T09:00:00.000Z',
    outputClass: null,
  }

  it('names exactly the request fields: no company, no key, kilograms as text', () => {
    expect(factBody(base)).toEqual({
      factId: base.factId,
      expectedVersion: 3,
      parentRollId: ROLL_1,
      kg: '10.5',
      occurredAtUtc: '2026-09-18T09:00:00.000Z',
    })
  })

  it('adds the output class only to an output', () => {
    expect(factBody({ ...base, kind: 'outputs', outputClass: 'Scrap' })).toMatchObject({ outputClass: 'Scrap' })
    expect(factBody({ ...base, kind: 'wip-measurements', outputClass: 'Scrap' })).not.toHaveProperty('outputClass')
  })
})

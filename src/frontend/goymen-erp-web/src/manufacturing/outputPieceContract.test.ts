/**
 * AK-7 (URT-D112): the counted output view decoder (`OutputPieceViewV1`), the piece-count grammar of the two forms, the
 * request bodies that send pieces only when given, and the Turkish refusal texts of the piece rules.
 */
import { describe, expect, it } from 'vitest'
import { decodeOrder, decodeOutputPieces, factBody, outputCorrectionBody, parsePieceCount, type FactSnapshot } from './contract'
import { failureText } from './manufacturingText'
import { OUTPUT_PIECE_RULE_TEXT, signedText } from './outputPieceText'
import { ROLL_1, order } from './testHarness'

const OUTPUT = 'e0000000-0000-4000-8000-0000000000a7'

function view(overrides: Record<string, unknown> = {}) {
  return {
    outputId: OUTPUT, parentRollId: ROLL_1, outputClass: 'GoodProduct', declaredPieceCount: 48, netPieceCount: 44,
    netKg: '18', theoreticalKgPerPiece: '0.4', realKgPerPiece: '0.409', deviationKgPerPiece: '0.009',
    deviationPercent: '2.27', formulaVersion: 'manufacturing.output-piece-deviation.v1', deviationThresholdApplied: false,
    ...overrides,
  }
}

describe('counted output view decoder (AK-7)', () => {
  it('decodes the server figures as they came and treats an absent member as none', () => {
    expect(decodeOutputPieces([view()])).toEqual([view()])
    expect(decodeOutputPieces(undefined)).toEqual([])
    expect(decodeOutputPieces(null)).toEqual([])
    expect(decodeOrder(order())?.outputPieces).toEqual([])
    expect(decodeOrder(order({ outputPieces: [view()] }))?.outputPieces).toHaveLength(1)
  })

  it('accepts a fully corrected output with no deviation figures', () => {
    expect(decodeOutputPieces([view({ netPieceCount: 0, netKg: '0', realKgPerPiece: null, deviationKgPerPiece: null,
      deviationPercent: null })])).toHaveLength(1)
  })

  it.each([
    ['an extra member', { extra: 1 }],
    ['a threshold claimed applied', { deviationThresholdApplied: true }],
    ['a process loss', { outputClass: 'ProcessLoss' }],
    ['no declared piece', { declaredPieceCount: 0 }],
    ['more net than declared', { netPieceCount: 49 }],
    ['a fractional count', { declaredPieceCount: 4.5 }],
    ['a zero theoretical figure', { theoreticalKgPerPiece: '0' }],
    ['a formatted kg', { netKg: '18.0' }],
    ['a deviation without net pieces', { netPieceCount: 0 }],
    ['net pieces without a deviation', { deviationPercent: null }],
    ['a nil output id', { outputId: '00000000-0000-0000-0000-000000000000' }],
    ['no formula version', { formulaVersion: '' }],
  ])('refuses %s (fail-closed)', (_name, overrides) => {
    expect(decodeOutputPieces([view(overrides)])).toBeNull()
    expect(decodeOrder(order({ outputPieces: [view(overrides)] }))).toBeNull()
  })

  it('refuses a duplicated output and a non-list member', () => {
    expect(decodeOutputPieces([view(), view()])).toBeNull()
    expect(decodeOutputPieces({})).toBeNull()
  })
})

describe('piece count grammar and bodies (AK-7)', () => {
  it.each([
    ['', 1, null], ['  ', 1, null], ['48', 1, 48], [' 7 ', 1, 7], ['0', 0, 0],
    ['0', 1, undefined], ['-1', 0, undefined], ['2.5', 1, undefined], ['01', 1, undefined], ['1e3', 1, undefined],
    ['2147483648', 1, undefined],
  ] as const)('reads %j with minimum %d as %s', (text, minimum, expected) => {
    expect(parsePieceCount(text, minimum)).toBe(expected)
  })

  it('sends pieceCount on an output only when given', () => {
    const base = {
      kind: 'outputs', factId: OUTPUT, idempotencyKey: 'k', expectedVersion: 2, parentRollId: ROLL_1, kg: '20',
      occurredAtUtc: '2026-11-22T10:00:00.000Z', movementId: OUTPUT, expectedParentLabelVersion: '8',
      outputClass: 'GoodProduct', remnantClassification: null, productId: OUTPUT, expectedProductVersion: '12',
      lotId: OUTPUT, labelId: OUTPUT, labelCode: null, heatNumber: null, coilNumber: null, requiredDocuments: [],
      ownerKind: 'Company', ownerId: OUTPUT, custodianKind: 'Company', custodianId: OUTPUT, locationId: OUTPUT,
      initialBlock: 'Free',
    } as const satisfies FactSnapshot
    expect(factBody({ ...base, pieceCount: 48 })).toMatchObject({ pieceCount: 48 })
    expect(factBody(base)).not.toHaveProperty('pieceCount')
    expect(factBody({ ...base, pieceCount: null })).not.toHaveProperty('pieceCount')
  })

  it('sends pieceCount on a correction only when given, zero included', () => {
    const base = {
      idempotencyKey: 'k', outputId: OUTPUT, reversalId: OUTPUT, movementId: OUTPUT, expectedVersion: 3, labelId: OUTPUT,
      expectedLabelVersion: '7', kg: '5', reason: 'SENTETIK', occurredAtUtc: '2026-11-22T10:00:00.000Z',
    }
    expect(outputCorrectionBody({ ...base, pieceCount: 0 })).toMatchObject({ pieceCount: 0 })
    expect(outputCorrectionBody(base)).not.toHaveProperty('pieceCount')
  })
})

describe('piece refusal texts (AK-7)', () => {
  it.each(Object.keys(OUTPUT_PIECE_RULE_TEXT))('tells %s in Turkish without the code', (code) => {
    const text = failureText({ kind: 'rule', code })
    expect(text).toBe(OUTPUT_PIECE_RULE_TEXT[code])
    expect(text).not.toContain(code)
  })

  it('signs a heavier deviation and leaves a lighter or zero one as the server sent it', () => {
    expect(signedText('4.17')).toBe('+4.17')
    expect(signedText('-2.5')).toBe('-2.5')
    expect(signedText('0')).toBe('0')
  })
})

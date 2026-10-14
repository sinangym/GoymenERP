/**
 * `OrderViewV1` decoding and the fact body. Unit tests on synthetic JSON: a claim about
 * this decoder, not about the real API.
 */

import { describe, expect, it } from 'vitest'
import {
  closeBody,
  decodeCutToLengthSpec,
  decodeMassBalance,
  decodeManufacturingOutputLocationCandidates,
  decodeManufacturingOutputProductCandidates,
  decodeOrder,
  decodeOrderList,
  decodePlan,
  factBody,
  isKilogramText,
  type FactSnapshot,
} from './contract'
import {
  APPROVER,
  CUT_LINE_1,
  CUT_LINE_2,
  ORDER,
  PLAN,
  ROLL_1,
  massBalance,
  order,
  orderList,
  orderSummary,
  cutToLengthSpec,
  plan,
  planCalculation,
  roll,
  rollBalance,
  technicalSnapshot,
  unavailable,
} from './testHarness'

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

describe('decodeMassBalance (GDC-BAL/1)', () => {
  it("keeps BAL-003's signed difference as text and the engine's Unavailable with its input names", () => {
    const decoded = decodeMassBalance(
      massBalance({
        rolls: [
          rollBalance(ROLL_1, {
            difference: { actualInputKg: '1000.125', accountedKg: '1002.625', differenceKg: '-2.5', isFullyAccounted: false },
          }),
        ],
      }),
    )
    expect(decoded?.rolls[0].difference).toEqual({
      actualInputKg: '1000.125',
      accountedKg: '1002.625',
      differenceKg: '-2.5',
      isFullyAccounted: false,
    })
    expect(decoded?.rolls[0].scrapPercent).toEqual({
      outcome: 'Unavailable',
      value: null,
      missingParameters: ['fireKg', 'rawMaterialKg'],
      refusalCode: null,
      refusedParameter: null,
    })
    expect(decoded?.formulaVersion).toBe('GDC-BAL/1 (master v3 §28.9)')
  })

  it.each([
    ['a number where text is expected', { scrapPercent: { ...unavailable('fireKg'), value: 4 } }],
    ['a computed answer without its value', { scrapPercent: { outcome: 'Computed', value: null, missingParameters: [], refusalCode: null, refusedParameter: null } }],
    ['an unavailable answer that names no input', { actualUnitKg: unavailable() }],
    ['an unknown outcome', { actualUnitKg: { ...unavailable('actualCount'), outcome: 'Estimated' } }],
    ['a refused answer without its code', { scrapPercent: { outcome: 'Refused', value: null, missingParameters: [], refusalCode: null, refusedParameter: null } }],
    ['a padded difference', { difference: { actualInputKg: '10', accountedKg: '0', differenceKg: '10.50', isFullyAccounted: false } }],
    ['a difference without its flag', { difference: { actualInputKg: '10', accountedKg: '0', differenceKg: '10' } }],
  ])('fails closed on %s', (_label, overrides) => {
    expect(decodeMassBalance(massBalance({ rolls: [rollBalance(ROLL_1, overrides)] }))).toBeNull()
  })

  it('fails closed on a missing formula version or version', () => {
    expect(decodeMassBalance(massBalance({ formulaVersion: '' }))).toBeNull()
    expect(decodeMassBalance(massBalance({ version: 0 }))).toBeNull()
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
    movementId: 'f0000000-0000-4000-8000-000000000002',
    expectedParentLabelVersion: null,
    idempotencyKey: 'k',
    expectedVersion: 3,
    parentRollId: ROLL_1,
    kg: '10.5',
    occurredAtUtc: '2026-09-18T09:00:00.000Z',
  }

  it('names exactly the strict issue fields: no company, no key, kilograms as text', () => {
    expect(factBody(base)).toEqual({
      issueId: base.factId,
      expectedVersion: 3,
      parentRollId: ROLL_1,
      movementId: base.movementId,
      kg: '10.5',
      occurredAtUtc: '2026-09-18T09:00:00.000Z',
    })
  })

  it('freezes the fresh parent-label version only into a strict return', () => {
    expect(factBody({
      ...base,
      kind: 'input-returns',
      expectedParentLabelVersion: '8',
    })).toEqual({
      returnId: base.factId,
      expectedVersion: 3,
      parentRollId: ROLL_1,
      expectedParentLabelVersion: '8',
      movementId: base.movementId,
      kg: '10.5',
      occurredAtUtc: '2026-09-18T09:00:00.000Z',
    })
  })

  it('keeps WIP on RecordFactRequestV1 without an Inventory movement', () => {
    expect(factBody({
      ...base,
      kind: 'wip-measurements',
      movementId: null,
      expectedParentLabelVersion: null,
    })).toEqual({
      factId: base.factId,
      expectedVersion: 3,
      parentRollId: ROLL_1,
      kg: '10.5',
      occurredAtUtc: '2026-09-18T09:00:00.000Z',
    })
  })

  it('keeps process loss on RecordFactRequestV1 without an Inventory identity or movement', () => {
    expect(factBody({
      ...base,
      kind: 'process-losses',
      movementId: null,
      expectedParentLabelVersion: null,
    })).toEqual({
      factId: base.factId,
      expectedVersion: 3,
      parentRollId: ROLL_1,
      kg: '10.5',
      occurredAtUtc: '2026-09-18T09:00:00.000Z',
    })
  })

  it('freezes every explicit company-output identity and version into the strict output body', () => {
    expect(factBody({
      ...base,
      kind: 'outputs',
      expectedParentLabelVersion: '8',
      outputClass: 'GoodProduct',
      productId: 'a0000000-0000-4000-8000-000000000001',
      expectedProductVersion: '12',
      lotId: 'a0000000-0000-4000-8000-000000000002',
      labelId: 'a0000000-0000-4000-8000-000000000003',
      labelCode: 'OUT-1',
      heatNumber: null,
      coilNumber: 'C-1',
      requiredDocuments: ['CERT-1'],
      ownerKind: 'Company',
      ownerId: 'a0000000-0000-4000-8000-000000000004',
      custodianKind: 'Company',
      custodianId: 'a0000000-0000-4000-8000-000000000004',
      locationId: 'a0000000-0000-4000-8000-000000000005',
      initialBlock: 'Blocked',
    })).toEqual({
      outputId: base.factId,
      expectedVersion: 3,
      parentRollId: ROLL_1,
      expectedParentLabelVersion: '8',
      outputClass: 'GoodProduct',
      productId: 'a0000000-0000-4000-8000-000000000001',
      expectedProductVersion: '12',
      lotId: 'a0000000-0000-4000-8000-000000000002',
      labelId: 'a0000000-0000-4000-8000-000000000003',
      labelCode: 'OUT-1',
      heatNumber: null,
      coilNumber: 'C-1',
      requiredDocuments: ['CERT-1'],
      ownerKind: 'Company',
      ownerId: 'a0000000-0000-4000-8000-000000000004',
      custodianKind: 'Company',
      custodianId: 'a0000000-0000-4000-8000-000000000004',
      locationId: 'a0000000-0000-4000-8000-000000000005',
      initialBlock: 'Blocked',
      movementId: base.movementId,
      kg: '10.5',
      occurredAtUtc: '2026-09-18T09:00:00.000Z',
    })
  })
})

describe('output selector contracts', () => {
  const product = {
    productId: 'a0000000-0000-4000-8000-000000000001',
    productVersion: '12',
    code: 'PRD-12',
    name: 'Galvaniz bant',
    stockUnitCode: 'kg',
  }
  const location = {
    locationId: 'a0000000-0000-4000-8000-000000000002',
    locationCode: 'HOL-1',
    locationName: 'Üretim holü',
    siteId: 'a0000000-0000-4000-8000-000000000003',
    siteCode: 'OSM',
    siteName: 'Osmangazi',
  }

  it('accepts only exact bounded Product and Organization projections', () => {
    expect(decodeManufacturingOutputProductCandidates({ items: [product], searchWindowFull: false }))
      .toEqual({ items: [product], searchWindowFull: false })
    expect(decodeManufacturingOutputLocationCandidates({ items: [location], searchWindowFull: true }))
      .toEqual({ items: [location], searchWindowFull: true })
  })

  it('fails closed on duplicate ids, noncanonical versions and extra owner fields', () => {
    expect(decodeManufacturingOutputProductCandidates({ items: [product, product], searchWindowFull: false })).toBeNull()
    expect(decodeManufacturingOutputProductCandidates({
      items: [{ ...product, productVersion: '01' }], searchWindowFull: false,
    })).toBeNull()
    expect(decodeManufacturingOutputLocationCandidates({
      items: [{ ...location, companyId: 'a0000000-0000-4000-8000-000000000004' }], searchWindowFull: false,
    })).toBeNull()
  })
})

describe('decodeOrderList', () => {
  it('keeps the summary the server wrote, kilograms as text and the cursor opaque', () => {
    const decoded = decodeOrderList(
      orderList([orderSummary({ plannedInputKg: '1000.125', target: { basis: 'InputMass', kg: '900' } })], 'CURSOR-1'),
    )
    expect(decoded?.nextCursor).toBe('CURSOR-1')
    expect(decoded?.orders[0]).toMatchObject({
      orderId: ORDER,
      status: 'Open',
      plannedRollCount: 2,
      plannedInputKg: '1000.125',
      target: { basis: 'InputMass', kg: '900' },
      closedAtUtc: null,
    })
  })

  it('reads an empty page as an empty page, not as a missing answer', () => {
    expect(decodeOrderList(orderList([], null))).toEqual({ orders: [], nextCursor: null })
  })

  it.each([
    ['no orders array', { nextCursor: null }],
    ['a cursor that is neither text nor null', orderList([orderSummary()], 7 as unknown as string)],
    ['an empty cursor', orderList([orderSummary()], '')],
    ['an id that is not a guid', orderList([orderSummary({ orderId: 'nope' })])],
    ['a status the contract does not have', orderList([orderSummary({ status: 'Cancelled' })])],
    ['a kilogram that is not the server’s text', orderList([orderSummary({ plannedInputKg: '1,5' })])],
    ['a negative roll count', orderList([orderSummary({ plannedRollCount: -1 })])],
    ['a version of zero', orderList([orderSummary({ version: 0 })])],
    ['a closed order without its close time', orderList([orderSummary({ status: 'Closed' })])],
    ['an open order with a close time', orderList([orderSummary({ closedAtUtc: '2026-09-19T10:00:00.000Z' })])],
    ['a half-filled target', orderList([orderSummary({ target: { basis: 'InputMass' } })])],
  ])('fails closed on %s', (_case, body) => {
    expect(decodeOrderList(body)).toBeNull()
  })
})

describe('closeBody', () => {
  it('sends the version the screen read with an explicit absent exception reason', () => {
    expect(closeBody({ idempotencyKey: 'k', expectedVersion: 4 })).toEqual({
      expectedVersion: 4,
      exceptionReason: null,
    })
  })

  it('freezes the reason only for the dedicated exception attempt', () => {
    expect(closeBody({ idempotencyKey: 'k2', expectedVersion: 4, exceptionReason: 'Teknik inceleme tamamlandı.' }))
      .toEqual({ expectedVersion: 4, exceptionReason: 'Teknik inceleme tamamlandı.' })
  })
})

describe('decodePlan', () => {
  it('keeps the plan as the server wrote it: kilograms as text, the version as read', () => {
    const decoded = decodePlan(plan({ target: { basis: 'GoodOutputMass', kg: '900' } }))
    expect(decoded).toMatchObject({ planId: PLAN, status: 'Draft', version: 1 })
    expect(decoded?.inputs.map((input) => input.plannedKg)).toEqual(['1000.125', '500.5'])
    expect(decoded?.target).toEqual({ basis: 'GoodOutputMass', kg: '900' })
    expect(decoded?.approvedByUserId).toBeNull()
  })

  it('normalizes a legacy response that omits both additive technical projections', () => {
    const current = plan()
    const {
      calculation: _calculation,
      slittingSpec: _slittingSpec,
      cutToLengthSpec: _cutToLengthSpec,
      ...legacyPlan
    } = current
    const legacyInputs = current.inputs.map((input) => {
      const { technicalSnapshot: _technicalSnapshot, ...legacyInput } = input
      return legacyInput
    })

    const decoded = decodePlan({ ...legacyPlan, inputs: legacyInputs })

    expect(decoded?.calculation).toBeNull()
    expect(decoded?.slittingSpec).toBeNull()
    expect(decoded?.cutToLengthSpec).toBeNull()
    expect(decoded?.inputs.every((input) => input.technicalSnapshot === null)).toBe(true)
  })

  it('reads the current explicit null cut spec as the same absence as the legacy omission', () => {
    expect(plan()).toHaveProperty('cutToLengthSpec', null)
    expect(decodePlan(plan())?.cutToLengthSpec).toBeNull()
  })

  it('keeps stored cut lines losslessly: order, line id, signed sequence, canonical length and count', () => {
    const decoded = decodePlan(plan({ process: 'CutToLength', cutToLengthSpec: cutToLengthSpec() }))

    expect(decoded?.cutToLengthSpec).toEqual({
      lines: [
        { lineId: CUT_LINE_2, sequence: 20, lengthMm: '1250.5', count: 3 },
        { lineId: CUT_LINE_1, sequence: -1, lengthMm: '0.001', count: 2147483647 },
      ],
    })
    expect(decodeCutToLengthSpec(cutToLengthSpec({
      lines: [{ lineId: CUT_LINE_1, sequence: -2147483648, lengthMm: '7', count: 1 }],
    }))).toEqual({ lines: [{ lineId: CUT_LINE_1, sequence: -2147483648, lengthMm: '7', count: 1 }] })
  })

  const cutLine = (overrides: Record<string, unknown>) => ({
    lines: [{ lineId: CUT_LINE_1, sequence: 1, lengthMm: '1000', count: 2, ...overrides }],
  })

  it.each([
    ['a non-object spec', []],
    ['an extra spec member', { ...cutToLengthSpec(), windingTargetKg: null }],
    ['a missing line list', {}],
    ['a null line list', { lines: null }],
    ['an empty line list', { lines: [] }],
    ['a null line', { lines: [null] }],
    ['an extra line member', cutLine({ widthMm: '1000' })],
    ['a missing line member', { lines: [{ lineId: CUT_LINE_1, sequence: 1, lengthMm: '1000' }] }],
    ['a nil line id', cutLine({ lineId: '00000000-0000-0000-0000-000000000000' })],
    ['a malformed line id', cutLine({ lineId: 'line-1' })],
    ['a duplicate line id', { lines: [
      { lineId: CUT_LINE_1, sequence: 1, lengthMm: '1000', count: 2 },
      { lineId: CUT_LINE_1.toUpperCase(), sequence: 2, lengthMm: '900', count: 1 },
    ] }],
    ['a string sequence', cutLine({ sequence: '1' })],
    ['a fractional sequence', cutLine({ sequence: 1.5 })],
    ['an int32 overflow sequence', cutLine({ sequence: 2147483648 })],
    ['an int32 underflow sequence', cutLine({ sequence: -2147483649 })],
    ['a numeric length', cutLine({ lengthMm: 1000 })],
    ['a zero length', cutLine({ lengthMm: '0' })],
    ['a negative length', cutLine({ lengthMm: '-1' })],
    ['a fourth length decimal', cutLine({ lengthMm: '1000.0001' })],
    ['a non-canonical trailing zero length', cutLine({ lengthMm: '1000.50' })],
    ['a string count', cutLine({ count: '2' })],
    ['a zero count', cutLine({ count: 0 })],
    ['a negative count', cutLine({ count: -1 })],
    ['a fractional count', cutLine({ count: 1.5 })],
    ['an int32 overflow count', cutLine({ count: 2147483648 })],
  ])('fails closed on cut spec with %s', (_case, spec) => {
    expect(decodePlan(plan({ process: 'CutToLength', cutToLengthSpec: spec }))).toBeNull()
  })

  it('keeps the stored slitting lines in their exact order and accepts an optional winding target', () => {
    const decoded = decodePlan(plan({
      slittingSpec: {
        lines: [{ bandWidthMm: '0.001', count: 2147483647 }, { bandWidthMm: '240.5', count: 3 }],
        windingTargetKg: '2000',
      },
    }))

    expect(decoded?.slittingSpec).toEqual({
      lines: [{ bandWidthMm: '0.001', count: 2147483647 }, { bandWidthMm: '240.5', count: 3 }],
      windingTargetKg: '2000',
    })
  })

  it.each([
    ['an empty line list', { lines: [], windingTargetKg: null }],
    ['a zero width', { lines: [{ bandWidthMm: '0', count: 1 }], windingTargetKg: null }],
    ['a numeric width', { lines: [{ bandWidthMm: 150, count: 1 }], windingTargetKg: null }],
    ['a fourth width decimal', { lines: [{ bandWidthMm: '150.0001', count: 1 }], windingTargetKg: null }],
    ['a zero count', { lines: [{ bandWidthMm: '150', count: 0 }], windingTargetKg: null }],
    ['a fractional count', { lines: [{ bandWidthMm: '150', count: 1.5 }], windingTargetKg: null }],
    ['an int32 overflow count', { lines: [{ bandWidthMm: '150', count: 2147483648 }], windingTargetKg: null }],
    ['a zero winding target', { lines: [{ bandWidthMm: '150', count: 1 }], windingTargetKg: '0' }],
  ])('fails closed on slitting spec with %s', (_case, slittingSpec) => {
    expect(decodePlan(plan({ slittingSpec }))).toBeNull()
  })

  it('keeps the captured source and the matching backend calculation on a reopened plan', () => {
    const body = plan({
      inputs: [
        { parentRollId: ROLL_1, plannedKg: '1000.125', technicalSnapshot: technicalSnapshot() },
        { parentRollId: 'e0000000-0000-4000-8000-000000000002', plannedKg: '500.5', technicalSnapshot: null },
      ],
      calculation: planCalculation(),
    })

    const decoded = decodePlan(body)

    expect(decoded?.inputs[0].technicalSnapshot).toMatchObject({
      productVersion: '2', thicknessMm: '2', widthMm: '1500', effectiveOn: '2026-09-30',
    })
    expect(decoded?.inputs[1].technicalSnapshot).toBeNull()
    expect(decoded?.calculation?.calculatorVersion).toBe('GDC-PLAN-CALC/1')
  })

  it('accepts snapshot precision and provenance exactly at their domain boundaries', () => {
    const source = technicalSnapshot()
    const snapshot = technicalSnapshot({
      productVersion: 'p'.repeat(128),
      family: { ...source.family, familyCode: 'f'.repeat(64), sourceRevision: 'r'.repeat(64) },
      thicknessMm: '2.001',
      effectiveOn: '2024-02-29',
    })

    expect(decodePlan(plan({
      inputs: [{ parentRollId: ROLL_1, plannedKg: '1000.125', technicalSnapshot: snapshot }],
    }))?.inputs[0].technicalSnapshot).toMatchObject({ thicknessMm: '2.001', effectiveOn: '2024-02-29' })
  })

  it.each([
    ['a real fourth decimal', technicalSnapshot({ thicknessMm: '2.0001' })],
    ['a real fourth decimal in width', technicalSnapshot({ widthMm: '1500.0001' })],
    ['an impossible calendar date', technicalSnapshot({ effectiveOn: '2026-99-99' })],
    ['the domain default calendar date', technicalSnapshot({ effectiveOn: '0001-01-01' })],
    ['an oversized product version', technicalSnapshot({ productVersion: 'p'.repeat(129) })],
    ['an oversized domain code', technicalSnapshot({
      family: { ...technicalSnapshot().family, familyCode: 'f'.repeat(65) },
    })],
    ['a control character in provenance', technicalSnapshot({
      template: { ...technicalSnapshot().template, sourceRevision: 'template\u0001r3' },
    })],
  ])('fails closed on snapshot %s', (_, snapshot) => {
    expect(decodePlan(plan({
      inputs: [{ parentRollId: ROLL_1, plannedKg: '1000.125', technicalSnapshot: snapshot }],
    }))).toBeNull()
  })

  it('binds a stored calculation to the enclosing target and exact decimal input sum', () => {
    const target = { basis: 'GoodOutputMass', kg: '900' }
    const calculationTarget = {
      ...target,
      outcome: 'Unavailable',
      targetMinusPlannedInputKg: null,
      missingParameters: ['actualGoodOutputKg'],
    }
    expect(decodePlan(plan({ target, calculation: planCalculation({ target: calculationTarget }) }))).not.toBeNull()

    const exactInputs = [
      { parentRollId: ROLL_1, plannedKg: '0.1', technicalSnapshot: null },
      { parentRollId: 'e0000000-0000-4000-8000-000000000002', plannedKg: '0.2', technicalSnapshot: null },
    ]
    expect(decodePlan(plan({
      inputs: exactInputs,
      calculation: planCalculation({
        plannedInputKg: '0.3',
        rolls: exactInputs.map(({ parentRollId, plannedKg }) => ({ parentRollId, plannedKg, answers: [] })),
      }),
    }))).not.toBeNull()

    expect(decodePlan(plan({ calculation: planCalculation({ target: calculationTarget }) }))).toBeNull()
    expect(decodePlan(plan({ target, calculation: planCalculation({ target: null }) }))).toBeNull()
    expect(decodePlan(plan({
      target,
      calculation: planCalculation({ target: { ...calculationTarget, basis: 'InputMass' } }),
    }))).toBeNull()
    expect(decodePlan(plan({
      target,
      calculation: planCalculation({ target: { ...calculationTarget, kg: '901' } }),
    }))).toBeNull()
    expect(decodePlan(plan({ calculation: planCalculation({ plannedInputKg: '1500.624' }) }))).toBeNull()
  })

  it('fails closed when snapshot company or calculation roll identity drifts from the plan', () => {
    const foreignSnapshot = technicalSnapshot({
      unitKg: { ...technicalSnapshot().unitKg, companyId: 'bbbbbbbb-0000-0000-0000-000000000002' },
    })
    expect(decodePlan(plan({
      inputs: [{ parentRollId: ROLL_1, plannedKg: '1000.125', technicalSnapshot: foreignSnapshot }],
    }))).toBeNull()
    expect(decodePlan(plan({
      calculation: planCalculation({
        rolls: [
          { parentRollId: 'e0000000-0000-4000-8000-000000000099', plannedKg: '1000.125', answers: [] },
          { parentRollId: 'e0000000-0000-4000-8000-000000000002', plannedKg: '500.5', answers: [] },
        ],
      }),
    }))).toBeNull()
  })

  it('reads an approved plan as approved, with both halves of the approval', () => {
    const decoded = decodePlan(
      plan({
        status: 'Approved',
        version: 2,
        approvedByUserId: APPROVER,
        approvedAtUtc: '2026-09-18T09:00:00.000Z',
      }),
    )
    expect(decoded).toMatchObject({
      status: 'Approved',
      version: 2,
      approvedByUserId: APPROVER,
      approvedAtUtc: '2026-09-18T09:00:00.000Z',
    })
  })

  it('accepts a plan with no planned input: an empty list is a shape, not a failure', () => {
    // Fails-closed decoding must not turn "nothing planned yet" into "unreadable".
    expect(decodePlan(plan({ inputs: [] }))?.inputs).toEqual([])
  })

  it.each([
    ['a body that is not an object', 'nope'],
    ['a plan id that is not a guid', plan({ planId: 'nope' })],
    ['a company that is not a guid', plan({ companyId: 42 })],
    ['a status the contract does not have', plan({ status: 'Cancelled' })],
    ['a status of the ORDER contract, which is a different vocabulary', plan({ status: 'Open' })],
    ['a version of zero', plan({ version: 0 })],
    ['a fractional version', plan({ version: 1.5 })],
    ['inputs that are not a list', plan({ inputs: {} })],
    ['a planned kilogram as a number', plan({ inputs: [{ parentRollId: ROLL_1, plannedKg: 10 }] })],
    ['a planned kilogram in the local grammar', plan({ inputs: [{ parentRollId: ROLL_1, plannedKg: '1,5' }] })],
    ['a negative planned kilogram', plan({ inputs: [{ parentRollId: ROLL_1, plannedKg: '-1' }] })],
    ['a planned roll that is not a guid', plan({ inputs: [{ parentRollId: 'nope', plannedKg: '1' }] })],
    ['a half-filled target', plan({ target: { basis: 'GoodOutputMass' } })],
    // Status and both halves of the approval move together on the server (CK_Plans_Approval); any
    // cross-combination would display a contradictory approval state.
    ['an approved status with no approval pair', plan({ status: 'Approved', version: 2 })],
    [
      'a draft status with a complete approval pair',
      plan({ approvedByUserId: APPROVER, approvedAtUtc: '2026-09-18T09:00:00.000Z' }),
    ],
    ['an approver with no instant', plan({ status: 'Approved', approvedByUserId: APPROVER })],
    ['an instant with no approver', plan({ status: 'Approved', approvedAtUtc: '2026-09-18T09:00:00.000Z' })],
    ['an instant that is not a time', plan({ status: 'Approved', approvedByUserId: APPROVER, approvedAtUtc: 'dün' })],
  ])('fails closed on %s', (_case, body) => {
    expect(decodePlan(body)).toBeNull()
  })
})

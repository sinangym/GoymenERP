import { describe, expect, it } from 'vitest'
import {
  combineInventoryLabelDetail,
  decodeInventoryLabelBalances,
  decodeInventoryLabelGenealogy,
  decodeInventoryLabelIdentity,
} from './labelDetailContract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const PRODUCT = '33333333-3333-3333-3333-333333333333'
const LOT = '44444444-4444-4444-4444-444444444444'
const OWNER = '55555555-5555-5555-5555-555555555555'
const CUSTODIAN = '66666666-6666-6666-6666-666666666666'
const LOCATION = '77777777-7777-7777-7777-777777777777'
const SOURCE_LABEL = '88888888-8888-8888-8888-888888888888'
const TRANSFORMATION = '99999999-9999-9999-9999-999999999999'

function identity() {
  return {
    labelId: LABEL, companyId: COMPANY, code: 'LBL-100', productId: PRODUCT, lotId: LOT,
    heatNumber: null, coilNumber: 'COIL-9', documentStatus: 'Complete',
    requiredDocuments: ['MTC'],
    documents: [{ kind: 'MTC', attachedAtUtc: '2026-09-24T08:00:00Z' }],
    prints: [{ sequence: 1, printedAtUtc: '2026-09-24T08:01:00Z', reason: null }],
    lot: { id: LOT, heatNumber: 'HEAT-1', coilNumber: null },
  }
}

function balances() {
  return {
    labelId: LABEL, physicalKg: '1250.5', freeKg: '1200', blockedKg: '50.5',
    positions: [{
      ownerKind: 'Company', ownerId: OWNER, custodianKind: 'Company', custodianId: CUSTODIAN,
      locationId: LOCATION, productId: PRODUCT, block: 'Free', quantityKg: '1200',
    }, {
      ownerKind: 'Company', ownerId: OWNER, custodianKind: 'Company', custodianId: CUSTODIAN,
      locationId: LOCATION, productId: PRODUCT, block: 'Blocked', quantityKg: '50.5',
    }],
  }
}

function genealogy() {
  return {
    labelId: LABEL, labelCode: 'LBL-100', windowFull: false,
    edges: [{
      transformationId: TRANSFORMATION, kind: 'Split', occurredAtUtc: '2026-09-24T07:59:00Z',
      sourceLabelId: SOURCE_LABEL, sourceCode: 'LBL-090', sourceLabelVersion: '2', sourceQuantityKg: '1250.5',
      outputLabelId: LABEL, outputCode: 'LBL-100', outputLabelVersion: '3', outputQuantityKg: '1250.5',
    }],
  }
}

describe('Inventory label detail contract', () => {
  it('decodes the exact existing identity and balance contracts and binds them to the requested scope', () => {
    const decodedIdentity = decodeInventoryLabelIdentity(identity())
    const decodedBalances = decodeInventoryLabelBalances(balances())
    const decodedGenealogy = decodeInventoryLabelGenealogy(genealogy(), LABEL, 'LBL-100')
    expect(decodedIdentity).not.toBeNull()
    expect(decodedBalances).not.toBeNull()
    expect(decodedGenealogy).not.toBeNull()
    expect(combineInventoryLabelDetail('3', decodedIdentity!, decodedBalances!, decodedGenealogy!, COMPANY, LABEL)).toEqual({
      labelVersion: '3', identity: decodedIdentity, balances: decodedBalances, genealogy: decodedGenealogy,
    })
  })

  it('fails closed for extra fields, mismatched lot, company, label or position product', () => {
    expect(decodeInventoryLabelIdentity({ ...identity(), leakedActor: 'x' })).toBeNull()
    expect(decodeInventoryLabelIdentity({ ...identity(), lot: { ...identity().lot, id: OWNER } })).toBeNull()
    const decodedIdentity = decodeInventoryLabelIdentity(identity())!
    const decodedBalances = decodeInventoryLabelBalances(balances())!
    const decodedGenealogy = decodeInventoryLabelGenealogy(genealogy(), LABEL, 'LBL-100')!
    expect(combineInventoryLabelDetail('3', decodedIdentity, decodedBalances, decodedGenealogy, OWNER, LABEL)).toBeNull()
    expect(combineInventoryLabelDetail('3', decodedIdentity, decodedBalances, decodedGenealogy, COMPANY, OWNER)).toBeNull()
    expect(combineInventoryLabelDetail('3', decodedIdentity, {
      ...decodedBalances,
      positions: [{ ...decodedBalances.positions[0], productId: OWNER }],
    }, decodedGenealogy, COMPANY, LABEL)).toBeNull()
    expect(combineInventoryLabelDetail('03', decodedIdentity, decodedBalances, decodedGenealogy, COMPANY, LABEL)).toBeNull()
    expect(decodeInventoryLabelGenealogy({ ...genealogy(), leakedActor: 'x' }, LABEL, 'LBL-100')).toBeNull()
    expect(decodeInventoryLabelGenealogy({ ...genealogy(), labelId: OWNER }, LABEL, 'LBL-100')).toBeNull()
    expect(decodeInventoryLabelGenealogy({ ...genealogy(), labelCode: 'LBL-X' }, LABEL, 'LBL-100')).toBeNull()
    expect(decodeInventoryLabelGenealogy({ ...genealogy(), edges: [{ ...genealogy().edges[0], sourceLabelVersion: '02' }] }, LABEL, 'LBL-100')).toBeNull()
    expect(decodeInventoryLabelGenealogy({ ...genealogy(), edges: [{ ...genealogy().edges[0], sourceLabelId: OWNER, outputLabelId: SOURCE_LABEL }] }, LABEL, 'LBL-100')).toBeNull()
    expect(decodeInventoryLabelGenealogy({
      ...genealogy(),
      edges: [genealogy().edges[0], {
        ...genealogy().edges[0], transformationId: OWNER, sourceLabelId: CUSTODIAN, outputLabelId: LOCATION,
      }],
    }, LABEL, 'LBL-100')).toBeNull()
  })

  it('rejects negative quantities, non-positive positions and totals that do not match their positions', () => {
    expect(decodeInventoryLabelBalances({ ...balances(), physicalKg: '-1250.5' })).toBeNull()
    expect(decodeInventoryLabelBalances({ ...balances(), freeKg: '-1' })).toBeNull()
    expect(decodeInventoryLabelBalances({ ...balances(), blockedKg: '-1' })).toBeNull()
    expect(decodeInventoryLabelBalances({
      ...balances(), positions: [{ ...balances().positions[0], quantityKg: '-1200' }],
    })).toBeNull()
    expect(decodeInventoryLabelBalances({
      ...balances(), positions: [{ ...balances().positions[0], quantityKg: '0' }],
    })).toBeNull()
    expect(decodeInventoryLabelBalances({ ...balances(), physicalKg: '1250.6' })).toBeNull()
    expect(decodeInventoryLabelBalances({ ...balances(), freeKg: '1199.9' })).toBeNull()
    expect(decodeInventoryLabelBalances({ ...balances(), blockedKg: '50.4' })).toBeNull()
    expect(decodeInventoryLabelBalances({
      ...balances(), positions: balances().positions.map((item) => ({ ...item, block: 'Reserved' })),
    })).toBeNull()
    expect(decodeInventoryLabelBalances({
      ...balances(), physicalKg: '1.0001', freeKg: '1.0001', blockedKg: '0',
      positions: [{ ...balances().positions[0], quantityKg: '1.0001' }],
    })).toBeNull()
  })
})

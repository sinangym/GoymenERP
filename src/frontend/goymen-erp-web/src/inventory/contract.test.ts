import { describe, expect, it } from 'vitest'
import {
  decodeInventoryStockPositions,
  decodeOpeningCount,
  decodeOrganizationLocationCandidates,
  decodeProductStockCandidates,
  isOpeningCountRequest,
} from './contract'

const COUNT = '11111111-1111-4111-8111-111111111111'
const COMPANY = '22222222-2222-4222-8222-222222222222'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const SITE = '44444444-4444-4444-8444-444444444444'
const LINE = '55555555-5555-4555-8555-555555555555'
const LABEL = '66666666-6666-4666-8666-666666666666'
const LOT = '77777777-7777-4777-8777-777777777777'
const PRODUCT = '88888888-8888-4888-8888-888888888888'
const UNIT = '99999999-9999-4999-8999-999999999999'
const PROFILE = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'

const request = {
  countId: COUNT, locationId: LOCATION, countedOn: '2026-09-23', sheetReference: 'SAYIM-1',
  lines: [{
    lineId: LINE, labelId: LABEL, labelCode: 'LBL-1', lotId: LOT, heatNumber: null, coilNumber: 'COIL-1',
    productId: PRODUCT, productVersion: '7', quantityKg: '12.5',
  }],
}

describe('Inventory opening wire contracts', () => {
  it('accepts the exact request and stored response but refuses zero, extra fields and malformed dates', () => {
    expect(isOpeningCountRequest(request)).toBe(true)
    expect(isOpeningCountRequest({ ...request, countedOn: '2026-02-29' })).toBe(false)
    expect(isOpeningCountRequest({ ...request, companyId: COMPANY })).toBe(false)
    expect(isOpeningCountRequest({ ...request, lines: [{ ...request.lines[0], quantityKg: '0' }] })).toBe(false)
    expect(decodeOpeningCount({
      ...request, companyId: COMPANY,
      lines: request.lines.map(({ productVersion: _productVersion, ...line }) => line),
    })).not.toBeNull()
  })

  it('decodes exact bounded Product and Organization candidates', () => {
    expect(decodeProductStockCandidates({
      items: [{
        productId: PRODUCT, productVersion: '7', code: 'PRD-1', name: 'Galvaniz bant',
        stockUnitId: UNIT, stockUnitCode: 'kg', profileId: PROFILE, profileVersion: '3',
      }],
      searchWindowFull: false,
    })?.items[0].stockUnitCode).toBe('kg')
    expect(decodeOrganizationLocationCandidates({
      items: [{
        locationId: LOCATION, locationCode: 'A-01', locationName: 'Ana depo',
        siteId: SITE, siteCode: 'TESIS-1', siteName: 'Merkez tesis',
      }],
      searchWindowFull: false,
    })?.items[0].locationId).toBe(LOCATION)
  })

  it('fails closed on duplicate identities and unknown members', () => {
    const product = {
      productId: PRODUCT, productVersion: '7', code: 'PRD-1', name: 'Bant',
      stockUnitId: UNIT, stockUnitCode: 'kg', profileId: PROFILE, profileVersion: '3',
    }
    expect(decodeProductStockCandidates({ items: [product, product], searchWindowFull: true })).toBeNull()
    expect(decodeOrganizationLocationCandidates({
      items: [{ locationId: LOCATION, locationCode: 'A', locationName: 'Ana', siteId: SITE, siteCode: 'S', siteName: 'Site', extra: true }],
      searchWindowFull: false,
    })).toBeNull()
  })

  it('decodes stock components only when physical and free equations reconcile exactly', () => {
    const position = {
      companyId: COMPANY, labelId: LABEL, labelVersion: '3', labelCode: 'LBL-1', productId: PRODUCT,
      lot: { id: LOT, heatNumber: 'H-1', coilNumber: null }, heatNumber: null, coilNumber: 'C-1',
      documentStatus: 'Complete', ownerKind: 'Company', ownerId: COMPANY,
      custodianKind: 'Company', custodianId: COMPANY, locationId: LOCATION,
      physicalKg: '12.125', freePhysicalKg: '10.125', blockedPhysicalKg: '2',
      activeAllocationKg: '4.5', availableKg: '5.625',
    }
    expect(decodeInventoryStockPositions({ items: [position], searchWindowFull: false })?.items[0].availableKg)
      .toBe('5.625')
    expect(decodeInventoryStockPositions({
      items: [{ ...position, availableKg: '5.624' }], searchWindowFull: false,
    })).toBeNull()
    expect(decodeInventoryStockPositions({
      items: [{ ...position, physicalKg: '12.124' }], searchWindowFull: false,
    })).toBeNull()
    expect(decodeInventoryStockPositions({
      items: [{ ...position, physicalKg: '12.1250' }], searchWindowFull: false,
    })).toBeNull()
    expect(decodeInventoryStockPositions({ items: [position, position], searchWindowFull: true })).toBeNull()
  })
})

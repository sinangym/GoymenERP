import { describe, expect, it } from 'vitest'
import { decodePaintCatalog, decodePaintSelection, samePaintSelection } from './paintSelectionContract'
const company = '10000000-0000-0000-0000-000000000001'
const selection = { productId: '20000000-0000-0000-0000-000000000002', productVersion: '12', membershipVersion: '3',
  productSourceVersion: 'source:3', supplierPartyId: '30000000-0000-0000-0000-000000000003',
  supplierCompanyAccountId: '40000000-0000-0000-0000-000000000004', supplierPartyVersion: '4', supplierAccountVersion: '5', colorId: null, colorVersion: null }
describe('paint owner references', () => {
  it('keeps exact product and supplier versions with an optional absent color', () => {
    expect(decodePaintSelection(selection)).toEqual(selection)
    expect(samePaintSelection(selection, { ...selection })).toBe(true)
    expect(samePaintSelection(selection, { ...selection, membershipVersion: '4' })).toBe(false)
    expect(decodePaintSelection({ ...selection, colorId: company })).toBeUndefined()
    expect(decodePaintSelection({ ...selection, membershipVersion: '' })).toBeUndefined()
    expect(decodePaintSelection({ ...selection, invented: true })).toBeUndefined()
  })
  it('rejects another company, duplicate products and malformed owner identity rather than presenting a partial list', () => {
    const product = { productId: selection.productId, productVersion: '12', membershipVersion: '3', sourceVersion: 'source:3', code: 'PNT', name: 'Boya' }
    const catalog = { companyId: company, products: [product], suppliers: [], colors: [], supplierSearchWindowFull: false }
    expect(decodePaintCatalog(catalog, company)).toEqual(catalog)
    expect(decodePaintCatalog(catalog, selection.productId)).toBeUndefined()
    expect(decodePaintCatalog({ ...catalog, products: [product, product] }, company)).toBeUndefined()
    expect(decodePaintCatalog({ ...catalog, products: [{ ...product, sourceVersion: null }] }, company)).toBeUndefined()
  })
})

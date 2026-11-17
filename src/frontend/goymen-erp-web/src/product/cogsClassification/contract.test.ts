import { describe, expect, it } from 'vitest'
import { COGS_SOURCE, cogsVersion, decodeCogsMutation, decodeCogsView, type CogsView, type SetCogsRequest } from './contract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const PRODUCT = '22222222-2222-2222-2222-222222222222'
const OTHER = '33333333-3333-3333-3333-333333333333'
const request: SetCogsRequest = { expectedProductVersion: '7', expectedClassificationVersion: '0', classification: 1, reason: 'SENTETIK' }
const initial = (): CogsView => ({ companyId: COMPANY, productId: PRODUCT, productVersion: '7', classificationVersion: '0',
  classification: null, changedInCompanyId: null, updatedAtUtc: null, sourceVersion: COGS_SOURCE })
const assigned = (overrides: Partial<CogsView> = {}): CogsView => ({ ...initial(), classificationVersion: '1', classification: 1,
  changedInCompanyId: COMPANY, updatedAtUtc: '2026-11-17T10:00:00+00:00', ...overrides })
const mutation = (overrides: Record<string, unknown> = {}) => ({ classification: assigned(), isReplay: false, originalCorrelationId: 'synthetic-original', ...overrides })

describe('COGS exact response contracts', () => {
  it('keeps a real unset class distinct from either explicit kind', () => {
    expect(decodeCogsView(initial(), COMPANY, PRODUCT)).toEqual(initial())
    expect(decodeCogsView(assigned(), COMPANY, PRODUCT)?.classification).toBe(1)
    expect(decodeCogsView(assigned({ classification: 2 }), COMPANY, PRODUCT)?.classification).toBe(2)
  })
  it('allows the actual global class to have changed in another company', () => {
    expect(decodeCogsView(assigned({ changedInCompanyId: OTHER }), COMPANY, PRODUCT)?.changedInCompanyId).toBe(OTHER)
  })
  it.each<readonly [string, unknown]>([
    ['companyId', OTHER], ['productId', OTHER], ['companyId', null], ['productVersion', 7], ['productVersion', '0'],
    ['productVersion', '07'], ['classificationVersion', -1], ['classificationVersion', '01'],
    ['classificationVersion', '9223372036854775808'], ['classification', 'TradeGoods'], ['classification', 3],
    ['classification', null], ['changedInCompanyId', null], ['changedInCompanyId', '00000000-0000-0000-0000-000000000000'],
    ['updatedAtUtc', null], ['updatedAtUtc', '2026-11-17T10:00:00'], ['updatedAtUtc', '2026-11-17T10:00:00+03:00'],
    ['sourceVersion', 'product.cogs-classification.v2'],
  ])('refuses inconsistent assigned field %s=%j', (field, value) => {
    expect(decodeCogsView({ ...assigned(), [field]: value }, COMPANY, PRODUCT)).toBeNull()
  })
  it.each(['classification', 'changedInCompanyId', 'updatedAtUtc'])('refuses unset metadata %s', (field) => {
    const values: Record<string, unknown> = { classification: 1, changedInCompanyId: COMPANY, updatedAtUtc: '2026-11-17T10:00:00Z' }
    expect(decodeCogsView({ ...initial(), [field]: values[field] }, COMPANY, PRODUCT)).toBeNull()
  })
  it.each(Object.keys(initial()))('refuses a missing required view field %s', (field) => {
    const value: Record<string, unknown> = { ...initial() }
    delete value[field]
    expect(decodeCogsView(value, COMPANY, PRODUCT)).toBeNull()
  })
  it.each([null, [], { ...assigned(), accountCode: '621' }])('refuses non-contract values and inferred accounts %j', (value) => {
    expect(decodeCogsView(value, COMPANY, PRODUCT)).toBeNull()
  })
  it('keeps int64 revisions exact past the JavaScript number boundary', () => {
    const value = assigned({ productVersion: '9007199254740993', classificationVersion: '9223372036854775807' })
    expect(decodeCogsView(value, COMPANY, PRODUCT)).toEqual(value)
    expect(cogsVersion('9223372036854775808')).toBe(false)
  })
  it('accepts a new class revision while preserving the actual product revision', () => {
    expect(decodeCogsMutation(mutation(), COMPANY, PRODUCT, request)).toEqual(mutation())
  })
  it('accepts an owner no-op without incrementing class or product revisions', () => {
    const result = mutation({ classification: assigned({ changedInCompanyId: OTHER }) })
    expect(decodeCogsMutation(result, COMPANY, PRODUCT, { ...request, expectedClassificationVersion: '1' })).toEqual(result)
  })
  it.each<readonly [string, unknown]>([
    ['companyId', OTHER], ['productId', OTHER], ['productVersion', '8'], ['classificationVersion', '2'],
    ['classificationVersion', '0'], ['classification', 2], ['changedInCompanyId', OTHER],
  ])('rejects an unbound new mutation field %s=%j', (field, value) => {
    expect(decodeCogsMutation(mutation({ classification: { ...assigned(), [field]: value } }), COMPANY, PRODUCT, request)).toBeNull()
  })
  it.each<readonly [string, unknown]>([
    ['isReplay', 'true'], ['originalCorrelationId', ''], ['originalCorrelationId', ' '.repeat(3)],
    ['originalCorrelationId', 'a'.repeat(129)], ['extra', 1],
  ])('rejects malformed mutation metadata %s=%j', (field, value) => {
    expect(decodeCogsMutation(mutation({ [field]: value }), COMPANY, PRODUCT, request)).toBeNull()
  })
})

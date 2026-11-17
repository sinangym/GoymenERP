import { describe, expect, it } from 'vitest'
import { decodePaintCandidates, decodePaintCatalog, decodePaintMutation, decodePaintPreview, PAINT_SOURCE, type PaintCommand } from './contract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const PRODUCT = '22222222-2222-2222-2222-222222222222'
const OTHER = '33333333-3333-3333-3333-333333333333'
const SHA = 'a'.repeat(64)
const entry = () => ({ productId: PRODUCT, productVersion: '7', membershipVersion: '1', productCode: '00012', productName: 'SENTETIK boya', sourceVersion: PAINT_SOURCE })
const catalog = () => ({ outcome: 1, companyId: COMPANY, version: '1', entries: [entry()], sourceVersion: PAINT_SOURCE })
const row = () => ({ rowNumber: 2, productId: PRODUCT, productVersion: '7', membershipVersion: '0', productCode: '00012', productName: 'SENTETIK boya', alreadyMember: false })
const preview = () => ({ companyId: COMPANY, version: '0', fileSha256: SHA, productCodeColumn: 'Ürün kodu', previewToken: 'source-bound-token', sourceRowCount: 1, rows: [row()], errors: [], ready: true })
const expected = { sha256: SHA, column: 'Ürün kodu' }
const command: PaintCommand = { kind: 'set', productId: PRODUCT, payload: { expectedVersion: '1', expectedProductVersion: '7', expectedMembershipVersion: '1', isMember: false, reason: 'SENTETIK düzeltme' } }
const mutation = () => ({ companyId: COMPANY, version: '2', changedEntries: [], removedProductIds: [PRODUCT], isReplay: false, originalCorrelationId: 'synthetic-correlation' })
const candidate = () => ({ productId: PRODUCT, productVersion: '7', membershipVersion: '0', productCode: '00012', productName: 'SENTETIK boya', alreadyMember: false })
const candidates = () => ({ outcome: 1, companyId: COMPANY, version: '2', q: '00012', limit: 25,
  items: [candidate()], searchWindowFull: false, sourceVersion: PAINT_SOURCE })

describe('paint membership candidate discovery is bound to the real source frame', () => {
  it('preserves leading zeros and the actual Product and membership version fences', () => {
    expect(decodePaintCandidates(candidates(), COMPANY, '00012')?.items[0]).toEqual(candidate())
    expect(decodePaintCandidates({ ...candidates(), items: [{ ...candidate(), alreadyMember: true, membershipVersion: '1' }] }, COMPANY, '00012')?.items[0].alreadyMember).toBe(true)
  })
  it('distinguishes an initialized empty result from no source', () => {
    expect(decodePaintCandidates({ ...candidates(), items: [] }, COMPANY, '00012')?.outcome).toBe(1)
    expect(decodePaintCandidates({ ...candidates(), outcome: 2, version: '0', items: [] }, COMPANY, '00012')?.outcome).toBe(2)
  })
  it.each([
    { companyId: OTHER }, { q: 'other' }, { limit: 50 }, { outcome: 'Available' }, { sourceVersion: 'global.product' },
    { extra: true }, { version: '0' }, { items: [candidate(), candidate()] }, { searchWindowFull: true },
    { items: [{ ...candidate(), productVersion: '0' }] }, { items: [{ ...candidate(), productVersion: 7 }] },
    { items: [{ ...candidate(), alreadyMember: true }] }, { items: [{ ...candidate(), membershipVersion: '3', alreadyMember: true }] },
    { items: [{ ...candidate(), productCode: 12 }] }, { items: [{ ...candidate(), productName: 'unsafe\nname' }] },
    { items: [{ ...candidate(), price: '1' }] }, { items: [{ ...candidate(), productId: 'not-an-id' }] },
    { outcome: 2 }, { items: null },
  ])('rejects foreign stale unbounded or contradictory candidate proof %j', (change) => {
    expect(decodePaintCandidates({ ...candidates(), ...change }, COMPANY, '00012')).toBeNull()
  })
  it('accepts a full bounded window only when it has the requested number of unique actual products', () => {
    const items = Array.from({ length: 25 }, (_, index) => ({ ...candidate(), productId: `22222222-2222-2222-2222-${String(index + 1).padStart(12, '0')}` }))
    expect(decodePaintCandidates({ ...candidates(), items, searchWindowFull: true }, COMPANY, '00012')?.items).toHaveLength(25)
    expect(decodePaintCandidates({ ...candidates(), items: [...items, { ...candidate(), productId: OTHER }] }, COMPANY, '00012')).toBeNull()
  })
})

describe('paint membership strict catalog contract', () => {
  it('keeps textual leading zero codes and exact version strings', () => {
    expect(decodePaintCatalog(catalog(), COMPANY)?.entries[0].productCode).toBe('00012')
    expect(decodePaintCatalog({ ...catalog(), version: '9007199254740993' }, COMPANY)?.version).toBe('9007199254740993')
  })
  it('distinguishes no source from a real empty list and unavailable', () => {
    expect(decodePaintCatalog({ ...catalog(), outcome: 2, version: '0', entries: [] }, COMPANY)?.outcome).toBe(2)
    expect(decodePaintCatalog({ ...catalog(), entries: [] }, COMPANY)?.outcome).toBe(1)
    expect(decodePaintCatalog({ ...catalog(), outcome: 3, version: '0', entries: [] }, COMPANY)?.outcome).toBe(3)
  })
  it.each([
    { companyId: OTHER }, { outcome: 'Available' }, { outcome: 4 }, { version: 1 }, { version: '01' }, { version: '9223372036854775808' },
    { version: '0' }, { sourceVersion: 'other-owner' }, { extra: true }, { entries: [entry(), entry()] },
    { entries: [{ ...entry(), productVersion: null }] }, { entries: [{ ...entry(), membershipVersion: '2' }] },
    { entries: [{ ...entry(), sourceVersion: 'other-owner' }] }, { entries: [{ ...entry(), extra: 1 }] },
    { outcome: 2 }, { entries: null },
  ])('rejects invalid catalog field set %j', (change) => {
    expect(decodePaintCatalog({ ...catalog(), ...change }, COMPANY)).toBeNull()
  })
  it('rejects a missing field instead of filling a default', () => {
    const body: Record<string, unknown> = catalog()
    delete body.sourceVersion
    expect(decodePaintCatalog(body, COMPANY)).toBeNull()
  })
})
describe('paint membership preview identity and readiness', () => {
  it('accepts a fully bound source preview and a refused row preview', () => {
    expect(decodePaintPreview(preview(), COMPANY, expected)?.ready).toBe(true)
    expect(decodePaintPreview({ ...preview(), ready: false, rows: [], errors: [{ rowNumber: 2, field: 'productCode', code: 'invalid', message: 'SENTETIK hata' }] }, COMPANY, expected)?.ready).toBe(false)
  })
  it.each([
    { companyId: OTHER }, { fileSha256: 'b'.repeat(64) }, { productCodeColumn: 'other' }, { ready: false }, { ready: 'true' },
    { previewToken: null }, { sourceRowCount: 0 }, { sourceRowCount: 5001 }, { extra: 1 },
    { rows: [{ ...row(), membershipVersion: '1' }] }, { rows: [{ ...row(), rowNumber: 1 }] },
    { rows: [{ ...row(), alreadyMember: true }] }, { rows: [{ ...row(), productVersion: 7 }] },
    { sourceRowCount: 2, rows: [row(), { ...row(), rowNumber: 3 }] },
    { errors: [{ rowNumber: 2, field: 'productCode', code: 'invalid', message: 'SENTETIK hata', extra: 1 }] },
  ])('refuses foreign, mismatched, duplicate or malformed preview %j', (change) => {
    expect(decodePaintPreview({ ...preview(), ...change }, COMPANY, expected)).toBeNull()
  })
})
describe('paint membership mutation identity', () => {
  it('accepts explicit removal and nullable removal property on unchanged replay', () => {
    expect(decodePaintMutation(mutation(), COMPANY, command)?.removedProductIds).toEqual([PRODUCT])
    const unchanged = { ...command, payload: { ...command.payload, expectedVersion: '2', expectedMembershipVersion: '0' } }
    expect(decodePaintMutation({ ...mutation(), removedProductIds: null }, COMPANY, unchanged)?.removedProductIds).toBeNull()
  })
  it.each([
    { companyId: OTHER }, { removedProductIds: [OTHER] }, { removedProductIds: [PRODUCT, PRODUCT] },
    { changedEntries: [entry()] }, { originalCorrelationId: '' }, { isReplay: 'true' }, { extra: 1 }, { version: 2 },
    { removedProductIds: [] }, { removedProductIds: null }, { version: '3' },
  ])('refuses unrelated or malformed mutation %j', (change) => {
    expect(decodePaintMutation({ ...mutation(), ...change }, COMPANY, command)).toBeNull()
  })
  it('refuses implicit deletion in an import and requires the nullable property to exist', () => {
    const importCommand: PaintCommand = { kind: 'import', expectedProductIds: [PRODUCT], payload: { expectedVersion: '1', fileName: 'SENTETIK.xlsx', contentBase64: 'YQ==', productCodeColumn: 'Ürün kodu', fileSha256: SHA, previewToken: 't', sourceLabel: 'SENTETIK', reason: 'SENTETIK' } }
    expect(decodePaintMutation(mutation(), COMPANY, importCommand)).toBeNull()
    const body: Record<string, unknown> = mutation()
    delete body.removedProductIds
    expect(decodePaintMutation(body, COMPANY, command)).toBeNull()
  })
})

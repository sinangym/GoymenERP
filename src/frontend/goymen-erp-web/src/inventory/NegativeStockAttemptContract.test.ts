import { describe, expect, it } from 'vitest'
import { decodeNegativeStockAttemptPage, isNegativeStockAttemptKg, type NegativeStockAttempt, type NegativeStockAttemptQuery } from './NegativeStockAttemptContract'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const PRODUCT = '22222222-2222-4222-8222-222222222222'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const LABEL = '44444444-4444-4444-8444-444444444444'
const OTHER = '55555555-5555-4555-8555-555555555555'
const query: NegativeStockAttemptQuery = { productId: null, locationId: null, openOnly: false }
const row = (overrides: Partial<NegativeStockAttempt> = {}): NegativeStockAttempt => ({
  attemptId: '66666666-6666-4666-8666-666666666666', companyId: COMPANY, movementId: '77777777-7777-4777-8777-777777777777',
  commandName: 'inventory.stock-transfer.dispatch.v1', actorId: 'synthetic-actor', occurredAtUtc: '2026-11-17T10:00:00+00:00',
  productId: PRODUCT, locationId: LOCATION, labelId: LABEL, labelCode: 'SENTETIK-0001', movementKind: 'TransferDispatch',
  sourceKind: 'inventory.transfer', sourceId: '88888888-8888-4888-8888-888888888888', sourceLineId: null,
  requestedKg: '500.001', availableKg: '480', missingKg: '20.001', resolution: null, ...overrides,
})
const page = (overrides: Record<string, unknown> = {}) => ({ companyId: COMPANY, items: [row()], windowFull: false, ...overrides })
const decode = (value: unknown, filters = query) => decodeNegativeStockAttemptPage(value, COMPANY, filters)

describe('negative stock attempt immutable owner contract', () => {
  it('preserves every recorded figure and internal identity without recalculation', () => {
    expect(decode(page())?.items[0]).toEqual(row())
    expect(Object.isFrozen(decode(page())?.items)).toBe(true)
    expect(Object.isFrozen(decode(page())?.items[0])).toBe(true)
  })
  it('preserves a negative legacy balance without converting it to zero or a successful stock event', () => {
    const value = row({ requestedKg: '0.001', availableKg: '-2.002', missingKg: '2.003' })
    expect(decode(page({ items: [value] }))?.items[0]).toEqual(value)
  })
  it('bounds the owner decimal snapshot without passing through JavaScript numbers', () => {
    expect(isNegativeStockAttemptKg('999999999999999.999', true)).toBe(true)
    expect(isNegativeStockAttemptKg('-999999999999999.999')).toBe(true)
    expect(isNegativeStockAttemptKg('1000000000000000')).toBe(false)
    expect(isNegativeStockAttemptKg('-1000000000000000')).toBe(false)
  })
  it('accepts real empty and full-window pages without inventing a total', () => {
    expect(decode(page({ items: [] }))?.items).toEqual([])
    const items = Array.from({ length: 50 }, (_, index) => row({ attemptId: `99999999-9999-4999-8999-${String(index + 1).padStart(12, '0')}` }))
    expect(decode(page({ items, windowFull: true }))?.items).toHaveLength(50)
    expect(decode(page({ items: [...items, row()] }))).toBeNull()
  })
  it('keeps the actual approved count resolution immutable and allows a distinct checker', () => {
    const resolution = { stockCountId: OTHER, decidedAtUtc: '2026-11-17T11:00:00Z', decidedBy: 'synthetic-checker' }
    const result = decode(page({ items: [row({ resolution })] }))
    expect(result?.items[0].resolution).toEqual(resolution)
    expect(Object.isFrozen(result?.items[0].resolution)).toBe(true)
    expect(result?.items[0].actorId).toBe('synthetic-actor')
  })
  it.each(['companyId', 'attemptId', 'movementId', 'productId', 'locationId', 'labelId', 'sourceId', 'sourceLineId'])('refuses malformed identity %s', (field) => {
    expect(decode(page({ items: [{ ...row(), [field]: 'not-a-guid' }] }))).toBeNull()
    expect(decode(page({ items: [{ ...row(), [field]: '00000000-0000-0000-0000-000000000000' }] }))).toBeNull()
  })
  it.each(['actorId', 'commandName', 'labelCode', 'movementKind', 'sourceKind'])('refuses unbounded or unsafe owner text %s', (field) => {
    for (const value of ['', ' padded ', 'a'.repeat(129), 'hidden\u202evalue', 'line\nvalue']) {
      expect(decode(page({ items: [{ ...row(), [field]: value }] }))).toBeNull()
    }
  })
  it.each(['occurredAtUtc', 'resolution'])('rejects invalid timestamp evidence %s', (field) => {
    const value = field === 'occurredAtUtc' ? { ...row(), occurredAtUtc: '2026-11-17T10:00:00+03:00' }
      : row({ resolution: { stockCountId: OTHER, decidedBy: 'synthetic-checker', decidedAtUtc: '2026-11-17T10:00:00' } })
    expect(decode(page({ items: [value] }))).toBeNull()
  })
  it.each(['requestedKg', 'missingKg'])('requires positive canonical owner quantity %s', (field) => {
    for (const value of ['0', '-1', '01', '1.000', '0.0001', '1e3', '1,2', '+1', 1, null]) {
      expect(decode(page({ items: [{ ...row(), [field]: value }] }))).toBeNull()
    }
  })
  it.each(['-0', '-0.00', '00', '2.000', '2.0001', '+2', 'NaN', 2])('rejects a non-canonical balance %j', (value) => {
    expect(isNegativeStockAttemptKg(value)).toBe(false)
    expect(decode(page({ items: [{ ...row(), availableKg: value }] }))).toBeNull()
  })
  it.each(Object.keys(row()))('requires row field %s', (field) => {
    const value: Record<string, unknown> = { ...row() }; delete value[field]
    expect(decode(page({ items: [value] }))).toBeNull()
  })
  it.each<readonly [string, unknown]>([['companyId', OTHER], ['windowFull', 'false'], ['extra', 1]])('refuses a foreign or drifting page %s', (field, value) => {
    expect(decode(page({ [field]: value }))).toBeNull()
  })
  it('rejects foreign rows, duplicate identities and extra row/resolution fields', () => {
    expect(decode(page({ items: [row({ companyId: OTHER })] }))).toBeNull()
    expect(decode(page({ items: [row(), row()] }))).toBeNull()
    expect(decode(page({ items: [{ ...row(), freeStockNow: '10' }] }))).toBeNull()
    expect(decode(page({ items: [{ ...row(), resolution: { stockCountId: OTHER, decidedAtUtc: '2026-11-17T11:00:00Z', decidedBy: 'synthetic-checker', deleted: false } }] }))).toBeNull()
  })
  it('binds product, location and open-only results to exactly the requested filters', () => {
    expect(decode(page(), { productId: PRODUCT, locationId: LOCATION, openOnly: true })).not.toBeNull()
    expect(decode(page(), { ...query, productId: OTHER })).toBeNull()
    expect(decode(page(), { ...query, locationId: OTHER })).toBeNull()
    expect(decode(page({ items: [row({ resolution: { stockCountId: OTHER, decidedAtUtc: '2026-11-17T11:00:00Z', decidedBy: 'synthetic-checker' } })] }),
      { ...query, openOnly: true })).toBeNull()
  })
  it('accepts another current owner movement name without using an outdated closed timeline enum', () => {
    expect(decode(page({ items: [row({ movementKind: 'PurchaseAcceptanceReversal' })] }))?.items[0].movementKind).toBe('PurchaseAcceptanceReversal')
  })
})

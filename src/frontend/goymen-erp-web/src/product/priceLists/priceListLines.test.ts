import { describe, expect, it } from 'vitest'
import { decodePriceListView, parseQuantityInput, type PriceListLine } from './contract'
import { buildLines, rowsFromLines } from './priceListLines'

const PRODUCT = '22222222-2222-2222-2222-222222222222'
const KG = '44444444-4444-4444-4444-444444444444'

function line(lineId: string, lowerQuantity: string, upperQuantity: string | null): PriceListLine {
  return {
    lineId, lineNo: 1, productId: PRODUCT, productCode: 'P-1', productName: 'Bant', priceUnitId: KG,
    unitPrice: '42', floorUnitPrice: null, lowerQuantity, upperQuantity,
  }
}

describe('price-list quantity tier transport', () => {
  it('keeps a boundary at 10 on the second tier and a 20..25 gap without matching or widening either interval', () => {
    const source = [line('l1', '0', '10'), line('l2', '10', '20'), line('l3', '25', null)]

    const result = buildLines(rowsFromLines(source))

    expect(result).toEqual({ ok: true, lines: [
      { lineId: 'l1', productId: PRODUCT, priceUnitId: KG, unitPrice: '42', floorUnitPrice: null, lowerQuantity: '0', upperQuantity: '10' },
      { lineId: 'l2', productId: PRODUCT, priceUnitId: KG, unitPrice: '42', floorUnitPrice: null, lowerQuantity: '10', upperQuantity: '20' },
      { lineId: 'l3', productId: PRODUCT, priceUnitId: KG, unitPrice: '42', floorUnitPrice: null, lowerQuantity: '25', upperQuantity: null },
    ] })
  })

  it('decodes reopened bounds exactly and refuses a response that omits them', () => {
    const body = {
      id: 'list', name: 'Liste', currencyCode: 'TRY', channel: 'Sales', vatBasis: 'ExcludingVat', version: '1',
      createdAtUtc: '2026-10-01T00:00:00Z', updatedAtUtc: '2026-10-01T00:00:00Z', activeVersionNo: null, draftVersionNo: 1,
      versions: [{ versionId: 'v1', versionNo: 1, state: 'Draft', status: 'Draft', validFrom: '2026-10-01', validThrough: null,
        createdAtUtc: '2026-10-01T00:00:00Z', createdBy: 'actor', publishedAtUtc: null, publishedBy: null,
        lines: [line('l1', '10.125', '20.5')] }],
    }

    const decoded = decodePriceListView(body)
    expect(decoded?.versions[0].lines[0]).toMatchObject({ lowerQuantity: '10.125', upperQuantity: '20.5' })
    expect(rowsFromLines(decoded?.versions[0].lines ?? [])).toMatchObject([{ lowerQuantity: '10,125', upperQuantity: '20,5' }])

    const withoutBounds = structuredClone(body)
    delete (withoutBounds.versions[0].lines[0] as Partial<PriceListLine>).lowerQuantity
    expect(decodePriceListView(withoutBounds)).toBeNull()
  })

  it('accepts the explicit untiered compatibility interval and enforces only the wire decimal scale locally', () => {
    expect(parseQuantityInput(' 000 ')).toBe('0')
    expect(parseQuantityInput('10,125')).toBe('10.125')
    expect(parseQuantityInput('10.1250')).toBeNull()
    expect(parseQuantityInput('-1')).toBeNull()
  })
})

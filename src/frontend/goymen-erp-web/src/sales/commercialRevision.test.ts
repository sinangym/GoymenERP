import { describe, expect, it } from 'vitest'
import { buildCommercialRevisionRequest, type CommercialRevisionRow } from './commercialRevision'

const row = (over: Partial<CommercialRevisionRow> = {}): CommercialRevisionRow => ({
  lineId: '11111111-1111-4111-8111-111111111111',
  lineNo: 1,
  productName: 'SENTETIK ürün',
  orderUnitId: '22222222-2222-4222-8222-222222222222',
  unitPrice: '',
  priceUnitId: '22222222-2222-4222-8222-222222222222',
  discountPercent: '',
  ...over,
})

describe('commercial revision payload', () => {
  it('names every row once and keeps decimal values as trimmed text', () => {
    expect(buildCommercialRevisionRequest('7', [row({ unitPrice: ' 12.50 ', discountPercent: ' 5 ' })])).toEqual({
      ok: true,
      payload: {
        expectedVersion: '7',
        lines: [{
          lineId: '11111111-1111-4111-8111-111111111111',
          unitPrice: '12.50',
          priceUnitId: '22222222-2222-4222-8222-222222222222',
          discountPercent: '5',
        }],
      },
    })
  })

  it('sends an empty commercial row as explicit nulls, never zero', () => {
    const built = buildCommercialRevisionRequest('7', [row()])
    expect(built.ok && built.payload.lines[0]).toEqual({
      lineId: '11111111-1111-4111-8111-111111111111',
      unitPrice: null,
      priceUnitId: null,
      discountPercent: null,
    })
  })

  it('refuses a discount without a price before creating an attempt', () => {
    expect(buildCommercialRevisionRequest('7', [row({ discountPercent: '5' })])).toEqual({
      ok: false,
      lineNo: 1,
      reason: 'discount-without-price',
    })
  })
})

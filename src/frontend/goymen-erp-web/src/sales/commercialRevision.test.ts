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
  reference: null,
  discountKind: 'percent',
  surchargeKind: 'percent',
  surchargeValue: '',
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
          priceReference: null,
          discountAmount: null,
          surchargePercent: null,
          surchargeAmount: null,
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
      priceReference: null,
      discountAmount: null,
      surchargePercent: null,
      surchargeAmount: null,
    })
  })

  it('refuses a discount without a price before creating an attempt', () => {
    expect(buildCommercialRevisionRequest('7', [row({ discountPercent: '5' })])).toEqual({
      ok: false,
      lineNo: 1,
      reason: 'discount-without-price',
    })
  })

  it('AT-018: sends the chosen list version by id only, and each adjustment under its own kind', () => {
    const reference = {
      priceListId: '55555555-5555-4555-8555-555555555555', priceListName: 'Yurt içi', versionNo: 2, validFrom: '2026-10-01',
      validThrough: null, currencyCode: 'TRY', priceUnitId: '22222222-2222-4222-8222-222222222222', unitPrice: '42000.50',
      floorUnitPrice: '40000', vatBasis: 'ExcludingVat', lowerQuantity: '10', upperQuantity: '20',
    }
    const built = buildCommercialRevisionRequest('7', [row({ unitPrice: '42000.50', reference, discountKind: 'amount', discountPercent: '500',
      surchargeKind: 'percent', surchargeValue: '1.5' })])
    expect(built.ok && built.payload.lines[0]).toEqual({
      lineId: '11111111-1111-4111-8111-111111111111',
      unitPrice: '42000.50',
      priceUnitId: '22222222-2222-4222-8222-222222222222',
      discountPercent: null,
      priceReference: { priceListId: '55555555-5555-4555-8555-555555555555', versionNo: 2 },
      discountAmount: '500',
      surchargePercent: '1.5',
      surchargeAmount: null,
    })
    expect(buildCommercialRevisionRequest('7', [row({ reference, unitPrice: '' })])).toEqual({ ok: false, lineNo: 1, reason: 'discount-without-price' })
  })
})

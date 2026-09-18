import { describe, expect, it } from 'vitest'
import { unplannedQuantity } from './deliveryPlan'

const KG = 'kg-unit'
const PIECE = 'piece-unit'
const line = (quantity: string) => ({ orderUnitId: KG, quantity })
const slice = (quantity: string, orderUnitId = KG) => ({ orderUnitId, quantity })

describe('unplannedQuantity — the #254 §5 golden', () => {
  it('1000 = 400 + 600 leaves nothing unplanned', () => {
    expect(unplannedQuantity(line('1000'), [slice('400'), slice('600')])).toEqual({ ok: true, unplanned: '0' })
  })

  it('400 + 700 exceeds the 1000 ordered and is refused', () => {
    expect(unplannedQuantity(line('1000'), [slice('400'), slice('700')])).toEqual({
      ok: false,
      error: 'exceeds-order',
    })
  })

  it('400 + 400 leaves 200 unplanned', () => {
    expect(unplannedQuantity(line('1000'), [slice('400'), slice('400')])).toEqual({ ok: true, unplanned: '200' })
  })
})

describe('unplannedQuantity — exactness and units', () => {
  it('subtracts across scales without floating point', () => {
    // 0.1 + 0.2 in doubles is 0.30000000000000004; here it is exact.
    expect(unplannedQuantity(line('0.3'), [slice('0.1'), slice('0.2')])).toEqual({ ok: true, unplanned: '0' })
    expect(unplannedQuantity(line('1000.5'), [slice('0.25')])).toEqual({ ok: true, unplanned: '1000.25' })
    expect(unplannedQuantity(line('12345678901234567890'), [slice('0.000000001')])).toEqual({
      ok: true,
      unplanned: '12345678901234567889.999999999',
    })
  })

  it('treats an unknown plan and an empty plan alike: everything is still unplanned', () => {
    expect(unplannedQuantity(line('1000'), null)).toEqual({ ok: true, unplanned: '1000' })
    expect(unplannedQuantity(line('1000'), [])).toEqual({ ok: true, unplanned: '1000' })
  })

  it('refuses a slice in another unit instead of converting it', () => {
    expect(unplannedQuantity(line('1000'), [slice('1', PIECE)])).toEqual({ ok: false, error: 'foreign-unit' })
  })

  it('refuses non-positive or malformed quantities', () => {
    expect(unplannedQuantity(line('0'), [])).toEqual({ ok: false, error: 'invalid-quantity' })
    expect(unplannedQuantity(line('1000'), [slice('0')])).toEqual({ ok: false, error: 'invalid-quantity' })
    expect(unplannedQuantity(line('1e3'), [])).toEqual({ ok: false, error: 'invalid-quantity' })
  })
})

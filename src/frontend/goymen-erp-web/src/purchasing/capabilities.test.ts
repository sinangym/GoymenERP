import { describe, expect, it } from 'vitest'
import { PURCHASING_CAPABILITY, PURCHASING_NAV_LABEL, PURCHASING_ROUTE, purchaseOrderDetailPath } from './capabilities'

describe('purchasing capability contract', () => {
  it('pins the owner ids and paths without publishing a create navigation entry', () => {
    expect(PURCHASING_CAPABILITY).toEqual({
      orderList: 'purchasing.orders.list',
      orderCreate: 'purchasing.orders.create',
      orderDetail: 'purchasing.orders.detail',
      orderApprove: 'purchasing.orders.approve',
    })
    expect(PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderList]).toBe('/satinalma/siparisler')
    expect(PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderCreate]).toBe('/satinalma/siparisler/yeni')
    expect(PURCHASING_NAV_LABEL[PURCHASING_CAPABILITY.orderCreate]).toBe('')
    expect(purchaseOrderDetailPath('a/b')).toBe('/satinalma/siparisler/a%2Fb')
  })
})

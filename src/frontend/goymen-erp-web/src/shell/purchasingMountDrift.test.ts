import { describe, expect, it } from 'vitest'
import { matchPath } from 'react-router'
import { CAPABILITY, CAPABILITY_NAV_LABEL, CAPABILITY_ROUTE } from './capabilities'
import {
  PURCHASING_CAPABILITY,
  PURCHASING_NAV_LABEL,
  PURCHASING_ROUTE,
  purchaseOrderDetailPath,
} from '../purchasing/capabilities'

describe('shell ↔ Purchasing lane route catalogue', () => {
  it('mounts exactly the three route ids and keeps the approve child out of the route catalogue', () => {
    expect(CAPABILITY.purchasingOrdersList).toBe(PURCHASING_CAPABILITY.orderList)
    expect(CAPABILITY.purchasingOrdersCreate).toBe(PURCHASING_CAPABILITY.orderCreate)
    expect(CAPABILITY.purchasingOrdersDetail).toBe(PURCHASING_CAPABILITY.orderDetail)

    const shellIds = (Object.values(CAPABILITY) as string[]).filter((id) => id.startsWith('purchasing.'))
    expect([...shellIds].sort()).toEqual([
      PURCHASING_CAPABILITY.orderCreate,
      PURCHASING_CAPABILITY.orderDetail,
      PURCHASING_CAPABILITY.orderList,
    ].sort())
    expect(shellIds).not.toContain(PURCHASING_CAPABILITY.orderApprove)
  })

  it('agrees with the lane on every route and never reads the static create segment as an order id', () => {
    expect(CAPABILITY_ROUTE[CAPABILITY.purchasingOrdersList]).toBe(
      PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderList],
    )
    expect(CAPABILITY_ROUTE[CAPABILITY.purchasingOrdersCreate]).toBe(
      PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderCreate],
    )
    expect(CAPABILITY_ROUTE[CAPABILITY.purchasingOrdersDetail]).toBe(
      PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderDetail],
    )

    const orderId = '7b1c0f2e-0000-4a00-8000-000000000003'
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.purchasingOrdersDetail], purchaseOrderDetailPath(orderId))?.params.orderId)
      .toBe(orderId)
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.purchasingOrdersDetail], PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderCreate]))
      .toBeNull()
  })

  it('gives only the list the lane navigation label', () => {
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.purchasingOrdersList]).toBe(
      PURCHASING_NAV_LABEL[PURCHASING_CAPABILITY.orderList],
    )
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.purchasingOrdersCreate]).toBe('')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.purchasingOrdersDetail]).toBe('')
  })
})

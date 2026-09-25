import { describe, expect, it } from 'vitest'
import { matchPath, matchRoutes } from 'react-router'
import { CAPABILITY, CAPABILITY_NAV_LABEL, CAPABILITY_ROUTE } from './capabilities'
import { shellRoutes } from './router'
import {
  PURCHASING_CAPABILITY,
  PURCHASING_NAV_LABEL,
  PURCHASING_ROUTE,
  purchaseOrderDetailPath,
  purchaseRequestDetailPath,
} from '../purchasing/capabilities'
import { purchasingRoutes } from '../purchasing/purchasingRoutes'

describe('shell ↔ Purchasing lane route catalogue', () => {
  it('mounts exactly the six route ids and keeps submit and approve children out of the route catalogue', () => {
    expect(CAPABILITY.purchasingRequestsList).toBe(PURCHASING_CAPABILITY.requestList)
    expect(CAPABILITY.purchasingRequestsCreate).toBe(PURCHASING_CAPABILITY.requestCreate)
    expect(CAPABILITY.purchasingRequestsDetail).toBe(PURCHASING_CAPABILITY.requestDetail)
    expect(CAPABILITY.purchasingOrdersList).toBe(PURCHASING_CAPABILITY.orderList)
    expect(CAPABILITY.purchasingOrdersCreate).toBe(PURCHASING_CAPABILITY.orderCreate)
    expect(CAPABILITY.purchasingOrdersDetail).toBe(PURCHASING_CAPABILITY.orderDetail)

    const shellIds = (Object.values(CAPABILITY) as string[]).filter((id) => id.startsWith('purchasing.'))
    expect([...shellIds].sort()).toEqual([
      PURCHASING_CAPABILITY.requestCreate,
      PURCHASING_CAPABILITY.requestDetail,
      PURCHASING_CAPABILITY.requestList,
      PURCHASING_CAPABILITY.orderCreate,
      PURCHASING_CAPABILITY.orderDetail,
      PURCHASING_CAPABILITY.orderList,
    ].sort())
    expect(shellIds).not.toContain(PURCHASING_CAPABILITY.requestSubmit)
    expect(shellIds).not.toContain(PURCHASING_CAPABILITY.orderApprove)
  })

  it('agrees with the lane on every route and never reads a static create segment as a record id', () => {
    expect(CAPABILITY_ROUTE[CAPABILITY.purchasingRequestsList]).toBe(
      PURCHASING_ROUTE[PURCHASING_CAPABILITY.requestList],
    )
    expect(CAPABILITY_ROUTE[CAPABILITY.purchasingRequestsCreate]).toBe(
      PURCHASING_ROUTE[PURCHASING_CAPABILITY.requestCreate],
    )
    expect(CAPABILITY_ROUTE[CAPABILITY.purchasingRequestsDetail]).toBe(
      PURCHASING_ROUTE[PURCHASING_CAPABILITY.requestDetail],
    )
    expect(CAPABILITY_ROUTE[CAPABILITY.purchasingOrdersList]).toBe(
      PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderList],
    )
    expect(CAPABILITY_ROUTE[CAPABILITY.purchasingOrdersCreate]).toBe(
      PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderCreate],
    )
    expect(CAPABILITY_ROUTE[CAPABILITY.purchasingOrdersDetail]).toBe(
      PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderDetail],
    )

    const requestId = '7b1c0f2e-0000-4a00-8000-000000000001'
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.purchasingRequestsDetail], purchaseRequestDetailPath(requestId))?.params.requestId)
      .toBe(requestId)
    const requestCreateBranch = matchRoutes(shellRoutes, PURCHASING_ROUTE[PURCHASING_CAPABILITY.requestCreate])
    expect(requestCreateBranch?.at(-1)?.route.path).toBe(
      CAPABILITY_ROUTE[CAPABILITY.purchasingRequestsCreate].slice(1),
    )
    expect(requestCreateBranch?.at(-1)?.route.path).not.toBe(
      CAPABILITY_ROUTE[CAPABILITY.purchasingRequestsDetail].slice(1),
    )

    const orderId = '7b1c0f2e-0000-4a00-8000-000000000003'
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.purchasingOrdersDetail], purchaseOrderDetailPath(orderId))?.params.orderId)
      .toBe(orderId)
    const createBranch = matchRoutes(shellRoutes, PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderCreate])
    expect(createBranch?.at(-1)?.route.path).toBe(
      CAPABILITY_ROUTE[CAPABILITY.purchasingOrdersCreate].slice(1),
    )
    expect(createBranch?.at(-1)?.route.path).not.toBe(
      CAPABILITY_ROUTE[CAPABILITY.purchasingOrdersDetail].slice(1),
    )

    const shellChildren = shellRoutes[0]?.children ?? []
    const ownerRequestCreate = purchasingRoutes.find((route) =>
      route.path === PURCHASING_ROUTE[PURCHASING_CAPABILITY.requestCreate].slice(1))
    expect(ownerRequestCreate).toBeDefined()
    expect(shellChildren.filter((route) => route.path === ownerRequestCreate?.path)).toEqual([ownerRequestCreate])
    const ownerCreate = purchasingRoutes.find((route) =>
      route.path === PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderCreate].slice(1))
    expect(ownerCreate).toBeDefined()
    expect(shellChildren.filter((route) => route.path === ownerCreate?.path)).toEqual([ownerCreate])
  })

  it('gives only the two lists their lane navigation labels', () => {
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.purchasingRequestsList]).toBe(
      PURCHASING_NAV_LABEL[PURCHASING_CAPABILITY.requestList],
    )
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.purchasingRequestsCreate]).toBe('')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.purchasingRequestsDetail]).toBe('')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.purchasingOrdersList]).toBe(
      PURCHASING_NAV_LABEL[PURCHASING_CAPABILITY.orderList],
    )
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.purchasingOrdersCreate]).toBe('')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.purchasingOrdersDetail]).toBe('')
  })
})

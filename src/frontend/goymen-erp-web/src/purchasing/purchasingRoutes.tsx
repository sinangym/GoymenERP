import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import { PURCHASING_CAPABILITY, PURCHASING_ROUTE, type PurchasingCapabilityId } from './capabilities'
import { PurchaseOrderDetailPage } from './PurchaseOrderDetailPage'
import { PurchaseOrderListPage } from './PurchaseOrderListPage'

function gate(capability: PurchasingCapabilityId, children: ReactNode) {
  return <CapabilityRoute capability={capability as CapabilityId}>{children}</CapabilityRoute>
}

export const purchasingRoutes: RouteObject[] = [
  { path: PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderList].slice(1), element: gate(PURCHASING_CAPABILITY.orderList, <PurchaseOrderListPage />) },
  { path: PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderDetail].slice(1), element: gate(PURCHASING_CAPABILITY.orderDetail, <PurchaseOrderDetailPage />) },
]

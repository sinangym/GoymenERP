/**
 * The Manufacturing route table, spread into `shell/router.tsx` as `...manufacturingRoutes`.
 *
 * Wrapped in `<CapabilityRoute>`: the capability is published (MFG-UI-F #349), but the
 * registry grants it only to an actor holding its permission; for anyone else the route
 * mounts nothing and renders the same neutral not-found block any unknown path gets.
 */

import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import {
  MANUFACTURING_CAPABILITY,
  MANUFACTURING_ROUTE,
  type ManufacturingCapabilityId,
} from './capabilities'
import { OrderDetailPage } from './OrderDetailPage'
import { OrderListPage } from './OrderListPage'
import { PlanCreatePage } from './PlanCreatePage'
import { PlanDetailPage } from './PlanDetailPage'
import { PlanListPage } from './PlanListPage'

/** The five routed ids are declared in the shell's `CAPABILITY` table, so no cast is needed. */
function gate(capability: CapabilityId & ManufacturingCapabilityId, children: ReactNode) {
  return <CapabilityRoute capability={capability}>{children}</CapabilityRoute>
}

export const manufacturingRoutes: RouteObject[] = [
  {
    path: MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.planCreate].slice(1),
    element: gate(MANUFACTURING_CAPABILITY.planCreate, <PlanCreatePage />),
  },
  {
    // MFG-PLAN-LIST: `/uretim/planlar` is its own static segment, never read as `/uretim/plan/:planId`.
    path: MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.planList].slice(1),
    element: gate(MANUFACTURING_CAPABILITY.planList, <PlanListPage />),
  },
  {
    path: MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.planDetail].slice(1),
    element: gate(MANUFACTURING_CAPABILITY.planDetail, <PlanDetailPage />),
  },
  {
    // Static before parameterized: `/uretim/emirler` is not an order id.
    path: MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.orderList].slice(1),
    element: gate(MANUFACTURING_CAPABILITY.orderList, <OrderListPage />),
  },
  {
    path: MANUFACTURING_ROUTE[MANUFACTURING_CAPABILITY.orderDetail].slice(1),
    element: gate(MANUFACTURING_CAPABILITY.orderDetail, <OrderDetailPage />),
  },
]
